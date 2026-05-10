using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega ImageUrl a SupportTickets para almacenar la imagen enviada
    /// por el cliente desde WhatsApp cuando reporta un problema técnico.
    /// </summary>
    [Migration("20260504000006_AddTicketImageUrl")]
    public partial class AddTicketImageUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "SupportTickets",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "SupportTickets");
        }
    }
}
