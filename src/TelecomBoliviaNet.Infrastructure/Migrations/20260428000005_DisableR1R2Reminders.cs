using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Deshabilita RECORDATORIO_R1 y RECORDATORIO_R2.
    /// Estos tipos buscan facturas en estado Pendiente exactamente N días antes del vencimiento,
    /// pero el job mensual crea facturas como Emitida (nunca pasan por Pendiente automáticamente),
    /// por lo que el job no los dispara nunca en la práctica.
    /// Las notificaciones de deuda vencida las cubre FACTURA_VENCIDA (día 7 automático).
    /// </summary>
    [Migration("20260428000005_DisableR1R2Reminders")]
    public partial class DisableR1R2Reminders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""NotifConfigs""
                SET ""Activo"" = FALSE, ""ActualizadoAt"" = NOW()
                WHERE ""Tipo"" IN ('RECORDATORIO_R1', 'RECORDATORIO_R2');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""NotifConfigs""
                SET ""Activo"" = TRUE, ""ActualizadoAt"" = NOW()
                WHERE ""Tipo"" IN ('RECORDATORIO_R1', 'RECORDATORIO_R2');
            ");
        }
    }
}
