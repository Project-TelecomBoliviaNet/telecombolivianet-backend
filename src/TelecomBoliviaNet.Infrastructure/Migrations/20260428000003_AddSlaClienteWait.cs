using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega soporte para el estado PendienteCliente y la pausa de SLA.
    /// - SlaPausedAt: momento en que el ticket entró en estado PendienteCliente.
    /// - SlaTotalPausedMinutes: minutos acumulados en pausa (se suma en cada ciclo).
    /// El enum TicketStatus se almacena como integer en PostgreSQL (EF Core default),
    /// por lo que agregar PendienteCliente al final (valor 4) no requiere ALTER TYPE.
    /// </summary>
    [Migration("20260428000003_AddSlaClienteWait")]
    public partial class AddSlaClienteWait : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SlaPausedAt",
                table: "SupportTickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaTotalPausedMinutes",
                table: "SupportTickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SlaPausedAt",           table: "SupportTickets");
            migrationBuilder.DropColumn(name: "SlaTotalPausedMinutes", table: "SupportTickets");
        }
    }
}
