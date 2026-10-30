using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaProyectos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProyectoId",
                table: "Tareas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProyectoId",
                table: "Gastos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Proyectos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Prioridad = table.Column<int>(type: "integer", nullable: false),
                    FechaLimite = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Presupuesto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CreadoPorUsuarioId = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ZonaId = table.Column<int>(type: "integer", nullable: true),
                    DimensionTematicaId = table.Column<int>(type: "integer", nullable: true),
                    OrganismoResponsableId = table.Column<int>(type: "integer", nullable: true),
                    OrigenFinanciamientoId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proyectos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Proyectos_DimensionesTematicas_DimensionTematicaId",
                        column: x => x.DimensionTematicaId,
                        principalTable: "DimensionesTematicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Proyectos_OrganismosResponsables_OrganismoResponsableId",
                        column: x => x.OrganismoResponsableId,
                        principalTable: "OrganismosResponsables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Proyectos_OrigenesFinanciamiento_OrigenFinanciamientoId",
                        column: x => x.OrigenFinanciamientoId,
                        principalTable: "OrigenesFinanciamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Proyectos_Zonas_ZonaId",
                        column: x => x.ZonaId,
                        principalTable: "Zonas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotasProyecto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProyectoId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Texto = table.Column<string>(type: "text", nullable: false),
                    EsAutomatica = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotasProyecto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotasProyecto_Proyectos_ProyectoId",
                        column: x => x.ProyectoId,
                        principalTable: "Proyectos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotasProyecto_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_ProyectoId",
                table: "Tareas",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_ProyectoId",
                table: "Gastos",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasProyecto_ProyectoId",
                table: "NotasProyecto",
                column: "ProyectoId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasProyecto_UsuarioId",
                table: "NotasProyecto",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Proyectos_DimensionTematicaId",
                table: "Proyectos",
                column: "DimensionTematicaId");

            migrationBuilder.CreateIndex(
                name: "IX_Proyectos_Estado",
                table: "Proyectos",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Proyectos_OrganismoResponsableId",
                table: "Proyectos",
                column: "OrganismoResponsableId");

            migrationBuilder.CreateIndex(
                name: "IX_Proyectos_OrigenFinanciamientoId",
                table: "Proyectos",
                column: "OrigenFinanciamientoId");

            migrationBuilder.CreateIndex(
                name: "IX_Proyectos_ZonaId",
                table: "Proyectos",
                column: "ZonaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gastos_Proyectos_ProyectoId",
                table: "Gastos",
                column: "ProyectoId",
                principalTable: "Proyectos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_Proyectos_ProyectoId",
                table: "Tareas",
                column: "ProyectoId",
                principalTable: "Proyectos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(ProyectosPermisosBackfillSql.InsertarPermisosProyectos);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"PermisosUsuario\" WHERE \"Permiso\" IN ('proyectos.ver', 'proyectos.gestionar');");

            migrationBuilder.DropForeignKey(
                name: "FK_Gastos_Proyectos_ProyectoId",
                table: "Gastos");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_Proyectos_ProyectoId",
                table: "Tareas");

            migrationBuilder.DropTable(
                name: "NotasProyecto");

            migrationBuilder.DropTable(
                name: "Proyectos");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_ProyectoId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Gastos_ProyectoId",
                table: "Gastos");

            migrationBuilder.DropColumn(
                name: "ProyectoId",
                table: "Tareas");

            migrationBuilder.DropColumn(
                name: "ProyectoId",
                table: "Gastos");
        }
    }
}
