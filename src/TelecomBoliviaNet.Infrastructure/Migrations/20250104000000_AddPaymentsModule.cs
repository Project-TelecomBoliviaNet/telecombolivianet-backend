using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20250104000000_AddPaymentsModule")]
    public partial class AddPaymentsModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppReceipts",
                columns: table => new
                {
                    Id             = table.Column<Guid>(nullable: false),
                    ClientId       = table.Column<Guid>(nullable: false),
                    ImageUrl       = table.Column<string>(nullable: false),
                    MessageText    = table.Column<string>(nullable: true),
                    DeclaredAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Status         = table.Column<string>(maxLength: 20, nullable: false),
                    ReceivedAt     = table.Column<DateTime>(nullable: false),
                    ProcessedAt    = table.Column<DateTime>(nullable: true),
                    PaymentId      = table.Column<Guid>(nullable: true),
                    RejectionNote  = table.Column<string>(nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_WhatsAppReceipts", x => x.Id);
                    t.ForeignKey("FK_WhatsAppReceipts_Clients_ClientId",
                        x => x.ClientId, "Clients", "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Campos de anulación en Payment (US-31)
            migrationBuilder.AddColumn<bool>("IsVoided",          "Payments", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>("VoidJustification", "Payments", nullable: true);
            migrationBuilder.AddColumn<DateTime>("VoidedAt",        "Payments", nullable: true);
            migrationBuilder.AddColumn<Guid>("VoidedByUserId",      "Payments", nullable: true);

            migrationBuilder.CreateIndex(
                "IX_WhatsAppReceipts_Status",     "WhatsAppReceipts", "Status");
            migrationBuilder.CreateIndex(
                "IX_WhatsAppReceipts_ClientId",   "WhatsAppReceipts", "ClientId");
            migrationBuilder.CreateIndex(
                "IX_WhatsAppReceipts_ReceivedAt", "WhatsAppReceipts", "ReceivedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DropTable("WhatsAppReceipts");
    }
}
