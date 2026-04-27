using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// CORRECCIÓN Bug #3: reemplaza la tabla de secuencia + SemaphoreSlim por secuencias nativas PostgreSQL.
    /// CORRECCIÓN Bug #10: agrega columna EntityId a AuditLogs (faltaba en todas las migraciones previas)
    ///                     y crea el índice requerido por ClientHistorialService / AuditLogService.
    /// </summary>
    [Migration("20260414000006_AddPostgresSequences")]
    public partial class AddPostgresSequences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Secuencias nativas PostgreSQL ────────────────────────────────────
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS invoice_number_seq START 1 INCREMENT 1;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS invoice_extra_seq START 1 INCREMENT 1;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS ticket_number_seq START 1 INCREMENT 1;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS receipt_number_seq START 1 INCREMENT 1;");

            // ── BUG FIX: columna EntityId en AuditLogs ───────────────────────────
            // La propiedad AuditLog.EntityId fue añadida al entity como "BUG FIX" pero
            // ninguna migración anterior la incluyó en el schema. Sin esta columna,
            // el CREATE INDEX de abajo falla con "column EntityId does not exist" y
            // MigrateAsync() lanza excepción — el backend crashea antes de crear tablas.
            // Usar ADD COLUMN IF NOT EXISTS para ser idempotente en BDs que ya la tengan.
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ADD COLUMN IF NOT EXISTS ""EntityId"" uuid;");

            // ── Índice para ClientHistorialService / AuditLogService ─────────────
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_AuditLogs_EntityId"
                ON "AuditLogs" ("EntityId")
                WHERE "EntityId" IS NOT NULL;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_AuditLogs_EntityId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" DROP COLUMN IF EXISTS ""EntityId"";");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS invoice_number_seq;");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS invoice_extra_seq;");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS ticket_number_seq;");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS receipt_number_seq;");
        }
    }
}
