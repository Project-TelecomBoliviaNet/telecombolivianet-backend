using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Application.Services.Notifications;

namespace TelecomBoliviaNet.Infrastructure.Jobs;

/// <summary>
/// Job diario que detecta QRs de clientes próximos a vencer (≤ 5 días)
/// y envía una alerta en tiempo real al panel admin vía SignalR.
/// Se ejecuta cada día a las 09:00 hora Bolivia (UTC-4 = 13:00 UTC).
/// </summary>
public class QrExpiryAlertJob : BackgroundService
{
    private readonly IServiceScopeFactory      _scopeFactory;
    // BUG FIX: IAdminHubNotifier es Scoped — NO se puede inyectar en un Singleton (BackgroundService).
    // Se resuelve desde el scope de cada ejecución en RunAsync() para evitar captive dependency.
    private readonly ILogger<QrExpiryAlertJob> _logger;

    public QrExpiryAlertJob(
        IServiceScopeFactory       scopeFactory,
        ILogger<QrExpiryAlertJob>  logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("QrExpiryAlertJob iniciado.");

        // BUG #11 FIX: El chequeo original usaba nowBolivia.Minute == 0 exacto.
        // Si el contenedor se reinicia en el minuto 0 ya transcurrido (ej: 09:00:45),
        // el próximo chequeo es a los 60s (09:01:45), ya fuera de la ventana → el job
        // no se ejecuta hasta el día siguiente.
        // La corrección usa una ventana de 10 minutos (09:00–09:10) con un flag
        // de fecha para garantizar ejecución una sola vez por día aunque el pod
        // arranque a las 09:05.
        DateTime? lastRunDate = null;

        while (!ct.IsCancellationRequested)
        {
            var nowBolivia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NotifShared.BoliviaZone);
            var today      = nowBolivia.Date;

            if (nowBolivia.Hour == 9 && nowBolivia.Minute is >= 0 and <= 10
                && lastRunDate != today)
            {
                lastRunDate = today;
                await RunAsync();
                // Esperar al menos 15 min para evitar re-ejecución dentro de la misma ventana
                await Task.Delay(TimeSpan.FromMinutes(15), ct);
                continue;
            }

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ClientQrService>();
            // BUG FIX: resolver IAdminHubNotifier desde el scope, no desde el constructor del Singleton
            var hub = scope.ServiceProvider.GetRequiredService<IAdminHubNotifier>();

            var expiring = await svc.GetQrsExpiringSoonAsync();

            if (!expiring.Any())
            {
                _logger.LogDebug("QrExpiryAlertJob: sin QRs próximos a vencer.");
                return;
            }

            _logger.LogInformation("QrExpiryAlertJob: {Count} QR(s) próximos a vencer.", expiring.Count);

            foreach (var (clientId, clientName, tbnCode, expiresAt) in expiring)
            {
                var daysLeft = (int)Math.Round((expiresAt - DateTime.UtcNow).TotalDays);

                var payload = new
                {
                    EventType  = "QR_EXPIRING_SOON",
                    ClientId   = clientId.ToString(),
                    ClientName = clientName,
                    TbnCode    = tbnCode,
                    ExpiresAt  = expiresAt.ToString("O"),
                    DaysLeft   = daysLeft,
                    Message    = $"El QR de {tbnCode} – {clientName} vence en {daysLeft} día(s).",
                    ReceivedAt = DateTimeOffset.UtcNow.ToString("O"),
                };

                // Enviar alerta al panel admin vía SignalR
                await hub.SendToAllAsync("QrExpiringSoon", payload);

                // Marcar alerta como enviada para no duplicar
                await svc.MarkAlertSentAsync(clientId);

                _logger.LogInformation(
                    "Alerta QR enviada: {TbnCode} — vence {ExpiresAt:dd/MM/yyyy}",
                    tbnCode, expiresAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QrExpiryAlertJob: error en ejecución.");
        }
    }
}
