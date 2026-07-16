using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

public class GastoService : IGastoService
{
    private readonly IGastoRepository                _repo;
    private readonly IProveedorRepository            _proveedores;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly IRubroGastoRepository           _rubros;
    private readonly ILineaPoaRepository             _lineasPoa;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public GastoService(
        IGastoRepository repo,
        IProveedorRepository proveedores,
        IFuenteFinanciamientoRepository fuentes,
        IRubroGastoRepository rubros,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo        = repo;
        _proveedores = proveedores;
        _fuentes     = fuentes;
        _rubros      = rubros;
        _lineasPoa   = lineasPoa;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
    }

    // ── Alta ──────────────────────────────────────────────────────────────────

    public async Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var linea = await ValidarAsync(gasto, esAlta: true, original: null);
        await ValidarFacturaUnicaAsync(gasto);
        var advertencia = await AdvertirSobregiroAsync(gasto, linea, excluyendoGastoId: null);

        // Contado ⇒ pago automático por el total en la fecha del gasto (spec §4)
        if (gasto.CondicionPago == CondicionPago.Contado)
            gasto.Pagos = new List<PagoGasto>
            {
                new() { Fecha = gasto.Fecha, Monto = gasto.MontoTotal, Nota = "Pago contado (automático)" },
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
            (movimientoIds is { Count: > 0 } ? $"; Movimientos vinculados: {movimientoIds.Count}" : string.Empty));

        return new ResultadoGastoDto(id, advertencia);
    }

    // ── Modificación ─────────────────────────────────────────────────────────

    public async Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var original = await _repo.ObtenerPorIdAsync(gasto.Id)
            ?? throw new EntidadNoEncontradaException($"Gasto {gasto.Id} no encontrado.");

        if (!original.Activo)
            throw new ReglaDeNegocioException("No se puede modificar un gasto anulado.");
        if (gasto.CondicionPago != original.CondicionPago)
            throw new ReglaDeNegocioException(
                "No se puede cambiar la condición de pago de un gasto registrado: anulalo y cargalo de nuevo.");

        var linea = await ValidarAsync(gasto, esAlta: false, original);

        if (gasto.MontoTotal < original.TotalPagado)
            throw new ReglaDeNegocioException(
                $"El monto total no puede quedar por debajo de lo ya pagado ({original.TotalPagado}).");

        if (!string.IsNullOrWhiteSpace(gasto.NumeroFactura)
            && (gasto.NumeroFactura != original.NumeroFactura || gasto.ProveedorId != original.ProveedorId))
            await ValidarFacturaUnicaAsync(gasto);

        var advertencia = await AdvertirSobregiroAsync(gasto, linea, excluyendoGastoId: gasto.Id);

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
        Comparar("Fuente", original.FuenteFinanciamientoId, gasto.FuenteFinanciamientoId);
        Comparar("Rubro", original.RubroGastoId, gasto.RubroGastoId);
        Comparar("Línea POA", original.LineaPoaId, gasto.LineaPoaId);
        Comparar("Vencimiento", original.FechaVencimiento, gasto.FechaVencimiento);

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
        original.FuenteFinanciamientoId = gasto.FuenteFinanciamientoId;
        original.RubroGastoId           = gasto.RubroGastoId;
        original.LineaPoaId             = gasto.LineaPoaId;
        original.FechaVencimiento       = gasto.FechaVencimiento;

        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ModificacionGasto, "Gasto", gasto.Id,
            string.Join("; ", cambios));

        return new ResultadoGastoDto(gasto.Id, advertencia);
    }

    // ── Anulación ────────────────────────────────────────────────────────────

    public async Task AnularAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException($"El gasto {id} ya está anulado.");
        if (gasto.Pagos.Any(p => p.Activo))
            throw new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anulá los pagos.");

        gasto.Activo = false;
        await _repo.ActualizarAsync(gasto);
        // Los movimientos quedan libres para re-facturar (el gasto anulado no los retiene)
        await _repo.DesvincularMovimientosAsync(id);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionGasto, "Gasto", id,
            $"Anulación de '{gasto.Detalle}' (factura {gasto.NumeroFactura ?? "s/n"}, monto {gasto.MontoTotal})");
    }

    // ── Pagos ────────────────────────────────────────────────────────────────

    public async Task<int> RegistrarPagoAsync(PagoGasto pago)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarPagos);

        if (pago.Monto <= 0)
            throw new ArgumentException("El monto del pago debe ser mayor a cero.");
        if (pago.Fecha == default)
            throw new ArgumentException("La fecha del pago es obligatoria.");

        var gasto = await _repo.ObtenerPorIdAsync(pago.GastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {pago.GastoId} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException("No se pueden registrar pagos sobre un gasto anulado.");
        if (pago.Monto > gasto.SaldoPendiente)
            throw new ReglaDeNegocioException(
                $"El pago ({pago.Monto}) supera el saldo pendiente de la factura ({gasto.SaldoPendiente}).");

        var pagoId = await _repo.AgregarPagoAsync(pago);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaPagoGasto, "PagoGasto", pagoId,
            $"Gasto: {pago.GastoId}; Monto: {pago.Monto}; Fecha: {pago.Fecha:yyyy-MM-dd}");

        return pagoId;
    }

    public async Task AnularPagoAsync(int gastoId, int pagoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarPagos);

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
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

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
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");
    }

    public async Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, numeroFactura);
    }

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
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

        var fuente = await _fuentes.ObtenerPorIdAsync(gasto.FuenteFinanciamientoId)
            ?? throw new EntidadNoEncontradaException(
                $"Fuente de financiamiento {gasto.FuenteFinanciamientoId} no encontrada.");
        if (!fuente.Activo && (esAlta || original!.FuenteFinanciamientoId != gasto.FuenteFinanciamientoId))
            throw new ReglaDeNegocioException($"La fuente de financiamiento '{fuente.Nombre}' está dada de baja.");

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

    private async Task ValidarFacturaUnicaAsync(Gasto gasto)
    {
        if (string.IsNullOrWhiteSpace(gasto.NumeroFactura))
            return;

        var existente = await _repo.ObtenerPorProveedorYFacturaAsync(gasto.ProveedorId, gasto.NumeroFactura!);
        if (existente is not null && existente.Id != gasto.Id)
            throw new ReglaDeNegocioException(
                $"Ya existe la factura '{gasto.NumeroFactura}' para ese proveedor.");
    }

    /// <summary>
    /// Sobregiro POA (spec §10): la fuente sin asignación en la línea es regla DURA (409);
    /// superar el presupuesto asignado solo ADVIERTE — la app avisa, el humano decide.
    /// </summary>
    private async Task<string?> AdvertirSobregiroAsync(Gasto gasto, LineaPoa? linea, int? excluyendoGastoId)
    {
        if (linea is null)
            return null;

        var asignacion = linea.Asignaciones
            .FirstOrDefault(a => a.FuenteFinanciamientoId == gasto.FuenteFinanciamientoId)
            ?? throw new ReglaDeNegocioException(
                $"La línea POA '{linea.Nombre}' no tiene asignación presupuestal " +
                "para la fuente de financiamiento seleccionada.");

        var gastado = await _repo.TotalGastadoLineaFuenteAsync(
            linea.Id, gasto.FuenteFinanciamientoId, excluyendoGastoId);
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
