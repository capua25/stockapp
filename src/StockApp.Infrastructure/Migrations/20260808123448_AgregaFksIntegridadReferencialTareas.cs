using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaFksIntegridadReferencialTareas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tareas_CerradaPorUsuarioId",
                table: "Tareas",
                column: "CerradaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Tareas_CreadaPorUsuarioId",
                table: "Tareas",
                column: "CreadaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_NotasTarea_UsuarioId",
                table: "NotasTarea",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_NotasTarea_Usuarios_UsuarioId",
                table: "NotasTarea",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_Usuarios_CerradaPorUsuarioId",
                table: "Tareas",
                column: "CerradaPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tareas_Usuarios_CreadaPorUsuarioId",
                table: "Tareas",
                column: "CreadaPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotasTarea_Usuarios_UsuarioId",
                table: "NotasTarea");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_Usuarios_CerradaPorUsuarioId",
                table: "Tareas");

            migrationBuilder.DropForeignKey(
                name: "FK_Tareas_Usuarios_CreadaPorUsuarioId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_CerradaPorUsuarioId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_Tareas_CreadaPorUsuarioId",
                table: "Tareas");

            migrationBuilder.DropIndex(
                name: "IX_NotasTarea_UsuarioId",
                table: "NotasTarea");
        }
    }
}
