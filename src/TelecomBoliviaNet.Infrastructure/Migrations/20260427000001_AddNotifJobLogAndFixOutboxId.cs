using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260427000001_AddNotifJobLogAndFixOutboxId")]
    public partial class AddNotifJobLogAndFixOutboxId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Tabla NotifJobLog — persistencia de última ejecución de jobs ──
            // Requerida por ReminderJob para evitar re-ejecución doble el mismo día.
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""NotifJobLog"" (
                    ""JobName""  TEXT        NOT NULL,
                    ""LastRun""  TIMESTAMPTZ NOT NULL,
                    CONSTRAINT ""PK_NotifJobLog"" PRIMARY KEY (""JobName"")
                );
            ");

            // ── 2. NotifLog.OutboxId → nullable ────────────────────────────────
            // Registros OMITIDO (sin teléfono, anti-spam) no tienen outbox asociado.
            // Antes se usaba Guid.Empty como workaround; ahora es NULL correctamente.
            migrationBuilder.AlterColumn<Guid>(
                name:       "OutboxId",
                table:      "NotifLogs",
                type:       "uuid",
                nullable:   true,
                oldNullable: false,
                oldDefaultValue: null);

            // ── 3. Migrar Guid.Empty → NULL en registros existentes ─────────────
            migrationBuilder.Sql(@"
                UPDATE ""NotifLogs""
                SET ""OutboxId"" = NULL
                WHERE ""OutboxId"" = '00000000-0000-0000-0000-000000000000';
            ");

            // ── 4. Índice en NotifOutbox para acelerar consultas del worker ──────
            // El worker consulta frecuentemente por EstadoFinal IS NULL + Publicado + EnviarDesde
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_NotifOutbox_Worker""
                ON ""NotifOutbox"" (""EstadoFinal"", ""Publicado"", ""EnviarDesde"")
                WHERE ""EstadoFinal"" IS NULL;
            ");

            // ── 5. Índice en NotifLogs para anti-spam (consulta por cliente+tipo+estado+fecha) ──
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_NotifLogs_AntiSpam""
                ON ""NotifLogs"" (""ClienteId"", ""Tipo"", ""Estado"", ""RegistradoAt"")
                WHERE ""Estado"" = 'ENVIADO';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""NotifJobLog"";");

            migrationBuilder.AlterColumn<Guid>(
                name:       "OutboxId",
                table:      "NotifLogs",
                type:       "uuid",
                nullable:   false,
                oldNullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""NotifLogs""
                SET ""OutboxId"" = '00000000-0000-0000-0000-000000000000'
                WHERE ""OutboxId"" IS NULL;
            ");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_NotifOutbox_Worker"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_NotifLogs_AntiSpam"";");
        }
    }
}
