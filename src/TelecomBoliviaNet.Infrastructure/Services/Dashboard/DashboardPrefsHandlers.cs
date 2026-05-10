using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Dashboard.Queries;
using TelecomBoliviaNet.Application.DTOs.Dashboard;
using TelecomBoliviaNet.Domain.Entities.Dashboard;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

// ── GetDashPreferencesHandler ─────────────────────────────────────────────

public class GetDashPreferencesHandler : IRequestHandler<GetDashPreferencesQuery, DashboardPreferencesDto>
{
    private readonly AppDbContext _db;
    public GetDashPreferencesHandler(AppDbContext db) => _db = db;

    public async Task<DashboardPreferencesDto> Handle(GetDashPreferencesQuery q, CancellationToken ct)
    {
        var p = await _db.DashboardPreferences.FirstOrDefaultAsync(x => x.UserId == q.UserId, ct);
        return p is null
            ? new DashboardPreferencesDto(true, true, true, true, true, true, true, true)
            : new DashboardPreferencesDto(p.ShowKpis, p.ShowTendencia, p.ShowTickets, p.ShowWhatsApp, p.ShowDeudores, p.ShowZonas, p.ShowMetodosPago, p.ShowComprobantes);
    }
}

// ── SaveDashPreferencesHandler ────────────────────────────────────────────

public class SaveDashPreferencesHandler : IRequestHandler<SaveDashPreferencesCommand>
{
    private readonly AppDbContext _db;
    public SaveDashPreferencesHandler(AppDbContext db) => _db = db;

    public async Task Handle(SaveDashPreferencesCommand cmd, CancellationToken ct)
    {
        var p = await _db.DashboardPreferences.FirstOrDefaultAsync(x => x.UserId == cmd.UserId, ct);
        if (p is null) { p = new DashboardPreference { Id = Guid.NewGuid(), UserId = cmd.UserId }; _db.DashboardPreferences.Add(p); }
        var dto = cmd.Dto;
        p.ShowKpis = dto.ShowKpis; p.ShowTendencia = dto.ShowTendencia;
        p.ShowTickets = dto.ShowTickets; p.ShowWhatsApp = dto.ShowWhatsApp;
        p.ShowDeudores = dto.ShowDeudores; p.ShowZonas = dto.ShowZonas;
        p.ShowMetodosPago = dto.ShowMetodosPago; p.ShowComprobantes = dto.ShowComprobantes;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
