using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Escritura transaccional del importador de planillas (F5c). ConfirmarAsync abre UNA
/// transacción, toma pg_advisory_xact_lock(ejercicio) y hace TODO el trabajo con un solo
/// SaveChangesAsync — mismo patrón que MovimientoStockRepository.RegistrarMovimientoAtomicoAsync.
/// </summary>
public class ImportacionRepository : IImportacionRepository
{
    private readonly AppDbContext _ctx;

    public ImportacionRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        var idImportacion = Guid.NewGuid();

        await using var tx = await _ctx.Database.BeginTransactionAsync();
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({dto.Ejercicio})");

        var (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
                proveedoresCreados, fuentesCreadas, rubrosCreados,
                proveedoresReactivados, fuentesReactivadas, rubrosReactivados) =
            await GetOrCrearMaestrosAsync(dto);

        var (lineaPorNombre, lineasPoaCreadas, lineasPoaReactivadas, asignacionesCreadas) =
            await GetOrCrearLineasPoaAsync(dto, fuentePorNombre, idImportacion);

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoConfirmacionDto(
            idImportacion,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            lineasPoaCreadas, asignacionesCreadas,
            IngresosCreados: 0, IngresosOmitidos: 0,
            GastosCreados: 0, GastosOmitidos: 0, PagosCreados: 0,
            ProveedoresReactivados: proveedoresReactivados,
            FuentesReactivadas: fuentesReactivadas,
            RubrosReactivados: rubrosReactivados,
            LineasPoaReactivadas: lineasPoaReactivadas);
    }

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId) =>
        throw new NotImplementedException("Se implementa en Task 8.");

    /// <summary>
    /// Get-or-create de Proveedor/FuenteFinanciamiento/RubroGasto declarados en
    /// MaestrosNuevos. Devuelve diccionarios normalizados (Trim + ToUpperInvariant, mismo
    /// criterio que AnalisisImportacionService.Normalizar de F5b) con el OBJETO de la entidad
    /// (no su Id): las entidades nuevas todavía no tienen Id real hasta el SaveChangesAsync
    /// único del final — el resto del método las referencia por navegación, no por FK manual.
    /// Los índices únicos son sobre el nombre crudo (case-sensitive en Postgres): los ABM de
    /// Proveedor/Fuente permiten que coexistan dos filas que difieren solo en mayúsculas
    /// (comparan con <c>==</c>, no normalizado). Por eso el armado de los diccionarios usa
    /// GroupBy + First en vez de ToDictionary directo — con dos filas así ya en la base,
    /// ToDictionary lanzaría ArgumentException por clave duplicada antes de tocar el payload.
    /// Si existe un maestro declarado como nuevo pero está inactivo (baja lógica), se REACTIVA
    /// (Activo = true) en vez de crearse de nuevo, y se cuenta aparte como reactivado — no como
    /// creado.
    /// </summary>
    private async Task<(
        Dictionary<string, Proveedor> ProveedorPorNombre,
        Dictionary<string, FuenteFinanciamiento> FuentePorNombre,
        Dictionary<int, RubroGasto> RubroPorCodigo,
        int ProveedoresCreados, int FuentesCreadas, int RubrosCreados,
        int ProveedoresReactivados, int FuentesReactivadas, int RubrosReactivados)>
        GetOrCrearMaestrosAsync(ConfirmarImportacionDto dto)
    {
        var proveedorPorNombre = (await _ctx.Proveedores.ToListAsync())
            .GroupBy(p => Normalizar(p.Nombre))
            .ToDictionary(g => g.Key, g => g.First());
        var fuentePorNombre = (await _ctx.FuentesFinanciamiento.ToListAsync())
            .GroupBy(f => Normalizar(f.Nombre))
            .ToDictionary(g => g.Key, g => g.First());
        var rubroPorCodigo = (await _ctx.RubrosGasto.ToListAsync())
            .ToDictionary(r => r.Codigo);

        var proveedoresCreados = 0;
        var proveedoresReactivados = 0;
        foreach (var nombre in dto.MaestrosNuevos.Proveedores)
        {
            var clave = Normalizar(nombre);
            if (proveedorPorNombre.TryGetValue(clave, out var existente))
            {
                if (!existente.Activo)
                {
                    existente.Activo = true;
                    proveedoresReactivados++;
                }
                continue;
            }

            var proveedor = new Proveedor { Nombre = nombre.Trim() };
            _ctx.Proveedores.Add(proveedor);
            proveedorPorNombre[clave] = proveedor;
            proveedoresCreados++;
        }

        var fuentesCreadas = 0;
        var fuentesReactivadas = 0;
        foreach (var nombre in dto.MaestrosNuevos.Fuentes)
        {
            var clave = Normalizar(nombre);
            if (fuentePorNombre.TryGetValue(clave, out var existente))
            {
                if (!existente.Activo)
                {
                    existente.Activo = true;
                    fuentesReactivadas++;
                }
                continue;
            }

            var fuente = new FuenteFinanciamiento { Nombre = nombre.Trim() };
            _ctx.FuentesFinanciamiento.Add(fuente);
            fuentePorNombre[clave] = fuente;
            fuentesCreadas++;
        }

        var rubrosCreados = 0;
        var rubrosReactivados = 0;
        foreach (var rubroNuevo in dto.MaestrosNuevos.Rubros)
        {
            if (rubroPorCodigo.TryGetValue(rubroNuevo.Codigo, out var existente))
            {
                if (!existente.Activo)
                {
                    existente.Activo = true;
                    rubrosReactivados++;
                }
                continue;
            }

            var rubro = new RubroGasto { Codigo = rubroNuevo.Codigo, Nombre = rubroNuevo.Nombre.Trim() };
            _ctx.RubrosGasto.Add(rubro);
            rubroPorCodigo[rubroNuevo.Codigo] = rubro;
            rubrosCreados++;
        }

        return (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            proveedoresReactivados, fuentesReactivadas, rubrosReactivados);
    }

    /// <summary>
    /// Get-or-create de LineaPoa (clave natural Nombre+Ejercicio) y sus AsignacionPresupuestal
    /// (clave natural LineaPoaId+FuenteFinanciamientoId, único en BD — AppDbContext.cs:142). La
    /// query de existentes NO filtra Activo a propósito: una línea inactiva (baja lógica de un
    /// /revertir anterior) tiene que ser visible acá para poder reactivarse en vez de quedar
    /// duplicada o invisible. IdImportacion se estampa en las líneas NUEVAS y en las que se
    /// REACTIVAN (así la reversa del lote nuevo las puede volver a revertir de forma coherente);
    /// una línea ya existente y ACTIVA a la que esta corrida solo le agrega una asignación
    /// (financiamiento mixto declarado en dos corridas separadas) conserva su IdImportacion
    /// original — sigue siendo, a todos los efectos, "de antes", no la creó ni la reactivó esta
    /// importación. Ambos casos (nueva/reactivada) se cuentan por separado.
    /// </summary>
    private async Task<(
        Dictionary<string, LineaPoa> LineaPorNombre,
        int LineasCreadas, int LineasReactivadas, int AsignacionesCreadas)>
        GetOrCrearLineasPoaAsync(
            ConfirmarImportacionDto dto,
            Dictionary<string, FuenteFinanciamiento> fuentePorNombre,
            Guid idImportacion)
    {
        var lineasExistentes = await _ctx.LineasPoa
            .Where(l => l.Ejercicio == dto.Ejercicio)
            .Include(l => l.Asignaciones).ThenInclude(a => a.FuenteFinanciamiento)
            .ToListAsync();
        var lineaPorNombre = lineasExistentes
            .GroupBy(l => Normalizar(l.Nombre))
            .ToDictionary(g => g.Key, g => g.First());

        var lineasCreadas = 0;
        var lineasReactivadas = 0;
        var asignacionesCreadas = 0;

        foreach (var lineaDto in dto.LineasPoa)
        {
            var clave = Normalizar(lineaDto.Nombre);
            if (!lineaPorNombre.TryGetValue(clave, out var linea))
            {
                linea = new LineaPoa
                {
                    Nombre = lineaDto.Nombre.Trim(),
                    Programa = lineaDto.Programa.Trim(),
                    Ejercicio = dto.Ejercicio,
                    IdImportacion = idImportacion,
                };
                _ctx.LineasPoa.Add(linea);
                lineaPorNombre[clave] = linea;
                lineasCreadas++;
            }
            else if (!linea.Activo)
            {
                linea.Activo = true;
                linea.IdImportacion = idImportacion;
                lineasReactivadas++;
            }

            foreach (var asignacionDto in lineaDto.Asignaciones)
            {
                var claveFuente = Normalizar(asignacionDto.Fuente);
                if (!fuentePorNombre.TryGetValue(claveFuente, out var fuente))
                    throw new EntidadNoEncontradaException(
                        $"La fuente de financiamiento '{asignacionDto.Fuente}' referenciada en la línea POA " +
                        $"'{lineaDto.Nombre}' no existe ni fue declarada nueva en MaestrosNuevos.Fuentes.");

                // Comparación por NOMBRE NORMALIZADO, no por FuenteFinanciamientoId ni por
                // referencia de objeto. Por Id: dos fuentes nuevas en la MISMA corrida todavía
                // no tienen Id real (recién se asigna en el SaveChangesAsync único del final) ni
                // el FK escalar de una AsignacionPresupuestal recién agregada a la lista se
                // sincroniza hasta que corre DetectChanges — comparar por Id producía falsos
                // positivos (0 == 0) entre dos asignaciones nuevas distintas. Por referencia (fix
                // anterior, Minor #8 del review): dependía de que fuentePorNombre devolviera
                // SIEMPRE la misma instancia por nombre normalizado y de que el identity map de
                // EF Core resolviera las asignaciones YA existentes contra esas mismas instancias
                // trackeadas — un AsNoTracking() futuro en cualquiera de las dos queries (esta o
                // los .ToListAsync() de maestros) rompería esa garantía EN SILENCIO y resucitaría
                // el bug de la asignación mixta perdida. Comparar por nombre normalizado no
                // depende de tracking ni de identidad de objetos: es válida sea cual sea el
                // estado de tracking de las entidades.
                if (linea.Asignaciones.Any(a => Normalizar(a.FuenteFinanciamiento!.Nombre) == claveFuente))
                    continue;

                linea.Asignaciones.Add(new AsignacionPresupuestal
                {
                    FuenteFinanciamiento = fuente,
                    Monto = asignacionDto.Monto,
                });
                asignacionesCreadas++;
            }
        }

        return (lineaPorNombre, lineasCreadas, lineasReactivadas, asignacionesCreadas);
    }

    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();
}
