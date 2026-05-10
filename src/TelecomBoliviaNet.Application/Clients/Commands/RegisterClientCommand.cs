using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Clients.Commands;

public record RegisterClientCommand(
    RegisterClientDto Dto,
    Guid              ActorId,
    string            ActorName,
    string            ClientIp) : IRequest<Result<ClientListItemDto>>;

public class RegisterClientHandler : IRequestHandler<RegisterClientCommand, Result<ClientListItemDto>>
{
    private readonly IGenericRepository<Client>         _clientRepo;
    private readonly IGenericRepository<Invoice>        _invoiceRepo;
    private readonly IGenericRepository<Payment>        _paymentRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;
    private readonly IGenericRepository<Plan>           _planRepo;
    private readonly ITbnService                        _tbn;
    private readonly AuditService                       _audit;
    private readonly BillingService                     _billing;
    private readonly IUnitOfWork                        _uow;
    private readonly ILogger<RegisterClientHandler>     _logger;

    public RegisterClientHandler(
        IGenericRepository<Client>         clientRepo,
        IGenericRepository<Invoice>        invoiceRepo,
        IGenericRepository<Payment>        paymentRepo,
        IGenericRepository<PaymentInvoice> piRepo,
        IGenericRepository<Plan>           planRepo,
        ITbnService                        tbn,
        AuditService                       audit,
        BillingService                     billing,
        IUnitOfWork                        uow,
        ILogger<RegisterClientHandler>     logger)
    {
        _clientRepo  = clientRepo;
        _invoiceRepo = invoiceRepo;
        _paymentRepo = paymentRepo;
        _piRepo      = piRepo;
        _planRepo    = planRepo;
        _tbn         = tbn;
        _audit       = audit;
        _billing     = billing;
        _uow         = uow;
        _logger      = logger;
    }

    public async Task<Result<ClientListItemDto>> Handle(RegisterClientCommand cmd, CancellationToken ct)
    {
        var dto = cmd.Dto;

        if (await _clientRepo.AnyAsync(c => c.IdentityCard == dto.IdentityCard))
            return Result<ClientListItemDto>.Failure($"Ya existe un cliente con el CI '{dto.IdentityCard}'.");

        if (await _clientRepo.AnyAsync(c => c.WinboxNumber == dto.WinboxNumber))
            return Result<ClientListItemDto>.Failure($"El número Winbox '{dto.WinboxNumber}' ya está registrado.");

        var plan = await _planRepo.GetByIdAsync(dto.PlanId);
        if (plan is null || !plan.IsActive)
            return Result<ClientListItemDto>.Failure("El plan seleccionado no existe o está inactivo.");

        PaymentMethod? paymentMethod = null;
        if (dto.PaymentMethod is not null)
        {
            if (!Enum.TryParse<PaymentMethod>(dto.PaymentMethod, out var parsedMethod))
                return Result<ClientListItemDto>.Failure("Método de pago inválido.");
            paymentMethod = parsedMethod;
        }

        var tbnCode = await _tbn.GenerateNextAsync();
        var client  = Client.Create(
            tbnCode,
            dto.FullName.Trim(),
            dto.IdentityCard.Trim(),
            dto.PhoneMain.Trim(),
            dto.Zone.Trim(),
            DateTime.SpecifyKind(dto.InstallationDate, DateTimeKind.Utc),
            dto.InstalledByUserId,
            dto.PlanId);

        client.PhoneSecondary  = dto.PhoneSecondary?.Trim();
        client.Street          = dto.Street?.Trim();
        client.LocationRef     = dto.LocationRef?.Trim();
        client.GpsLatitude     = dto.GpsLatitude;
        client.GpsLongitude    = dto.GpsLongitude;
        client.WinboxNumber    = dto.WinboxNumber.Trim();
        client.HasTvCable      = dto.HasTvCable;
        client.OnuSerialNumber = dto.OnuSerialNumber?.Trim();

        var now        = DateTime.UtcNow;
        var invoiceIds = new List<Guid>();

        await _uow.BeginTransactionAsync();
        try
        {
            await _clientRepo.AddAsync(client);

            var instInvoice = new Invoice
            {
                ClientId = client.Id,
                Type     = InvoiceType.Instalacion,
                Status   = dto.PaidInstallation ? InvoiceStatus.Pagada : InvoiceStatus.Pendiente,
                Year     = now.Year,
                Month    = 0,
                Amount   = dto.InstallationCost,
                IssuedAt = now,
                DueDate  = now.AddDays(5)
            };
            await _invoiceRepo.AddAsync(instInvoice);
            if (dto.PaidInstallation) invoiceIds.Add(instInvoice.Id);

            // Flush necesario: GenerateBackfillInvoicesAsync necesita ver el cliente en BD
            await _clientRepo.SaveChangesAsync();
            await _billing.GenerateBackfillInvoicesAsync(client, plan.MonthlyPrice);
            await _invoiceRepo.SaveChangesAsync();

            if (dto.PaidFirstMonth)
            {
                var installDate       = client.InstallationDate;
                var firstMonthInvoice = await _invoiceRepo.GetAll()
                    .FirstOrDefaultAsync(i =>
                        i.ClientId == client.Id &&
                        i.Year     == installDate.Year  &&
                        i.Month    == installDate.Month &&
                        i.Type     == InvoiceType.Mensualidad, ct);

                if (firstMonthInvoice is not null)
                {
                    firstMonthInvoice.Status    = InvoiceStatus.Pagada;
                    firstMonthInvoice.UpdatedAt = DateTime.UtcNow;
                    await _invoiceRepo.UpdateAsync(firstMonthInvoice);
                    invoiceIds.Add(firstMonthInvoice.Id);
                }
            }

            if (invoiceIds.Count > 0 && paymentMethod is not null)
            {
                var paidInvoices = await _invoiceRepo.GetAll()
                    .Where(i => invoiceIds.Contains(i.Id))
                    .ToListAsync(ct);

                var payment = new Payment
                {
                    ClientId              = client.Id,
                    Amount                = paidInvoices.Sum(i => i.Amount),
                    Method                = paymentMethod.Value,
                    Bank                  = dto.Bank,
                    PhysicalReceiptNumber = dto.PhysicalReceiptNumber,
                    PaidAt                = DateTime.UtcNow,
                    RegisteredByUserId    = cmd.ActorId,
                    FromWhatsApp          = false
                };
                await _paymentRepo.AddAsync(payment);
                var piEntries = invoiceIds
                    .Select(invId => new PaymentInvoice { PaymentId = payment.Id, InvoiceId = invId })
                    .ToList();
                await _piRepo.AddRangeAsync(piEntries);
                await _paymentRepo.SaveChangesAsync();
            }

            await _uow.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en transacción RegisterAsync para TBN={TbnCode}", tbnCode);
            await _uow.RollbackAsync();
            return Result<ClientListItemDto>.Failure(
                "Error al registrar el cliente. Los datos no fueron guardados.");
        }

        await _audit.LogAsync("Clientes", "CLIENT_REGISTERED",
            $"Cliente registrado: {tbnCode} — {client.FullName}",
            userId: cmd.ActorId, userName: cmd.ActorName, ip: cmd.ClientIp,
            newData: JsonSerializer.Serialize(new { client.TbnCode, client.FullName, Plan = plan.Name }),
            entityId: client.Id);

        return Result<ClientListItemDto>.Success(new ClientListItemDto(
            client.Id, tbnCode, client.FullName, client.Zone,
            client.PhoneMain, plan.Name, client.HasTvCable, "Activo", 0, 0));
    }
}
