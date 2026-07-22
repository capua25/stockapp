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
        // rastro de auditoría de una corrida que no se confirmó. IdLote es la fuente de verdad
        // del vínculo con el lote (post-review de Task 6: columna tipada + índice no único en
        // LogAuditoria, AppDbContext.cs); Detalle queda solo como resumen legible para un humano,
        // sin ningún dato que otro código necesite parsear.
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.ImportacionPlanillas,
            Entidad = "Importacion",
            EntidadId = dto.Ejercicio,
            IdLote = idImportacion,
            Detalle = $"Ejercicio: {dto.Ejercicio}; " +
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

    /// <summary>
    /// Reversa por lote (spec §2.7, Task 8): baja lógica de TODO lo que
    /// <see cref="ConfirmarAsync"/> creó o reactivó con este <paramref name="idImportacion"/>,
    /// en UNA transacción — mismo patrón (una tx, un solo SaveChangesAsync, commit al final) que
    /// ConfirmarAsync. Busca y marca "revertida" por <see cref="LogAuditoria.IdLote"/> (columna
    /// tipada, post-review de Task 6) — NO hay ningún parseo de texto embebido en Detalle.
    ///
    /// Gasto/IngresoCaja/LineaPoa del lote → Activo = false. PagoGasto del lote (el pago
    /// automático de contado que el propio importador creó, identificado por
    /// PagoGasto.IdImportacion — re-review IMPORTANT 2) → Activo = false también. Un pago MANUAL
    /// sobre un gasto importado (IdImportacion == null o de otro lote) NUNCA se toca: la reversa
    /// se BLOQUEA si encuentra uno (ver <see cref="ValidarSinPagosManuales"/>), mismo
    /// criterio que <see cref="StockApp.Application.Finanzas.GastoService.AnularAsync"/> aplica a
    /// la anulación individual (GastoService.cs:158-160). AsignacionPresupuestal NO tiene un
    /// Activo propio en el dominio: queda colgando de su LineaPoa inactiva, que es el estado
    /// correcto — nada en el sistema la filtra por separado de LineaPoa.Activo, así que no hay
    /// nada que tocar ahí salvo contarlas.
    ///
    /// MovimientoStock vinculado a un gasto revertido (re-review CRITICAL 1) → GastoId = null,
    /// mutando entidades trackeadas DENTRO de esta misma transacción (nunca
    /// GastoRepository.DesvincularMovimientosAsync: ese método hace su propio SaveChangesAsync,
    /// lo que rompería la regla de "un solo SaveChangesAsync" de este método). Sin esto, un
    /// movimiento asociado a un gasto importado (GastoService.AsociarMovimientosAsync no
    /// distingue el origen del gasto) quedaba atado para siempre a un GastoId inactivo: no se
    /// podía refacturar (el gasto ya "tiene" el movimiento) ni liberar (el gasto ya está
    /// anulado, y AnularAsync rechaza anular dos veces) — exactamente el estado sin salida que
    /// esta reversa existe para eliminar.
    ///
    /// Los maestros (Proveedor/FuenteFinanciamiento/RubroGasto) NUNCA se tocan: para cuando se
    /// ejecuta una reversa, esos maestros pueden estar ya referenciados por gastos cargados a
    /// mano en paralelo a la migración — un maestro de más es inocuo, un gasto huérfano no.
    /// </summary>
    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId)
    {
        // Existencia del lote (re-review Minor): se resuelve ANTES de abrir la transacción — el
        // camino 404 no paga el costo de una conexión + BEGIN + ROLLBACK al pedo. OrderByDescending
        // + FirstOrDefaultAsync (re-review Minor) da un resultado determinístico aunque, en la
        // práctica, cada corrida de ConfirmarAsync escribe un IdLote nuevo (Guid.NewGuid()), así
        // que nunca hay más de UN LogAuditoria de ImportacionPlanillas con el mismo IdLote.
        var logConfirmacion = await _ctx.LogsAuditoria
            .Where(l => l.Accion == AccionAuditada.ImportacionPlanillas && l.IdLote == idImportacion)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync();

        if (logConfirmacion is null)
            throw new EntidadNoEncontradaException(
                $"No existe ninguna importación con IdImportacion {idImportacion}.");

        await using var tx = await _ctx.Database.BeginTransactionAsync();

        // Advisory lock por EJERCICIO (re-review IMPORTANT 1), tomado con el MISMO recurso
        // (logConfirmacion.EntidadId, que ConfirmarAsync estampa con dto.Ejercicio) que
        // ConfirmarAsync toma en su propio pg_advisory_xact_lock — así ambos métodos se
        // serializan entre sí para el mismo ejercicio, no solo entre sí mismos. Sin esto: (1) dos
        // /revertir concurrentes sobre el mismo lote pasaban ambos el guard yaRevertida bajo READ
        // COMMITTED (ninguno ve el LogAuditoria no committeado del otro) y escribían DOS logs de
        // ReversionImportacion; (2) un /revertir concurrente con un /confirmar del mismo ejercicio
        // corría en paralelo sin ningún orden garantizado, dejando estados imposibles de
        // reconciliar (un gasto ACTIVO de una corrida nueva colgado de una LineaPoa que la reversa
        // de la corrida vieja dejó INACTIVA).
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({logConfirmacion.EntidadId})");

        // Sin tx.RollbackAsync() explícito (mismo criterio que el guard de re-importación de
        // ConfirmarAsync): el `await using var tx` de arriba hace rollback al disponerse cuando
        // la excepción se propaga.
        var yaRevertida = await _ctx.LogsAuditoria.AnyAsync(l =>
            l.Accion == AccionAuditada.ReversionImportacion && l.IdLote == idImportacion);

        if (yaRevertida)
            throw new ReglaDeNegocioException(
                $"La importación {idImportacion} ya fue revertida anteriormente.");

        var gastos = await _ctx.Gastos.Include(g => g.Proveedor).Include(g => g.Pagos)
            .Where(g => g.IdImportacion == idImportacion && g.Activo).ToListAsync();

        ValidarSinPagosManuales(gastos, idImportacion);

        var ingresos = await _ctx.IngresosCaja
            .Where(i => i.IdImportacion == idImportacion && i.Activo).ToListAsync();
        var lineasPoa = await _ctx.LineasPoa.Include(l => l.Asignaciones)
            .Where(l => l.IdImportacion == idImportacion && l.Activo).ToListAsync();

        var gastoIds = gastos.Select(g => g.Id).ToList();
        var movimientos = gastoIds.Count == 0
            ? new List<MovimientoStock>()
            : await _ctx.MovimientosStock
                .Where(m => m.GastoId != null && gastoIds.Contains(m.GastoId!.Value))
                .ToListAsync();

        var pagosRevertidos = 0;
        foreach (var gasto in gastos)
        {
            gasto.Activo = false;
            foreach (var pago in gasto.Pagos.Where(p => p.Activo))
            {
                pago.Activo = false;
                pagosRevertidos++;
            }
        }

        // CRITICAL 1 (re-review): desvincula los movimientos de stock de los gastos revertidos,
        // mutando entidades YA trackeadas por el _ctx.MovimientosStock.Where(...).ToListAsync() de
        // arriba — nada de esto dispara un SaveChangesAsync propio, se acumula en el mismo
        // ChangeTracker que todo lo demás y sale en el único SaveChangesAsync del final.
        foreach (var movimiento in movimientos)
            movimiento.GastoId = null;

        foreach (var ingreso in ingresos)
            ingreso.Activo = false;

        var asignacionesRevertidas = 0;
        foreach (var linea in lineasPoa)
        {
            linea.Activo = false;
            // AsignacionPresupuestal no tiene Activo propio (spec §2.7): queda colgando de una
            // LineaPoa inactiva, que es el estado correcto — nada que tocar acá salvo contarlas.
            //
            // Imprecisión de reporte conocida (re-review Minor, sin fix posible con el modelo
            // actual): si otra corrida agregó una asignación a esta MISMA LineaPoa mientras
            // seguía activa (el camino "línea activa + asignación nueva" de
            // GetOrCrearLineasPoaAsync, que a propósito NO re-estampa IdImportacion sobre una
            // línea que ya estaba activa), esa asignación se cuenta acá como "revertida" aunque
            // no la haya creado ESTE lote. AsignacionPresupuestal no tiene columna IdImportacion
            // propia (decisión de diseño: es hija de LineaPoa, no un agregado propio con
            // trazabilidad de lote) — no hay forma de distinguir su origen sin agregar una.
            asignacionesRevertidas += linea.Asignaciones.Count;
        }

        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.ReversionImportacion,
            Entidad = "Importacion",
            EntidadId = logConfirmacion.EntidadId,
            IdLote = idImportacion,
            Detalle = $"Gastos revertidos: {gastos.Count}; Pagos revertidos: {pagosRevertidos}; " +
                $"Ingresos revertidos: {ingresos.Count}; LineasPoa revertidas: {lineasPoa.Count}; " +
                $"Asignaciones revertidas: {asignacionesRevertidas}",
        });

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoReversionDto(
            idImportacion, gastos.Count, pagosRevertidos, ingresos.Count, lineasPoa.Count, asignacionesRevertidas);
    }

    /// <summary>
    /// Bloquea la reversa si algún gasto activo del lote tiene un pago ACTIVO que el importador
    /// NO creó (re-review IMPORTANT 2, decisión del usuario). "No creado por este lote" =
    /// PagoGasto.IdImportacion distinto de <paramref name="idImportacion"/> — cubre tanto un pago
    /// cargado a mano (IdImportacion == null) como, en teoría, uno de otro lote. Espeja
    /// GastoService.AnularAsync (GastoService.cs:158-160, "No se puede anular un gasto con pagos
    /// activos: primero anulá los pagos"): nunca destruye en silencio un registro de plata que
    /// efectivamente salió. El mensaje enumera proveedor + número de factura de cada gasto
    /// afectado para que un humano los ubique en el desktop, anule esos pagos ahí (operación
    /// normal, ya soportada) y reintente la reversa.
    /// </summary>
    private static void ValidarSinPagosManuales(IReadOnlyList<Gasto> gastos, Guid idImportacion)
    {
        var gastosConPagoManual = gastos
            .Where(g => g.Pagos.Any(p => p.Activo && p.IdImportacion != idImportacion))
            .ToList();

        if (gastosConPagoManual.Count == 0)
            return;

        var detalle = string.Join(", ", gastosConPagoManual.Select(g =>
        {
            var nombreProveedor = g.Proveedor?.Nombre ?? $"Proveedor {g.ProveedorId}";
            return $"{nombreProveedor} (factura {g.NumeroFactura ?? "s/n"})";
        }));

        throw new ReglaDeNegocioException(
            "No se puede revertir: los siguientes gastos tienen pagos que no fueron creados por " +
            "esta importación. Anulá esos pagos desde el desktop y reintentá la reversa: " + detalle);
    }

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
    /// Guard de re-importación (spec §2.6). Filtra en SQL por Accion + EntidadId (el Ejercicio,
    /// para no traer todo el historial de auditoría del sistema a memoria) y compara por
    /// LogAuditoria.IdLote — columna tipada con índice no único (post-review de Task 6), sin
    /// parseo de string ni EF.Functions.Like sobre Detalle. "No revertida" = no existe ningún
    /// LogAuditoria de AccionAuditada.ReversionImportacion (Task 8) con el mismo IdLote. Hasta
    /// que Task 8 exista, ninguna corrida puede estar revertida — esta consulta ya queda lista
    /// para cuando RevertirAsync empiece a escribir esos logs con Accion =
    /// AccionAuditada.ReversionImportacion e IdLote = el mismo Guid de la corrida que revierte.
    /// Ordenada por Id descendente: si hay más de una corrida sin revertir para el mismo
    /// ejercicio, se reporta la MÁS RECIENTE — es la que le sirve al humano que lee el 409 para
    /// saber qué lote revertir (re-review, Minor 3).
    /// </summary>
    private async Task<Guid?> BuscarImportacionNoRevertidaAsync(int ejercicio)
    {
        var confirmaciones = await _ctx.LogsAuditoria
            .Where(l => l.Accion == AccionAuditada.ImportacionPlanillas && l.EntidadId == ejercicio
                // IdLote != null (re-review, Important 1): el flujo real siempre lo estampa, pero
                // si alguna fila legacy o una escritura futura fuera de este flujo lo dejara en
                // null, "l.IdLote == idLote" con idLote = null se traduce a "IS NULL" en SQL — el
                // guard terminaría matcheando esa fila como "revertida" contra cualquier búsqueda
                // sin lote real, dejando pasar una re-importación que en realidad no lo fue.
                && l.IdLote != null)
            .OrderByDescending(l => l.Id)
            .Select(l => l.IdLote!.Value)
            .ToListAsync();

        if (confirmaciones.Count == 0)
            return null;

        // Colapsado a 2 queries (re-review, Minor 2): antes había un AnyAsync por confirmación,
        // corriendo con el advisory lock tomado. Acá se trae de una el set de IdLote revertidos
        // (mismo filtro IdLote != null, mismo motivo) y se compara en memoria.
        var revertidos = await _ctx.LogsAuditoria
            .Where(l => l.Accion == AccionAuditada.ReversionImportacion && l.IdLote != null)
            .Select(l => l.IdLote!.Value)
            .ToHashSetAsync();

        foreach (var idLote in confirmaciones)
        {
            if (!revertidos.Contains(idLote))
                return idLote;
        }

        return null;
    }

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
                    new()
                    {
                        Fecha = fechaUtc, Monto = gastoDto.MontoTotal, Nota = "Pago contado (importación)",
                        // IdImportacion (re-review IMPORTANT 2): estampa el pago automático de
                        // contado como "del lote" — es lo que distingue este pago de uno manual a
                        // la hora de revertir (RevertirAsync bloquea si encuentra un pago activo
                        // con IdImportacion distinto del lote que se está revirtiendo).
                        IdImportacion = idImportacion,
                    },
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
