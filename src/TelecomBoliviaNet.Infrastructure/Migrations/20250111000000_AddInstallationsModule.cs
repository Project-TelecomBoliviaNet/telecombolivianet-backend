using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelecomBoliviaNet.Infrastructure.Migrations
{
    /// <summary>
    /// Crea la tabla Installations para el módulo de agendamiento de instalaciones.
    /// </summary>
    [Migration("20250111000000_AddInstallationsModule")]
    public partial class AddInstallationsModule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HoraInicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DuracionMin = table.Column<int>(type: "integer", nullable: false, defaultValue: 120),
                    Direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Notas = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pendiente"),
                    TecnicoId = table.Column<Guid>(type: "uuid", nullable: true),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    MotivoCancelacion = table.Column<string>(type: "text", nullable: true),
                    CanceladoPor = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CreadoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreadoPorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installations_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Installations_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Installations_UserSystems_TecnicoId",
                        column: x => x.TecnicoId,
                        principalTable: "UserSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Installations_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Installations_UserSystems_CreadoPorId",
                        column: x => x.CreadoPorId,
                        principalTable: "UserSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Índices para consultas frecuentes
            migrationBuilder.CreateIndex(
                name: "IX_Installations_Fecha_HoraInicio",
                table: "Installations",
                columns: new[] { "Fecha", "HoraInicio" });

            migrationBuilder.CreateIndex(
                name: "IX_Installations_ClientId",
                table: "Installations",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Installations_Status",
                table: "Installations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Installations_TecnicoId",
                table: "Installations",
                column: "TecnicoId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Installations");
        }
    }
}
