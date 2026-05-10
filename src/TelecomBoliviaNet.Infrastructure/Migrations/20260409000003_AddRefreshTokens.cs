using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Fase 2 — Seguridad: tabla RefreshTokens para soporte de renovación JWT.
    /// Permite sesiones largas sin re-login, con rotación automática de tokens.
    /// </summary>
    [Migration("20260409000003_AddRefreshTokens")]
    public partial class AddRefreshTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id                  = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash           = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId              = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt           = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt           = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByIp         = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt           = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_UserSystems_UserId",
                        column: x => x.UserId,
                        principalTable: "UserSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Índice en TokenHash — búsqueda directa al recibir el refresh token
            migrationBuilder.CreateIndex(
                name:   "IX_RefreshTokens_TokenHash",
                table:  "RefreshTokens",
                column: "TokenHash",
                unique: true);

            // Índice en UserId + ExpiresAt — limpieza de tokens expirados por usuario
            migrationBuilder.CreateIndex(
                name:    "IX_RefreshTokens_UserId_ExpiresAt",
                table:   "RefreshTokens",
                columns: new[] { "UserId", "ExpiresAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("RefreshTokens");
        }
    }
}
