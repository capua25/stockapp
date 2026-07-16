using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanzasMaestros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuentesFinanciamiento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuentesFinanciamiento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LineasPoa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Programa = table.Column<string>(type: "text", nullable: false),
                    Ejercicio = table.Column<int>(type: "integer", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineasPoa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RubrosGasto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<int>(type: "integer", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubrosGasto", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AsignacionesPresupuestales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LineaPoaId = table.Column<int>(type: "integer", nullable: false),
                    FuenteFinanciamientoId = table.Column<int>(type: "integer", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsignacionesPresupuestales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AsignacionesPresupuestales_FuentesFinanciamiento_FuenteFina~",
                        column: x => x.FuenteFinanciamientoId,
                        principalTable: "FuentesFinanciamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AsignacionesPresupuestales_LineasPoa_LineaPoaId",
                        column: x => x.LineaPoaId,
                        principalTable: "LineasPoa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsignacionesPresupuestales_FuenteFinanciamientoId",
                table: "AsignacionesPresupuestales",
                column: "FuenteFinanciamientoId");

            migrationBuilder.CreateIndex(
                name: "IX_AsignacionesPresupuestales_LineaPoaId_FuenteFinanciamientoId",
                table: "AsignacionesPresupuestales",
                columns: new[] { "LineaPoaId", "FuenteFinanciamientoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuentesFinanciamiento_Nombre",
                table: "FuentesFinanciamiento",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineasPoa_Nombre_Ejercicio",
                table: "LineasPoa",
                columns: new[] { "Nombre", "Ejercicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RubrosGasto_Codigo",
                table: "RubrosGasto",
                column: "Codigo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsignacionesPresupuestales");

            migrationBuilder.DropTable(
                name: "RubrosGasto");

            migrationBuilder.DropTable(
                name: "FuentesFinanciamiento");

            migrationBuilder.DropTable(
                name: "LineasPoa");
        }
    }
}
