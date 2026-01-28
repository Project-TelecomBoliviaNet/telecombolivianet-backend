using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations;

/// <inheritdoc />
[Migration("20260508000003_AddInternalAlerts")]
public partial class AddInternalAlerts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InternalAlerts",
            columns: table => new
            {
                Id        = table.Column<Guid>(type: "uuid", nullable: false),
                ForUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ForRole   = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                Type      = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Title     = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Message   = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Link      = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                IsRead    = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InternalAlerts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InternalAlerts_ForRole_IsRead",
            table: "InternalAlerts",
            columns: new[] { "ForRole", "IsRead" });

        migrationBuilder.CreateIndex(
            name: "IX_InternalAlerts_ForUserId_IsRead",
            table: "InternalAlerts",
            columns: new[] { "ForUserId", "IsRead" });

        migrationBuilder.CreateIndex(
            name: "IX_InternalAlerts_CreatedAt",
            table: "InternalAlerts",
            column: "CreatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InternalAlerts");
    }
}
