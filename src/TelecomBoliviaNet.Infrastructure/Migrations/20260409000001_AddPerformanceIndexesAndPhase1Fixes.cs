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
            // Usa los tipos exactos que EF Core espera segun la entidad:
            // Tipo = string (HasConversion<string>), HoraInicio/HoraFin = TimeOnly
            migrationBuilder.InsertData(
                table:   "NotifConfigs",
                columns: new[]
                {
                    "Id", "Tipo", "Activo", "DelaySegundos",
                    "HoraInicio", "HoraFin", "Inmediato", "DiasAntes", "ActualizadoAt"
                },
                values: new object[]
                {
                    Guid.Parse("00000000-0000-0000-0008-000000000008"),
                    "TICKET_ASIGNADO",
                    true,
                    0,
                    new TimeOnly(8, 0),    // TimeOnly — igual que el resto de seeds en OnModelCreating
                    new TimeOnly(22, 0),
                    true,
                    (int?)null,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });

            // 5b. Seed NotifPlantilla para TICKET_ASIGNADO
            // Columnas exactas segun entidad NotifPlantilla: Id, Tipo, Texto, Activa, CreadoAt, CreadoPorId
            migrationBuilder.InsertData(
                table:   "NotifPlantillas",
                columns: new[] { "Id", "Tipo", "Texto", "Activa", "CreadoAt", "CreadoPorId" },
                values: new object[]
                {
                    Guid.Parse("00000000-0000-0000-0009-000000000008"),
                    "TICKET_ASIGNADO",
                    "{{prefijo}} — Ticket #{{ticket_id}}\n" +
                    "Asunto: {{asunto}}\n" +
                    "Cliente: {{cliente}}\n" +
                    "Prioridad: {{prioridad}}\n" +
                    "Vence: {{vence}}\n\n" +
                    "Ingresa al sistema para ver los detalles. *TelecomBoliviaNet*",
                    true,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    (Guid?)null
                });
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
