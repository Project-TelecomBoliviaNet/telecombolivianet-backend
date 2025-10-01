using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20250103000000_AddSupportTickets")]
    public partial class AddSupportTickets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id               = table.Column<Guid>(nullable: false),
                    ClientId         = table.Column<Guid>(nullable: false),
                    Type             = table.Column<string>(maxLength: 30, nullable: false),
                    Priority         = table.Column<string>(maxLength: 15, nullable: false),
                    Status           = table.Column<string>(maxLength: 15, nullable: false),
                    Origin           = table.Column<string>(maxLength: 15, nullable: false),
                    Description      = table.Column<string>(nullable: false),
                    AssignedToUserId = table.Column<Guid>(nullable: true),
                    CreatedByUserId  = table.Column<Guid>(nullable: false),
                    CreatedAt        = table.Column<DateTime>(nullable: false),
                    DueDate          = table.Column<DateTime>(nullable: true),
                    ResolvedAt       = table.Column<DateTime>(nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_SupportTickets", x => x.Id);
                    t.ForeignKey("FK_SupportTickets_Clients_ClientId",
                        x => x.ClientId, "Clients", "Id",
                        onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_SupportTickets_UserSystems_AssignedToUserId",
                        x => x.AssignedToUserId, "UserSystems", "Id",
                        onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_SupportTickets_UserSystems_CreatedByUserId",
                        x => x.CreatedByUserId, "UserSystems", "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_SupportTickets_ClientId",    "SupportTickets", "ClientId");
            migrationBuilder.CreateIndex("IX_SupportTickets_Status",      "SupportTickets", "Status");
            migrationBuilder.CreateIndex("IX_SupportTickets_CreatedAt",   "SupportTickets", "CreatedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DropTable("SupportTickets");
    }
}
