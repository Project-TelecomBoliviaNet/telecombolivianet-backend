using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 3 improvements:
    ///   M13 — TicketCannedResponses table for quick-reply templates
    /// </summary>
    [Migration("20260508000002_Phase3Improvements")]
    public partial class Phase3Improvements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketCannedResponses",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    Title           = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body            = table.Column<string>(type: "text", nullable: false),
                    Category        = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive        = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketCannedResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketCannedResponses_UserSystems_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketCannedResponses_IsActive",
                table: "TicketCannedResponses",
                column: "IsActive");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TicketCannedResponses");
        }
    }
}
