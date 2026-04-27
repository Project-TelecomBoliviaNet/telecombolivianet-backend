using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20250109000000_AddNotificationsModule")]
    public partial class AddNotificationsModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── NotifConfigs ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotifConfigs",
                columns: table => new
                {
                    Id               = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo             = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Activo           = table.Column<bool>(nullable: false, defaultValue: true),
                    DelaySegundos    = table.Column<int>(nullable: false, defaultValue: 0),
                    HoraInicio       = table.Column<TimeOnly>(type: "time", nullable: false),
                    HoraFin          = table.Column<TimeOnly>(type: "time", nullable: false),
                    Inmediato        = table.Column<bool>(nullable: false, defaultValue: false),
                    DiasAntes        = table.Column<int>(nullable: true),
                    ActualizadoAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: t => t.PrimaryKey("PK_NotifConfigs", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_NotifConfigs_Tipo",
                table: "NotifConfigs",
                column: "Tipo",
                unique: true);

            // ── NotifPlantillas ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotifPlantillas",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo        = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Texto       = table.Column<string>(nullable: false),
                    Activa      = table.Column<bool>(nullable: false, defaultValue: true),
                    CreadoAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: t => t.PrimaryKey("PK_NotifPlantillas", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_NotifPlantillas_Tipo_Activa",
                table: "NotifPlantillas",
                columns: new[] { "Tipo", "Activa" });

            // ── NotifPlantillaHistorial ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotifPlantillaHistorial",
                columns: table => new
                {
                    Id             = table.Column<Guid>(type: "uuid", nullable: false),
                    PlantillaId    = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo           = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Texto          = table.Column<string>(nullable: false),
                    ArchivadoAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: t => t.PrimaryKey("PK_NotifPlantillaHistorial", x => x.Id));

            // ── NotifOutbox ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotifOutbox",
                columns: table => new
                {
                    Id             = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo           = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ClienteId      = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber    = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PlantillaId    = table.Column<Guid>(type: "uuid", nullable: true),
                    Publicado      = table.Column<bool>(nullable: false, defaultValue: false),
                    Intentos       = table.Column<int>(nullable: false, defaultValue: 0),
                    EnviarDesde    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProximoIntento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstadoFinal    = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    CreadoAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcesadoAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContextoJson   = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    ReferenciaId   = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_NotifOutbox", x => x.Id);
                    t.ForeignKey(
                        name: "FK_NotifOutbox_Clients_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotifOutbox_EstadoFinal_Publicado_EnviarDesde",
                table: "NotifOutbox",
                columns: new[] { "EstadoFinal", "Publicado", "EnviarDesde" });

            // ── NotifLogs ─────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NotifLogs",
                columns: table => new
                {
                    Id           = table.Column<Guid>(type: "uuid", nullable: false),
                    OutboxId     = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId    = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo         = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PhoneNumber  = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Mensaje      = table.Column<string>(nullable: false),
                    Estado       = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    IntentoNum   = table.Column<int>(nullable: false),
                    ErrorDetalle = table.Column<string>(nullable: true),
                    RegistradoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_NotifLogs", x => x.Id);
                    t.ForeignKey(
                        name: "FK_NotifLogs_Clients_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotifLogs_ClienteId_RegistradoAt",
                table: "NotifLogs",
                columns: new[] { "ClienteId", "RegistradoAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotifLogs_ClienteId_Tipo_RegistradoAt",
                table: "NotifLogs",
                columns: new[] { "ClienteId", "Tipo", "RegistradoAt" });

            // ── Seeds: NotifConfig ────────────────────────────────────────────
            var seed    = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var adminId = new Guid("00000000-0000-0000-0000-000000000001");

            // Tipos sin DiasAntes (columna omitida → queda NULL por defecto)
            migrationBuilder.InsertData(
                table: "NotifConfigs",
                columns: new[] { "Id", "Tipo", "Activo", "DelaySegundos", "HoraInicio", "HoraFin", "Inmediato", "ActualizadoAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0008-000000000001"), "SUSPENSION",        true, 0,    new TimeOnly(8,0), new TimeOnly(20,0), false, seed },
                    { new Guid("00000000-0000-0000-0008-000000000002"), "REACTIVACION",      true, 0,    new TimeOnly(8,0), new TimeOnly(20,0), false, seed },
                    { new Guid("00000000-0000-0000-0008-000000000006"), "FACTURA_VENCIDA",   true, 3600, new TimeOnly(8,0), new TimeOnly(20,0), false, seed },
                    { new Guid("00000000-0000-0000-0008-000000000007"), "CONFIRMACION_PAGO", true, 0,    new TimeOnly(8,0), new TimeOnly(20,0), true,  seed },
                });

            // Tipos con DiasAntes
            migrationBuilder.InsertData(
                table: "NotifConfigs",
                columns: new[] { "Id", "Tipo", "Activo", "DelaySegundos", "HoraInicio", "HoraFin", "Inmediato", "DiasAntes", "ActualizadoAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0008-000000000003"), "RECORDATORIO_R1", true, 0, new TimeOnly(8,0), new TimeOnly(20,0), false, 5, seed },
                    { new Guid("00000000-0000-0000-0008-000000000004"), "RECORDATORIO_R2", true, 0, new TimeOnly(8,0), new TimeOnly(20,0), false, 3, seed },
                    { new Guid("00000000-0000-0000-0008-000000000005"), "RECORDATORIO_R3", true, 0, new TimeOnly(8,0), new TimeOnly(20,0), false, 1, seed },
                });

            // ── Seeds: NotifPlantillas ────────────────────────────────────────
            migrationBuilder.InsertData(
                table: "NotifPlantillas",
                columns: new[] { "Id", "Tipo", "Activa", "CreadoAt", "CreadoPorId", "Texto" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0009-000000000001"), "SUSPENSION",        true, seed, adminId, "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *suspendido* por falta de pago.\nComuníquese con nosotros para regularizar su cuenta.\n\n*TelecomBoliviaNet*" },
                    { new Guid("00000000-0000-0000-0009-000000000002"), "REACTIVACION",      true, seed, adminId, "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *reactivado* exitosamente. ¡Ya puede usarlo con normalidad!\n\n*TelecomBoliviaNet*" },
                    { new Guid("00000000-0000-0000-0009-000000000003"), "RECORDATORIO_R1",   true, seed, adminId, "Estimado/a {{nombre}},\n\nLe recordamos que tiene una factura de *Bs. {{monto}}* con vencimiento el *{{fecha_vencimiento}}* ({{meses_pendientes}} mes(es) pendiente(s)).\n\nEvite inconvenientes pagando antes del vencimiento.\n\n*TelecomBoliviaNet*" },
                    { new Guid("00000000-0000-0000-0009-000000000004"), "RECORDATORIO_R2",   true, seed, adminId, "Estimado/a {{nombre}},\n\n⚠️ Su factura de *Bs. {{monto}}* vence el *{{fecha_vencimiento}}*. Evite la suspensión del servicio pagando a tiempo.\n\n*TelecomBoliviaNet*" },
                    { new Guid("00000000-0000-0000-0009-000000000005"), "RECORDATORIO_R3",   true, seed, adminId, "Estimado/a {{nombre}},\n\n🚨 Su factura vence *mañana* ({{fecha_vencimiento}}). Monto: *Bs. {{monto}}*.\nRealice su pago hoy para no perder el servicio.\n\n*TelecomBoliviaNet*" },
                    { new Guid("00000000-0000-0000-0009-000000000006"), "FACTURA_VENCIDA",   true, seed, adminId, "Estimado/a {{nombre}},\n\nSu factura del periodo *{{periodo}}* por *Bs. {{monto}}* está *vencida*.\nPor favor regularice su pago para evitar la suspensión del servicio.\n\n*TelecomBoliviaNet*" },
                    { new Guid("00000000-0000-0000-0009-000000000007"), "CONFIRMACION_PAGO", true, seed, adminId, "Estimado/a {{nombre}},\n\n✅ Hemos registrado su pago de *Bs. {{monto}}* correspondiente al periodo *{{periodo}}*.\n\nGracias por su pago puntual. *TelecomBoliviaNet*" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("NotifLogs");
            migrationBuilder.DropTable("NotifOutbox");
            migrationBuilder.DropTable("NotifPlantillaHistorial");
            migrationBuilder.DropTable("NotifPlantillas");
            migrationBuilder.DropTable("NotifConfigs");
        }
    }
}
