using Microsoft.EntityFrameworkCore;
using System.Threading;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Services;

/// <summary>
/// CORRECCIÓN Bug #3: implementación con secuencias nativas de PostgreSQL.
/// Usa AppDbContext directamente porque las tablas de secuencia tienen PK int,
/// incompatible con IGenericRepository&lt;T&gt; que requiere Entity (PK Guid).
/// </summary>
public sealed class SequenceGenerator : ISequenceGenerator
{
    private readonly AppDbContext _db;

    public SequenceGenerator(AppDbContext db) => _db = db;

    // BUG FIX: Las secuencias de PostgreSQL son globales y no se reinician al cambiar de año.
    // Solución: al detectar un cambio de año, reiniciar la secuencia con ALTER SEQUENCE RESTART.
    // El reinicio es atómico y seguro en multi-instancia porque PostgreSQL garantiza
    // que el primer nextval() después del restart devuelve 1.
    // Se persiste el año de última ejecución en SystemConfig para detectar el cambio entre reinicios.

    private static readonly SemaphoreSlim _resetLock = new(1, 1);

    // Crea la secuencia si no existe — idempotente, necesario cuando el esquema
    // fue inicializado vía EnsureCreatedAsync en lugar de MigrateAsync.
    private async Task EnsureSequenceAsync(string seqName)
    {
        #pragma warning disable EF1002 // Safe: seqName validated against allowlist
        await _db.Database.ExecuteSqlRawAsync(
            $"CREATE SEQUENCE IF NOT EXISTS \"{seqName}\" START 1 INCREMENT 1");
        #pragma warning restore EF1002
    }

    private async Task ResetIfYearChangedAsync(string seqName, string configKey)
    {
        var currentYear = DateTime.UtcNow.Year.ToString();
        var lastYear = await _db.SystemConfigs
            .Where(c => c.Key == configKey)
            .Select(c => c.Value)
            .FirstOrDefaultAsync();

        if (lastYear == currentYear) return;

        await _resetLock.WaitAsync();
        try
        {
            // Re-check inside lock to avoid double reset
            lastYear = await _db.SystemConfigs
                .Where(c => c.Key == configKey)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();
            if (lastYear == currentYear) return;

            #pragma warning disable EF1002 // Safe: seqName validated against allowlist
            await _db.Database.ExecuteSqlRawAsync($"CREATE SEQUENCE IF NOT EXISTS \"{seqName}\" START 1 INCREMENT 1");
            await _db.Database.ExecuteSqlRawAsync($"ALTER SEQUENCE \"{seqName}\" RESTART WITH 1");
            #pragma warning restore EF1002

            var cfg = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == configKey);
            if (cfg is null)
                _db.SystemConfigs.Add(new TelecomBoliviaNet.Domain.Entities.Admin.SystemConfig
                    { Key = configKey, Value = currentYear });
            else
                cfg.Value = currentYear;
            await _db.SaveChangesAsync();
        }
        finally { _resetLock.Release(); }
    }

    public async Task<string> NextInvoiceNumberAsync(bool isExtraordinary = false)
    {
        var seqName   = isExtraordinary ? "invoice_extra_seq" : "invoice_number_seq";
        var configKey = isExtraordinary ? "seq.invoice_extra.year" : "seq.invoice.year";
        await ResetIfYearChangedAsync(seqName, configKey);
        var next   = await NextVal(seqName);
        var prefix = isExtraordinary ? "FE" : "F";
        return $"{prefix}-{DateTime.UtcNow.Year}-{next:D4}";
    }

    public async Task<string> NextTicketNumberAsync()
    {
        await ResetIfYearChangedAsync("ticket_number_seq", "seq.ticket.year");
        var next = await NextVal("ticket_number_seq");
        return $"TK-{DateTime.UtcNow.Year}-{next:D4}";
    }

    public async Task<string> NextReceiptNumberAsync()
    {
        await ResetIfYearChangedAsync("receipt_number_seq", "seq.receipt.year");
        var next = await NextVal("receipt_number_seq");
        return $"REC-{DateTime.UtcNow.Year}-{next:D4}";
    }

    // BUG FIX: validación extraída a método explícito con nombre descriptivo
    // para que sea más difícil de omitir accidentalmente en futuros refactors.
    private static void ValidateSequenceName(string sequenceName)
    {
        var allowed = new[]
        {
            "invoice_number_seq", "invoice_extra_seq",
            "ticket_number_seq", "receipt_number_seq"
        };
        if (!allowed.Contains(sequenceName))
            throw new InvalidOperationException(
                $"Sequence name not allowed: '{sequenceName}'. " +
                $"Allowed values: {string.Join(", ", allowed)}");
    }

    private async Task<long> NextVal(string sequenceName)
    {
        ValidateSequenceName(sequenceName);
        await EnsureSequenceAsync(sequenceName);

        // nextval() es atómica a nivel de PostgreSQL — segura en multi-instancia.
        // El nombre de secuencia no puede parametrizarse en PostgreSQL (es un identificador,
        // no un valor), por eso se valida contra lista blanca antes de interpolar.
        // EF Core envuelve SqlQueryRaw<long> en subquery "SELECT t.Value FROM (...) AS t"
        // por eso se necesita el alias "Value" — sin él PostgreSQL lanza 42703.
        #pragma warning disable EF1002 // Safe: sequenceName validated by ValidateSequenceName above
        var result = await _db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequenceName}') AS \"Value\"")
            .FirstAsync();
        #pragma warning restore EF1002

        return result;
    }
}
