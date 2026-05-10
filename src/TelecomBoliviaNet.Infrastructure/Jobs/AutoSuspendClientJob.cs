using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Jobs;

/// <summary>M12 · Suspensión automática de clientes con deuda vencida. Ejecuta a las 09:30 Bolivia (13:30 UTC).</summary>
public class AutoSuspendClientJob : BackgroundService
{
    private readonly IServiceScopeFactory          _scopeFactory;
    private readonly ILogger<AutoSuspendClientJob> _logger;

    public AutoSuspendClientJob(IServiceScopeFactory s, ILogger<AutoSuspendClientJob> l)
    { _scopeFactory = s; _logger = l; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now  = DateTime.UtcNow;
            // Bolivia = UTC-4 → 09:30 Bolivia = 13:30 UTC
            var next = now.Date.AddHours(13).AddMinutes(30);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, ct); } catch (TaskCanceledException) { break; }
            await RunAsync(ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope    = _scopeFactory.CreateScope();
        var db             = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifPublisher = scope.ServiceProvider.GetRequiredService<INotifPublisher>();

        var configs = await db.SystemConfigs.ToListAsync(ct);
        var activo  = configs.FirstOrDefault(c => c.Key == SystemConfigKeys.AutoSuspendActivo)?.Value == "true";
        if (!activo)
        {
            _logger.LogInformation("AutoSuspendClientJob: desactivado por configuración.");
            return;
        }

        var diasStr = configs.FirstOrDefault(c => c.Key == SystemConfigKeys.AutoSuspendDiasGracia)?.Value;
        var dias    = int.TryParse(diasStr, out var d) ? d : 10;
        var cutoff  = DateTime.UtcNow.AddDays(-dias);

        var clientsToSuspend = await db.Clients
            .Include(c => c.Plan)
            .Where(c => c.Status == ClientStatus.Activo && !c.IsDeleted &&
                        db.Invoices.Any(i =>
                            i.ClientId == c.Id && !i.IsDeleted &&
                            i.Status == InvoiceStatus.Vencida &&
                            i.DueDate <= cutoff))
            .Take(20)
            .ToListAsync(ct);

        int suspended = 0;
        foreach (var client in clientsToSuspend)
        {
            var recentPayment = await db.Payments
                .AnyAsync(p => p.ClientId == client.Id && !p.IsDeleted
                            && p.PaidAt >= DateTime.UtcNow.AddHours(-24), ct);
            if (recentPayment) continue;

            try
            {
                client.Suspend();

                await notifPublisher.PublishAsync(
                    NotifType.SUSPENSION,
                    client.Id,
                    client.PhoneMain,
                    new Dictionary<string, string>
                    {
                        ["nombre"] = client.FullName.Split(' ')[0],
                        ["plan"]   = client.Plan?.Name ?? "su plan",
                    });

                db.AuditLogs.Add(new AuditLog
                {
                    UserName    = "Sistema",
                    Module      = "Clientes",
                    Action      = "AUTO_SUSPEND",
                    Description = $"Cliente '{client.FullName}' ({client.TbnCode}) suspendido automáticamente por deuda vencida >{dias} días.",
                    EntityId    = client.Id,
                    CreatedAt   = DateTime.UtcNow,
                });

                suspended++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AutoSuspendClientJob: no se pudo suspender cliente {TbnCode}.", client.TbnCode);
            }
        }

        if (suspended > 0) await db.SaveChangesAsync(ct);
        _logger.LogInformation("AutoSuspendClientJob: {Count} clientes suspendidos.", suspended);
    }
}
