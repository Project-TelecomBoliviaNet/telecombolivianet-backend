using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20250106000000_AddTicketFullModule")]
    public partial class AddTicketFullModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Nuevas columnas en SupportTickets ─────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Subject", table: "SupportTickets",
                type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupportGroup", table: "SupportTickets",
                type: "character varying(100)", maxLength: 100, nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstRespondedAt", table: "SupportTickets",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt", table: "SupportTickets",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionMessage", table: "SupportTickets",
                type: "text", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RootCause", table: "SupportTickets",
                type: "text", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CsatScore", table: "SupportTickets",
                type: "integer", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CsatRespondedAt", table: "SupportTickets",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SlaCompliant", table: "SupportTickets",
                type: "boolean", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaAlertSentAt", table: "SupportTickets",
                type: "timestamp with time zone", nullable: true);

            // ── SlaPlan ───────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "SlaPlans",
                columns: table => new
                {
                    Id                   = table.Column<Guid>(type: "uuid", nullable: false),
                    Name                 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Priority             = table.Column<string>(type: "character varying(15)",  maxLength: 15,  nullable: false),
                    FirstResponseMinutes = table.Column<int>(type: "integer", nullable: false),
                    ResolutionMinutes    = table.Column<int>(type: "integer", nullable: false),
                    Schedule             = table.Column<string>(type: "character varying(20)",  maxLength: 20,  nullable: false),
                    IsActive             = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_SlaPlans", x => x.Id));

            migrationBuilder.CreateIndex("IX_SlaPlans_Priority", "SlaPlans", "Priority", unique: true);

            migrationBuilder.InsertData("SlaPlans",
                new[] { "Id","Name","Priority","FirstResponseMinutes","ResolutionMinutes","Schedule","IsActive","CreatedAt" },
                new object[,]
                {
                    { Guid.Parse("00000000-0000-0000-0002-000000000001"), "SLA Crítico", "Critica", 15,  240,  "Veinticuatro7", true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) },
                    { Guid.Parse("00000000-0000-0000-0002-000000000002"), "SLA Alto",    "Alta",    30,  480,  "Veinticuatro7", true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) },
                    { Guid.Parse("00000000-0000-0000-0002-000000000003"), "SLA Medio",   "Media",   120, 1440, "Laboral",        true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) },
                    { Guid.Parse("00000000-0000-0000-0002-000000000004"), "SLA Bajo",    "Baja",    240, 4320, "Laboral",        true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) }
                });

            // ── TicketNotifications ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TicketNotifications",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId    = table.Column<Guid>(type: "uuid", nullable: false),
                    Type        = table.Column<string>(type: "character varying(30)",  maxLength: 30,  nullable: false),
                    Status      = table.Column<string>(type: "character varying(10)",  maxLength: 10,  nullable: false),
                    Recipient   = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message     = table.Column<string>(type: "text", nullable: false),
                    ErrorDetail = table.Column<string>(type: "text", nullable: true),
                    SentAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketNotifications", x => x.Id);
                    table.ForeignKey("FK_TicketNotifications_SupportTickets_TicketId",
                        x => x.TicketId, "SupportTickets", "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex("IX_TicketNotifications_TicketId", "TicketNotifications", "TicketId");

            // ── TicketComments ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TicketComments",
                columns: table => new
                {
                    Id        = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId  = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId  = table.Column<Guid>(type: "uuid", nullable: false),
                    Type      = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Body      = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketComments", x => x.Id);
                    table.ForeignKey("FK_TicketComments_SupportTickets_TicketId",
                        x => x.TicketId, "SupportTickets", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_TicketComments_UserSystems_AuthorId",
                        x => x.AuthorId, "UserSystems", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_TicketComments_TicketId", "TicketComments", "TicketId");

            // ── TicketWorkLogs ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TicketWorkLogs",
                columns: table => new
                {
                    Id       = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId   = table.Column<Guid>(type: "uuid", nullable: false),
                    Minutes  = table.Column<int>(type: "integer", nullable: false),
                    Notes    = table.Column<string>(type: "text", nullable: true),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketWorkLogs", x => x.Id);
                    table.ForeignKey("FK_TicketWorkLogs_SupportTickets_TicketId",
                        x => x.TicketId, "SupportTickets", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_TicketWorkLogs_UserSystems_UserId",
                        x => x.UserId, "UserSystems", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_TicketWorkLogs_TicketId", "TicketWorkLogs", "TicketId");

            // ── TicketVisits ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TicketVisits",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId        = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TechnicianId    = table.Column<Guid>(type: "uuid", nullable: true),
                    Observations    = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketVisits", x => x.Id);
                    table.ForeignKey("FK_TicketVisits_SupportTickets_TicketId",
                        x => x.TicketId, "SupportTickets", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_TicketVisits_UserSystems_TechnicianId",
                        x => x.TechnicianId, "UserSystems", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_TicketVisits_TicketId", "TicketVisits", "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("TicketComments");
            migrationBuilder.DropTable("TicketWorkLogs");
            migrationBuilder.DropTable("TicketVisits");
            migrationBuilder.DropTable("TicketNotifications");
            migrationBuilder.DropTable("SlaPlans");

            foreach (var col in new[]
                { "Subject","SupportGroup","FirstRespondedAt","ClosedAt","ResolutionMessage",
                  "RootCause","CsatScore","CsatRespondedAt","SlaCompliant","SlaAlertSentAt" })
                migrationBuilder.DropColumn(col, "SupportTickets");
        }
    }
}
