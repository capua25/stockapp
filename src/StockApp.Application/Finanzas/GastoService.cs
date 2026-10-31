using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Domain.Formato;

namespace StockApp.Application.Finanzas;

public class GastoService : IGastoService
{
    private readonly IGastoRepository                _repo;
    private readonly IProveedorRepository            _proveedores;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly IRubroGastoRepository           _rubros;
    private readonly ILineaPoaRepository             _lineasPoa;
    private readonly IMovimientoStockRepository      _movRepo;
    private readonly IProyectoRepository             _proyectos;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;
    private readonly IVersionReportes                _version;

    public GastoService(
        IGastoRepository repo,
        IProveedorRepository proveedores,
        IFuenteFinanciamientoRepository fuentes,
        IRubroGastoRepository rubros,
        ILineaPoaRepository lineasPoa,
        IMovimientoStockRepository movRepo,
        IProyectoRepository proyectos,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit,
        IVersionReportes version)
    {
        _repo        = repo;
        _proveedores = proveedores;
        _fuentes     = fuentes;
        _rubros      = rubros;
        _lineasPoa   = lineasPoa;
        _movRepo     = movRepo;
        _proyectos   = proyectos;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
        _version     = version;
    }

    // ── Alta ──────────────────────────────────────────────────────────────────

    public async Task<ResultadoGastoDto> AltaAsync(
        Gasto gasto, PagoContadoDto? pagoContado, IReadOnlyList<int>? movimientoIds = null)
    {
        _auth.Verificar(_session, Permisos.RegistrarGastos);

        var linea = await ValidarAsync(gasto, esAlta: true, original: null);
        await ValidarCajaYLiteralAsync(gasto, pagoContado, original: null);
        await ImputacionProyectoGasto.ValidarAsync(_proyectos, gasto.ProyectoId);
        await ValidarFacturaUnicaAsync(gasto);
        var advertencia = await AdvertirSobregiroAsync(
            gasto, linea, pagoContado?.FuenteFinanciamientoId, excluyendoGastoId: null);

        // Contado ⇒ pago automático por el total en la fecha del gasto (spec §4), de la caja y el
        // literal elegidos
        if (gasto.CondicionPago == CondicionPago.Contado)
            gasto.Pagos = new List<PagoGasto>
            {
                PagoGasto.Automatico(
                    gasto.Fecha, gasto.MontoTotal, "Pago contado (automático)",
                    pagoContado!.Caja, pagoContado.FuenteFinanciamientoId),
            };

        if (movimientoIds is { Count: > 0 })
            await ValidarMovimientosAsync(movimientoIds);

        var id = await _repo.AgregarAsync(gasto);

        if (movimientoIds is { Count: > 0 })
            await _repo.AsignarGastoAMovimientosAsync(id, movimientoIds);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaGasto, "Gasto", id,
            $"Proveedor: {gasto.ProveedorId}; Factura: {gasto.NumeroFactura ?? "(sin factura)"}; " +
            $"Monto: {gasto.MontoTotal}; Condición: {gasto.CondicionPago}" +
            (pagoContado is null ? string.Empty : $"; Caja: {pagoContado.Caja.Nombre()}; Fuente: {pagoContado.FuenteFinanciamientoId}") +
            (movimientoIds is { Count: > 0 } ? $"; Movimientos vinculados: {movimientoIds.Count}" : string.Empty));

        return new ResultadoGastoDto(id, advertencia);
    }

    // ── Modificación ─────────────────────────────────────────────────────────

    public async Task<ResultadoGastoDto> ModificarAsync(Gasto gasto, PagoContadoDto? pagoContado)
    {
        _auth.Verificar(_session, Permisos.RegistrarGastos);

        var original = await _repo.ObtenerPorIdAsync(gasto.Id)
            ?? throw new EntidadNoEncontradaException($"Gasto {gasto.Id} no encontrado.");

        if (!original.Activo)
            throw new ReglaDeNegocioException("No se puede modificar un gasto anulado.");
        if (gasto.CondicionPago != original.CondicionPago)
            throw new ReglaDeNegocioException(
                "No se puede cambiar la condición de pago de un gasto registrado: anulalo y cargalo de nuevo.");

        var linea = await ValidarAsync(gasto, esAlta: false, original);
        await ValidarCajaYLiteralAsync(gasto, pagoContado, original);
        await ImputacionProyectoGasto.ValidarAsync(_proyectos, gasto.ProyectoId, original.ProyectoId);

        if (gasto.MontoTotal < original.TotalPagado)
            throw new ReglaDeNegocioException(
                $"El monto total no puede quedar por debajo de lo ya pagado ({FormatoEsUy.Moneda(original.TotalPagado)}).");

        // F5c: NumeroOrden es parte de la clave de unicidad (Proveedor, Factura, Orden), así que
        // un cambio de SOLO el orden (factura y proveedor iguales) también puede generar una
        // colisión nueva contra otro gasto activo — re-validar en los tres casos, no solo en los
        // dos de antes.
        if (!string.IsNullOrWhiteSpace(gasto.NumeroFactura)
            && (gasto.NumeroFactura != original.NumeroFactura
                || gasto.ProveedorId != original.ProveedorId
                || gasto.NumeroOrden != original.NumeroOrden))
            await ValidarFacturaUnicaAsync(gasto);

        var advertencia = await AdvertirSobregiroAsync(
            gasto, linea, pagoContado?.FuenteFinanciamientoId, excluyendoGastoId: gasto.Id);

        var cambios = new List<string>();
        void Comparar<T>(string campo, T viejo, T nuevo)
        {
            if (!EqualityComparer<T>.Default.Equals(viejo, nuevo))
                cambios.Add($"{campo}: {viejo} → {nuevo}");
        }

        Comparar("Proveedor", original.ProveedorId, gasto.ProveedorId);
        Comparar("Factura", original.NumeroFactura, gasto.NumeroFactura);
        Comparar("Orden", original.NumeroOrden, gasto.NumeroOrden);
        Comparar("Detalle", original.Detalle, gasto.Detalle);
        Comparar("Destino", original.Destino, gasto.Destino);
        Comparar("Fecha", original.Fecha, gasto.Fecha);
        Comparar("Monto", original.MontoTotal, gasto.MontoTotal);
        Comparar("Rubro", original.RubroGastoId, gasto.RubroGastoId);
        Comparar("Línea POA", original.LineaPoaId, gasto.LineaPoaId);
        Comparar("Vencimiento", original.FechaVencimiento, gasto.FechaVencimiento);
        // El proyecto va por título: un id suelto no le dice nada a quien lee la Auditoría.
        if (gasto.ProyectoId != original.ProyectoId)
            Comparar("Proyecto", await TituloProyectoAsync(original.ProyectoId, original.Proyecto),
                await TituloProyectoAsync(gasto.ProyectoId, null));

        // La caja y el literal de un gasto de contado viven en su pago automático (ya validado arriba).
        var automatico = pagoContado is null ? null : original.PagoAutomatico;
        if (automatico is not null)
        {
            Comparar("Caja", automatico.Caja.Nombre(), pagoContado!.Caja.Nombre());
            Comparar("Fuente", automatico.FuenteFinanciamientoId, pagoContado.FuenteFinanciamientoId);
        }

        if (cambios.Count == 0)
            return new ResultadoGastoDto(gasto.Id, null);

        // Solo se tocan las FKs, NUNCA las navs: cambiar la nav a null en una instancia
        // tracked haría que el fixup de EF pise el FK nuevo. Con la nav intacta y el FK
        // modificado, EF da precedencia al FK (comportamiento documentado de DetectChanges).
        original.ProveedorId            = gasto.ProveedorId;
        original.NumeroFactura          = gasto.NumeroFactura;
        original.NumeroOrden            = gasto.NumeroOrden;
        original.Detalle                = gasto.Detalle;
        original.Destino                = gasto.Destino;
        original.Fecha                  = gasto.Fecha;
        original.MontoTotal             = gasto.MontoTotal;
        original.RubroGastoId           = gasto.RubroGastoId;
        original.LineaPoaId             = gasto.LineaPoaId;
        original.FechaVencimiento       = gasto.FechaVencimiento;
        original.ProyectoId             = gasto.ProyectoId;

        // Solo la FK y la caja, nunca la nav del pago (mismo criterio que arriba).
        if (automatico is not null)
        {
            automatico.Caja                   = pagoContado!.Caja;
            automatico.FuenteFinanciamientoId = pagoContado.FuenteFinanciamientoId;
        }

        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ModificacionGasto, "Gasto", gasto.Id,
            string.Join("; ", cambios));

        return new ResultadoGastoDto(gasto.Id, advertencia);
    }

    private async Task<string> TituloProyectoAsync(int? proyectoId, Proyecto? cargado)
    {
        if (proyectoId is not int id)
            return "(sin proyecto)";
        var proyecto = cargado ?? await _proyectos.ObtenerPorIdAsync(id);
        return proyecto?.Titulo ?? $"proyecto {id}";
    }

    // ── Anulación ────────────────────────────────────────────────────────────

    public async Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
    {
        _auth.Verificar(_session, Permisos.RegistrarGastos);

        var gasto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException($"El gasto {id} ya está anulado.");

        // Guard unificado (Gasto.PagosAutomaticosADarDeBajaEnAnulacion): bloquea siempre si
        // hay algún pago MANUAL activo; si el único activo es el automático de contado, exige
        // confirmarAnulacionDePagoAutomatico y devuelve los pagos a dar de baja en cascada —
        // recién se anulan más abajo, DESPUÉS de que la anulación del gasto en sí haya
        // efectivamente sucedido (nunca antes: si el asiento inverso falla por stock
        // insuficiente, el pago automático tiene que seguir activo).
        var pagosAutomaticosADarDeBaja = gasto.PagosAutomaticosADarDeBajaEnAnulacion(confirmarAnulacionDePagoAutomatico);

        if (await _movRepo.ExistenMovimientosDeGastoAsync(id))
        {
            // Decisión 9 del spec: un gasto CON movimientos asociados se anula por asiento
            // inverso — la misma ruta atómica que IIngresoPorFacturaService.AnularLoteAsync
            // (Task 5) — para que el stock se revierta en vez de quedar sumado con el gasto
            // desvinculado (el agujero que tenía DesvincularMovimientosAsync).
            var detalle = $"Anulación de '{gasto.Detalle}' (factura {gasto.NumeroFactura ?? "s/n"}, monto {gasto.MontoTotal})";
            var resultado = await _movRepo.AnularIngresoPorFacturaAtomicoAsync(
                id, _session.UsuarioActual!.Id, detalle);

            // GastoYaAnulado: la re-verificación bajo lock DENTRO de la transacción del repo
            // detectó que el gasto ya no está activo (cierra la ventana de doble anulación
            // concurrente que el chequeo de arriba, hecho ANTES de entrar a la transacción, no
            // puede cerrar por sí solo) — el repo NO escribió nada en ese caso. Mismo mensaje
            // que el chequeo optimista de arriba, por consistencia (mismo patrón que
            // IngresoPorFacturaService.AnularLoteAsync).
            if (resultado.Estado == ResultadoAnulacionIngresoEstado.GastoYaAnulado)
                throw new ReglaDeNegocioException($"El gasto {id} ya está anulado.");

            if (resultado.Estado == ResultadoAnulacionIngresoEstado.StockInsuficiente)
            {
                var detalleFaltantes = string.Join("; ", resultado.Faltantes.Select(f =>
                    $"{f.ProductoNombre}: stock {FormatoEsUy.Cantidad(f.StockActual)}, necesita {FormatoEsUy.Cantidad(f.CantidadNecesaria)}"));
                throw new ReglaDeNegocioException(
                    $"No se puede anular: stock insuficiente en {resultado.Faltantes.Count} producto(s). {detalleFaltantes}");
            }

            // Estado Ok: AnularIngresoPorFacturaAtomicoAsync ya marcó Gasto.Activo=false y
            // escribió su propio LogAuditoria (AnulacionIngresoPorFactura) dentro de la misma
            // transacción. Esta rama SÍ mutó StockActual (asiento inverso de N productos) — el
            // caché de reportes de Valorización queda stale si no se invalida (Fix 3, revisión
            // final: IngresoPorFacturaService.AnularLoteAsync ya invalida en el mismo camino
            // atómico; a este service le faltaba porque hasta ahora nunca tocaba stock).
            await AnularPagosAutomaticosAsync(id, pagosAutomaticosADarDeBaja);
            _version.Invalidar();
            return;
        }

        await AnularPagosAutomaticosAsync(id, pagosAutomaticosADarDeBaja);

        gasto.Activo = false;
        await _repo.ActualizarAsync(gasto);
        // Los movimientos quedan libres para re-facturar (el gasto anulado no los retiene)
        await _repo.DesvincularMovimientosAsync(id);
        // Rama SIN movimientos: no toca StockActual (mismo criterio que
        // FuenteFinanciamientoService — "ese caché versiona solo reportes de stock"), así que NO
        // se invalida acá.

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionGasto, "Gasto", id,
            $"Anulación de '{gasto.Detalle}' (factura {gasto.NumeroFactura ?? "s/n"}, monto {gasto.MontoTotal})");
    }

    /// <summary>
    /// Da de baja en cascada los pagos automáticos de contado devueltos por
    /// Gasto.PagosAutomaticosADarDeBajaEnAnulacion (0 o 1 en la práctica — Contado crea uno
    /// solo). Misma AccionAuditada que AnularPagoAsync (anulación manual de un pago): es la
    /// misma operación de dominio, solo que la dispara la anulación del gasto en cascada en
    /// vez de un click directo del operador sobre el pago.
    /// </summary>
    private async Task AnularPagosAutomaticosAsync(int gastoId, IReadOnlyList<PagoGasto> pagosAutomaticos)
    {
        foreach (var pago in pagosAutomaticos)
        {
            pago.Activo = false;
            await _repo.ActualizarPagoAsync(pago);

            await _audit.RegistrarAsync(
                _session.UsuarioActual!.Id, AccionAuditada.AnulacionPagoGasto, "PagoGasto", pago.Id,
                $"Gasto: {gastoId}; Monto anulado: {pago.Monto} (anulación automática en cascada)");
        }
    }

    // ── Pagos ────────────────────────────────────────────────────────────────

    public async Task<int> RegistrarPagoAsync(PagoGasto pago)
    {
        _auth.Verificar(_session, Permisos.RegistrarPagos);

        if (pago.Monto <= 0)
            throw new ArgumentException("El monto del pago debe ser mayor a cero.");
        if (pago.Fecha == default)
            throw new ArgumentException("La fecha del pago es obligatoria.");

        // El pago manual es siempre de la caja Local: lo que venga en Caja se ignora, y el literal
        // tiene que ser de esa caja.
        pago.Caja = Caja.Local;
        await CajaYLiteral.ValidarFuenteAsync(_fuentes, Caja.Local, pago.FuenteFinanciamientoId, cambio: true);

        // Un gasto contado POA que perdió su pago automático (anulado) queda con saldo pendiente: un pago
        // manual, que es Local, haría pagar un gasto POA desde la caja Local.
        if ((await _repo.ObtenerPorIdAsync(pago.GastoId))?.PagoAutomatico is { Caja: Caja.Poa })
            throw new ReglaDeNegocioException(CajaYLiteral.MensajePagoManualEnGastoPoa);

        // La existencia del gasto, el estado (anulado) y el saldo pendiente se re-verifican
        // DENTRO de la transacción atómica del repo (FOR UPDATE) — evita el check-then-insert
        // que permitía sobrepago con dos pagos concurrentes sobre el mismo gasto.
        var pagoId = await _repo.RegistrarPagoAtomicoAsync(pago);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaPagoGasto, "PagoGasto", pagoId,
            $"Gasto: {pago.GastoId}; Monto: {pago.Monto}; Fecha: {pago.Fecha:yyyy-MM-dd}; " +
            $"Caja: {pago.Caja.Nombre()}; Fuente: {pago.FuenteFinanciamientoId}");

        return pagoId;
    }

    public async Task AnularPagoAsync(int gastoId, int pagoId)
    {
        _auth.Verificar(_session, Permisos.RegistrarPagos);

        var gasto = await _repo.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {gastoId} no encontrado.");
        var pago = gasto.Pagos.FirstOrDefault(p => p.Id == pagoId)
            ?? throw new EntidadNoEncontradaException($"Pago {pagoId} no encontrado en el gasto {gastoId}.");

        if (!pago.Activo)
            throw new ReglaDeNegocioException($"El pago {pagoId} ya está anulado.");

        pago.Activo = false;
        await _repo.ActualizarPagoAsync(pago);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionPagoGasto, "PagoGasto", pagoId,
            $"Gasto: {gastoId}; Monto anulado: {pago.Monto}");
    }

    // ── Vínculo con movimientos de stock ─────────────────────────────────────

    public async Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
    {
        _auth.Verificar(_session, Permisos.RegistrarGastos);

        var gasto = await _repo.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {gastoId} no encontrado.");
        if (!gasto.Activo)
            throw new ReglaDeNegocioException("No se pueden asociar movimientos a un gasto anulado.");

        await ValidarMovimientosAsync(movimientoIds);
        await _repo.AsignarGastoAMovimientosAsync(gastoId, movimientoIds);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AsociacionMovimientosAGasto, "Gasto", gastoId,
            $"Movimientos vinculados: {string.Join(", ", movimientoIds)}");
    }

    // ── Lecturas ─────────────────────────────────────────────────────────────

    public async Task<Gasto> ObtenerPorIdAsync(int id)
    {
        _auth.Verificar(_session, Permisos.VerFinanzas);
        return await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");
    }

    public async Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden)
    {
        _auth.Verificar(_session, Permisos.VerFinanzas);
        return await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, numeroFactura, numeroOrden);
    }

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        _auth.Verificar(_session, Permisos.VerFinanzas);
        return await _repo.ListarAsync(filtro);
    }

    // ── Validaciones privadas ────────────────────────────────────────────────

    /// <summary>
    /// Valida input + existencia/actividad de los maestros. Devuelve la LineaPoa cargada
    /// (con asignaciones) si el gasto la tiene, para reutilizarla en el chequeo de sobregiro.
    /// En modificación, un maestro inactivo solo se rechaza si el campo CAMBIÓ (los
    /// históricos se conservan — spec §10).
    /// </summary>
    private async Task<LineaPoa?> ValidarAsync(Gasto gasto, bool esAlta, Gasto? original)
    {
        if (string.IsNullOrWhiteSpace(gasto.Detalle))
            throw new ArgumentException("El detalle del gasto es obligatorio.");
        if (gasto.MontoTotal <= 0)
            throw new ArgumentException("El monto total del gasto debe ser mayor a cero.");
        if (gasto.Fecha == default)
            throw new ArgumentException("La fecha del gasto es obligatoria.");

        if (gasto.CondicionPago == CondicionPago.Credito && gasto.FechaVencimiento is null)
            throw new ReglaDeNegocioException("Un gasto a crédito exige fecha de vencimiento.");
        if (gasto.CondicionPago == CondicionPago.Contado && gasto.FechaVencimiento is not null)
            throw new ReglaDeNegocioException("Un gasto de contado no lleva fecha de vencimiento.");

        var proveedor = await _proveedores.ObtenerPorIdAsync(gasto.ProveedorId)
            ?? throw new EntidadNoEncontradaException($"Proveedor {gasto.ProveedorId} no encontrado.");
        if (!proveedor.Activo && (esAlta || original!.ProveedorId != gasto.ProveedorId))
            throw new ReglaDeNegocioException($"El proveedor '{proveedor.Nombre}' está dado de baja.");

        var rubro = await _rubros.ObtenerPorIdAsync(gasto.RubroGastoId)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {gasto.RubroGastoId} no encontrado.");
        if (!rubro.Activo && (esAlta || original!.RubroGastoId != gasto.RubroGastoId))
            throw new ReglaDeNegocioException($"El rubro '{rubro.Nombre}' está dado de baja.");

        if (gasto.LineaPoaId is null)
            return null;

        var linea = await _lineasPoa.ObtenerPorIdAsync(gasto.LineaPoaId.Value)
            ?? throw new EntidadNoEncontradaException($"Línea POA {gasto.LineaPoaId} no encontrada.");
        if (!linea.Activo && (esAlta || original!.LineaPoaId != gasto.LineaPoaId))
            throw new ReglaDeNegocioException($"La línea POA '{linea.Nombre}' está dada de baja.");

        return linea;
    }

    /// <summary>
    /// Espeja el índice único parcial (Proveedor, Factura, Orden) de la base (migración
    /// AmpliaIndiceFacturaConNumeroOrden — F5c): dos gastos activos del mismo proveedor pueden
    /// compartir número de factura si el número de orden difiere (caso real de la planilla 2026,
    /// una factura imputada en varias partes — docs/finanzas-facturas-duplicadas-planilla-2026.md).
    /// Mismo criterio de normalización que antes: ninguno — el match es por igualdad cruda,
    /// como el índice de base (Postgres case-sensitive, sin Trim ni ToUpper).
    /// </summary>
    private async Task ValidarFacturaUnicaAsync(Gasto gasto)
    {
        if (string.IsNullOrWhiteSpace(gasto.NumeroFactura))
            return;

        var existente = await _repo.ObtenerPorProveedorYFacturaAsync(
            gasto.ProveedorId, gasto.NumeroFactura!, gasto.NumeroOrden);
        if (existente is not null && existente.Id != gasto.Id)
            throw new ReglaDeNegocioException(
                $"Ya existe la factura '{gasto.NumeroFactura}' para ese proveedor.");
    }

    /// <summary>
    /// Caja y literal del gasto de contado (viven en su pago automático) y regla de la línea POA. Crédito no
    /// lleva caja ni literal; contado los exige. En modificación, el literal solo se re-valida contra la caja
    /// (y la baja) si cambió la caja o el literal, y la línea solo si cambió ella o el pago: un gasto viejo de
    /// caja Local con línea POA tiene que poder editar otros campos.
    /// </summary>
    private async Task ValidarCajaYLiteralAsync(Gasto gasto, PagoContadoDto? pagoContado, Gasto? original)
    {
        var cambioPago = false;
        if (gasto.CondicionPago == CondicionPago.Credito)
        {
            if (pagoContado is not null)
                throw new ReglaDeNegocioException(CajaYLiteral.MensajeCreditoConCajaOFuente);
        }
        else
        {
            if (pagoContado is null)
                throw new ReglaDeNegocioException(CajaYLiteral.MensajeContadoSinCajaOFuente);

            var automatico = original?.PagoAutomatico;
            if (original is not null && automatico is null)
                throw new ReglaDeNegocioException(CajaYLiteral.MensajeContadoSinPagoAutomatico);

            cambioPago = automatico is null
                || automatico.Caja != pagoContado.Caja
                || automatico.FuenteFinanciamientoId != pagoContado.FuenteFinanciamientoId;
            await CajaYLiteral.ValidarFuenteAsync(
                _fuentes, pagoContado.Caja, pagoContado.FuenteFinanciamientoId, cambioPago);
        }

        if (gasto.LineaPoaId is not null
            && (original is null || original.LineaPoaId != gasto.LineaPoaId || cambioPago))
            CajaYLiteral.ValidarLineaPoa(gasto.LineaPoaId, gasto.CondicionPago, pagoContado?.Caja);
    }

    /// <summary>
    /// Sobregiro POA (spec §10): el literal sin asignación en la línea es regla DURA (409);
    /// superar el presupuesto asignado solo ADVIERTE — la app avisa, el humano decide. El literal es el
    /// del pago de contado: sin línea o sin literal (crédito) no hay nada que medir.
    /// </summary>
    private async Task<string?> AdvertirSobregiroAsync(
        Gasto gasto, LineaPoa? linea, int? fuenteFinanciamientoId, int? excluyendoGastoId)
    {
        if (linea is null || fuenteFinanciamientoId is null)
            return null;

        var asignacion = linea.Asignaciones
            .FirstOrDefault(a => a.FuenteFinanciamientoId == fuenteFinanciamientoId)
            ?? throw new ReglaDeNegocioException(
                $"La línea POA '{linea.Nombre}' no tiene asignación presupuestal " +
                "para la fuente de financiamiento seleccionada.");

        var gastado = await _repo.TotalGastadoLineaFuenteAsync(
            linea.Id, fuenteFinanciamientoId.Value, excluyendoGastoId);
        var restante = asignacion.Monto - gastado - gasto.MontoTotal;

        return restante >= 0
            ? null
            : $"Atención: la línea POA '{linea.Nombre}' queda sobregirada en {Math.Abs(restante):0.##} " +
              "para esa fuente de financiamiento. El gasto se registra igual.";
    }

    private async Task ValidarMovimientosAsync(IReadOnlyList<int> movimientoIds)
    {
        var movimientos = await _repo.ObtenerMovimientosAsync(movimientoIds);
        if (movimientos.Count != movimientoIds.Distinct().Count())
            throw new EntidadNoEncontradaException("Alguno de los movimientos de stock a asociar no existe.");
        if (movimientos.Any(m => m.Tipo != TipoMovimiento.Entrada))
            throw new ReglaDeNegocioException(
                "Solo se pueden asociar a una factura movimientos de ENTRADA de stock.");
        if (movimientos.Any(m => m.GastoId is not null))
            throw new ReglaDeNegocioException(
                "Alguno de los movimientos ya está asociado a otra factura.");
    }
}
