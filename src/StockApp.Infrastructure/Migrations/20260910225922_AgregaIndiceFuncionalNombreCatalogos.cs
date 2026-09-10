using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <summary>
    /// Reemplaza, para los 7 catálogos con Nombre único (Categoria, Proveedor, UnidadMedida,
    /// Zona, DimensionTematica, OrganismoResponsable, OrigenFinanciamiento), el índice único
    /// plano sobre "Nombre" por un índice único FUNCIONAL sobre LOWER("Nombre") — para que
    /// "Centro", "centro" y " Centro " (ya trimmeado en Application) sean la misma fila a
    /// nivel de base, no solo a nivel de validación en C#. Ver
    /// StockApp.Infrastructure.Repositories.*Repository.ExisteNombreAsync (usa .ToLower(),
    /// no ILIKE, para poder aprovechar este mismo índice).
    ///
    /// EF Core no expresa índices funcionales con HasIndex, así que el DDL va en SQL crudo
    /// (ver NormalizacionCatalogosSql). El modelo (AppDbContext) NO se toca: HasIndex(x =>
    /// x.Nombre).IsUnique() sigue ahí — el snapshot de EF se genera desde esa configuración
    /// fluida, no desde el estado real de la base, así que un futuro
    /// `dotnet ef migrations add` no intenta "corregir" este índice de vuelta a uno plano.
    ///
    /// Antes de tocar los índices, corre un guardián (NormalizacionCatalogosSql.
    /// GuardiaColisionesDeNombre) que aborta TODA la migración si alguno de los 3 catálogos
    /// con datos en producción (Categoria/Proveedor/UnidadMedida) ya tiene dos filas cuyo
    /// nombre colisiona sin distinguir mayúsculas/minúsculas — decisión del usuario: no
    /// fusiona ni renombra automáticamente, solo informa (tabla + nombre en conflicto + IDs)
    /// para que se resuelva a mano desde el ABM. Los 4 catálogos nuevos arrancan vacíos, así
    /// que para ellos el chequeo es trivial, pero se corre uniforme para los 7.
    /// </summary>
    public partial class AgregaIndiceFuncionalNombreCatalogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NormalizacionCatalogosSql.GuardiaColisionesDeNombre);

            migrationBuilder.Sql("DROP INDEX \"IX_Categorias_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Categorias_Nombre\" ON \"Categorias\" (LOWER(\"Nombre\"));");

            migrationBuilder.Sql("DROP INDEX \"IX_Proveedores_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Proveedores_Nombre\" ON \"Proveedores\" (LOWER(\"Nombre\"));");

            migrationBuilder.Sql("DROP INDEX \"IX_UnidadesMedida_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_UnidadesMedida_Nombre\" ON \"UnidadesMedida\" (LOWER(\"Nombre\"));");

            migrationBuilder.Sql("DROP INDEX \"IX_Zonas_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Zonas_Nombre\" ON \"Zonas\" (LOWER(\"Nombre\"));");

            migrationBuilder.Sql("DROP INDEX \"IX_DimensionesTematicas_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_DimensionesTematicas_Nombre\" ON \"DimensionesTematicas\" (LOWER(\"Nombre\"));");

            migrationBuilder.Sql("DROP INDEX \"IX_OrganismosResponsables_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_OrganismosResponsables_Nombre\" ON \"OrganismosResponsables\" (LOWER(\"Nombre\"));");

            migrationBuilder.Sql("DROP INDEX \"IX_OrigenesFinanciamiento_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_OrigenesFinanciamiento_Nombre\" ON \"OrigenesFinanciamiento\" (LOWER(\"Nombre\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Categorias_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Categorias_Nombre\" ON \"Categorias\" (\"Nombre\");");

            migrationBuilder.Sql("DROP INDEX \"IX_Proveedores_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Proveedores_Nombre\" ON \"Proveedores\" (\"Nombre\");");

            migrationBuilder.Sql("DROP INDEX \"IX_UnidadesMedida_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_UnidadesMedida_Nombre\" ON \"UnidadesMedida\" (\"Nombre\");");

            migrationBuilder.Sql("DROP INDEX \"IX_Zonas_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Zonas_Nombre\" ON \"Zonas\" (\"Nombre\");");

            migrationBuilder.Sql("DROP INDEX \"IX_DimensionesTematicas_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_DimensionesTematicas_Nombre\" ON \"DimensionesTematicas\" (\"Nombre\");");

            migrationBuilder.Sql("DROP INDEX \"IX_OrganismosResponsables_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_OrganismosResponsables_Nombre\" ON \"OrganismosResponsables\" (\"Nombre\");");

            migrationBuilder.Sql("DROP INDEX \"IX_OrigenesFinanciamiento_Nombre\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_OrigenesFinanciamiento_Nombre\" ON \"OrigenesFinanciamiento\" (\"Nombre\");");
        }
    }
}
