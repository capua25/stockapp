using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
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
    /// <summary>
    /// LogAuditoria no tiene columna propia para el Guid del lote (spec §2.7 solo la pide en
    /// Gasto/IngresoCaja/LineaPoa) — se codifica como el primer token de Detalle con este
    /// prefijo fijo. Task 8 (RevertirAsync) reutiliza esta constante con su propia query inline
    /// para ubicar el LogAuditoria del lote a revertir; no llama a
    /// BuscarImportacionNoRevertidaAsync ni a ExtraerIdImportacion (esos dos son específicos del
    /// guard de /confirmar, que resuelve el Guid a partir del Ejercicio — /revertir/{id} ya lo
    /// recibe directo como parámetro de ruta).
    /// </summary>
    private const string PrefijoIdImportacion = "IdImportacion=";

    private readonly AppDbContext _ctx;

    public ImportacionRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        var idImportacion = Guid.NewGuid();

        await using var tx = await _ctx.Database.BeginTransactionAsync();
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({dto.Ejercicio})");

        // Guard de re-importación (spec §2.6): mismo patrón de "falla temprana antes de tocar la
        // BD" que el resto del método (ValidacionImportacionException más abajo) — sin
        // tx.RollbackAsync() explícito, el `await using var tx` de arriba hace rollback al
        // disponerse cuando la excepción se propaga.
        var idNoRevertida = await BuscarImportacionNoRevertidaAsync(dto.Ejercicio);
        if (idNoRevertida is not null && !dto.Forzar)
            throw new ReglaDeNegocioException(
                $"El ejercicio {dto.Ejercicio} ya tiene una importación previa (IdImportacion " +
                $"{idNoRevertida}) sin revertir. Usá Forzar=true para reimportar de todas formas, " +
                "o revertí esa corrida primero con /finanzas/importar/revertir/{id}.");

        var (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
                proveedoresCreados, fuentesCreadas, rubrosCreados,
                proveedoresReactivados, fuentesReactivadas, rubrosReactivados) =
            await GetOrCrearMaestrosAsync(dto);

        var (lineaPorNombre, lineasPoaCreadas, lineasPoaReactivadas, asignacionesCreadas) =
            await GetOrCrearLineasPoaAsync(dto, fuentePorNombre, idImportacion);

        var (ingresosCreados, ingresosOmitidos) =
            await ProcesarIngresosAsync(dto, fuentePorNombre, idImportacion);

        var (gastosCreados, gastosOmitidos, pagosCreados, conflictos) =
            await ProcesarGastosAsync(dto, proveedorPorNombre, fuentePorNombre, rubroPorCodigo, lineaPorNombre, idImportacion);

        // Auditoría de la corrida (spec §2.6/§2.7): DENTRO de la transacción y ANTES del único
        // SaveChangesAsync — si algo de arriba o de este mismo save rollbackea, no puede quedar
        // rastro de auditoría de una corrida que no se confirmó.
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.ImportacionPlanillas,
            Entidad = "Importacion",
            EntidadId = dto.Ejercicio,
            Detalle = $"{PrefijoIdImportacion}{idImportacion}; Ejercicio: {dto.Ejercicio}; " +
                $"Proveedores creados: {proveedoresCreados}; Fuentes creadas: {fuentesCreadas}; " +
                $"Rubros creados: {rubrosCreados}; LineasPoa creadas: {lineasPoaCreadas}; " +
                $"Asignaciones creadas: {asignacionesCreadas}; " +
                $"Ingresos creados: {ingresosCreados}; Ingresos omitidos: {ingresosOmitidos}; " +
                $"Gastos creados: {gastosCreados}; Gastos omitidos: {gastosOmitidos}; " +
                $"Pagos creados: {pagosCreados}",
        });

        await AntesDeGuardarAsync();

        // A.5 (review Important A): red de seguridad. A.1 (clave natural = índice) y A.4
        // (validación intra-payload en el Service) deberían evitar cualquier 23505 acá, pero si
        // igual llega uno se traduce a una excepción de dominio con el nombre de la restricción,
        // mismo patrón que GastoRepository.EsViolacionFacturaUnica (GastoRepository.cs:122-124).
        // Nunca un 500 pelado.
        try
        {
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ObtenerRestriccionUnicaViolada(ex) is { } restriccion)
        {
            // Encadena ex como InnerException (review Minor): sin esto se perdía el stack de
            // Npgsql (PostgresException con el detalle real de la violación) para diagnóstico.
            throw new ReglaDeNegocioException(
                $"Violación de la restricción única '{restriccion}' al confirmar la importación.", ex);
        }

        await tx.CommitAsync();

        return new ResultadoConfirmacionDto(
            idImportacion,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            lineasPoaCreadas, asignacionesCreadas,
            ingresosCreados, ingresosOmitidos,
            gastosCreados, gastosOmitidos, pagosCreados,
            ProveedoresReactivados: proveedoresReactivados,
            FuentesReactivadas: fuentesReactivadas,
            RubrosReactivados: rubrosReactivados,
            LineasPoaReactivadas: lineasPoaReactivadas,
            Conflictos: conflictos);
    }

    private static string? ObtenerRestriccionUnicaViolada(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            ? pg.ConstraintName
            : null;

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId) =>
        throw new NotImplementedException("Se implementa en Task 8.");

    /// <summary>
    /// Seam de test (review Important B): no-op en producción. Se invoca justo antes del único
    /// SaveChangesAsync de ConfirmarAsync — todo el grafo de get-or-create (maestros + líneas POA
    /// + asignaciones) ya está en el ChangeTracker en ese punto pero SIN un solo round-trip de
    /// escritura a Postgres todavía. Una subclase de test puede sobrescribirlo para agregar una
    /// entidad inválida y forzar un DbUpdateException REAL (violación de constraint) dentro de la
    /// transacción, y así verificar que el rollback revierte TODO lo que esta corrida acumuló —
    /// no solo "no hay nada que revertir porque fallamos antes de guardar".
    /// </summary>
    protected virtual Task AntesDeGuardarAsync() => Task.CompletedTask;

    /// <summary>
    /// Guard de re-importación (spec §2.6). LogAuditoria no tiene columna propia para el Guid
    /// del lote — se codifica como el primer token de Detalle y EntidadId guarda el Ejercicio
    /// para filtrar en SQL sin traer todo el historial de auditoría del sistema a memoria.
    /// "No revertida" = no existe ningún LogAuditoria de AccionAuditada.ReversionImportacion
    /// (Task 8) cuyo Detalle referencie ese mismo IdImportacion con el mismo prefijo. Hasta que
    /// Task 8 exista, ninguna corrida puede estar revertida — esta consulta ya queda lista para
    /// cuando RevertirAsync empiece a escribir esos logs.
    /// </summary>
    private async Task<Guid?> BuscarImportacionNoRevertidaAsync(int ejercicio)
    {
        var confirmaciones = await _ctx.LogsAuditoria
            .Where(l => l.Accion == AccionAuditada.ImportacionPlanillas && l.EntidadId == ejercicio)
            .Select(l => l.Detalle)
            .ToListAsync();

        foreach (var detalle in confirmaciones)
        {
            var id = ExtraerIdImportacion(detalle);
            var patronReversion = $"{PrefijoIdImportacion}{id}%";
            var fueRevertida = await _ctx.LogsAuditoria.AnyAsync(l =>
                l.Accion == AccionAuditada.ReversionImportacion
                && EF.Functions.Like(l.Detalle, patronReversion));

            if (!fueRevertida)
                return id;
        }

        return null;
    }

    private static Guid ExtraerIdImportacion(string detalle) =>
        Guid.Parse(detalle.Substring(PrefijoIdImportacion.Length, 36));

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

        for (var i = 0; i < dto.LineasPoa.Count; i++)
        {
            var lineaDto = dto.LineasPoa[i];
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

            for (var j = 0; j < lineaDto.Asignaciones.Count; j++)
            {
                var asignacionDto = lineaDto.Asignaciones[j];
                var claveFuente = Normalizar(asignacionDto.Fuente);
                if (!fuentePorNombre.TryGetValue(claveFuente, out var fuente))
                    // Defensivo: ConfirmacionImportacionService.ValidarAsync (F5c) ya filtra esta
                    // MISMA condición antes de llegar al repo con la clave estructurada
                    // "LineasPoa[i].Asignaciones[j].Fuente" (mismo mensaje). Si algún día se
                    // invoca el repositorio sin pasar por el Service, el cliente tiene que
                    // recibir el mismo 400 estructurado — no un 404 genérico sin metadata de
                    // campo (review Important A).
                    throw new ValidacionImportacionException(
                        new Dictionary<string, string[]>
                        {
                            [$"LineasPoa[{i}].Asignaciones[{j}].Fuente"] =
                                new[] { $"La fuente '{asignacionDto.Fuente}' no existe ni fue declarada nueva" },
                        });

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

    /// <summary>
    /// Única función de normalización de claves de todo el archivo (Minor del review: existía un
    /// duplicado byte a byte de esta misma función con otro nombre — unificado acá).
    /// </summary>
    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();

    private static string? NormalizarOpcional(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : Normalizar(texto);

    /// <summary>Colapsa blanco ("" o " ") a null antes de persistir (review Important 3): sin
    /// esto, un valor en blanco se guardaba tal cual (`?.Trim()` da "", no null). Para
    /// NumeroFactura eso es un bug real — el payload decide la rama SIN factura con
    /// `!string.IsNullOrWhiteSpace(...)`, pero "" persistida NO es null, así que el índice único
    /// parcial (WHERE "NumeroFactura" IS NOT NULL) SÍ le aplica, y en la relectura
    /// (Where(g => g.NumeroFactura is null)) esa fila queda del lado CON factura mientras el
    /// payload la sigue tratando SIN factura — dos predicados partiendo el mismo universo de
    /// forma distinta. Con este colapso, ambos lados usan el mismo criterio: blanco == ausente,
    /// siempre. Se aplica también a NumeroOrden/Destino por consistencia, aunque ninguno de los
    /// dos tiene un índice único de por medio.</summary>
    private static string? TrimoNulo(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    // ── Dedupe por clave natural (spec §4) ──────────────────────────────────────────────────
    //
    // Una sola función de proyección por caso, compartida entre la carga del set existente
    // (desde la BD) y la comparación de cada fila nueva del payload — evita que ambos lados se
    // desincronicen silenciosamente (spec §4, "Riesgo asumido").
    //
    // Gasto CON NumeroFactura (review Important A.1): la clave es (ProveedorId, NumeroFactura),
    // EXACTAMENTE la del índice único parcial IX_Gastos_ProveedorId_NumeroFactura
    // (AppDbContext.cs, filtro "Activo" = TRUE AND "NumeroFactura" IS NOT NULL). La clave
    // natural del importador nunca puede ser más fina que esa restricción — si lo fuera (como
    // antes, que incluía Fecha/MontoTotal), dos corridas con el mismo proveedor+factura pero un
    // dato distinto (el usuario corrigió el monto y reimportó) generaban un 23505 de Postgres
    // que tiraba abajo TODA la importación. Gasto SIN NumeroFactura: no hay índice único que lo
    // limite, así que sigue con la clave ancha (ProveedorId, NumeroOrden, Fecha, MontoTotal).
    //
    // INVARIANTE GENERAL (re-review, CRITICAL 1 / IMPORTANT 2): el set de dedupe que se carga
    // desde la base tiene que cubrir AL MENOS el mismo universo que la restricción única que lo
    // respalda — nunca menos. Hubo una regresión real acá: un acotado "por ejercicio"
    // (Where(... && Fecha >= inicioEjercicio && Fecha < finEjercicio)) se aprobó como Minor de
    // performance y en realidad rompía la invariante en los dos sentidos. (1)
    // IX_Gastos_ProveedorId_NumeroFactura NO tiene ninguna restricción de fecha — un proveedor
    // puede reusar el mismo número de factura en dos ejercicios distintos, y acotar el set por
    // ejercicio dejaba esa colisión invisible para el dedupe en memoria, con un 23505 real
    // esperando en Postgres. (2) El camino SIN factura y los ingresos NO tienen ningún índice
    // único que los frene — asumir que toda Fecha del payload cae dentro del Ejercicio declarado
    // (nadie lo valida: ni ValidarAsync, ni el parser de .ods, ni los compromisos POA tipeados a
    // mano en F5d) dejaba pasar duplicados SILENCIOSOS de filas arrastradas de otro ejercicio,
    // sin error ni warning. Por eso las queries de abajo cargan SIEMPRE el histórico completo de
    // activos, sin acotar por fecha/ejercicio — no volver a "optimizar" esto sin releer este
    // comentario.

    private readonly record struct ClaveIngreso(DateTime Fecha, string Concepto, decimal Monto, int FuenteId);
    private readonly record struct ClaveGastoConFactura(int ProveedorId, string NumeroFactura);
    private readonly record struct ClaveGastoSinFactura(
        int ProveedorId, string? NumeroOrden, DateTime Fecha, decimal MontoTotal);

    /// <summary>Datos comparables de un gasto CON factura ya activo en la base — lo necesario
    /// para distinguir "mismo gasto ya importado" (A.2: omitido) de "misma factura con datos
    /// distintos" (A.2: conflicto), sin traer la entidad completa.</summary>
    private readonly record struct DatosGastoConFactura(DateTime Fecha, decimal MontoTotal, string? NumeroOrden);

    private static ClaveIngreso ProyectarClaveIngreso(DateTime fecha, string concepto, decimal monto, int fuenteId) =>
        new(fecha, Normalizar(concepto), monto, fuenteId);

    private static ClaveGastoConFactura ProyectarClaveGastoConFactura(int proveedorId, string numeroFactura) =>
        new(proveedorId, Normalizar(numeroFactura));

    private static ClaveGastoSinFactura ProyectarClaveGastoSinFactura(
        int proveedorId, string? numeroOrden, DateTime fecha, decimal montoTotal) =>
        new(proveedorId, NormalizarOpcional(numeroOrden), fecha, montoTotal);

    private static DateTime AFechaUtc(DateOnly fecha) =>
        new(fecha.Year, fecha.Month, fecha.Day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime? AFechaUtc(DateOnly? fecha) => fecha is { } valor ? AFechaUtc(valor) : null;

    private async Task<(int Creados, int Omitidos)> ProcesarIngresosAsync(
        ConfirmarImportacionDto dto,
        Dictionary<string, FuenteFinanciamiento> fuentePorNombre,
        Guid idImportacion)
    {
        var clavesExistentes = (await _ctx.IngresosCaja
                .Where(i => i.Activo)
                .Select(i => new { i.Fecha, i.Concepto, i.Monto, i.FuenteFinanciamientoId })
                .ToListAsync())
            .Select(i => ProyectarClaveIngreso(i.Fecha, i.Concepto, i.Monto, i.FuenteFinanciamientoId))
            .ToHashSet();

        var creados = 0;
        var omitidos = 0;

        foreach (var ingresoDto in dto.Ingresos)
        {
            var fuente = fuentePorNombre[Normalizar(ingresoDto.Fuente)];
            var fechaUtc = AFechaUtc(ingresoDto.Fecha);
            var clave = ProyectarClaveIngreso(fechaUtc, ingresoDto.Concepto, ingresoDto.Monto, fuente.Id);

            if (clavesExistentes.Contains(clave))
            {
                omitidos++;
                continue;
            }

            _ctx.IngresosCaja.Add(new IngresoCaja
            {
                Fecha = fechaUtc,
                Concepto = ingresoDto.Concepto.Trim(),
                Monto = ingresoDto.Monto,
                FuenteFinanciamiento = fuente,
                IdImportacion = idImportacion,
            });
            creados++;
        }

        return (creados, omitidos);
    }

    /// <summary>
    /// Contado ⇒ pago automático por el total en la fecha del gasto (mismo criterio que
    /// GastoService.AltaAsync, GastoService.cs:50-55). Los compromisos POA importados (spec
    /// §2.3) van Credito SIN pago: SaldoPendiente == MontoTotal refleja que es un compromiso
    /// pendiente, no una factura pagada — el Control POA (F4) ya calcula el saldo de la línea a
    /// partir de gastos activos con su LineaPoaId, sin cambios.
    ///
    /// Dedupe partido en dos casos (review Important A.1/A.2): CON NumeroFactura, matchea contra
    /// la clave del índice único de la base y compara los demás datos — todo igual es "omitido",
    /// algo distinto es "conflicto" (no se escribe nada, se reporta aparte en
    /// ResultadoConfirmacionDto.Conflictos). SIN NumeroFactura, sigue el dedupe amplio de
    /// siempre (ahí no hay índice único que lo restrinja).
    /// </summary>
    private async Task<(int Creados, int Omitidos, int PagosCreados, IReadOnlyList<ConflictoGastoDto> Conflictos)>
        ProcesarGastosAsync(
            ConfirmarImportacionDto dto,
            Dictionary<string, Proveedor> proveedorPorNombre,
            Dictionary<string, FuenteFinanciamiento> fuentePorNombre,
            Dictionary<int, RubroGasto> rubroPorCodigo,
            Dictionary<string, LineaPoa> lineaPorNombre,
            Guid idImportacion)
    {
        var gastosActivos = await _ctx.Gastos
            .Where(g => g.Activo)
            .Select(g => new { g.Id, g.ProveedorId, g.NumeroFactura, g.NumeroOrden, g.Fecha, g.MontoTotal })
            .ToListAsync();

        // GroupBy + First (no ToDictionary directo): mismo criterio que proveedorPorNombre/
        // fuentePorNombre en GetOrCrearMaestrosAsync — el índice único de la base es sobre
        // NumeroFactura CRUDO (Postgres case-sensitive), así que en teoría podrían coexistir dos
        // gastos activos que normalizados colisionan (p.ej. "F-1" y "f-1"). Caso de borde ya
        // aceptado en el resto del archivo para el mismo tipo de colisión. OrderBy(g => g.Id)
        // ANTES del GroupBy (review Minor): sin esto, gr.First() no es determinístico entre dos
        // gastos que colisionan al normalizar — dependía del orden que devolviera Postgres, que
        // no está garantizado sin ORDER BY explícito. Con el OrderBy, el "ganador" es siempre el
        // gasto con Id más chico (el más antiguo).
        var datosPorClaveConFactura = gastosActivos
            .Where(g => g.NumeroFactura is not null)
            .OrderBy(g => g.Id)
            .GroupBy(g => ProyectarClaveGastoConFactura(g.ProveedorId, g.NumeroFactura!))
            .ToDictionary(
                gr => gr.Key,
                gr => new DatosGastoConFactura(gr.First().Fecha, gr.First().MontoTotal, gr.First().NumeroOrden));

        var clavesSinFactura = gastosActivos
            .Where(g => g.NumeroFactura is null)
            .Select(g => ProyectarClaveGastoSinFactura(g.ProveedorId, g.NumeroOrden, g.Fecha, g.MontoTotal))
            .ToHashSet();

        var creados = 0;
        var omitidos = 0;
        var pagosCreados = 0;
        var conflictos = new List<ConflictoGastoDto>();

        for (var i = 0; i < dto.Gastos.Count; i++)
        {
            var gastoDto = dto.Gastos[i];
            var proveedor = proveedorPorNombre[Normalizar(gastoDto.Proveedor)];
            var fuente = fuentePorNombre[Normalizar(gastoDto.Fuente)];
            var rubro = rubroPorCodigo[gastoDto.CodigoRubro];
            var fechaUtc = AFechaUtc(gastoDto.Fecha);
            var tieneFactura = !string.IsNullOrWhiteSpace(gastoDto.NumeroFactura);

            if (tieneFactura)
            {
                var claveFactura = ProyectarClaveGastoConFactura(proveedor.Id, gastoDto.NumeroFactura!);
                if (datosPorClaveConFactura.TryGetValue(claveFactura, out var datosExistentes))
                {
                    var camposDivergentes = CamposDivergentes(datosExistentes, gastoDto, fechaUtc);
                    if (camposDivergentes.Count > 0)
                        conflictos.Add(new ConflictoGastoDto(
                            gastoDto.Proveedor, gastoDto.NumeroFactura!.Trim(), camposDivergentes, i));
                    else
                        omitidos++;
                    continue;
                }
            }
            else
            {
                var claveSinFactura = ProyectarClaveGastoSinFactura(
                    proveedor.Id, gastoDto.NumeroOrden, fechaUtc, gastoDto.MontoTotal);
                if (clavesSinFactura.Contains(claveSinFactura))
                {
                    omitidos++;
                    continue;
                }
            }

            var gasto = new Gasto
            {
                Proveedor = proveedor,
                NumeroFactura = TrimoNulo(gastoDto.NumeroFactura),
                NumeroOrden = TrimoNulo(gastoDto.NumeroOrden),
                Detalle = gastoDto.Detalle.Trim(),
                Destino = TrimoNulo(gastoDto.Destino),
                Fecha = fechaUtc,
                MontoTotal = gastoDto.MontoTotal,
                FuenteFinanciamiento = fuente,
                RubroGasto = rubro,
                LineaPoa = string.IsNullOrWhiteSpace(gastoDto.LineaPoa)
                    ? null : lineaPorNombre[Normalizar(gastoDto.LineaPoa)],
                CondicionPago = gastoDto.Condicion,
                FechaVencimiento = AFechaUtc(gastoDto.FechaVencimiento),
                IdImportacion = idImportacion,
            };

            if (gastoDto.Condicion == CondicionPago.Contado)
            {
                gasto.Pagos = new List<PagoGasto>
                {
                    new() { Fecha = fechaUtc, Monto = gastoDto.MontoTotal, Nota = "Pago contado (importación)" },
                };
                pagosCreados++;
            }

            _ctx.Gastos.Add(gasto);
            creados++;
        }

        return (creados, omitidos, pagosCreados, conflictos);
    }

    /// <summary>Compara un gasto CON factura ya activo en la base contra la fila nueva del
    /// payload que matcheó por (ProveedorId, NumeroFactura) y arma la lista de campos que
    /// difieren (review Important A.2/A.3). Vacía ⇒ es el mismo gasto (omitido, no conflicto).
    /// NumeroOrden se compara normalizado, como el resto de las claves del archivo — no
    /// queremos un falso conflicto por un espacio o un casing de más.</summary>
    private static List<CampoDivergenteDto> CamposDivergentes(
        DatosGastoConFactura existente, GastoConfirmarDto nuevo, DateTime fechaNuevaUtc)
    {
        var campos = new List<CampoDivergenteDto>();

        if (existente.Fecha != fechaNuevaUtc)
            campos.Add(new CampoDivergenteDto(
                "Fecha", existente.Fecha.ToString("yyyy-MM-dd"), fechaNuevaUtc.ToString("yyyy-MM-dd")));

        if (existente.MontoTotal != nuevo.MontoTotal)
            campos.Add(new CampoDivergenteDto(
                "MontoTotal", FormatearMonto(existente.MontoTotal), FormatearMonto(nuevo.MontoTotal)));

        if (NormalizarOpcional(existente.NumeroOrden) != NormalizarOpcional(nuevo.NumeroOrden))
            campos.Add(new CampoDivergenteDto(
                "NumeroOrden", existente.NumeroOrden ?? "(vacío)", nuevo.NumeroOrden ?? "(vacío)"));

        return campos;
    }

    /// <summary>Formatea un monto para el reporte de conflictos sin arrastrar la escala interna
    /// del decimal (Postgres/EF devuelven MontoTotal con la escala fija de la columna, 18,4:
    /// 500m llega como 500.0000m) — sin este formateo, "500.0000" vs "550" se leería como un
    /// campo distinto todavía más confuso de lo que ya es.</summary>
    private static string FormatearMonto(decimal monto) => monto.ToString("0.####", CultureInfo.InvariantCulture);
}
