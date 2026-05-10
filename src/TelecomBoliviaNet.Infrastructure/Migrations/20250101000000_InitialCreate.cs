using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20250101000000_InitialCreate")]
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id           = table.Column<Guid>(nullable: false),
                    UserId       = table.Column<Guid>(nullable: true),
                    UserName     = table.Column<string>(nullable: false, defaultValue: "Sistema"),
                    Module       = table.Column<string>(maxLength: 50,  nullable: false),
                    Action       = table.Column<string>(maxLength: 50,  nullable: false),
                    Description  = table.Column<string>(nullable: false),
                    IpAddress    = table.Column<string>(nullable: true),
                    PreviousData = table.Column<string>(nullable: true),
                    NewData      = table.Column<string>(nullable: true),
                    CreatedAt    = table.Column<DateTime>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));

            migrationBuilder.CreateTable(
                name: "TokenBlacklists",
                columns: table => new
                {
                    Id            = table.Column<Guid>(nullable: false),
                    Token         = table.Column<string>(nullable: false),
                    UserId        = table.Column<Guid>(nullable: false),
                    InvalidatedAt = table.Column<DateTime>(nullable: false),
                    ExpiresAt     = table.Column<DateTime>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_TokenBlacklists", x => x.Id));

            migrationBuilder.CreateTable(
                name: "UserSystems",
                columns: table => new
                {
                    Id                     = table.Column<Guid>(nullable: false),
                    FullName               = table.Column<string>(maxLength: 150, nullable: false),
                    Email                  = table.Column<string>(maxLength: 200, nullable: false),
                    PasswordHash           = table.Column<string>(nullable: false),
                    Role                   = table.Column<string>(maxLength: 30,  nullable: false),
                    Status                 = table.Column<string>(maxLength: 20,  nullable: false),
                    RequiresPasswordChange = table.Column<bool>(nullable: false),
                    FailedLoginAttempts    = table.Column<int>(nullable: false),
                    LastLoginAt            = table.Column<DateTime>(nullable: true),
                    CreatedAt              = table.Column<DateTime>(nullable: false),
                    UpdatedAt              = table.Column<DateTime>(nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_UserSystems", x => x.Id));

            // Índices
            migrationBuilder.CreateIndex("IX_AuditLogs_UserId",  "AuditLogs",  "UserId");
            migrationBuilder.CreateIndex("IX_AuditLogs_CreatedAt","AuditLogs", "CreatedAt");
            migrationBuilder.CreateIndex("IX_AuditLogs_Action",  "AuditLogs",  "Action");
            migrationBuilder.CreateIndex("IX_TokenBlacklists_Token",    "TokenBlacklists", "Token");
            migrationBuilder.CreateIndex("IX_TokenBlacklists_ExpiresAt","TokenBlacklists", "ExpiresAt");
            migrationBuilder.CreateIndex("IX_UserSystems_Email", "UserSystems", "Email", unique: true);

            // Seed handled by Program.cs startup seed (idempotent)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("AuditLogs");
            migrationBuilder.DropTable("TokenBlacklists");
            migrationBuilder.DropTable("UserSystems");
        }
    }
}
