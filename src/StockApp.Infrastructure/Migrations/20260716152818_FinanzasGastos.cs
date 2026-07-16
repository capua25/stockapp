using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanzasGastos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GastoId",
                table: "MovimientosStock",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Gastos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProveedorId = table.Column<int>(type: "integer", nullable: false),
                    NumeroFactura = table.Column<string>(type: "text", nullable: true),
                    NumeroOrden = table.Column<string>(type: "text", nullable: true),
                    Detalle = table.Column<string>(type: "text", nullable: false),
                    Destino = table.Column<string>(type: "text", nullable: true),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FuenteFinanciamientoId = table.Column<int>(type: "integer", nullable: false),
                    RubroGastoId = table.Column<int>(type: "integer", nullable: false),
                    LineaPoaId = table.Column<int>(type: "integer", nullable: true),
                    CondicionPago = table.Column<int>(type: "integer", nullable: false),
                    FechaVencimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gastos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gastos_FuentesFinanciamiento_FuenteFinanciamientoId",
                        column: x => x.FuenteFinanciamientoId,
                        principalTable: "FuentesFinanciamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gastos_LineasPoa_LineaPoaId",
                        column: x => x.LineaPoaId,
                        principalTable: "LineasPoa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gastos_Proveedores_ProveedorId",
                        column: x => x.ProveedorId,
                        principalTable: "Proveedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gastos_RubrosGasto_RubroGastoId",
                        column: x => x.RubroGastoId,
                        principalTable: "RubrosGasto",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IngresosCaja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Concepto = table.Column<string>(type: "text", nullable: false),
                    FuenteFinanciamientoId = table.Column<int>(type: "integer", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngresosCaja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngresosCaja_FuentesFinanciamiento_FuenteFinanciamientoId",
                        column: x => x.FuenteFinanciamientoId,
                        principalTable: "FuentesFinanciamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PagosGasto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GastoId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Nota = table.Column<string>(type: "text", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagosGasto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagosGasto_Gastos_GastoId",
                        column: x => x.GastoId,
                        principalTable: "Gastos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosStock_GastoId",
                table: "MovimientosStock",
                column: "GastoId");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_Fecha",
                table: "Gastos",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_FuenteFinanciamientoId",
                table: "Gastos",
                column: "FuenteFinanciamientoId");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_LineaPoaId",
                table: "Gastos",
                column: "LineaPoaId");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_ProveedorId_NumeroFactura",
                table: "Gastos",
                columns: new[] { "ProveedorId", "NumeroFactura" });

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_RubroGastoId",
                table: "Gastos",
                column: "RubroGastoId");

            migrationBuilder.CreateIndex(
                name: "IX_IngresosCaja_Fecha",
                table: "IngresosCaja",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_IngresosCaja_FuenteFinanciamientoId",
                table: "IngresosCaja",
                column: "FuenteFinanciamientoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosGasto_GastoId",
                table: "PagosGasto",
                column: "GastoId");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimientosStock_Gastos_GastoId",
                table: "MovimientosStock",
                column: "GastoId",
                principalTable: "Gastos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimientosStock_Gastos_GastoId",
                table: "MovimientosStock");

            migrationBuilder.DropTable(
                name: "IngresosCaja");

            migrationBuilder.DropTable(
                name: "PagosGasto");

            migrationBuilder.DropTable(
                name: "Gastos");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosStock_GastoId",
                table: "MovimientosStock");

            migrationBuilder.DropColumn(
                name: "GastoId",
                table: "MovimientosStock");
        }
    }
}
