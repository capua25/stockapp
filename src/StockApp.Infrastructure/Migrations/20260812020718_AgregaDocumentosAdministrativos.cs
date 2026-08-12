using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaDocumentosAdministrativos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentosAdministrativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Numero = table.Column<string>(type: "text", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    RegistradoPorUsuarioId = table.Column<int>(type: "integer", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentosAdministrativos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentosAdministrativos_Usuarios_RegistradoPorUsuarioId",
                        column: x => x.RegistradoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdjuntosDocumento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentoAdministrativoId = table.Column<int>(type: "integer", nullable: false),
                    NombreArchivo = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FechaAltaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjuntosDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdjuntosDocumento_DocumentosAdministrativos_DocumentoAdmini~",
                        column: x => x.DocumentoAdministrativoId,
                        principalTable: "DocumentosAdministrativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventosDocumento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentoAdministrativoId = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    EstadoAnterior = table.Column<int>(type: "integer", nullable: true),
                    EstadoNuevo = table.Column<int>(type: "integer", nullable: true),
                    Texto = table.Column<string>(type: "text", nullable: false),
                    EsAutomatico = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosDocumento_DocumentosAdministrativos_DocumentoAdminis~",
                        column: x => x.DocumentoAdministrativoId,
                        principalTable: "DocumentosAdministrativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventosDocumento_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdjuntosDocumentoContenido",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Contenido = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjuntosDocumentoContenido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdjuntosDocumentoContenido_AdjuntosDocumento_Id",
                        column: x => x.Id,
                        principalTable: "AdjuntosDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdjuntosDocumento_DocumentoAdministrativoId",
                table: "AdjuntosDocumento",
                column: "DocumentoAdministrativoId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosAdministrativos_Estado",
                table: "DocumentosAdministrativos",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosAdministrativos_Numero",
                table: "DocumentosAdministrativos",
                column: "Numero");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosAdministrativos_RegistradoPorUsuarioId",
                table: "DocumentosAdministrativos",
                column: "RegistradoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosAdministrativos_Tipo_Anio_Numero",
                table: "DocumentosAdministrativos",
                columns: new[] { "Tipo", "Anio", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosDocumento_DocumentoAdministrativoId",
                table: "EventosDocumento",
                column: "DocumentoAdministrativoId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosDocumento_UsuarioId",
                table: "EventosDocumento",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdjuntosDocumentoContenido");

            migrationBuilder.DropTable(
                name: "EventosDocumento");

            migrationBuilder.DropTable(
                name: "AdjuntosDocumento");

            migrationBuilder.DropTable(
                name: "DocumentosAdministrativos");
        }
    }
}
