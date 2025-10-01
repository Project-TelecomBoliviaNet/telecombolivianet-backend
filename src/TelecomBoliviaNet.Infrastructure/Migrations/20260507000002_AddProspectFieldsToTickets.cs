using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260507000002_AddProspectFieldsToTickets")]
    public partial class AddProspectFieldsToTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Make ClientId nullable (prospect tickets have no client)
            migrationBuilder.AlterColumn<Guid>(
                name: "ClientId",
                table: "SupportTickets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "ProspectName",
                table: "SupportTickets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProspectPhone",
                table: "SupportTickets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProspectName",  table: "SupportTickets");
            migrationBuilder.DropColumn(name: "ProspectPhone", table: "SupportTickets");

            migrationBuilder.AlterColumn<Guid>(
                name: "ClientId",
                table: "SupportTickets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
