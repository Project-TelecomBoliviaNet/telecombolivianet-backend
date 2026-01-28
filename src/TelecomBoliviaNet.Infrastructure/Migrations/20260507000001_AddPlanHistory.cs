using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// FIX-H: Agrega tabla PlanHistories para registrar cada versión anterior de un plan.
    /// Se inserta un snapshot ANTES de cada UPDATE en PlanService.UpdateAsync.
    /// Permite auditar cambios de precio, velocidad y estado con el actor responsable.
    /// </summary>
    [Migration("20260507000001_AddPlanHistory")]
    public partial class AddPlanHistory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanHistories",
                columns: table => new
                {
                    Id           = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId       = table.Column<Guid>(type: "uuid", nullable: false),
                    Name         = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SpeedMb      = table.Column<int>(type: "integer", nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsActive     = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedById  = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ChangeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanHistories_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanHistories_PlanId",
                table: "PlanHistories",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanHistories_ChangedAt",
                table: "PlanHistories",
                column: "ChangedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlanHistories");
        }
    }
}
