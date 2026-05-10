using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// FIX-34: Índice funcional sobre DATE("DueDate" AT TIME ZONE 'America/La_Paz')
    ///         para optimizar las queries del notifier que buscan facturas por fecha de vencimiento
    ///         en hora boliviana sin hacer un seq-scan sobre la tabla completa.
    /// FIX-35: Índices faltantes en Invoice (DueDate) y NotifOutbox (ClienteId).
    /// </summary>
    [Migration("20260505000001_AddMissingIndexes")]
    public partial class AddMissingIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX-35: índice simple en DueDate — IF NOT EXISTS por si fue creado fuera de EF
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Invoices_DueDate"
                    ON "Invoices" ("DueDate");
                """);

            // FIX-34: índice funcional para el notifier Python que consulta
            //   DATE("DueDate" AT TIME ZONE 'America/La_Paz') = $fecha_bo
            // IF NOT EXISTS porque puede haberse creado manualmente antes de esta migración.
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Invoices_DueDate_BO"
                    ON "Invoices" (DATE("DueDate" AT TIME ZONE 'America/La_Paz'))
                    WHERE "IsDeleted" = false;
                """);

            // FIX-35: índice en NotifOutbox.ClienteId — IF NOT EXISTS por si fue creado fuera de EF
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_NotifOutbox_ClienteId"
                    ON "NotifOutbox" ("ClienteId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Invoices_DueDate"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Invoices_DueDate_BO"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_NotifOutbox_ClienteId"";");
        }
    }
}
