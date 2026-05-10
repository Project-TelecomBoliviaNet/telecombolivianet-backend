using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega soporte para alertas de vencimiento del QR de empresa:
    ///  - NotifConfig seed para QR_COMPANY_EXPIRA (Inmediato=true)
    ///  - NotifPlantilla seed con el mensaje WhatsApp al admin
    /// </summary>
    [Migration("20260501000001_AddCompanyQrExpiryAlert")]
    public partial class AddCompanyQrExpiryAlert : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Config: Inmediato=true — se envía sin importar ventana horaria
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (
                    ""Id"", ""Tipo"", ""Activo"", ""DelaySegundos"",
                    ""HoraInicio"", ""HoraFin"", ""Inmediato"", ""DiasAntes"", ""ActualizadoAt""
                ) VALUES (
                    '00000000-0000-0000-0010-000000000010',
                    'QR_COMPANY_EXPIRA',
                    TRUE,
                    0,
                    '08:00:00',
                    '20:00:00',
                    TRUE,
                    NULL,
                    NOW()
                )
                ON CONFLICT DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""NotifPlantillas"" (
                    ""Id"", ""Tipo"", ""Activa"", ""Categoria"", ""HsmStatus"", ""Texto"", ""CreadoAt"", ""CreadoPorId""
                ) VALUES (
                    '00000000-0000-0000-0010-000000000011',
                    'QR_COMPANY_EXPIRA',
                    TRUE,
                    'Administracion',
                    'Aprobada',
                    E'⚠️ *Alerta QR de pago — TelecomBoliviaNet*\n\nEl QR de pago de la empresa vence en *{{dias_restantes}} día(s)* ({{fecha_expira}}).\n\nActualiza el QR desde el panel de administración para que los clientes puedan seguir pagando.\n\n👉 Panel: {{panel_url}}',
                    NOW(),
                    (SELECT ""Id"" FROM ""UserSystems"" WHERE ""Role"" = 'Admin' LIMIT 1)
                )
                ON CONFLICT DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""NotifPlantillas"" WHERE ""Id"" = '00000000-0000-0000-0010-000000000011';");
            migrationBuilder.Sql(@"DELETE FROM ""NotifConfigs""    WHERE ""Id"" = '00000000-0000-0000-0010-000000000010';");
        }
    }
}
