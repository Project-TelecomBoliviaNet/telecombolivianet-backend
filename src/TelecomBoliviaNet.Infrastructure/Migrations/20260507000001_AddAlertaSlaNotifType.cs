using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega el tipo ALERTA_SLA al catálogo de notificaciones.
    /// Corrige FIX-A: SlaAlertJob usaba RECORDATORIO_R1 (template de cobro)
    /// en lugar de un tipo semánticamente correcto para alertas internas al técnico.
    /// </summary>
    [Migration("20260507000001_AddAlertaSlaNotifType")]
    public partial class AddAlertaSlaNotifType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (""Id"",""Tipo"",""Activo"",""DelaySegundos"",""HoraInicio"",""HoraFin"",""Inmediato"",""ActualizadoAt"") VALUES
                ('00000000-0000-0000-0008-000000000012','ALERTA_SLA',true,0,'07:00:00','22:00:00',true,'2025-01-01 00:00:00Z')
                ON CONFLICT DO NOTHING;
                INSERT INTO ""NotifPlantillas"" (""Id"",""Tipo"",""Activa"",""CreadoAt"",""CreadoPorId"",""Texto"") VALUES
                ('00000000-0000-0000-0009-000000000012','ALERTA_SLA',true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','⚠️ *ALERTA SLA* — Ticket {{num_ticket}} | Cliente: {{cliente}} | Vence en menos de {{horas}}h ({{vence}}). Por favor atienda.')
                ON CONFLICT DO NOTHING;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "NotifPlantillas",
                keyColumn: "Id",
                keyValue: Guid.Parse("00000000-0000-0000-0009-000000000012"));

            migrationBuilder.DeleteData(
                table: "NotifConfigs",
                keyColumn: "Id",
                keyValue: Guid.Parse("00000000-0000-0000-0008-000000000012"));
        }
    }
}
