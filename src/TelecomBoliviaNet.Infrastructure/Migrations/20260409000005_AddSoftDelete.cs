using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Fase 3 — Soft Delete:
    /// Agrega columnas IsDeleted / DeletedAt / DeletedById a Client, Invoice y Payment.
    ///
    /// El filtro global HasQueryFilter(e => !e.IsDeleted) en AppDbContext garantiza
    /// que los registros eliminados queden excluidos de todas las queries EF Core
    /// sin modificar ningún servicio. Para consultar eliminados usar .IgnoreQueryFilters().
    ///
    /// Ningún dato existente se pierde — todos los registros arrancan con IsDeleted=false.
    /// </summary>
    [Migration("20260409000005_AddSoftDelete")]
    public partial class AddSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Clients ───────────────────────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted", table: "Clients",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt", table: "Clients",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById", table: "Clients",
                type: "uuid", nullable: true);
            migrationBuilder.CreateIndex(
                name: "IX_Clients_IsDeleted", table: "Clients", column: "IsDeleted");

            // ── Invoices ──────────────────────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted", table: "Invoices",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt", table: "Invoices",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById", table: "Invoices",
                type: "uuid", nullable: true);

            // ── Payments ──────────────────────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted", table: "Payments",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt", table: "Payments",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById", table: "Payments",
                type: "uuid", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_Clients_IsDeleted", "Clients");
            migrationBuilder.DropColumn("DeletedById", "Clients");
            migrationBuilder.DropColumn("DeletedAt",   "Clients");
            migrationBuilder.DropColumn("IsDeleted",   "Clients");

            migrationBuilder.DropColumn("DeletedById", "Invoices");
            migrationBuilder.DropColumn("DeletedAt",   "Invoices");
            migrationBuilder.DropColumn("IsDeleted",   "Invoices");

            migrationBuilder.DropColumn("DeletedById", "Payments");
            migrationBuilder.DropColumn("DeletedAt",   "Payments");
            migrationBuilder.DropColumn("IsDeleted",   "Payments");
        }
    }
}
