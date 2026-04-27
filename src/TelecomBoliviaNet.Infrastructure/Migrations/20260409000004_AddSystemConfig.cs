using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Fase 3 — AdminSettings en BD:
    /// Crea tabla SystemConfig(Key, Value) para persistir configuración de runtime
    /// sin dependencia del filesystem del contenedor.
    /// Elimina la limitación de appsettings.runtime.json en entornos multi-réplica.
    /// </summary>
    [Migration("20260409000004_AddSystemConfig")]
    public partial class AddSystemConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemConfigs",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "uuid", nullable: false),
                    Key         = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value       = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSecret    = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UpdatedAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigs", x => x.Id);
                });

            // Índice único en Key — la búsqueda siempre es por clave
            migrationBuilder.CreateIndex(
                name:   "IX_SystemConfigs_Key",
                table:  "SystemConfigs",
                column: "Key",
                unique: true);

            // Datos iniciales con valores por defecto (no sobreescriben .env)
            var seed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table:   "SystemConfigs",
                columns: new[] { "Id", "Key", "Value", "Description", "IsSecret", "UpdatedAt" },
                values: new object[,]
                {
                    { Guid.Parse("00000000-0000-0000-0010-000000000001"), "WhatsApp:Token",         "",     "Bearer token de la Meta WhatsApp Business API",                true,  seed },
                    { Guid.Parse("00000000-0000-0000-0010-000000000002"), "WhatsApp:PhoneNumberId", "",     "ID del número de teléfono de negocio en Meta",                 false, seed },
                    { Guid.Parse("00000000-0000-0000-0010-000000000003"), "WhatsApp:ApiVersion",    "v18.0","Versión de la API de WhatsApp Business (ej: v18.0)",           false, seed },
                    { Guid.Parse("00000000-0000-0000-0010-000000000004"), "SlaAlert:HorasAnticipacion","4", "Horas de anticipación para alertas SLA (1-24)",               false, seed },
                    { Guid.Parse("00000000-0000-0000-0010-000000000005"), "SlaAlert:HoraInicioLaboral","7", "Hora de inicio del horario laboral (0-23)",                   false, seed },
                    { Guid.Parse("00000000-0000-0000-0010-000000000006"), "SlaAlert:HoraFinLaboral", "22",  "Hora de fin del horario laboral (1-23)",                      false, seed },
                    { Guid.Parse("00000000-0000-0000-0010-000000000007"), "Security:MaxFailedLoginAttempts","5","Intentos fallidos de login antes de bloqueo (3-10)",      false, seed },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("SystemConfigs");
        }
    }
}
