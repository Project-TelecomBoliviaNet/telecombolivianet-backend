using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Jobs;

/// <summary>US-20 · Cierre automático de tickets resueltos sin actividad. Ejecuta a las 02:00 UTC.</summary>
public class AutoCloseTicketJob : BackgroundService
{
    private readonly IServiceScopeFactory        _scopeFactory;
    private readonly IConfiguration              _config;
    private readonly ILogger<AutoCloseTicketJob> _logger;

    public AutoCloseTicketJob(IServiceScopeFactory s, IConfiguration c, ILogger<AutoCloseTicketJob> l)
    { _scopeFactory = s; _config = c; _logger = l; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now  = DateTime.UtcNow;
            var next = now.Date.AddDays(1).AddHours(2);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, ct); } catch (TaskCanceledException) { break; }
            await RunAsync(ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int waitDays = _config.GetValue<int>("AutoClose:WaitDays", 3);
        using var scope  = _scopeFactory.CreateScope();
        var db             = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // BUG FIX: usar INotifPublisher en lugar de IWhatsAppNotifier directo
        var notifPublisher = scope.ServiceProvider.GetRequiredService<INotifPublisher>();

        var cutoff  = DateTime.UtcNow.AddDays(-waitDays);
        var tickets = await db.SupportTickets.Include(t => t.Client)
            .Where(t => t.Status == TicketStatus.Resuelto && t.ResolvedAt.HasValue && t.ResolvedAt.Value <= cutoff)
            .ToListAsync(ct);

        foreach (var ticket in tickets)
        {
            ticket.Status   = TicketStatus.Cerrado;
            ticket.ClosedAt = DateTime.UtcNow;

            // US-20 AC-3: notificar al cliente
            if (ticket.Client is not null && !string.IsNullOrWhiteSpace(ticket.Client.PhoneMain))
            {
                var msg = $"Hola {ticket.Client.FullName}, tu ticket #{ticket.Id.ToString()[..8].ToUpper()} " +
                          $"({ticket.Subject}) fue cerrado automáticamente tras {waitDays} días sin actividad. " +
                          $"Si el problema persiste, crea un nuevo ticket. ¡Gracias!";
                // BUG FIX: publicar vía outbox en lugar de WhatsApp directo
                await notifPublisher.PublishAsync(
                    NotifType.CONFIRMACION_PAGO,
                    ticket.ClientId,
                    ticket.Client.PhoneMain,
                    new Dictionary<string, string> { ["mensaje_cierre"] = msg });
                db.TicketNotifications.Add(new TicketNotification
                {
                    TicketId = ticket.Id, Type = NotificationType.CierreAutomatico,
                    Status = NotificationStatus.Enviado, Recipient = ticket.Client.PhoneMain,
                    Message = msg, SentAt = DateTime.UtcNow,
                });
            }
        }
        if (tickets.Count > 0) await db.SaveChangesAsync(ct);
        _logger.LogInformation("AutoCloseTicketJob: {Count} tickets cerrados.", tickets.Count);
    }
}
