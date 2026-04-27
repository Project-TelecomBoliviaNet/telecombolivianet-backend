using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260414000005_AddM9TicketFeatures")]
    public partial class AddM9TicketFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── US-TKT-CORRELATIVO ────────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "TicketNumber", table: "SupportTickets",
                type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.CreateIndex(
                "IX_SupportTickets_TicketNumber", "SupportTickets",
                "TicketNumber", unique: true, filter: "\"TicketNumber\" IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "TicketSequences",
                columns: table => new
                {
                    Id        = table.Column<int>(type: "integer", nullable: false),
                    Year      = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                },
                constraints: t => t.PrimaryKey("PK_TicketSequences", x => x.Id));

            // ── US-TKT-SLA ────────────────────────────────────────────────────
            migrationBuilder.AddColumn<DateTime>(
                name: "SlaDeadline", table: "SupportTickets",
                type: "timestamp with time zone", nullable: true);

            // ── US-TKT-BALANCEO ───────────────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "AutoAssigned", table: "SupportTickets",
                type: "boolean", nullable: false, defaultValue: false);

            // ── US-TKT-ADJ ────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TicketAttachments",
                columns: table => new
                {
                    Id            = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId      = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName      = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StoragePath   = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType   = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Descripcion   = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SubidoPorId   = table.Column<Guid>(type: "uuid", nullable: false),
                    SubidoAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted     = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TicketAttachments", x => x.Id);
                    t.ForeignKey("FK_TicketAttachments_SupportTickets_TicketId",
                        x => x.TicketId, "SupportTickets", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_TicketAttachments_TicketId", "TicketAttachments", "TicketId");

            // Seed secuencia
            var year = DateTime.UtcNow.Year;
            migrationBuilder.InsertData("TicketSequences", new[] { "Id", "Year", "LastValue" }, new object[] { 1, year, 0 });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("TicketAttachments");
            migrationBuilder.DropTable("TicketSequences");
            migrationBuilder.DropIndex("IX_SupportTickets_TicketNumber", "SupportTickets");
            migrationBuilder.DropColumn("TicketNumber",  "SupportTickets");
            migrationBuilder.DropColumn("SlaDeadline",   "SupportTickets");
            migrationBuilder.DropColumn("AutoAssigned",  "SupportTickets");
        }
    }
}
