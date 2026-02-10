using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Infrastructure.Data;
using static TelecomBoliviaNet.Infrastructure.Services.Dashboard.DashboardTimeHelper;

namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

// ── GetWhatsAppActividadHandler ───────────────────────────────────────────

public class GetWhatsAppActividadHandler : IRequestHandler<GetWhatsAppActividadQuery, List<WhatsAppActividadDto>>
{
    private readonly AppDbContext _db;
    public GetWhatsAppActividadHandler(AppDbContext db) => _db = db;

    public async Task<List<WhatsAppActividadDto>> Handle(GetWhatsAppActividadQuery q, CancellationToken ct)
    {
        var ini = StartOfDayUtc(NowBo());
        var raw = await _db.WhatsAppReceipts
            .Where(r => r.ReceivedAt >= ini)
            .OrderByDescending(r => r.ReceivedAt).Take(q.Top)
            .Select(r => new { r.ReceivedAt, ClienteNombre = r.Client!.FullName, r.Status, r.DeclaredAmount })
            .ToListAsync(ct);
        return raw.Select(r => new WhatsAppActividadDto(UtcToBoHHmm(r.ReceivedAt), r.ClienteNombre, r.Status.ToString(), r.DeclaredAmount)).ToList();
    }
}

// ── GetDeudoresHandler ────────────────────────────────────────────────────

public class GetDeudoresHandler : IRequestHandler<GetDeudoresQuery, List<DeudorItemDto>>
{
    private readonly AppDbContext _db;
    public GetDeudoresHandler(AppDbContext db) => _db = db;

    public async Task<List<DeudorItemDto>> Handle(GetDeudoresQuery _, CancellationToken ct)
    {
        var hoy = DateTime.UtcNow;
        var raw = await _db.Clients
            .Where(c => c.Invoices.Any(i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente))
            .Select(c => new {
                c.Id, c.FullName, c.Zone,
                Monto    = c.Invoices.Where(i => i.Status == InvoiceStatus.Vencida || i.Status == InvoiceStatus.Pendiente).Sum(i => i.Amount),
                Vencidas = c.Invoices.Count(i => i.Status == InvoiceStatus.Vencida),
                Earliest = c.Invoices.Where(i => i.Status == InvoiceStatus.Vencida).OrderBy(i => i.DueDate).Select(i => (DateTime?)i.DueDate).FirstOrDefault()
            }).ToListAsync(ct);

        return raw.Select(c => new DeudorItemDto(c.Id, c.FullName, c.Zone, c.Monto,
            c.Earliest.HasValue ? Math.Max(0, (int)(hoy - c.Earliest.Value).TotalDays) : 0, c.Vencidas))
            .OrderByDescending(d => d.DiasVencido).ToList();
    }
}

// ── GetActividadHorasHandler ──────────────────────────────────────────────

public class GetActividadHorasHandler : IRequestHandler<GetActividadHorasQuery, List<ActividadHoraDto>>
{
    private readonly AppDbContext _db;
    public GetActividadHorasHandler(AppDbContext db) => _db = db;

    public async Task<List<ActividadHoraDto>> Handle(GetActividadHorasQuery _, CancellationToken ct)
    {
        var nowBo = NowBo();
        var ini   = StartOfDayUtc(nowBo);
        var fin   = ini.AddDays(1);

        var pH = await _db.Payments.Where(p => p.PaidAt >= ini && p.PaidAt < fin).Select(p => p.PaidAt.Hour).ToListAsync(ct);
        var tH = await _db.SupportTickets.Where(t => t.CreatedAt >= ini && t.CreatedAt < fin).Select(t => t.CreatedAt.Hour).ToListAsync(ct);
        var wH = await _db.WhatsAppReceipts.Where(r => r.ReceivedAt >= ini && r.ReceivedAt < fin).Select(r => r.ReceivedAt.Hour).ToListAsync(ct);

        var pBo = pH.Select(UtcHourToBo).ToList();
        var tBo = tH.Select(UtcHourToBo).ToList();
        var wBo = wH.Select(UtcHourToBo).ToList();

        return Enumerable.Range(0, 24)
            .Select(h => new ActividadHoraDto(h, pBo.Count(x => x == h), tBo.Count(x => x == h), wBo.Count(x => x == h)))
            .ToList();
    }
}
