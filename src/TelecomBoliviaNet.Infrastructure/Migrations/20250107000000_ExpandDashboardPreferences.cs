using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations;

/// <summary>
/// Agrega las columnas ShowTickets, ShowWhatsApp, ShowZonas y ShowMetodosPago
/// a la tabla DashboardPreferences (expandión del Módulo 8 Dashboard).
/// </summary>
[Migration("20250107000000_ExpandDashboardPreferences")]
public partial class ExpandDashboardPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ShowTickets",
            table: "DashboardPreferences",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowWhatsApp",
            table: "DashboardPreferences",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowZonas",
            table: "DashboardPreferences",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "ShowMetodosPago",
            table: "DashboardPreferences",
            type: "boolean",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShowTickets",     table: "DashboardPreferences");
        migrationBuilder.DropColumn(name: "ShowWhatsApp",    table: "DashboardPreferences");
        migrationBuilder.DropColumn(name: "ShowZonas",       table: "DashboardPreferences");
        migrationBuilder.DropColumn(name: "ShowMetodosPago", table: "DashboardPreferences");
    }
}
