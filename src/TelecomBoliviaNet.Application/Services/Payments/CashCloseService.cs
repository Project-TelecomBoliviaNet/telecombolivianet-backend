using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Services.Payments;

/// <summary>
/// US-PAG-CAJA · Gestión de turnos de caja por operador.
/// Extraído de PaymentCreditService para cumplir SRP.
/// </summary>
public class CashCloseService : ICashCloseService
{
    private readonly IGenericRepository<CashClose>  _cashCloseRepo;
    private readonly IGenericRepository<Payment>    _paymentRepo;
    private readonly IGenericRepository<UserSystem> _userRepo;
    private readonly AuditService                   _audit;

    public CashCloseService(
        IGenericRepository<CashClose>  cashCloseRepo,
        IGenericRepository<Payment>    paymentRepo,
        IGenericRepository<UserSystem> userRepo,
        AuditService                   audit)
    {
        _cashCloseRepo = cashCloseRepo;
        _paymentRepo   = paymentRepo;
        _userRepo      = userRepo;
        _audit         = audit;
    }

    public async Task<CashCloseDto> GetOrCreateActiveTurnoAsync(Guid userId, string userName)
    {
        var turnoActivo = await _cashCloseRepo.GetAll()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ClosedAt == null);

        if (turnoActivo is not null)
            return await BuildCashCloseDto(turnoActivo, isClosed: false);

        var nuevo = new CashClose { UserId = userId, StartedAt = DateTime.UtcNow };
        await _cashCloseRepo.AddAsync(nuevo);
        return await BuildCashCloseDto(nuevo, isClosed: false);
    }

    public async Task<Result<CashCloseDto>> CerrarTurnoAsync(
        Guid userId, string userName, string ip)
    {
        var turno = await _cashCloseRepo.GetAll()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ClosedAt == null);

        if (turno is null)
            return Result<CashCloseDto>.Failure("No hay un turno activo para este operador.");

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

        turno.TotalAmount    = pagos.Sum(p => p.Amount);
        turno.DetailJson     = JsonSerializer.Serialize(detalle);
        turno.PagosValidados = pagos.Count;
        turno.ClosedAt       = DateTime.UtcNow;
        await _cashCloseRepo.UpdateAsync(turno);

        await _audit.LogAsync("Pagos", "CASH_TURN_CLOSED",
            $"Turno cerrado: usuario={userName} total={turno.TotalAmount:F2}",
            userId: userId, userName: userName, ip: ip,
            newData: JsonSerializer.Serialize(new { TotalAmount = turno.TotalAmount, Pagos = pagos.Count }));

        return Result<CashCloseDto>.Success(await BuildCashCloseDto(turno, isClosed: true));
    }

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

    private async Task<CashCloseDto> BuildCashCloseDto(CashClose cc, bool isClosed)
    {
        List<CashCloseChannelDetail> detalle;
        try { detalle = JsonSerializer.Deserialize<List<CashCloseChannelDetail>>(cc.DetailJson) ?? new(); }
        catch { detalle = new(); }

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
            cc.Id, cc.UserId, operatorName,
            cc.StartedAt, cc.ClosedAt,
            cc.TotalAmount, cc.PagosValidados, cc.PagosRechazados,
            detalle, isClosed, cc.PdfPath);
    }
}
