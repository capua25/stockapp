using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

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

    public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotImplementedException();

    public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio) => throw new NotImplementedException();

    public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) => throw new NotImplementedException();
}
