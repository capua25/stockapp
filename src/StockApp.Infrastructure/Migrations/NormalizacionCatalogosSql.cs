namespace StockApp.Infrastructure.Migrations;

/// <summary>
/// SQL crudo de la migración AgregaIndiceFuncionalNombreCatalogos, extraído a una constante
/// compartida entre la migración (AgregaIndiceFuncionalNombreCatalogos.Up) y el test de
/// Infrastructure (NormalizacionCatalogosGuardTests) — mismo criterio que
/// PermisoUsuarioBackfillSql: evita que el texto probado por el test diverja del que
/// realmente corre en la migración.
///
/// EF Core no expresa índices únicos FUNCIONALES (sobre LOWER("Nombre")) con HasIndex —
/// el fluent API de HasIndex solo mapea a un índice plano sobre la columna tal cual. Por
/// eso este DDL vive en SQL crudo dentro de la migración en lugar de en el modelo. Esto es
/// seguro respecto de migraciones futuras: el snapshot de EF (AppDbContextModelSnapshot)
/// se genera a partir de la configuración fluida en AppDbContext (HasIndex(x => x.Nombre)
/// .IsUnique() sigue ahí, sin tocar), no de una introspección real de la base — por lo
/// tanto un futuro `dotnet ef migrations add` no "ve" el índice funcional y no intentará
/// revertirlo a uno plano, siempre que nadie toque esas líneas de HasIndex en el modelo.
/// </summary>
public static class NormalizacionCatalogosSql
{
    /// <summary>
    /// Guardián pre-vuelo: para cada uno de los 7 catálogos con Nombre único, aborta la
    /// migración completa (RAISE EXCEPTION revierte toda la transacción de la migración,
    /// sin tocar ningún dato) si ya existen dos o más filas cuyo nombre colisiona al
    /// comparar sin distinguir mayúsculas/minúsculas. Decisión del usuario: la migración
    /// NO fusiona ni renombra automáticamente — solo informa.
    ///
    /// El mensaje de RAISE EXCEPTION es deliberadamente detallado (tabla, nombre en
    /// conflicto, cantidad de filas e IDs exactos) porque no hay acceso SSH al servidor
    /// del cliente post-instalación: ese mensaje es TODO lo que va a tener la persona que
    /// tenga que resolver el duplicado a mano desde el ABM.
    /// </summary>
    public const string GuardiaColisionesDeNombre =
        """
        DO $$
        DECLARE
            tabla text;
            fila record;
        BEGIN
            FOREACH tabla IN ARRAY ARRAY[
                'Categorias', 'Proveedores', 'UnidadesMedida', 'Zonas',
                'DimensionesTematicas', 'OrganismosResponsables', 'OrigenesFinanciamiento'
            ]
            LOOP
                FOR fila IN EXECUTE format(
                    'SELECT LOWER(%1$I) AS nombre_normalizado, ' ||
                    '       STRING_AGG(%2$I::text, '', '' ORDER BY %2$I) AS ids, ' ||
                    '       COUNT(*) AS cantidad ' ||
                    'FROM %3$I ' ||
                    'GROUP BY LOWER(%1$I) ' ||
                    'HAVING COUNT(*) > 1',
                    'Nombre', 'Id', tabla)
                LOOP
                    RAISE EXCEPTION
                        'Migración de normalización de catálogos ABORTADA: la tabla "%" tiene % filas '
                        'con el nombre "%" (comparación sin distinguir mayúsculas/minúsculas). '
                        'IDs en conflicto: %. No se modificó ningún dato. Para continuar, resolvé el '
                        'duplicado a mano desde el ABM correspondiente (renombrando, fusionando o dando '
                        'de baja una de las filas) y volvé a aplicar la migración.',
                        tabla, fila.cantidad, fila.nombre_normalizado, fila.ids;
                END LOOP;
            END LOOP;
        END $$;
        """;
}
