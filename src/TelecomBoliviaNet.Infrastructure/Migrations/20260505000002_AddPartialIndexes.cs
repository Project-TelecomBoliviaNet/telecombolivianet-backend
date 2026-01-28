using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// FIX-35: Índices parciales para mejorar queries críticas de alta frecuencia.
    /// - IX_Invoices_ClientId_Active: cubre "SELECT * FROM Invoices WHERE ClientId = X AND IsDeleted = false"
    ///   (patrón dominante: perfil de cliente, historial de pagos, resumen de deuda).
    /// - IX_NotifOutbox_Pending: cubre el polling del worker Python de notificaciones
    ///   "SELECT * FROM NotifOutbox WHERE Publicado = false AND EstadoFinal IS NULL"
    ///   reduciendo el seq-scan sobre la tabla de outbox.
    /// CONCURRENTLY no puede correr dentro de una transacción — suppressTransaction: true.
    /// </summary>
    [Migration("20260505000002_AddPartialIndexes")]
    public partial class AddPartialIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Invoices_ClientId_Active"
                    ON "Invoices" ("ClientId")
                    WHERE "IsDeleted" = false;
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_NotifOutbox_Pending"
                    ON "NotifOutbox" ("Publicado", "EstadoFinal")
                    WHERE "Publicado" = false AND "EstadoFinal" IS NULL;
                """,
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ""IX_Invoices_ClientId_Active"";",
                suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ""IX_NotifOutbox_Pending"";",
                suppressTransaction: true);
        }
    }
}
