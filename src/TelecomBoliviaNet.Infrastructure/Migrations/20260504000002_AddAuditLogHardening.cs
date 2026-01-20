using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// US-10: Audit log hardening.
    /// Agrega UserAgent (navegador/cliente HTTP) e IntegrityHash (SHA-256 anti-tampering).
    /// </summary>
    [Migration("20260504000002_AddAuditLogHardening")]
    public partial class AddAuditLogHardening : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "AuditLogs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrityHash",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("UserAgent",      "AuditLogs");
            migrationBuilder.DropColumn("IntegrityHash",  "AuditLogs");
        }
    }
}
