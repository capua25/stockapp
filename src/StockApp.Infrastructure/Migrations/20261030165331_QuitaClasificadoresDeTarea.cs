using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuitaClasificadoresDeTarea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DESTRUCTIVA (spec 2026-10-30): borra los cuatro clasificadores cargados en las tareas,
            // que pasan al Proyecto. No se migran a proyectos (decisión de Martin). Requiere un backup
            // pg_dump verificado antes de aplicarla en el VPS. Expediente, proyecto, notas, estado y
            // trazabilidad de la tarea no se tocan, y los catálogos tampoco.
            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_DimensionesTematicas_DimensionTematicaId",
                table: "Tareas");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_OrganismosResponsables_OrganismoResponsableId",
                table: "Tareas");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_OrigenesFinanciamiento_OrigenFinanciamientoId",
                table: "Tareas");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_Zonas_ZonaId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_DimensionTematicaId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_OrganismoResponsableId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_OrigenFinanciamientoId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_ZonaId",
                table: "Tareas");

            migrationBuilder.DropColumn(
                name: "DimensionTematicaId",
                table: "Tareas");

            migrationBuilder.DropColumn(
                name: "OrganismoResponsableId",
                table: "Tareas");

            migrationBuilder.DropColumn(
                name: "OrigenFinanciamientoId",
                table: "Tareas");

            migrationBuilder.DropColumn(
                name: "ZonaId",
                table: "Tareas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recrea las columnas vacías: los valores borrados por Up solo vuelven desde el backup.
            migrationBuilder.AddColumn<int>(
                name: "DimensionTematicaId",
                table: "Tareas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganismoResponsableId",
                table: "Tareas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrigenFinanciamientoId",
                table: "Tareas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZonaId",
                table: "Tareas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_DimensionTematicaId",
                table: "Tareas",
                column: "DimensionTematicaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_OrganismoResponsableId",
                table: "Tareas",
                column: "OrganismoResponsableId");

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_OrigenFinanciamientoId",
                table: "Tareas",
                column: "OrigenFinanciamientoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_ZonaId",
                table: "Tareas",
                column: "ZonaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_DimensionesTematicas_DimensionTematicaId",
                table: "Tareas",
                column: "DimensionTematicaId",
                principalTable: "DimensionesTematicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_OrganismosResponsables_OrganismoResponsableId",
                table: "Tareas",
                column: "OrganismoResponsableId",
                principalTable: "OrganismosResponsables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_OrigenesFinanciamiento_OrigenFinanciamientoId",
                table: "Tareas",
                column: "OrigenFinanciamientoId",
                principalTable: "OrigenesFinanciamiento",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_Zonas_ZonaId",
                table: "Tareas",
                column: "ZonaId",
                principalTable: "Zonas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
