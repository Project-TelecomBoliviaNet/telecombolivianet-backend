using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260414000004_AddM7UsersAndRoles")]
    public partial class AddM7UsersAndRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── US-USR-01: Phone en Users (ya existe en UserSystem) ────────────
            // Ya existe el campo Phone en UserSystem (agregado en iteraciones prev.)
            // Solo aseguramos el índice

            // ── US-USR-RECOVERY: tabla PasswordResetTokens ────────────────────
            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id        = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId    = table.Column<Guid>(type: "uuid", nullable: false),
                    Token     = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Used      = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentTo    = table.Column<string>(type: "character varying(200)", nullable: true),
                    Channel   = table.Column<string>(type: "character varying(20)", nullable: false, defaultValue: "WhatsApp"),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    t.ForeignKey("FK_PasswordResetTokens_UserSystems_UserId",
                        x => x.UserId, "UserSystems", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_PasswordResetTokens_Token",  "PasswordResetTokens", "Token",  unique: true);
            migrationBuilder.CreateIndex("IX_PasswordResetTokens_UserId", "PasswordResetTokens", "UserId");

            // ── US-ROL-CRUD: agregar Operador al enum en seed ─────────────────
            // El enum en .NET es int → no hay cambio de schema, solo semántico.
            // La columna "Role" en Users es character varying(20) (HasConversion<string>).
            // Nada que migrar en schema para el nuevo valor "Operador".

            // ── US-USR-01: IsDeleted en Users para baja lógica ────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UserSystems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "UserSystems",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("PasswordResetTokens");
            migrationBuilder.DropColumn("IsDeleted",  "UserSystems");
            migrationBuilder.DropColumn("DeletedAt",  "UserSystems");
        }
    }
}
