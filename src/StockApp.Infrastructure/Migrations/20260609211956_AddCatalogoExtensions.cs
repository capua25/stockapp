using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogoExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "UnidadesMedida",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Proveedores",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Categorias",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnidadesMedida_Abreviatura",
                table: "UnidadesMedida",
                column: "Abreviatura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnidadesMedida_Nombre",
                table: "UnidadesMedida",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Proveedores_Nombre",
                table: "Proveedores",
                column: "Nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UnidadesMedida_Abreviatura",
                table: "UnidadesMedida");

            migrationBuilder.DropIndex(
                name: "IX_UnidadesMedida_Nombre",
                table: "UnidadesMedida");

            migrationBuilder.DropIndex(
                name: "IX_Proveedores_Nombre",
                table: "Proveedores");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "UnidadesMedida");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Proveedores");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Categorias");
        }
    }
}
