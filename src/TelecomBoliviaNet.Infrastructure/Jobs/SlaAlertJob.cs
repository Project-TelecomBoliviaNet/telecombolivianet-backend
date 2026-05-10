using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Jobs;

/// <summary>US-06 · Alertas automáticas de SLA próximo a vencer. Ejecuta cada hora.</summary>
public class SlaAlertJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration       _config;
    private readonly ILogger<SlaAlertJob> _logger;

    public SlaAlertJob(IServiceScopeFactory s, IConfiguration c, ILogger<SlaAlertJob> l)
    { _scopeFactory = s; _config = c; _logger = l; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now  = DateTime.UtcNow;
            var next = now.AddMinutes(60 - now.Minute).AddSeconds(-now.Second);
            try { await Task.Delay(next - now, ct); } catch (TaskCanceledException) { break; }
            await RunAsync(ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var nowBolivia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NotifShared.BoliviaZone);
        int horaInicio = _config.GetValue<int>("SlaAlert:HoraInicioLaboral", 7);
        int horaFin    = _config.GetValue<int>("SlaAlert:HoraFinLaboral",    22);
        if (nowBolivia.Hour < horaInicio || nowBolivia.Hour >= horaFin) return;

        int horasAnticipacion = _config.GetValue<int>("SlaAlert:HorasAnticipacion", 4);
        var now    = DateTime.UtcNow;
        var umbral = now.AddHours(horasAnticipacion);

        using var scope   = _scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // BUG FIX: usar INotifPublisher (outbox transaccional) en lugar de IWhatsAppNotifier directo.
        // Garantiza reintentos y respeta la ventana horaria configurada en NotifConfig.
        var notifPublisher = scope.ServiceProvider.GetRequiredService<INotifPublisher>();

        var tickets = await db.SupportTickets
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value <= umbral && t.DueDate.Value > now
                     && t.Status != TicketStatus.Resuelto
                     && t.Status != TicketStatus.Cerrado
                     && t.Status != TicketStatus.PendienteCliente
                     && t.Status != TicketStatus.EnVisita)
            .ToListAsync(ct);

        var adminPhone = await db.Set<UserSystem>()
            .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Activo && u.Phone != null)
            .Select(u => u.Phone).FirstOrDefaultAsync(ct);

        foreach (var ticket in tickets)
        {
            if (ticket.SlaAlertSentAt.HasValue)
            {
                var ventana = ticket.DueDate!.Value.AddHours(-horasAnticipacion);
                if (ticket.SlaAlertSentAt.Value >= ventana) continue;
            }
            var dueLocal = TimeZoneInfo.ConvertTimeFromUtc(ticket.DueDate!.Value, NotifShared.BoliviaZone).ToString("dd/MM/yyyy HH:mm");

            // CORRECCIÓN (Fix #6 / #19): Usar Phone (número WhatsApp) en lugar de Email.
            // La API de WhatsApp espera un número de teléfono, no una dirección de email.
            // Si el técnico/admin no tiene número registrado, se omite la notificación
            // para no enviar un mensaje a un destino inválido (y que falle silenciosamente).
            string? destino = ticket.AssignedTo?.Phone ?? adminPhone;
            if (string.IsNullOrEmpty(destino))
            {
                // Sin número de WhatsApp registrado — loguear y continuar sin crashear
                continue;
            }

            // FIX-A: usar ALERTA_SLA (tipo semánticamente correcto para alertas internas al técnico).
            // Las variables coinciden con el template "ALERTA_SLA" en NotifPlantillas.
            var sinTecnico = ticket.AssignedTo is null ? " — ⚠️ sin técnico asignado" : string.Empty;

            await notifPublisher.PublishAsync(
                NotifType.ALERTA_SLA,
                ticket.ClientId ?? Guid.Empty,
                destino,
                new Dictionary<string, string>
                {
                    ["num_ticket"]  = ticket.TicketNumber ?? ticket.Id.ToString()[..8].ToUpper(),
                    ["cliente"]     = ticket.Client?.FullName ?? "?",
                    ["horas"]       = horasAnticipacion.ToString(),
                    ["vence"]       = dueLocal,
                    ["sin_tecnico"] = sinTecnico,
                });
            ticket.SlaAlertSentAt = DateTime.UtcNow;
            // No try/catch — el outbox garantiza persistencia; el publisher maneja fallos internamente.
            // Conservar registro en TicketNotification para trazabilidad del ticket.
            db.TicketNotifications.Add(new TicketNotification
            {
                TicketId = ticket.Id, Type = NotificationType.AlertaSla,
                Status = NotificationStatus.Enviado, Recipient = destino,
                Message = $"ALERTA SLA: {ticket.TicketNumber ?? ticket.Id.ToString()[..8].ToUpper()} — vence {dueLocal}{sinTecnico}",
                SentAt = DateTime.UtcNow,
            });

        }
        if (tickets.Count > 0) await db.SaveChangesAsync(ct);

        // ── M09: Escalación a administrador por SLA vencido ─────────────────
        await EscalateBreachedAsync(db, notifPublisher, adminPhone, now, ct);
    }

    private static async Task EscalateBreachedAsync(
        AppDbContext    db,
        INotifPublisher notifPublisher,
        string?         adminPhone,
        DateTime        now,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(adminPhone)) return;

        // Tickets cuyo SLA ya venció sin escalación previa
        var alreadyEscalated = await db.TicketNotifications
            .Where(n => n.Type == NotificationType.EscalacionSla)
            .Select(n => n.TicketId)
            .ToListAsync(ct);

        var breached = await db.SupportTickets
            .Include(t => t.Client).Include(t => t.AssignedTo)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < now
                     && t.Status != TicketStatus.Resuelto
                     && t.Status != TicketStatus.Cerrado
                     && !alreadyEscalated.Contains(t.Id))
            .ToListAsync(ct);

        foreach (var ticket in breached)
        {
            var dueLocal    = TimeZoneInfo.ConvertTimeFromUtc(ticket.DueDate!.Value, NotifShared.BoliviaZone).ToString("dd/MM/yyyy HH:mm");
            var vencidoMins = (int)(now - ticket.DueDate.Value).TotalMinutes;
            var techName    = ticket.AssignedTo?.FullName ?? "Sin asignar";

            await notifPublisher.PublishAsync(
                NotifType.ALERTA_SLA,
                ticket.ClientId ?? Guid.Empty,
                adminPhone,
                new Dictionary<string, string>
                {
                    ["num_ticket"]  = ticket.TicketNumber ?? ticket.Id.ToString()[..8].ToUpper(),
                    ["cliente"]     = ticket.Client?.FullName ?? "?",
                    ["horas"]       = $"{vencidoMins / 60}h {vencidoMins % 60}m",
                    ["vence"]       = dueLocal,
                    ["sin_tecnico"] = $" — Técnico: {techName} | ⚠️ ESCALADO AL ADMIN",
                });

            db.TicketNotifications.Add(new TicketNotification
            {
                TicketId  = ticket.Id,
                Type      = NotificationType.EscalacionSla,
                Status    = NotificationStatus.Enviado,
                Recipient = adminPhone,
                Message   = $"ESCALACIÓN SLA: {ticket.TicketNumber ?? ticket.Id.ToString()[..8].ToUpper()} vencido hace {vencidoMins}m — técnico: {techName}",
                SentAt    = DateTime.UtcNow,
            });
        }
        if (breached.Count > 0) await db.SaveChangesAsync(ct);
    }
}
