using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    [Migration("20260427000002_AddMetaTemplateFieldsToNotifPlantillas")]
    public partial class AddMetaTemplateFieldsToNotifPlantillas : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // US-META: campos para Meta WhatsApp Business API template messages
            migrationBuilder.Sql(@"
                ALTER TABLE ""NotifPlantillas""
                    ADD COLUMN IF NOT EXISTS ""MetaTemplateName""  TEXT,
                    ADD COLUMN IF NOT EXISTS ""MetaLanguageCode""  TEXT DEFAULT 'es',
                    ADD COLUMN IF NOT EXISTS ""MetaParamOrder""    TEXT;
            ");

            // Las plantillas existentes pasan a Pendiente hasta que el admin configure
            // su nombre en Meta y Meta las apruebe.
            migrationBuilder.Sql(@"
                UPDATE ""NotifPlantillas""
                SET ""HsmStatus"" = 'Pendiente'
                WHERE ""MetaTemplateName"" IS NULL;
            ");

            // Índice para búsquedas frecuentes por nombre de template Meta
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_NotifPlantillas_MetaTemplateName""
                ON ""NotifPlantillas"" (""MetaTemplateName"")
                WHERE ""MetaTemplateName"" IS NOT NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_NotifPlantillas_MetaTemplateName"";
                ALTER TABLE ""NotifPlantillas""
                    DROP COLUMN IF EXISTS ""MetaTemplateName"",
                    DROP COLUMN IF EXISTS ""MetaLanguageCode"",
                    DROP COLUMN IF EXISTS ""MetaParamOrder"";
            ");
        }
    }
}
