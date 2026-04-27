using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelecomBoliviaNet.Application.DTOs.Notifications;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// Helpers y datos de solo lectura compartidos entre los servicios de notificaciones.
/// Extraído de NotifConfigService (749 líneas → 5 servicios especializados).
/// CORRECCIÓN Problema #6 / #8.
/// </summary>
public static class NotifShared
{
    // ── Zona horaria de Bolivia (UTC-4, sin DST) — compatible sin tzdata ────
    public static readonly TimeZoneInfo BoliviaZone =
        TimeZoneInfo.CreateCustomTimeZone("BOT", TimeSpan.FromHours(-4), "Bolivia Time", "BOT");

    // ── Variables disponibles (US-NOT-VARS) ──────────────────────────────────

    public static readonly IReadOnlyDictionary<string, string> VariableDescriptions =
        new Dictionary<string, string>
        {
            ["{{nombre}}"]            = "Primer nombre del cliente",
            ["{{apellido}}"]          = "Apellido del cliente",
            ["{{nombre_completo}}"]   = "Nombre completo del cliente",
            ["{{deuda}}"]             = "Deuda total pendiente en Bs.",
            ["{{monto}}"]             = "Monto de la factura o pago",
            ["{{periodo}}"]           = "Período de la factura (ej: Enero 2026)",
            ["{{fecha_vencimiento}}"] = "Fecha de vencimiento de la factura",
            ["{{plan}}"]              = "Nombre del plan del cliente",
            ["{{zona}}"]              = "Zona del cliente",
            ["{{empresa}}"]           = "Nombre del ISP (SystemConfig)",
            ["{{dias_mora}}"]         = "Días de mora de la factura más antigua",
            ["{{meses_mora}}"]        = "Meses de mora",
            ["{{meses_pendientes}}"]  = "Cantidad de meses con facturas pendientes",
            ["{{fecha_corte}}"]       = "Fecha de corte configurada",
            ["{{num_ticket}}"]        = "Número correlativo del ticket (TK-AAAA-NNNN)",
            ["{{tecnico}}"]           = "Nombre del técnico asignado al ticket",
            ["{{fecha_visita}}"]      = "Fecha programada de visita técnica",
        };

    // ── Textos por defecto (US-37) ─────────────────────────────────────────

    public static readonly IReadOnlyDictionary<NotifType, string> DefaultTextos =
        new Dictionary<NotifType, string>
        {
            [NotifType.SUSPENSION]        = "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *suspendido* por falta de pago.\nComuníquese con nosotros para regularizar.\n\n*{{empresa}}*",
            [NotifType.REACTIVACION]      = "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *reactivado*. ¡Ya puede usarlo con normalidad!\n\n*{{empresa}}*",
            [NotifType.RECORDATORIO_R1]   = "Estimado/a {{nombre}},\n\nLe recordamos que tiene una factura de *Bs. {{monto}}* con vencimiento el *{{fecha_vencimiento}}* ({{meses_pendientes}} mes(es) pendiente(s)).\n\n*{{empresa}}*",
            [NotifType.RECORDATORIO_R2]   = "Estimado/a {{nombre}},\n\n⚠️ Su factura de *Bs. {{monto}}* vence el *{{fecha_vencimiento}}*. Evite la suspensión pagando a tiempo.\n\n*{{empresa}}*",
            [NotifType.RECORDATORIO_R3]   = "Estimado/a {{nombre}},\n\n🚨 Su factura vence *mañana* ({{fecha_vencimiento}}). Monto: *Bs. {{monto}}*. Pague hoy para no perder el servicio.\n\n*{{empresa}}*",
            [NotifType.FACTURA_VENCIDA]   = "Estimado/a {{nombre}},\n\nSu factura del periodo *{{periodo}}* por *Bs. {{monto}}* está *vencida*. Regularice su pago para evitar la suspensión.\n\n*{{empresa}}*",
            [NotifType.CONFIRMACION_PAGO] = "Estimado/a {{nombre}},\n\n✅ Hemos registrado su pago de *Bs. {{monto}}* correspondiente al periodo *{{periodo}}*.\n\nGracias por su pago. *{{empresa}}*",
            [NotifType.TICKET_CREADO]     = "Estimado/a {{nombre}},\n\nSu solicitud de soporte ha sido registrada con el número *{{num_ticket}}*.\n\nLe atenderemos a la brevedad. *{{empresa}}*",
            [NotifType.TICKET_RESUELTO]   = "Estimado/a {{nombre}},\n\nSu ticket *{{num_ticket}}* ha sido *resuelto* por {{tecnico}}.\n\nSi tiene alguna consulta adicional, contáctenos. *{{empresa}}*",
            [NotifType.CAMBIO_PLAN]       = "Estimado/a {{nombre}},\n\nSu plan de servicio ha sido actualizado a *{{plan}}*.\n\nEl cambio es efectivo inmediatamente. *{{empresa}}*",
        };

    // ── DTO mappers (sin dependencias de EF) ──────────────────────────────────

    public static NotifConfigDto ToConfigDto(NotifConfig c) => new(
        c.Tipo, c.Activo, c.DelaySegundos,
        c.HoraInicio.ToString("HH:mm"), c.HoraFin.ToString("HH:mm"),
        c.Inmediato, c.DiasAntes, c.PlantillaId);

    public static NotifPlantillaDto ToPlantillaDto(NotifPlantilla p) => new(
        p.Id, p.Tipo, p.Texto, p.Activa, p.Categoria, p.HsmStatus, p.CreadoAt);

    public static NotifSegmentDto ToSegmentDto(NotifSegment s, int? preview)
    {
        var reglas = string.IsNullOrEmpty(s.ReglasJson)
            ? new List<SegmentConditionGroup>()
            : JsonSerializer.Deserialize<List<SegmentConditionGroup>>(s.ReglasJson)
              ?? new List<SegmentConditionGroup>();
        return new NotifSegmentDto(s.Id, s.Nombre, s.Descripcion, reglas, s.CreadoAt, preview);
    }

    // ── Lógica de evaluación de segmentos (reutilizada por NotifSegmentService y NotifEnvioService) ──

    public static bool EvaluaCondicion(Client c, List<Invoice> inv, SegmentCondition cond)
    {
        try
        {
            return cond.Campo switch
            {
                "zona"      => ComparaString(c.Zone,                         cond.Operador, cond.Valor),
                "plan"      => ComparaString(c.Plan?.Name ?? string.Empty,  cond.Operador, cond.Valor),
                "estado"    => ComparaString(c.Status.ToString(),            cond.Operador, cond.Valor),
                "deuda"     => ComparaDecimal(inv.Sum(i => i.Amount),       cond.Operador, decimal.Parse(cond.Valor)),
                "dias_mora" => ComparaDecimal(
                    inv.Any() ? (decimal)(DateTime.UtcNow - inv.Min(i => i.DueDate)).TotalDays : 0,
                    cond.Operador, decimal.Parse(cond.Valor)),
                _ => false
            };
        }
        catch { return false; }
    }

    private static bool ComparaString(string actual, string op, string valor)
        => op switch
        {
            "="  => actual.Equals(valor, StringComparison.OrdinalIgnoreCase),
            "!=" => !actual.Equals(valor, StringComparison.OrdinalIgnoreCase),
            _    => false
        };

    private static bool ComparaDecimal(decimal actual, string op, decimal valor)
        => op switch
        {
            "="  => actual == valor,
            "!=" => actual != valor,
            ">"  => actual > valor,
            "<"  => actual < valor,
            ">=" => actual >= valor,
            "<=" => actual <= valor,
            _    => false
        };
}
