using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaLotesImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LotesImportacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    // UsuarioId/Ejercicio nullable (review adversarial, BLOQUEANTE 1+2): un lote
                    // reconstruido por el backfill de AgregaFksLotesImportacion, para un Guid sin
                    // LogAuditoria (Accion=ImportacionPlanillas) de origen, no tiene de dónde
                    // derivar ni el autor ni el ejercicio -- se prefiere preservar el lote con
                    // esos datos en null antes que perder el vínculo (nulear IdImportacion en los
                    // hijos) o fallar la migración.
                    UsuarioId = table.Column<int>(type: "integer", nullable: true),
                    Ejercicio = table.Column<int>(type: "integer", nullable: true),
                    RevertidaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevertidaPorUsuarioId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesImportacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotesImportacion_Usuarios_RevertidaPorUsuarioId",
                        column: x => x.RevertidaPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LotesImportacion_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LotesImportacion_Ejercicio",
                table: "LotesImportacion",
                column: "Ejercicio");

            migrationBuilder.CreateIndex(
                name: "IX_LotesImportacion_RevertidaPorUsuarioId",
                table: "LotesImportacion",
                column: "RevertidaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_LotesImportacion_UsuarioId",
                table: "LotesImportacion",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotesImportacion");
        }
    }
}
