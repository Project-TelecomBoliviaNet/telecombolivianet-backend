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

            // ── Seeds: NotifConfig + NotifPlantillas ──────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (""Id"",""Tipo"",""Activo"",""DelaySegundos"",""HoraInicio"",""HoraFin"",""Inmediato"",""ActualizadoAt"") VALUES
                ('00000000-0000-0000-0008-000000000001','SUSPENSION',        true,0,   '08:00:00','20:00:00',false,'2025-01-01 00:00:00Z'),
                ('00000000-0000-0000-0008-000000000002','REACTIVACION',      true,0,   '08:00:00','20:00:00',false,'2025-01-01 00:00:00Z'),
                ('00000000-0000-0000-0008-000000000006','FACTURA_VENCIDA',   true,3600,'08:00:00','20:00:00',false,'2025-01-01 00:00:00Z'),
                ('00000000-0000-0000-0008-000000000007','CONFIRMACION_PAGO', true,0,   '08:00:00','20:00:00',true, '2025-01-01 00:00:00Z'),
                ('00000000-0000-0000-0008-000000000003','RECORDATORIO_R1',   true,0,   '08:00:00','20:00:00',false,'2025-01-01 00:00:00Z'),
                ('00000000-0000-0000-0008-000000000004','RECORDATORIO_R2',   true,0,   '08:00:00','20:00:00',false,'2025-01-01 00:00:00Z'),
                ('00000000-0000-0000-0008-000000000005','RECORDATORIO_R3',   true,0,   '08:00:00','20:00:00',false,'2025-01-01 00:00:00Z')
                ON CONFLICT DO NOTHING;
                UPDATE ""NotifConfigs"" SET ""DiasAntes""=5 WHERE ""Id""='00000000-0000-0000-0008-000000000003';
                UPDATE ""NotifConfigs"" SET ""DiasAntes""=3 WHERE ""Id""='00000000-0000-0000-0008-000000000004';
                UPDATE ""NotifConfigs"" SET ""DiasAntes""=1 WHERE ""Id""='00000000-0000-0000-0008-000000000005';

                INSERT INTO ""NotifPlantillas"" (""Id"",""Tipo"",""Activa"",""CreadoAt"",""CreadoPorId"",""Texto"") VALUES
                ('00000000-0000-0000-0009-000000000001','SUSPENSION',       true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, su servicio *{{plan}}* ha sido *suspendido* por falta de pago. *TelecomBoliviaNet*'),
                ('00000000-0000-0000-0009-000000000002','REACTIVACION',     true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, su servicio *{{plan}}* ha sido *reactivado* exitosamente. *TelecomBoliviaNet*'),
                ('00000000-0000-0000-0009-000000000003','RECORDATORIO_R1',  true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, tiene una factura de *Bs. {{monto}}* con vencimiento el *{{fecha_vencimiento}}*. *TelecomBoliviaNet*'),
                ('00000000-0000-0000-0009-000000000004','RECORDATORIO_R2',  true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, su factura de *Bs. {{monto}}* vence el *{{fecha_vencimiento}}*. *TelecomBoliviaNet*'),
                ('00000000-0000-0000-0009-000000000005','RECORDATORIO_R3',  true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, su factura vence mañana ({{fecha_vencimiento}}). Monto: *Bs. {{monto}}*. *TelecomBoliviaNet*'),
                ('00000000-0000-0000-0009-000000000006','FACTURA_VENCIDA',  true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, su factura del periodo *{{periodo}}* por *Bs. {{monto}}* está vencida. *TelecomBoliviaNet*'),
                ('00000000-0000-0000-0009-000000000007','CONFIRMACION_PAGO',true,'2025-01-01 00:00:00Z','00000000-0000-0000-0000-000000000001','Estimado/a {{nombre}}, hemos registrado su pago de *Bs. {{monto}}* del periodo *{{periodo}}*. *TelecomBoliviaNet*')
                ON CONFLICT DO NOTHING;");
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
