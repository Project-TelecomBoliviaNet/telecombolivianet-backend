using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260414000001_AddNotifM1Features")]
    public partial class AddNotifM1Features : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── US-NOT-03: Agregar Categoria y HsmStatus a NotifPlantillas ────
            migrationBuilder.AddColumn<string>(
                name: "Categoria",
                table: "NotifPlantillas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "HsmStatus",
                table: "NotifPlantillas",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "Aprobada");

            // ── US-NOT-04: Agregar PlantillaId a NotifConfigs ─────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "PlantillaId",
                table: "NotifConfigs",
                type: "uuid",
                nullable: true);

            // ── US-NOT-04: Nuevos tipos de trigger en seed ────────────────────
            // Los nuevos tipos (TICKET_CREADO, TICKET_RESUELTO, CAMBIO_PLAN)
            // se insertan via seed en AppDbContext.

            // ── US-NOT-02: Tabla NotifSegments ────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotifSegments",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre          = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descripcion     = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReglasJson      = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    CreadoAt        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreadoPorId     = table.Column<Guid>(type: "uuid", nullable: true),
                    ActualizadoAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: t => t.PrimaryKey("PK_NotifSegments", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_NotifSegments_Nombre",
                table: "NotifSegments",
                column: "Nombre",
                unique: true);

            // ── US-NOT-ANTISPAM: Índice deduplicación en NotifLog ────────────
            // Permite consulta rápida: ¿este cliente recibió este tipo en 24h?
            // IF NOT EXISTS: AddNotificationsModule ya creó este índice; evita fallo en DB limpia.
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_NotifLogs_ClienteId_Tipo_RegistradoAt""
                ON ""NotifLogs"" (""ClienteId"", ""Tipo"", ""RegistradoAt"");");

            // ── Agregar columna Mensaje a NotifLogs si no existe ──────────────
            // (puede que ya exista según la migración 20250109)
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NotifSegments");
            migrationBuilder.DropColumn(name: "Categoria",    table: "NotifPlantillas");
            migrationBuilder.DropColumn(name: "HsmStatus",    table: "NotifPlantillas");
            migrationBuilder.DropColumn(name: "PlantillaId",  table: "NotifConfigs");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_NotifLogs_ClienteId_Tipo_RegistradoAt"";");
        }
    }
}
