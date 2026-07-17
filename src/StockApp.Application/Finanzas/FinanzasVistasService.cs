using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

public class FinanzasVistasService : IFinanzasVistasService
{
    private readonly IIngresoCajaRepository _ingresos;
    private readonly IGastoRepository       _gastos;
    private readonly ILineaPoaRepository    _lineasPoa;
    private readonly ICurrentSession        _session;
    private readonly IAuthorizationService  _auth;

    public FinanzasVistasService(
        IIngresoCajaRepository ingresos,
        IGastoRepository gastos,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth)
    {
        _ingresos  = ingresos;
        _gastos    = gastos;
        _lineasPoa = lineasPoa;
        _session   = session;
        _auth      = auth;
    }

    public async Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        if (mes is < 1 or > 12)
            throw new ArgumentException("El mes debe estar entre 1 y 12.");

        var desde = new DateTime(anio, mes, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddMonths(1).AddTicks(-1);

        var saldoInicial =
            await _ingresos.TotalActivosAntesDeAsync(desde) - await _gastos.TotalPagosActivosAntesDeAsync(desde);

        var ingresos = await _ingresos.ListarPorRangoAsync(desde, hasta);
        var pagos = await _gastos.ListarPagosActivosPorRangoAsync(desde, hasta);

        var crudos = ingresos
            .Select(i => (
                Fecha: i.Fecha, Tipo: "Ingreso", Concepto: i.Concepto,
                ProveedorNombre: (string?)null, NumeroFactura: (string?)null,
                FuenteNombre: i.FuenteFinanciamiento?.Nombre, RubroNombre: (string?)null,
                Ingreso: i.Monto, Egreso: 0m))
            .Concat(pagos.Select(p => (
                Fecha: p.Fecha, Tipo: "Egreso", Concepto: p.Gasto?.Detalle ?? string.Empty,
                ProveedorNombre: p.Gasto?.Proveedor?.Nombre, NumeroFactura: p.Gasto?.NumeroFactura,
                FuenteNombre: p.Gasto?.FuenteFinanciamiento?.Nombre, RubroNombre: p.Gasto?.RubroGasto?.Nombre,
                Ingreso: 0m, Egreso: p.Monto)))
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.Tipo)
            .ToList();

        var movimientos = new List<MovimientoCajaDto>(crudos.Count);
        var corrido = saldoInicial;
        foreach (var x in crudos)
        {
            corrido += x.Ingreso - x.Egreso;
            movimientos.Add(new MovimientoCajaDto(
                DateOnly.FromDateTime(x.Fecha), x.Tipo, x.Concepto, x.ProveedorNombre, x.NumeroFactura,
                x.FuenteNombre, x.RubroNombre, x.Ingreso, x.Egreso, corrido));
        }

        var totalesPorRubro = pagos
            .GroupBy(p => p.Gasto?.RubroGasto?.Nombre ?? "(sin rubro)")
            .Select(g => new TotalPorClaveDto(g.Key, g.Sum(p => p.Monto)))
            .OrderByDescending(t => t.Total)
            .ToList();

        var clavesFuente = ingresos.Select(i => i.FuenteFinanciamiento?.Nombre ?? "(sin fuente)")
            .Concat(pagos.Select(p => p.Gasto?.FuenteFinanciamiento?.Nombre ?? "(sin fuente)"))
            .Distinct();
        var totalesPorFuente = clavesFuente
            .Select(clave => new TotalPorClaveDto(
                clave,
                ingresos.Where(i => (i.FuenteFinanciamiento?.Nombre ?? "(sin fuente)") == clave).Sum(i => i.Monto)
                - pagos.Where(p => (p.Gasto?.FuenteFinanciamiento?.Nombre ?? "(sin fuente)") == clave).Sum(p => p.Monto)))
            .OrderByDescending(t => t.Total)
            .ToList();

        return new LibroCajaMesDto(
            anio, mes, saldoInicial, corrido, movimientos, totalesPorRubro, totalesPorFuente);
    }

    public async Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var desde = new DateTime(anio, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddYears(1).AddTicks(-1);

        var ingresos = await _ingresos.ListarPorRangoAsync(desde, hasta);
        var pagos = await _gastos.ListarPagosActivosPorRangoAsync(desde, hasta);

        var totalesPorMes = Enumerable.Range(1, 12)
            .Select(mes =>
            {
                var ingresosMes = ingresos.Where(i => i.Fecha.Month == mes).Sum(i => i.Monto);
                var egresosMes = pagos.Where(p => p.Fecha.Month == mes).Sum(p => p.Monto);
                return new TotalMensualDto(mes, ingresosMes, egresosMes, ingresosMes - egresosMes);
            })
            .ToList();

        var totalesPorRubro = pagos
            .GroupBy(p => p.Gasto?.RubroGasto?.Nombre ?? "(sin rubro)")
            .Select(g => new TotalPorClaveDto(g.Key, g.Sum(p => p.Monto)))
            .OrderByDescending(t => t.Total)
            .ToList();

        return new LibroCajaAnualDto(anio, totalesPorMes, totalesPorRubro);
    }

    public async Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var lineas = (await _lineasPoa.ListarTodasAsync()).Where(l => l.Ejercicio == ejercicio).ToList();
        var gastadoPorLinea = await _gastos.TotalGastadoPorLineaAsync(ejercicio);

        return lineas
            .Select(l =>
            {
                var presupuesto = l.Asignaciones.Sum(a => a.Monto);
                var gastado = gastadoPorLinea.TryGetValue(l.Id, out var g) ? g : 0m;
                var saldo = presupuesto - gastado;
                var porcentaje = presupuesto == 0m ? 0m : Math.Round(gastado / presupuesto * 100m, 2);
                return new ControlPoaLineaDto(
                    l.Id, l.Nombre, l.Programa, l.Ejercicio, presupuesto, gastado, saldo, porcentaje, saldo < 0m);
            })
            .OrderBy(d => d.Nombre)
            .ToList();
    }

    public async Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var hoy = (fechaReferencia ?? DateTime.UtcNow).Date;
        var gastos = await _gastos.ListarActivosConSaldoAsync();

        var pendientesConVencimiento = gastos
            .Where(g => g.CondicionPago == CondicionPago.Credito
                        && g.FechaVencimiento is not null
                        && g.CalcularEstado(hoy) is EstadoGasto.Pendiente or EstadoGasto.Parcial or EstadoGasto.Vencida)
            .ToList();

        var vencidas = pendientesConVencimiento
            .Where(g => g.FechaVencimiento!.Value.Date < hoy)
            .Select(g => AFacturaDto(g, hoy))
            .OrderBy(f => f.FechaVencimiento)
            .ToList();
        var aVencer7 = pendientesConVencimiento
            .Where(g => g.FechaVencimiento!.Value.Date >= hoy && g.FechaVencimiento.Value.Date <= hoy.AddDays(7))
            .Select(g => AFacturaDto(g, hoy))
            .OrderBy(f => f.FechaVencimiento)
            .ToList();
        var aVencer30 = pendientesConVencimiento
            .Where(g => g.FechaVencimiento!.Value.Date > hoy.AddDays(7) && g.FechaVencimiento.Value.Date <= hoy.AddDays(30))
            .Select(g => AFacturaDto(g, hoy))
            .OrderBy(f => f.FechaVencimiento)
            .ToList();

        var pagosRecientes = gastos
            .SelectMany(g => g.Pagos
                .Where(p => p.Activo && p.Fecha.Date <= hoy && p.Fecha.Date >= hoy.AddDays(-7))
                .Select(p => new PagoRecienteDto(
                    g.Id, g.Proveedor?.Nombre ?? string.Empty, g.NumeroFactura,
                    DateOnly.FromDateTime(p.Fecha), p.Monto)))
            .OrderByDescending(p => p.FechaPago)
            .ToList();

        return new CalendarioPagosDto(vencidas, aVencer7, aVencer30, pagosRecientes);
    }

    private static FacturaCalendarioDto AFacturaDto(Gasto g, DateTime hoy) => new(
        g.Id, g.Proveedor?.Nombre ?? string.Empty, g.NumeroFactura,
        g.SaldoPendiente, DateOnly.FromDateTime(g.FechaVencimiento!.Value), g.CalcularEstado(hoy).ToString());
}
