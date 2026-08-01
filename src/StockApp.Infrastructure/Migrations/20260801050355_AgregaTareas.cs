using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaTareas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tareas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Prioridad = table.Column<int>(type: "integer", nullable: false),
                    FechaLimite = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreadaPorUsuarioId = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TomadaPorUsuarioId = table.Column<int>(type: "integer", nullable: true),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CerradaPorUsuarioId = table.Column<int>(type: "integer", nullable: true),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tareas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tareas_Usuarios_TomadaPorUsuarioId",
                        column: x => x.TomadaPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotasTarea",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TareaId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Texto = table.Column<string>(type: "text", nullable: false),
                    EsAutomatica = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotasTarea", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotasTarea_Tareas_TareaId",
                        column: x => x.TareaId,
                        principalTable: "Tareas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotasTarea_TareaId",
                table: "NotasTarea",
                column: "TareaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_Estado",
                table: "Tareas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_TomadaPorUsuarioId",
                table: "Tareas",
                column: "TomadaPorUsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotasTarea");

            migrationBuilder.DropTable(
                name: "Tareas");
        }
    }
}
