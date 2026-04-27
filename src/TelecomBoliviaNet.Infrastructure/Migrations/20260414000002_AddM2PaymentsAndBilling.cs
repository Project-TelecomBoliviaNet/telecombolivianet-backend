using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260414000002_AddM2PaymentsAndBilling")]
    public partial class AddM2PaymentsAndBilling : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── US-PAG-CREDITO: CreditBalance en Clients ──────────────────────
            migrationBuilder.AddColumn<decimal>(
                name: "CreditBalance",
                table: "Clients",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // ── US-CLI-01: Email en Clients ────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Email",
                table: "Clients",
                column: "Email",
                unique: false,
                filter: "\"Email\" IS NOT NULL");

            // ── US-FAC-CORRELATIVO: InvoiceNumber en Invoices ─────────────────
            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true,
                filter: "\"InvoiceNumber\" IS NOT NULL");

            // ── US-FAC-CREDITO + US-FAC-ESTADOS ───────────────────────────────
            migrationBuilder.AddColumn<decimal>(
                name: "CreditApplied",
                table: "Invoices",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "Invoices",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // ── US-FAC-02: Facturas extraordinarias ────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsExtraordinary",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExtraordinaryReason",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // ── US-FAC-CORRELATIVO: tabla InvoiceSequences ─────────────────────
            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                columns: table => new
                {
                    Id        = table.Column<int>(type: "integer", nullable: false),
                    Year      = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                },
                constraints: t => t.PrimaryKey("PK_InvoiceSequences", x => x.Id));

            // ── US-PAG-RECIBO: tabla ReceiptSequences ─────────────────────────
            migrationBuilder.CreateTable(
                name: "ReceiptSequences",
                columns: table => new
                {
                    Id        = table.Column<int>(type: "integer", nullable: false),
                    Year      = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                },
                constraints: t => t.PrimaryKey("PK_ReceiptSequences", x => x.Id));

            // ── US-PAG-CAJA: tabla CashCloses ─────────────────────────────────
            migrationBuilder.CreateTable(
                name: "CashCloses",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId          = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt        = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalAmount     = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DetailJson      = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    PagosValidados  = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PagosRechazados = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PdfPath         = table.Column<string>(type: "character varying(500)", nullable: true),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_CashCloses", x => x.Id);
                    t.ForeignKey("FK_CashCloses_UserSystems_UserId", x => x.UserId, "UserSystems", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_CashCloses_UserId_ClosedAt", "CashCloses", new[] { "UserId", "ClosedAt" });

            // ── US-PAG-RECIBO: tabla PaymentReceipts ──────────────────────────
            migrationBuilder.CreateTable(
                name: "PaymentReceipts",
                columns: table => new
                {
                    Id                = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber     = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentId         = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId          = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount            = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Method            = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Bank              = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaidAt            = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvoiceNumbers    = table.Column<string>(type: "character varying(500)", nullable: true),
                    PdfPath           = table.Column<string>(type: "character varying(500)", nullable: false),
                    GeneratedAt       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentByWhatsApp    = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SentAt            = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: t => t.PrimaryKey("PK_PaymentReceipts", x => x.Id));

            migrationBuilder.CreateIndex("IX_PaymentReceipts_ReceiptNumber", "PaymentReceipts", "ReceiptNumber", unique: true);
            migrationBuilder.CreateIndex("IX_PaymentReceipts_PaymentId",     "PaymentReceipts", "PaymentId",     unique: true);

            // ── Seed secuencias ────────────────────────────────────────────────
            var year = DateTime.UtcNow.Year;
            migrationBuilder.InsertData("InvoiceSequences", new[] { "Id", "Year", "LastValue" }, new object[] { 1, year, 0 });
            migrationBuilder.InsertData("ReceiptSequences", new[] { "Id", "Year", "LastValue" }, new object[] { 1, year, 0 });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("PaymentReceipts");
            migrationBuilder.DropTable("CashCloses");
            migrationBuilder.DropTable("ReceiptSequences");
            migrationBuilder.DropTable("InvoiceSequences");
            migrationBuilder.DropColumn("ExtraordinaryReason", "Invoices");
            migrationBuilder.DropColumn("IsExtraordinary",     "Invoices");
            migrationBuilder.DropColumn("AmountPaid",          "Invoices");
            migrationBuilder.DropColumn("CreditApplied",       "Invoices");
            migrationBuilder.DropIndex("IX_Invoices_InvoiceNumber", "Invoices");
            migrationBuilder.DropColumn("InvoiceNumber",       "Invoices");
            migrationBuilder.DropIndex("IX_Clients_Email",     "Clients");
            migrationBuilder.DropColumn("Email",               "Clients");
            migrationBuilder.DropColumn("CreditBalance",       "Clients");
        }
    }
}
