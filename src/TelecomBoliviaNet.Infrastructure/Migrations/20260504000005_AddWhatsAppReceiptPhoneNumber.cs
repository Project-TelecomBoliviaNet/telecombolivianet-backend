using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260504000005_AddWhatsAppReceiptPhoneNumber")]
    public partial class AddWhatsAppReceiptPhoneNumber : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Número WhatsApp del remitente — separado de Client.PhoneMain para
            // que la notificación de confirmación llegue al número exacto que usó
            // el cliente al enviar el comprobante.
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "WhatsAppReceipts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppReceipts_PhoneNumber",
                table: "WhatsAppReceipts",
                column: "PhoneNumber",
                unique: false,
                filter: "\"PhoneNumber\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppReceipts_PhoneNumber",
                table: "WhatsAppReceipts");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "WhatsAppReceipts");
        }
    }
}
