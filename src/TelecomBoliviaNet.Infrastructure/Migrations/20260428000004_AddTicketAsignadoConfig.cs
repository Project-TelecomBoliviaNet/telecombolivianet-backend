using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Inserta el config y la plantilla de TICKET_ASIGNADO que faltaban en el seed inicial.
    /// El trigger notifica al técnico cuando se le asigna o reasigna un ticket.
    /// </summary>
    [Migration("20260428000004_AddTicketAsignadoConfig")]
    public partial class AddTicketAsignadoConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ""NotifConfigs"" (
                    ""Id"", ""Tipo"", ""Activo"", ""DelaySegundos"",
                    ""HoraInicio"", ""HoraFin"", ""Inmediato"", ""DiasAntes"", ""ActualizadoAt""
                ) VALUES (
                    '00000000-0000-0000-0008-000000000008',
                    'TICKET_ASIGNADO',
                    TRUE,
                    0,
                    '08:00:00',
                    '20:00:00',
                    FALSE,
                    NULL,
                    NOW()
                )
                ON CONFLICT DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""NotifPlantillas"" (
                    ""Id"", ""Tipo"", ""Activa"", ""Categoria"", ""HsmStatus"", ""Texto"", ""CreadoAt"", ""CreadoPorId""
                ) VALUES (
                    '00000000-0000-0000-0009-000000000008',
                    'TICKET_ASIGNADO',
                    TRUE,
                    'Ticket',
                    'Aprobada',
                    E'Hola {{tecnico}},\n\nSe te ha asignado el ticket *{{num_ticket}}* del cliente {{nombre_completo}}.\n\nFecha de visita: *{{fecha_visita}}*\n\n*TelecomBoliviaNet*',
                    NOW(),
                    (SELECT ""Id"" FROM ""UserSystems"" WHERE ""Role"" = 'Admin' LIMIT 1)
                )
                ON CONFLICT DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""NotifPlantillas"" WHERE ""Id"" = '00000000-0000-0000-0009-000000000008';");
            migrationBuilder.Sql(@"DELETE FROM ""NotifConfigs""    WHERE ""Id"" = '00000000-0000-0000-0008-000000000008';");
        }
    }
}
