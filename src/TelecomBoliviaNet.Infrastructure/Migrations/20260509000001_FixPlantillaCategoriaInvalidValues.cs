using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations;

/// <summary>
/// Corrige valores inválidos en la columna Categoria de NotifPlantillas.
/// Cualquier valor que no sea Cobro|Bienvenida|Tecnico|Ticket|General
/// se normaliza a 'General'.
/// </summary>
[Migration("20260509000001_FixPlantillaCategoriaInvalidValues")]
public partial class FixPlantillaCategoriaInvalidValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            UPDATE ""NotifPlantillas""
            SET ""Categoria"" = 'General'
            WHERE ""Categoria"" NOT IN ('Cobro','Bienvenida','Tecnico','Ticket','General');
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No se puede revertir la normalización de valores inválidos.
    }
}
