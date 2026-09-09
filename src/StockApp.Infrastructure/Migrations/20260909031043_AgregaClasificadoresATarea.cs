using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaClasificadoresATarea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DimensionTematicaId",
                table: "Tareas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentoAdministrativoId",
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
                name: "IX_Tareas_DocumentoAdministrativoId",
                table: "Tareas",
                column: "DocumentoAdministrativoId");

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
                name: "FK_Tareas_DocumentosAdministrativos_DocumentoAdministrativoId",
                table: "Tareas",
                column: "DocumentoAdministrativoId",
                principalTable: "DocumentosAdministrativos",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_DimensionesTematicas_DimensionTematicaId",
                table: "Tareas");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_DocumentosAdministrativos_DocumentoAdministrativoId",
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
                name: "IX_Tareas_DocumentoAdministrativoId",
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
                name: "DocumentoAdministrativoId",
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
    }
}
