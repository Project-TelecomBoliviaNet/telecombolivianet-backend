using TelecomBoliviaNet.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;
#pragma warning disable CS8981  // "r" alias — intentional shorthand for Result
using r = TelecomBoliviaNet.Domain.Primitives.Result;
#pragma warning restore CS8981

namespace TelecomBoliviaNet.Application.Services.Payments;

/// <summary>
/// Métodos M2: crédito a favor, cierre de caja, recibo PDF y reporte por operador.
/// US-PAG-CREDITO · US-PAG-CAJA · US-PAG-RECIBO · US-PAG-06
/// </summary>
public class PaymentCreditService : IPaymentCreditService
{
    private readonly IGenericRepository<Payment>         _paymentRepo;
    private readonly IGenericRepository<Invoice>         _invoiceRepo;
    private readonly IGenericRepository<Client>          _clientRepo;
    private readonly IGenericRepository<PaymentInvoice>  _piRepo;
    private readonly IGenericRepository<CashClose>       _cashCloseRepo;
    private readonly IGenericRepository<PaymentReceipt>  _receiptRepo;
    private readonly ISequenceGenerator                  _seqGen;
    private readonly IGenericRepository<UserSystem>      _userRepo;
    private readonly INotifPublisher                     _notif;
    private readonly AuditService                        _audit;
    private readonly IUnitOfWork                         _uow;  // CORRECCIÓN Bug #2

    public PaymentCreditService(
        IGenericRepository<Payment>         paymentRepo,
        IGenericRepository<Invoice>         invoiceRepo,
        IGenericRepository<Client>          clientRepo,
        IGenericRepository<PaymentInvoice>  piRepo,
        IGenericRepository<CashClose>       cashCloseRepo,
        IGenericRepository<PaymentReceipt>  receiptRepo,
        ISequenceGenerator                  seqGen,
        IGenericRepository<UserSystem>      userRepo,
        INotifPublisher                     notif,
        AuditService                        audit,
        IUnitOfWork                         uow)
    {
        _paymentRepo   = paymentRepo;
        _invoiceRepo   = invoiceRepo;
        _clientRepo    = clientRepo;
        _piRepo        = piRepo;
        _cashCloseRepo = cashCloseRepo;
        _receiptRepo   = receiptRepo;
        _seqGen        = seqGen;
        _userRepo      = userRepo;
        _notif         = notif;
        _audit         = audit;
        _uow           = uow;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-PAG-CREDITO · Crédito a favor cuando el pago excede la deuda
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registra un pago, aplica créditos existentes, calcula excedente
    /// y lo guarda como CreditBalance en el cliente.
    /// Genera recibo PDF automáticamente (US-PAG-RECIBO).
    /// </summary>
    public async Task<Result<PaymentRegisteredDto>> RegisterPaymentWithCreditAsync(
        RegisterPaymentDto dto, Guid actorId, string actorName, string ip)
    {
        // Validaciones previas a la transacción
        var client = await _clientRepo.GetAll()
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == dto.ClientId);

        if (client is null)
            return Result<PaymentRegisteredDto>.Failure("Cliente no encontrado.");

        // Obtener facturas seleccionadas
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => dto.InvoiceIds.Contains(i.Id) && i.ClientId == dto.ClientId)
            .ToListAsync();

        if (!invoices.Any())
            return Result<PaymentRegisteredDto>.Failure("No se encontraron facturas válidas para este cliente.");

        // CORRECCIÓN Bug #2: envolver todas las operaciones en una transacción atómica.
        // Si falla cualquier paso (pago, actualización de facturas, crédito, recibo),
        // el rollback deja la BD en el estado original y no hay datos corruptos.
        await _uow.BeginTransactionAsync();
        try
        {

        var deudaTotal    = invoices.Sum(i => i.Amount - i.AmountPaid);
        var creditoPrevio = client.CreditBalance;
        var montoEfectivo = dto.Amount + creditoPrevio; // total disponible

        decimal excedente = 0m;
        decimal creditoUsado = 0m;

        if (montoEfectivo > deudaTotal)
        {
            excedente    = montoEfectivo - deudaTotal;
            creditoUsado = Math.Min(creditoPrevio, deudaTotal);
        }
        else
        {
            creditoUsado = Math.Min(creditoPrevio, montoEfectivo);
        }

        // Crear el pago
        var payment = new Payment
        {
            ClientId              = dto.ClientId,
            Amount                = dto.Amount,
            Method                = Enum.Parse<PaymentMethod>(dto.Method),
            Bank                  = dto.Bank,
            PaidAt                = dto.PaidAt.ToUniversalTime(),
            RegisteredAt          = DateTime.UtcNow,
            RegisteredByUserId    = actorId,
            PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
        };
        await _paymentRepo.AddAsync(payment);

        // Aplicar pago a facturas
        var montoPendiente = montoEfectivo;
        var invoiceNumbers = new List<string>();

        foreach (var inv in invoices.OrderBy(i => i.DueDate))
        {
            if (montoPendiente <= 0) break;
            var saldoInv = inv.Amount - inv.AmountPaid;
            var aplicar  = Math.Min(saldoInv, montoPendiente);

            inv.AmountPaid += aplicar;
            montoPendiente  -= aplicar;

            if (inv.AmountPaid >= inv.Amount)
            {
                inv.Status    = InvoiceStatus.Pagada;
                inv.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                inv.Status    = InvoiceStatus.ParcialmentePagada;
                inv.UpdatedAt = DateTime.UtcNow;
            }
            await _invoiceRepo.UpdateAsync(inv);

            await _piRepo.AddAsync(new PaymentInvoice
            {
                PaymentId = payment.Id,
                InvoiceId = inv.Id,
            });

            if (inv.InvoiceNumber is not null)
                invoiceNumbers.Add(inv.InvoiceNumber);
        }

        // Actualizar crédito del cliente
        var prevCredit  = client.CreditBalance;
        client.CreditBalance = excedente;
        await _clientRepo.UpdateAsync(client);

        // Generar número correlativo del recibo
        var receiptNumber = await GenerateReceiptNumberAsync();

        // Guardar recibo (PDF generado por InvoicePdfService en el controller)
        var receipt = new PaymentReceipt
        {
            ReceiptNumber     = receiptNumber,
            PaymentId         = payment.Id,
            ClientId          = dto.ClientId,
            GeneratedByUserId = actorId,
            Amount            = dto.Amount,
            Method            = dto.Method,
            Bank              = dto.Bank,
            PaidAt            = payment.PaidAt,
            InvoiceNumbers    = string.Join(", ", invoiceNumbers),
            PdfPath           = string.Empty, // el controller llama a InvoicePdfService y actualiza
            GeneratedAt       = DateTime.UtcNow,
        };
        await _receiptRepo.AddAsync(receipt);

            // Confirmar transacción ANTES de efectos secundarios (notif, audit)
            await _uow.CommitAsync();

            // Efectos secundarios fuera de la transacción (no críticos para integridad)
            try
            {
                await _notif.PublishAsync(
                    NotifType.CONFIRMACION_PAGO,
                    client.Id,
                    client.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"]  = client.FullName,
                        ["monto"]   = $"{dto.Amount:F2}",
                        ["periodo"] = invoiceNumbers.Any() ? string.Join(", ", invoiceNumbers) : "Varios periodos",
                    });
            }
            catch { /* La notificación falla silenciosamente — el pago ya está persistido */ }

            await _audit.LogAsync("Pagos", "PAYMENT_REGISTERED_WITH_CREDIT",
                $"Pago registrado: {client.TbnCode} monto={dto.Amount:F2} excedente={excedente:F2}",
                userId: actorId, userName: actorName, ip: ip,
                prevData: JsonSerializer.Serialize(new { PrevCredit = prevCredit }),
                newData:  JsonSerializer.Serialize(new { NewCredit = excedente, PaymentId = payment.Id }));

            return Result<PaymentRegisteredDto>.Success(new PaymentRegisteredDto(
                payment.Id,
                receiptNumber,
                dto.Amount,
                excedente,
                creditoUsado,
                $"Pago registrado. {(excedente > 0 ? $"Crédito a favor: Bs. {excedente:F2}" : string.Empty)}"));
        }
        catch (Exception)
        {
            // BUG FIX: catch sin filtro when — antes se excluía InvalidOperationException
            // (que es exactamente lo que EF Core lanza en concurrencia / mal estado),
            // dejando la transacción abierta y los datos en estado inconsistente.
            await _uow.RollbackAsync();
            return Result<PaymentRegisteredDto>.Failure(
                "Error al registrar el pago. La operación fue revertida completamente.");
        }
    }

    /// <summary>US-PAG-CREDITO · Reembolso manual de crédito a favor (solo admin).</summary>
    public async Task<r> ReembolsarCreditoAsync(
        Guid clientId, string justificacion, Guid actorId, string actorName, string ip)
    {
        var client = await _clientRepo.GetByIdAsync(clientId);
        if (client is null) return Result.Failure("Cliente no encontrado.");
        if (client.CreditBalance <= 0) return Result.Failure("El cliente no tiene crédito a favor.");

        var prev = client.CreditBalance;
        client.CreditBalance = 0m;
        await _clientRepo.UpdateAsync(client);

        await _audit.LogAsync("Pagos", "CREDIT_REIMBURSED",
            $"Crédito reembolsado: {client.TbnCode} monto={prev:F2} razón={justificacion}",
            userId: actorId, userName: actorName, ip: ip,
            prevData: JsonSerializer.Serialize(new { CreditBalance = prev }),
            newData:  JsonSerializer.Serialize(new { CreditBalance = 0m, Justificacion = justificacion }));

        return Result.Success();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-PAG-CAJA · Cierre de caja por turno de operador
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Obtiene el turno activo del operador o crea uno nuevo al primer login del día.</summary>
    public async Task<CashCloseDto> GetOrCreateActiveTurnoAsync(Guid userId, string userName)
    {
        var turnoActivo = await _cashCloseRepo.GetAll()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ClosedAt == null);

        if (turnoActivo is not null)
            return await BuildCashCloseDto(turnoActivo, isClosed: false);

        // Crear nuevo turno
        var nuevo = new CashClose
        {
            UserId    = userId,
            StartedAt = DateTime.UtcNow,
        };
        await _cashCloseRepo.AddAsync(nuevo);
        return await BuildCashCloseDto(nuevo, isClosed: false);
    }

    /// <summary>Cierra el turno activo del operador y calcula el resumen.</summary>
    public async Task<Result<CashCloseDto>> CerrarTurnoAsync(
        Guid userId, string userName, string ip)
    {
        var turno = await _cashCloseRepo.GetAll()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ClosedAt == null);

        if (turno is null)
            return Result<CashCloseDto>.Failure("No hay un turno activo para este operador.");

        // Calcular pagos del turno
        var pagos = await _paymentRepo.GetAll()
            .Include(p => p.PaymentInvoices)
            .Where(p => p.RegisteredByUserId == userId
                     && p.RegisteredAt >= turno.StartedAt
                     && !p.IsVoided)
            .ToListAsync();

        var detalle = pagos
            .GroupBy(p => p.Method.ToString())
            .Select(g => new CashCloseChannelDetail(g.Key, g.Count(), g.Sum(p => p.Amount)))
            .ToList();

        turno.TotalAmount     = pagos.Sum(p => p.Amount);
        turno.DetailJson      = JsonSerializer.Serialize(detalle);
        turno.PagosValidados  = pagos.Count;
        turno.ClosedAt        = DateTime.UtcNow;
        await _cashCloseRepo.UpdateAsync(turno);

        await _audit.LogAsync("Pagos", "CASH_TURN_CLOSED",
            $"Turno cerrado: usuario={userName} total={turno.TotalAmount:F2}",
            userId: userId, userName: userName, ip: ip,
            newData: JsonSerializer.Serialize(new { TotalAmount = turno.TotalAmount, Pagos = pagos.Count }));

        return Result<CashCloseDto>.Success(await BuildCashCloseDto(turno, isClosed: true));
    }

    /// <summary>US-PAG-CAJA · Lista de cierres de turno para supervisor/admin.</summary>
    public async Task<List<CashCloseDto>> GetCashClosesAsync(
        Guid? userId, DateTime? desde, DateTime? hasta)
    {
        var query = _cashCloseRepo.GetAll()
            .Include(c => c.User)
            .Where(c => c.ClosedAt != null);

        if (userId.HasValue) query = query.Where(c => c.UserId == userId.Value);
        if (desde.HasValue)  query = query.Where(c => c.ClosedAt >= desde.Value.ToUniversalTime());
        if (hasta.HasValue)  query = query.Where(c => c.ClosedAt <= hasta.Value.ToUniversalTime().AddDays(1));

        var list = await query.OrderByDescending(c => c.ClosedAt).ToListAsync();
        var dtos = new List<CashCloseDto>();
        foreach (var cc in list) dtos.Add(await BuildCashCloseDto(cc, isClosed: true));
        return dtos;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-PAG-RECIBO · Recibo PDF
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<PaymentReceipt?> GetReceiptByPaymentAsync(Guid paymentId)
        => await _receiptRepo.GetAll().FirstOrDefaultAsync(r => r.PaymentId == paymentId);

    public async Task UpdateReceiptPdfPathAsync(Guid receiptId, string pdfPath)
    {
        var rec = await _receiptRepo.GetByIdAsync(receiptId);
        if (rec is null) return;
        rec.PdfPath = pdfPath;
        await _receiptRepo.UpdateAsync(rec);
    }

    public async Task MarkReceiptSentByWhatsAppAsync(Guid receiptId)
    {
        var rec = await _receiptRepo.GetByIdAsync(receiptId);
        if (rec is null) return;
        rec.SentByWhatsApp = true;
        rec.SentAt         = DateTime.UtcNow;
        await _receiptRepo.UpdateAsync(rec);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // US-PAG-06 · Reporte por operador (extiende CollectionReport)
    // ═══════════════════════════════════════════════════════════════════════

    // BUG FIX: la interfaz declara Result<CollectionReportByOperatorDto> — la implementación debe coincidir
    public async Task<Result<CollectionReportByOperatorDto>> GetCollectionByOperatorAsync(
        DateTime from, DateTime to, Guid? operatorId)
    {
        var utcFrom = from.ToUniversalTime();
        var utcTo   = to.ToUniversalTime().AddDays(1);

        var query = _paymentRepo.GetAll()
            .Include(p => p.Client)
            .Include(p => p.RegisteredBy)
            .Include(p => p.PaymentInvoices).ThenInclude(pi => pi.Invoice)
            .Where(p => p.PaidAt >= utcFrom && p.PaidAt < utcTo && !p.IsVoided);

        if (operatorId.HasValue)
            query = query.Where(p => p.RegisteredByUserId == operatorId.Value);

        var payments = await query.OrderByDescending(p => p.PaidAt).ToListAsync();

        // Obtener todos los operadores para el dropdown
        var operators = await _userRepo.GetAll()
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.FullName)
            .Select(u => new OperatorDropdownDto(u.Id, u.FullName))
            .ToListAsync();

        return Result<CollectionReportByOperatorDto>.Success(new CollectionReportByOperatorDto(
            from, to,
            payments.Sum(p => p.Amount),
            payments.Count,
            operatorId.HasValue
                ? payments.FirstOrDefault()?.RegisteredBy?.FullName
                : null,
            payments.Select(p => new PaymentReportRowDto(
                p.Id,
                p.Client?.TbnCode   ?? "—",
                p.Client?.FullName  ?? "—",
                p.Amount,
                p.Method.ToString(),
                p.Bank,
                p.PaidAt,
                p.RegisteredBy?.FullName ?? "Sistema",
                p.PhysicalReceiptNumber,
                p.FromWhatsApp
            )).ToList(),
            operators
        ));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers privados
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<string> GenerateReceiptNumberAsync()
        => await _seqGen.NextReceiptNumberAsync();

    private async Task<CashCloseDto> BuildCashCloseDto(CashClose cc, bool isClosed)
    {
        List<CashCloseChannelDetail> detalle;
        try { detalle = JsonSerializer.Deserialize<List<CashCloseChannelDetail>>(cc.DetailJson) ?? new(); }
        catch { detalle = new(); }

        // If not closed yet, calculate live
        if (!isClosed)
        {
            var pagosVivos = await _paymentRepo.GetAll()
                .Where(p => p.RegisteredByUserId == cc.UserId
                         && p.RegisteredAt >= cc.StartedAt
                         && !p.IsVoided)
                .ToListAsync();

            detalle = pagosVivos
                .GroupBy(p => p.Method.ToString())
                .Select(g => new CashCloseChannelDetail(g.Key, g.Count(), g.Sum(p => p.Amount)))
                .ToList();

            cc.TotalAmount    = pagosVivos.Sum(p => p.Amount);
            cc.PagosValidados = pagosVivos.Count;
        }

        var operatorName = (await _userRepo.GetByIdAsync(cc.UserId))?.FullName ?? "—";

        return new CashCloseDto(
            cc.Id,
            cc.UserId,
            operatorName,
            cc.StartedAt,
            cc.ClosedAt,
            cc.TotalAmount,
            cc.PagosValidados,
            cc.PagosRechazados,
            detalle,
            isClosed,
            cc.PdfPath
        );
    }
}
