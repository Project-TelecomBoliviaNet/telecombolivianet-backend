using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// US-07: Horarios comerciales configurables en BD.
    /// Reemplaza BusinessHoursOptions (appsettings.json) por entidades persistidas.
    /// </summary>
    [Migration("20260504000001_AddBusinessSchedule")]
    public partial class AddBusinessSchedule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── BusinessSchedules ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "BusinessSchedules",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    Name            = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TimeZoneName    = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: false),
                    UtcOffsetHours  = table.Column<int>(type: "integer", nullable: false),
                    WorkingDaysMask = table.Column<int>(type: "integer", nullable: false),
                    StartHour       = table.Column<int>(type: "integer", nullable: false),
                    EndHour         = table.Column<int>(type: "integer", nullable: false),
                    Is24x7          = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault       = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive        = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: t => t.PrimaryKey("PK_BusinessSchedules", x => x.Id));

            migrationBuilder.CreateIndex("IX_BusinessSchedules_IsDefault", "BusinessSchedules", "IsDefault");
            migrationBuilder.CreateIndex("IX_BusinessSchedules_IsActive",  "BusinessSchedules", "IsActive");

            // ── ScheduleHolidays ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ScheduleHolidays",
                columns: table => new
                {
                    Id                 = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date               = table.Column<DateOnly>(type: "date", nullable: false),
                    Name               = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_ScheduleHolidays", x => x.Id);
                    t.ForeignKey(
                        "FK_ScheduleHolidays_BusinessSchedules_BusinessScheduleId",
                        x => x.BusinessScheduleId, "BusinessSchedules", "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                "IX_ScheduleHolidays_BusinessScheduleId_Date",
                "ScheduleHolidays",
                new[] { "BusinessScheduleId", "Date" },
                unique: true);

            // ── SlaPlan: FK opcional a BusinessSchedule ──────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "BusinessScheduleId",
                table: "SlaPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                "IX_SlaPlans_BusinessScheduleId",
                "SlaPlans",
                "BusinessScheduleId");

            migrationBuilder.AddForeignKey(
                name: "FK_SlaPlans_BusinessSchedules_BusinessScheduleId",
                table: "SlaPlans",
                column: "BusinessScheduleId",
                principalTable: "BusinessSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Seed: horario laboral Bolivia por defecto ─────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""BusinessSchedules"" (""Id"",""Name"",""TimeZoneName"",""UtcOffsetHours"",""WorkingDaysMask"",""StartHour"",""EndHour"",""Is24x7"",""IsDefault"",""IsActive"",""CreatedAt"") VALUES
                ('00000000-0000-0000-0007-000000000001','Horario Laboral Bolivia','America/La_Paz',-4,31,8,18,false,true,true,'2025-01-01 00:00:00Z')
                ON CONFLICT DO NOTHING;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_SlaPlans_BusinessSchedules_BusinessScheduleId", "SlaPlans");
            migrationBuilder.DropIndex("IX_SlaPlans_BusinessScheduleId", "SlaPlans");
            migrationBuilder.DropColumn("BusinessScheduleId", "SlaPlans");

            migrationBuilder.DropTable("ScheduleHolidays");
            migrationBuilder.DropTable("BusinessSchedules");
        }
    }
}
