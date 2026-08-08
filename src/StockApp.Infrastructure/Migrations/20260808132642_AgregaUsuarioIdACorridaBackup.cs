using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaUsuarioIdACorridaBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UsuarioId",
                table: "CorridasBackup",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorridasBackup_UsuarioId",
                table: "CorridasBackup",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_CorridasBackup_Usuarios_UsuarioId",
                table: "CorridasBackup",
                column: "UsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CorridasBackup_Usuarios_UsuarioId",
                table: "CorridasBackup");

            migrationBuilder.DropIndex(
                name: "IX_CorridasBackup_UsuarioId",
                table: "CorridasBackup");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "CorridasBackup");
        }
    }
}
