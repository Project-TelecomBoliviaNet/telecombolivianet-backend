using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations;

/// <summary>
/// US-D10 · Tabla de preferencias de personalización del dashboard por usuario.
/// </summary>
[Migration("20250105000000_AddDashboardPreferences")]
public partial class AddDashboardPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DashboardPreferences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ShowKpis = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                ShowTendencia = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                ShowComprobantes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                ShowDeudores = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DashboardPreferences", x => x.Id);
                table.ForeignKey(
                    name: "FK_DashboardPreferences_UserSystems_UserId",
                    column: x => x.UserId,
                    principalTable: "UserSystems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DashboardPreferences_UserId",
            table: "DashboardPreferences",
            column: "UserId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DashboardPreferences");
    }
}
