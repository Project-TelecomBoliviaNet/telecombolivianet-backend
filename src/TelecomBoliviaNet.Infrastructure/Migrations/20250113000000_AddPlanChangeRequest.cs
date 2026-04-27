using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20250113000000_AddPlanChangeRequest")]
    public partial class AddPlanChangeRequest : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanChangeRequests",
                columns: table => new
                {
                    Id             = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId       = table.Column<Guid>(type: "uuid", nullable: false),
                    OldPlanId      = table.Column<Guid>(type: "uuid", nullable: false),
                    NewPlanId      = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId       = table.Column<Guid>(type: "uuid", nullable: true),
                    Status         = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false, defaultValue: "Pendiente"),
                    EffectiveDate  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MidMonthChange = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RejectionReason= table.Column<string>(type: "text", nullable: true),
                    Notes          = table.Column<string>(type: "text", nullable: true),
                    RequestedAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedById  = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedById  = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanChangeRequests", x => x.Id);
                    table.ForeignKey("FK_PlanChangeRequests_Clients_ClientId",
                        x => x.ClientId, "Clients", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_PlanChangeRequests_Plans_OldPlanId",
                        x => x.OldPlanId, "Plans", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_PlanChangeRequests_Plans_NewPlanId",
                        x => x.NewPlanId, "Plans", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_PlanChangeRequests_SupportTickets_TicketId",
                        x => x.TicketId, "SupportTickets", "Id", onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_PlanChangeRequests_ClientId_Status",
                "PlanChangeRequests", new[] { "ClientId", "Status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("PlanChangeRequests");
        }
    }
}
