using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaCorridaBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorridasBackup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IniciadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resultado = table.Column<int>(type: "integer", nullable: false),
                    NombreArchivo = table.Column<string>(type: "text", nullable: true),
                    TamanioBytes = table.Column<long>(type: "bigint", nullable: true),
                    MotivoFallo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorridasBackup", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorridasBackup_FinalizadaEn",
                table: "CorridasBackup",
                column: "FinalizadaEn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorridasBackup");
        }
    }
}
