using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega configuración y plantilla para el nuevo tipo CAMBIO_PRECIO.
    /// Por defecto desactivado — el admin lo activa desde Notificaciones si lo desea.
    /// </summary>
    [Migration("20260428000002_AddCambioPrecioNotif")]
    public partial class AddCambioPrecioNotif : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Config — desactivado por defecto (no intrusivo)
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (
                    ""Id"", ""Tipo"", ""Activo"", ""DelaySegundos"",
                    ""HoraInicio"", ""HoraFin"", ""Inmediato"", ""DiasAntes"", ""ActualizadoAt""
                ) VALUES (
                    '00000000-0000-0000-0008-000000000012',
                    'CAMBIO_PRECIO',
                    FALSE,
                    0,
                    '08:00:00',
                    '20:00:00',
                    FALSE,
                    NULL,
                    NOW()
                )
                ON CONFLICT DO NOTHING;
            ");

            // Plantilla
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifPlantillas"" (
                    ""Id"", ""Tipo"", ""Activa"", ""Texto"", ""CreadoAt"", ""CreadoPorId""
                ) VALUES (
                    '00000000-0000-0000-0009-000000000012',
                    'CAMBIO_PRECIO',
                    TRUE,
                    E'Estimado/a {{nombre}},\n\nLe informamos que el precio de su plan *{{plan}}* ha sido actualizado a *Bs. {{precio_nuevo}}/mes*.\n\nEl nuevo precio se aplicará a partir de su próxima factura.\n\n*TelecomBoliviaNet*',
                    NOW(),
                    (SELECT ""Id"" FROM ""UserSystems"" WHERE ""Role"" = 'Admin' LIMIT 1)
                )
                ON CONFLICT DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""NotifPlantillas"" WHERE ""Id"" = '00000000-0000-0000-0009-000000000012';");
            migrationBuilder.Sql(@"DELETE FROM ""NotifConfigs""   WHERE ""Id"" = '00000000-0000-0000-0008-000000000012';");
        }
    }
}
