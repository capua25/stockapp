using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
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
                proveedoresCreados, fuentesCreadas, rubrosCreados) =
            await GetOrCrearMaestrosAsync(dto);

        var (lineaPorNombre, lineasPoaCreadas, asignacionesCreadas) =
            await GetOrCrearLineasPoaAsync(dto, fuentePorNombre, idImportacion);

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoConfirmacionDto(
            idImportacion,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            lineasPoaCreadas, asignacionesCreadas,
            IngresosCreados: 0, IngresosOmitidos: 0,
            GastosCreados: 0, GastosOmitidos: 0, PagosCreados: 0);
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
    /// </summary>
    private async Task<(
        Dictionary<string, Proveedor> ProveedorPorNombre,
        Dictionary<string, FuenteFinanciamiento> FuentePorNombre,
        Dictionary<int, RubroGasto> RubroPorCodigo,
        int ProveedoresCreados, int FuentesCreadas, int RubrosCreados)>
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
        foreach (var nombre in dto.MaestrosNuevos.Proveedores)
        {
            var clave = Normalizar(nombre);
            if (proveedorPorNombre.ContainsKey(clave))
                continue;

            var proveedor = new Proveedor { Nombre = nombre.Trim() };
            _ctx.Proveedores.Add(proveedor);
            proveedorPorNombre[clave] = proveedor;
            proveedoresCreados++;
        }

        var fuentesCreadas = 0;
        foreach (var nombre in dto.MaestrosNuevos.Fuentes)
        {
            var clave = Normalizar(nombre);
            if (fuentePorNombre.ContainsKey(clave))
                continue;

            var fuente = new FuenteFinanciamiento { Nombre = nombre.Trim() };
            _ctx.FuentesFinanciamiento.Add(fuente);
            fuentePorNombre[clave] = fuente;
            fuentesCreadas++;
        }

        var rubrosCreados = 0;
        foreach (var rubroNuevo in dto.MaestrosNuevos.Rubros)
        {
            if (rubroPorCodigo.ContainsKey(rubroNuevo.Codigo))
                continue;

            var rubro = new RubroGasto { Codigo = rubroNuevo.Codigo, Nombre = rubroNuevo.Nombre.Trim() };
            _ctx.RubrosGasto.Add(rubro);
            rubroPorCodigo[rubroNuevo.Codigo] = rubro;
            rubrosCreados++;
        }

        return (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
            proveedoresCreados, fuentesCreadas, rubrosCreados);
    }

    /// <summary>
    /// Get-or-create de LineaPoa (clave natural Nombre+Ejercicio) y sus AsignacionPresupuestal
    /// (clave natural LineaPoaId+FuenteFinanciamientoId, único en BD — AppDbContext.cs:142).
    /// IdImportacion se estampa SOLO en las líneas NUEVAS: una línea ya existente a la que esta
    /// corrida solo le agrega una asignación (financiamiento mixto declarado en dos corridas
    /// separadas) sigue siendo, a todos los efectos, "de antes" — no la creó esta importación.
    /// </summary>
    private async Task<(Dictionary<string, LineaPoa> LineaPorNombre, int LineasCreadas, int AsignacionesCreadas)>
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

            foreach (var asignacionDto in lineaDto.Asignaciones)
            {
                var fuente = fuentePorNombre[Normalizar(asignacionDto.Fuente)];

                // Comparación por REFERENCIA, no por FuenteFinanciamientoId: dos fuentes nuevas
                // en la MISMA corrida todavía no tienen Id real (recién se asigna en el
                // SaveChangesAsync único del final) ni el FK escalar de una AsignacionPresupuestal
                // recién agregada a la lista se sincroniza hasta que corre DetectChanges — comparar
                // por Id producía falsos positivos (0 == 0) entre dos asignaciones nuevas distintas.
                // fuentePorNombre devuelve SIEMPRE la misma instancia por nombre normalizado, y el
                // ThenInclude(FuenteFinanciamiento) de arriba resuelve las asignaciones YA
                // existentes contra esas mismas instancias trackeadas (identity map de EF Core)
                // — la igualdad por referencia es válida en ambos casos.
                if (linea.Asignaciones.Any(a => ReferenceEquals(a.FuenteFinanciamiento, fuente)))
                    continue;

                linea.Asignaciones.Add(new AsignacionPresupuestal
                {
                    FuenteFinanciamiento = fuente,
                    Monto = asignacionDto.Monto,
                });
                asignacionesCreadas++;
            }
        }

        return (lineaPorNombre, lineasCreadas, asignacionesCreadas);
    }

    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();
}
