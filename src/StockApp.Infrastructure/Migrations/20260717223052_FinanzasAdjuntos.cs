using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanzasAdjuntos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Adjuntos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NombreArchivo = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    GastoId = table.Column<int>(type: "integer", nullable: true),
                    PagoGastoId = table.Column<int>(type: "integer", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FechaAltaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjuntos", x => x.Id);
                    table.CheckConstraint("CK_Adjuntos_GastoOPago", "(\"GastoId\" IS NOT NULL AND \"PagoGastoId\" IS NULL) OR (\"GastoId\" IS NULL AND \"PagoGastoId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Adjuntos_Gastos_GastoId",
                        column: x => x.GastoId,
                        principalTable: "Gastos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Adjuntos_PagosGasto_PagoGastoId",
                        column: x => x.PagoGastoId,
                        principalTable: "PagosGasto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdjuntosContenido",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Contenido = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjuntosContenido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdjuntosContenido_Adjuntos_Id",
                        column: x => x.Id,
                        principalTable: "Adjuntos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_GastoId",
                table: "Adjuntos",
                column: "GastoId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_PagoGastoId",
                table: "Adjuntos",
                column: "PagoGastoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdjuntosContenido");

            migrationBuilder.DropTable(
                name: "Adjuntos");
        }
    }
}
