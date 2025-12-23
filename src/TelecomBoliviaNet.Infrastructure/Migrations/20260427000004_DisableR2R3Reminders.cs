using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Deshabilita RECORDATORIO_R2 y RECORDATORIO_R3.
    /// El sistema usa un único recordatorio configurable (RECORDATORIO_R1):
    /// el admin define DiasAntes y la ventana horaria desde el frontend.
    /// CONFIRMACION_PAGO permanece Inmediato = true.
    /// </summary>
    [Migration("20260427000004_DisableR2R3Reminders")]
    public partial class DisableR2R3Reminders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""NotifConfigs""
                SET ""Activo"" = FALSE
                WHERE ""Tipo"" IN ('RECORDATORIO_R2', 'RECORDATORIO_R3');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""NotifConfigs""
                SET ""Activo"" = TRUE
                WHERE ""Tipo"" IN ('RECORDATORIO_R2', 'RECORDATORIO_R3');
            ");
        }
    }
}
