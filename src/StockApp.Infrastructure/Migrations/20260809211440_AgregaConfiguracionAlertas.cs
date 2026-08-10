using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaConfiguracionAlertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesAlertas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    UrlWebhook = table.Column<string>(type: "text", nullable: true),
                    Habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadoPorUsuarioId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesAlertas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracionesAlertas_Usuarios_ActualizadoPorUsuarioId",
                        column: x => x.ActualizadoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesAlertas_ActualizadoPorUsuarioId",
                table: "ConfiguracionesAlertas",
                column: "ActualizadoPorUsuarioId");

            // NOW() a secas, NO "NOW() AT TIME ZONE 'utc'" (fix del review final): la columna es
            // timestamptz, así que el resultado de la resta de zona se REINTERPRETA con el
            // TimeZone de la sesión -- en un servidor UTC-3 la fila quedaba sembrada con un
            // instante 3 horas en el FUTURO. NOW() ya devuelve timestamptz correcto.
            migrationBuilder.Sql(
                """
                INSERT INTO "ConfiguracionesAlertas" ("Id", "UrlWebhook", "Habilitado", "ActualizadoEn", "ActualizadoPorUsuarioId")
                VALUES (1, NULL, FALSE, NOW(), NULL)
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesAlertas");
        }
    }
}
