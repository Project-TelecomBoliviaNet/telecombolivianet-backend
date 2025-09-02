using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega PlanName y PlanPrice a la tabla Invoices como snapshot histórico.
    /// Permite saber a qué precio y con qué nombre de plan se emitió cada factura,
    /// incluso si el plan fue modificado o renombrado posteriormente.
    /// </summary>
    [Migration("20260428000001_AddInvoicePlanSnapshot")]
    public partial class AddInvoicePlanSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanName",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlanPrice",
                table: "Invoices",
                type: "numeric",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PlanName",  table: "Invoices");
            migrationBuilder.DropColumn(name: "PlanPrice", table: "Invoices");
        }
    }
}
