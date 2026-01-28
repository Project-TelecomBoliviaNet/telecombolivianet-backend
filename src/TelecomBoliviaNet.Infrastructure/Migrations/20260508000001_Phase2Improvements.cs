using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 2 improvements:
    ///   M06 — VisitStatus column on TicketVisits + EnVisita value (enum addition, no column needed)
    ///   M08 — VISITA_PROGRAMADA NotifConfig + NotifPlantilla seed rows
    /// </summary>
    [Migration("20260508000001_Phase2Improvements")]
    public partial class Phase2Improvements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── M06: Status column on TicketVisits ───────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TicketVisits",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ── M08: NotifConfig + NotifPlantilla for VISITA_PROGRAMADA ────────
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (""Id"",""Tipo"",""Activo"",""DelaySegundos"",""HoraInicio"",""HoraFin"",""Inmediato"",""ActualizadoAt"") VALUES
                ('00000000-0000-0000-0008-000000000013','VISITA_PROGRAMADA',true,0,'07:00:00','21:00:00',true,'2025-01-01 00:00:00Z')
                ON CONFLICT DO NOTHING;
                INSERT INTO ""NotifPlantillas"" (""Id"",""Tipo"",""Activa"",""CreadoAt"",""CreadoPorId"",""Texto"",""Categoria"") VALUES
                ('00000000-0000-0000-0009-000000000013','VISITA_PROGRAMADA',true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','👷 *Visita técnica programada* — Ticket {{num_ticket}} | Hola {{cliente}}, un técnico visitará su domicilio el *{{fecha}}*. Técnico: {{tecnico}}.','Tecnico')
                ON CONFLICT (""Id"") DO UPDATE SET ""Categoria"" = 'Tecnico' WHERE ""NotifPlantillas"".""Categoria"" NOT IN ('Cobro','Bienvenida','Tecnico','Ticket','General');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Status", table: "TicketVisits");


            migrationBuilder.DeleteData(
                table: "NotifPlantillas",
                keyColumn: "Id",
                keyValue: Guid.Parse("00000000-0000-0000-0009-000000000013"));

            migrationBuilder.DeleteData(
                table: "NotifConfigs",
                keyColumn: "Id",
                keyValue: Guid.Parse("00000000-0000-0000-0008-000000000013"));
        }
    }
}
