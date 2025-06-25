using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Fase 1 — Mejoras de rendimiento y completitud arquitectural:
    ///  1. Columna Phone en UserSystems
    ///  2. Indice compuesto en NotifOutbox (Publicado, EstadoFinal, EnviarDesde)
    ///  3. Indice compuesto en Invoices (ClientId, Status)
    ///  4. Indice compuesto en SupportTickets (Status, AssignedToUserId)
    ///  5. Seed NotifConfig + NotifPlantilla para TICKET_ASIGNADO
    /// </summary>
    [Migration("20260409000001_AddPerformanceIndexesAndPhase1Fixes")]
    public partial class AddPerformanceIndexesAndPhase1Fixes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Phone en UserSystems
            migrationBuilder.AddColumn<string>(
                name:      "Phone",
                table:     "UserSystems",
                type:      "character varying(30)",
                maxLength: 30,
                nullable:  true);

            // 2. Indice NotifOutbox — hot-path del Worker Python
            migrationBuilder.CreateIndex(
                name:    "IX_NotifOutbox_Publicado_EstadoFinal_EnviarDesde",
                table:   "NotifOutbox",
                columns: new[] { "Publicado", "EstadoFinal", "EnviarDesde" });

            // 3. Indice Invoices
            migrationBuilder.CreateIndex(
                name:    "IX_Invoices_ClientId_Status",
                table:   "Invoices",
                columns: new[] { "ClientId", "Status" });

            // 4. Indice SupportTickets
            migrationBuilder.CreateIndex(
                name:    "IX_SupportTickets_Status_AssignedToUserId",
                table:   "SupportTickets",
                columns: new[] { "Status", "AssignedToUserId" });

            // 5a. Seed NotifConfig para TICKET_ASIGNADO
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (""Id"",""Tipo"",""Activo"",""DelaySegundos"",""HoraInicio"",""HoraFin"",""Inmediato"",""ActualizadoAt"") VALUES
                ('00000000-0000-0000-0008-000000000008','TICKET_ASIGNADO',true,0,'08:00:00','22:00:00',true,'2026-01-01 00:00:00Z')
                ON CONFLICT DO NOTHING;
                INSERT INTO ""NotifPlantillas"" (""Id"",""Tipo"",""Texto"",""Activa"",""CreadoAt"") VALUES
                ('00000000-0000-0000-0009-000000000008','TICKET_ASIGNADO','{{prefijo}} — Ticket #{{ticket_id}} | Asunto: {{asunto}} | Cliente: {{cliente}} | Prioridad: {{prioridad}} | Vence: {{vence}}. *TelecomBoliviaNet*',true,'2026-01-01 00:00:00Z')
                ON CONFLICT DO NOTHING;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table:     "NotifPlantillas",
                keyColumn: "Id",
                keyValue:  Guid.Parse("00000000-0000-0000-0009-000000000008"));

            migrationBuilder.DeleteData(
                table:     "NotifConfigs",
                keyColumn: "Id",
                keyValue:  Guid.Parse("00000000-0000-0000-0008-000000000008"));

            migrationBuilder.DropIndex("IX_SupportTickets_Status_AssignedToUserId", "SupportTickets");
            migrationBuilder.DropIndex("IX_Invoices_ClientId_Status",                "Invoices");
            migrationBuilder.DropIndex("IX_NotifOutbox_Publicado_EstadoFinal_EnviarDesde", "NotifOutbox");

            migrationBuilder.DropColumn(name: "Phone", table: "UserSystems");
        }
    }
}
