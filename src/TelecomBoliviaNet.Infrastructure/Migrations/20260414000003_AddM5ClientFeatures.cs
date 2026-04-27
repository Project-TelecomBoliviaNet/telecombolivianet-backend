using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260414000003_AddM5ClientFeatures")]
    public partial class AddM5ClientFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── US-CLI-ADJUNTOS: tabla ClientAttachments ───────────────────────
            migrationBuilder.CreateTable(
                name: "ClientAttachments",
                columns: table => new
                {
                    Id            = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId      = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName      = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StoragePath   = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType   = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TipoDoc       = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Otro"),
                    Descripcion   = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SubidoPorId   = table.Column<Guid>(type: "uuid", nullable: false),
                    SubidoAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted     = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedById   = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_ClientAttachments", x => x.Id);
                    t.ForeignKey("FK_ClientAttachments_Clients_ClientId",
                        x => x.ClientId, "Clients", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_ClientAttachments_ClientId", "ClientAttachments", "ClientId");
            migrationBuilder.CreateIndex("IX_ClientAttachments_TipoDoc",  "ClientAttachments", "TipoDoc");

            // ── US-CLI-BUSQUEDA: índice de búsqueda full-text en Clients ──────
            // PostgreSQL tsvector para búsqueda eficiente por nombre, TBN, CI, teléfono
            migrationBuilder.Sql(@"
                ALTER TABLE ""Clients""
                ADD COLUMN IF NOT EXISTS search_vector tsvector
                GENERATED ALWAYS AS (
                    to_tsvector('simple',
                        coalesce(""FullName"", '') || ' ' ||
                        coalesce(""TbnCode"", '') || ' ' ||
                        coalesce(""IdentityCard"", '') || ' ' ||
                        coalesce(""PhoneMain"", '') || ' ' ||
                        coalesce(""PhoneSecondary"", '') || ' ' ||
                        coalesce(""Email"", '')
                    )
                ) STORED;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Clients_SearchVector
                ON ""Clients"" USING gin(search_vector);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS IX_Clients_SearchVector;");
            migrationBuilder.Sql(@"ALTER TABLE ""Clients"" DROP COLUMN IF EXISTS search_vector;");
            migrationBuilder.DropTable("ClientAttachments");
        }
    }
}
