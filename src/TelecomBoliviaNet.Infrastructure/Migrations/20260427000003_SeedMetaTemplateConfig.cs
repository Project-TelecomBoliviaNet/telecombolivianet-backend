using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Siembra MetaTemplateName, MetaLanguageCode y MetaParamOrder en las
    /// plantillas existentes. HsmStatus queda como 'Pendiente' hasta que
    /// el admin confirme la aprobación en Meta Business Manager.
    /// Empresa y qr_enlace hardcodeados en los textos — no se pasan como parámetros.
    /// </summary>
    [Migration("20260427000003_SeedMetaTemplateConfig")]
    public partial class SeedMetaTemplateConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Recordatorios de pago ────────────────────────────────────────────

            // Aviso Inicial de Pago Pendiente (7 días antes del vencimiento)
            // Params: {{1}}=nombre  {{2}}=meses_deuda_detalle  {{3}}=deuda
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'aviso_pago_pendiente',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""meses_deuda_detalle"",""deuda""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'RECORDATORIO_R1' AND ""Activa"" = TRUE;
            ");

            // ── Estado del servicio ──────────────────────────────────────────────

            // Notificación de Suspensión de Servicio
            // Params: {{1}}=nombre  {{2}}=deuda
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'notificacion_suspension_servicio',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""deuda""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'SUSPENSION' AND ""Activa"" = TRUE;
            ");

            // Notificación de Reactivación de Servicio
            // Params: {{1}}=nombre
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'notificacion_reactivacion_servicio',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'REACTIVACION' AND ""Activa"" = TRUE;
            ");

            // ── Facturación ──────────────────────────────────────────────────────

            // Confirmación de Recepción de Pago
            // Params: {{1}}=nombre  {{2}}=monto  {{3}}=periodo
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'confirmacion_recepcion_pago',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""monto"",""periodo""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'CONFIRMACION_PAGO' AND ""Activa"" = TRUE;
            ");

            // Aviso de Factura Vencida
            // Params: {{1}}=nombre  {{2}}=meses_deuda_detalle  {{3}}=deuda
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'aviso_factura_vencida',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""meses_deuda_detalle"",""deuda""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'FACTURA_VENCIDA' AND ""Activa"" = TRUE;
            ");

            // ── Tickets de soporte ───────────────────────────────────────────────

            // Confirmación de Ticket de Soporte Creado
            // Params: {{1}}=nombre  {{2}}=num_ticket
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'confirmacion_ticket_soporte',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""num_ticket""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'TICKET_CREADO' AND ""Activa"" = TRUE;
            ");

            // Asignación de Técnico y Fecha de Visita
            // Params: {{1}}=nombre  {{2}}=num_ticket  {{3}}=tecnico  {{4}}=fecha_visita
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'asignacion_tecnico_visita',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""num_ticket"",""tecnico"",""fecha_visita""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'TICKET_ASIGNADO' AND ""Activa"" = TRUE;
            ");

            // Resolución de Ticket de Soporte
            // Params: {{1}}=nombre  {{2}}=num_ticket
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'resolucion_ticket_soporte',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""num_ticket""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'TICKET_RESUELTO' AND ""Activa"" = TRUE;
            ");

            // ── Plan ─────────────────────────────────────────────────────────────

            // Notificación de Cambio de Plan
            // Params: {{1}}=nombre  {{2}}=plan
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = 'notificacion_cambio_plan',
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = '[""nombre"",""plan""]',
                    ""HsmStatus""        = 'Pendiente'
                WHERE ""Tipo"" = 'CAMBIO_PLAN' AND ""Activa"" = TRUE;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas"" SET
                    ""MetaTemplateName"" = NULL,
                    ""MetaLanguageCode"" = 'es',
                    ""MetaParamOrder""   = NULL
                WHERE ""Activa"" = TRUE;
            ");
        }
    }
}
