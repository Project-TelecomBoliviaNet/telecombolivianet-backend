using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20250102000000_AddClientsModule")]
    public partial class AddClientsModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Plans ─────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id           = table.Column<Guid>(nullable: false),
                    Name         = table.Column<string>(maxLength: 100, nullable: false),
                    SpeedMb      = table.Column<int>(nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsActive     = table.Column<bool>(nullable: false, defaultValue: true),
                    CreatedAt    = table.Column<DateTime>(nullable: false),
                    UpdatedAt    = table.Column<DateTime>(nullable: true)
                },
                constraints: t => t.PrimaryKey("PK_Plans", x => x.Id));

            // ── TbnSequences ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "TbnSequences",
                columns: table => new
                {
                    Id        = table.Column<int>(nullable: false),
                    LastValue = table.Column<int>(nullable: false, defaultValue: 0),
                    Prefix    = table.Column<string>(maxLength: 10, nullable: false, defaultValue: "TBN")
                },
                constraints: t => t.PrimaryKey("PK_TbnSequences", x => x.Id));

            // ── Clients ───────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id               = table.Column<Guid>(nullable: false),
                    TbnCode          = table.Column<string>(maxLength: 20, nullable: false),
                    FullName         = table.Column<string>(maxLength: 150, nullable: false),
                    IdentityCard     = table.Column<string>(maxLength: 20, nullable: false),
                    PhoneMain        = table.Column<string>(maxLength: 20, nullable: false),
                    PhoneSecondary   = table.Column<string>(maxLength: 20, nullable: true),
                    Zone             = table.Column<string>(maxLength: 100, nullable: false),
                    Street           = table.Column<string>(nullable: true),
                    LocationRef      = table.Column<string>(nullable: true),
                    GpsLatitude      = table.Column<decimal>(type: "decimal(10,7)", nullable: true),
                    GpsLongitude     = table.Column<decimal>(type: "decimal(10,7)", nullable: true),
                    WinboxNumber     = table.Column<string>(maxLength: 50, nullable: false),
                    InstallationDate = table.Column<DateTime>(nullable: false),
                    InstalledByUserId = table.Column<Guid>(nullable: false),
                    PlanId           = table.Column<Guid>(nullable: false),
                    HasTvCable       = table.Column<bool>(nullable: false),
                    OnuSerialNumber  = table.Column<string>(nullable: true),
                    Status           = table.Column<string>(maxLength: 20, nullable: false),
                    SuspendedAt      = table.Column<DateTime>(nullable: true),
                    CancelledAt      = table.Column<DateTime>(nullable: true),
                    CreatedAt        = table.Column<DateTime>(nullable: false),
                    UpdatedAt        = table.Column<DateTime>(nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_Clients", x => x.Id);
                    t.ForeignKey("FK_Clients_Plans_PlanId",
                        x => x.PlanId, "Plans", "Id",
                        onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_Clients_UserSystems_InstalledByUserId",
                        x => x.InstalledByUserId, "UserSystems", "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_Clients_TbnCode",      "Clients", "TbnCode", unique: true);
            migrationBuilder.CreateIndex("IX_Clients_IdentityCard",  "Clients", "IdentityCard", unique: true);
            migrationBuilder.CreateIndex("IX_Clients_PhoneMain",     "Clients", "PhoneMain");
            migrationBuilder.CreateIndex("IX_Clients_PlanId",        "Clients", "PlanId");
            migrationBuilder.CreateIndex("IX_Clients_InstalledByUserId", "Clients", "InstalledByUserId");

            // ── Invoices ──────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id        = table.Column<Guid>(nullable: false),
                    ClientId  = table.Column<Guid>(nullable: false),
                    Type      = table.Column<string>(maxLength: 20, nullable: false),
                    Status    = table.Column<string>(maxLength: 20, nullable: false),
                    Year      = table.Column<int>(nullable: false),
                    Month     = table.Column<int>(nullable: false),
                    Amount    = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IssuedAt  = table.Column<DateTime>(nullable: false),
                    DueDate   = table.Column<DateTime>(nullable: false),
                    Notes     = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_Invoices", x => x.Id);
                    t.ForeignKey("FK_Invoices_Clients_ClientId",
                        x => x.ClientId, "Clients", "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_Invoices_ClientId_Year_Month_Type",
                "Invoices",
                new[] { "ClientId", "Year", "Month", "Type" },
                unique: true);

            // ── Payments ──────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id                    = table.Column<Guid>(nullable: false),
                    ClientId              = table.Column<Guid>(nullable: false),
                    Amount                = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Method                = table.Column<string>(maxLength: 30, nullable: false),
                    Bank                  = table.Column<string>(nullable: true),
                    ReceiptImageUrl       = table.Column<string>(nullable: true),
                    PhysicalReceiptNumber = table.Column<string>(nullable: true),
                    PaidAt                = table.Column<DateTime>(nullable: false),
                    RegisteredAt          = table.Column<DateTime>(nullable: false),
                    RegisteredByUserId    = table.Column<Guid>(nullable: false),
                    FromWhatsApp          = table.Column<bool>(nullable: false)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_Payments", x => x.Id);
                    t.ForeignKey("FK_Payments_Clients_ClientId",
                        x => x.ClientId, "Clients", "Id",
                        onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_Payments_UserSystems_RegisteredByUserId",
                        x => x.RegisteredByUserId, "UserSystems", "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_Payments_ClientId", "Payments", "ClientId");

            // ── PaymentInvoices ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PaymentInvoices",
                columns: table => new
                {
                    Id        = table.Column<Guid>(nullable: false),
                    PaymentId = table.Column<Guid>(nullable: false),
                    InvoiceId = table.Column<Guid>(nullable: false)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_PaymentInvoices", x => x.Id);
                    t.UniqueConstraint("UQ_PaymentInvoices_PaymentId_InvoiceId", x => new { x.PaymentId, x.InvoiceId });
                    t.ForeignKey("FK_PaymentInvoices_Payments_PaymentId",
                        x => x.PaymentId, "Payments", "Id",
                        onDelete: ReferentialAction.Cascade);
                    t.ForeignKey("FK_PaymentInvoices_Invoices_InvoiceId",
                        x => x.InvoiceId, "Invoices", "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Seeds ─────────────────────────────────────────────────────────
            migrationBuilder.InsertData("TbnSequences",
                new[] { "Id", "LastValue", "Prefix" },
                new object[] { 1, 0, "TBN" });

            migrationBuilder.InsertData("Plans",
                new[] { "Id", "Name", "SpeedMb", "MonthlyPrice", "IsActive", "CreatedAt" },
                new object[,]
                {
                    { Guid.Parse("00000000-0000-0000-0001-000000000001"), "Plan Cobre",  30, 99.00m,  true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) },
                    { Guid.Parse("00000000-0000-0000-0001-000000000002"), "Plan Plata",  50, 149.00m, true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) },
                    { Guid.Parse("00000000-0000-0000-0001-000000000003"), "Plan Oro",    80, 199.00m, true, new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc) }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("PaymentInvoices");
            migrationBuilder.DropTable("Payments");
            migrationBuilder.DropTable("Invoices");
            migrationBuilder.DropTable("Clients");
            migrationBuilder.DropTable("TbnSequences");
            migrationBuilder.DropTable("Plans");
        }
    }
}
