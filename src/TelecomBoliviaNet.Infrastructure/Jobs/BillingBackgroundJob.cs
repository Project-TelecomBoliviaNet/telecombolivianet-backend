using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Application.Services.Notifications;

namespace TelecomBoliviaNet.Infrastructure.Jobs;

/// <summary>
/// Job de facturación mensual.
/// Corre como IHostedService y evalúa cada minuto si debe ejecutar alguno de los tres procesos:
///   - Día 1 del mes a las 00:01 Bolivia (UTC-4 = 04:01 UTC): genera facturas (US-21)
///   - Día 7 del mes a las 09:01 Bolivia: envía recordatorios               (US-34)
///   - Día 6 del mes a las 00:01 Bolivia (04:01 UTC):          marca vencidas  (US-22)
///
/// CORRECCIONES (Fix #2):
///   - Usa TimeZoneInfo.CreateCustomTimeZone en lugar de FindSystemTimeZoneById
///     para compatibilidad en contenedores Linux sin tzdata instalado.
///   - Los servicios internos son idempotentes (verifican existingClientIds antes de insertar).
/// </summary>
public class BillingBackgroundJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BillingBackgroundJob> _logger;

    public BillingBackgroundJob(
        IServiceProvider services,
        ILogger<BillingBackgroundJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingBackgroundJob iniciado.");

        // BUG FIX: leer las fechas de última ejecución desde SystemConfig en BD
        // en lugar de variables en memoria que se pierden al reiniciar el contenedor.
        // Usar claves: billing.lastGeneratedMonth, billing.lastVoidedMonth, billing.lastReminderMonth
        // Formato: "YYYY-MM" (ej: "2026-04")

        string ReadLastRun(string key, IServiceScope scope)
        {
            var appDb = scope.ServiceProvider.GetRequiredService<TelecomBoliviaNet.Infrastructure.Data.AppDbContext>();
            return appDb.SystemConfigs.Where(c => c.Key == key).Select(c => c.Value).FirstOrDefault() ?? "";
        }

        async Task WriteLastRun(string key, string value, IServiceScope scope)
        {
            var appDb = scope.ServiceProvider.GetRequiredService<TelecomBoliviaNet.Infrastructure.Data.AppDbContext>();
            var cfg = await appDb.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key, stoppingToken);
            if (cfg is null)
                appDb.SystemConfigs.Add(new TelecomBoliviaNet.Domain.Entities.Admin.SystemConfig { Key = key, Value = value });
            else
                cfg.Value = value;
            await appDb.SaveChangesAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowBolivia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, NotifShared.BoliviaZone);
                var day   = nowBolivia.Day;
                var month = nowBolivia.Month;
                var year  = nowBolivia.Year;
                var hour  = nowBolivia.Hour;
                var min   = nowBolivia.Minute;
                var monthKey = $"{year:D4}-{month:D2}";

                // ── US-21 · Día 1 del mes, entre 00:01 y 00:10 ───────────────
                if (day == 1 && hour == 0 && min >= 1 && min <= 10)
                {
                    using var scope = _services.CreateScope();
                    if (ReadLastRun("billing.lastGeneratedMonth", scope) != monthKey)
                    {
                        _logger.LogInformation("Ejecutando job de GENERACIÓN {Month}/{Year}", month, year);
                        var billing = scope.ServiceProvider.GetRequiredService<BillingService>();
                        await billing.GenerateMonthlyInvoicesAsync(year, month);
                        await WriteLastRun("billing.lastGeneratedMonth", monthKey, scope);
                    }
                }

                // ── US-34 · Día 7 del mes, a las 09:01 Bolivia ───────────────
                if (day == 7 && hour == 9 && min >= 1 && min <= 10)
                {
                    using var scopeR = _services.CreateScope();
                    if (ReadLastRun("billing.lastReminderMonth", scopeR) != monthKey)
                    {
                        _logger.LogInformation("Ejecutando job de RECORDATORIOS {Month}/{Year}", month, year);
                        var paymentSvc = scopeR.ServiceProvider
                            .GetRequiredService<TelecomBoliviaNet.Application.Services.Payments.PaymentService>();
                        await paymentSvc.SendOverdueRemindersAsync();
                        await WriteLastRun("billing.lastReminderMonth", monthKey, scopeR);
                    }
                }

                // ── US-22 · Día 6 del mes, entre 00:01 y 00:10 ───────────────
                if (day == 6 && hour == 0 && min >= 1 && min <= 10)
                {
                    using var scope = _services.CreateScope();
                    if (ReadLastRun("billing.lastVoidedMonth", scope) != monthKey)
                    {
                        _logger.LogInformation("Ejecutando job de VENCIMIENTO {Month}/{Year}", month, year);
                        var billing = scope.ServiceProvider.GetRequiredService<BillingService>();
                        await billing.MarkOverdueInvoicesAsync();
                        await WriteLastRun("billing.lastVoidedMonth", monthKey, scope);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en BillingBackgroundJob");
            }

            // Evalúa cada 60 segundos
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }

        _logger.LogInformation("BillingBackgroundJob detenido.");
    }
}
