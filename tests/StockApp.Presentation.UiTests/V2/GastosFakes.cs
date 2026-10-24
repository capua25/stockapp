using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>IGastoService con los tipos reales. ListarAsync graba cada filtro y responde, en este orden: Falla, Pendiente
/// o Lista. Alta y Modificar graban o lanzan FallaAlGuardar (lo que ApiErrores reconstruye de un 409 o un 400).</summary>
internal sealed class GastosFake : IGastoService
{
    public List<Gasto> Lista { get; set; } = new();
    public TaskCompletionSource<IReadOnlyList<Gasto>>? Pendiente { get; set; }
    public Exception? Falla { get; set; }
    public List<GastoFiltro> Filtros { get; } = new();
    public List<int> Anulados { get; } = new();
    public List<Gasto> Altas { get; } = new();
    public List<Gasto> Modificaciones { get; } = new();
    /// <summary>El pago de contado que llegó con cada alta o modificación, en orden (null a crédito).</summary>
    public List<PagoContadoDto?> PagosContado { get; } = new();
    public Exception? FallaAlGuardar { get; set; }

    public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        Filtros.Add(filtro);
        return Falla is not null ? Task.FromException<IReadOnlyList<Gasto>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<Gasto>>(Lista.ToList());
    }

    public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
    {
        Anulados.Add(id);
        return Task.CompletedTask;
    }

    public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, PagoContadoDto? pagoContado, IReadOnlyList<int>? movimientoIds = null)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException<ResultadoGastoDto>(FallaAlGuardar);
        Altas.Add(gasto);
        PagosContado.Add(pagoContado);
        return Task.FromResult(new ResultadoGastoDto(99, null));
    }

    public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto, PagoContadoDto? pagoContado)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException<ResultadoGastoDto>(FallaAlGuardar);
        Modificaciones.Add(gasto);
        PagosContado.Add(pagoContado);
        return Task.FromResult(new ResultadoGastoDto(gasto.Id, null));
    }

    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden) =>
        Task.FromResult<Gasto?>(null);

    public Task<Gasto> ObtenerPorIdAsync(int id) => throw new NotSupportedException();
    public Task<int> RegistrarPagoAsync(PagoGasto pago) => throw new NotSupportedException();
    public Task AnularPagoAsync(int gastoId, int pagoId) => throw new NotSupportedException();
    public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
}

/// <summary>IProveedorService que puede fallar: la carga de combos del listado y del formulario empieza por proveedores.</summary>
internal sealed class ProveedoresFake : IProveedorService
{
    public List<Proveedor> Activos { get; set; } = new();
    public Exception? Falla { get; set; }

    public Task<IReadOnlyList<Proveedor>> ListarActivasAsync() =>
        Falla is not null ? Task.FromException<IReadOnlyList<Proveedor>>(Falla)
            : Task.FromResult<IReadOnlyList<Proveedor>>(Activos.ToList());

    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() => throw new NotSupportedException();
    public Task<int> AltaAsync(Proveedor proveedor) => throw new NotSupportedException();
    public Task ModificarAsync(Proveedor proveedor) => throw new NotSupportedException();
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
}

/// <summary>IGastoService de la pantalla de pagos con los tipos reales. ObtenerPorIdAsync responde, en este orden: Falla,
/// Pendiente o Gasto. RegistrarPago graba o lanza FallaAlRegistrar (lo que ApiErrores reconstruye de un 409).</summary>
internal sealed class PagosFake : IGastoService
{
    public Gasto? Gasto { get; set; }
    public TaskCompletionSource<Gasto>? Pendiente { get; set; }
    public Exception? Falla { get; set; }
    public Exception? FallaAlRegistrar { get; set; }
    public List<PagoGasto> Registrados { get; } = new();
    public List<(int GastoId, int PagoId)> AnuladosPagos { get; } = new();

    public Task<Gasto> ObtenerPorIdAsync(int id) =>
        Falla is not null ? Task.FromException<Gasto>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult(Gasto!);

    public Task<int> RegistrarPagoAsync(PagoGasto pago)
    {
        if (FallaAlRegistrar is not null)
            return Task.FromException<int>(FallaAlRegistrar);
        Registrados.Add(pago);
        return Task.FromResult(900 + Registrados.Count);
    }

    public Task AnularPagoAsync(int gastoId, int pagoId)
    {
        AnuladosPagos.Add((gastoId, pagoId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro) => throw new NotSupportedException();
    public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false) => throw new NotSupportedException();
    public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, PagoContadoDto? pagoContado, IReadOnlyList<int>? movimientoIds = null) => throw new NotSupportedException();
    public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto, PagoContadoDto? pagoContado) => throw new NotSupportedException();
    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden) => throw new NotSupportedException();
    public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
}

/// <summary>IAdjuntoService que lista por pago (cada pago con sus adjuntos) y puede fallar con un 403.</summary>
internal sealed class AdjuntosPorPagoFake : IAdjuntoService
{
    public Dictionary<int, List<AdjuntoDto>> PorPago { get; } = new();
    public Exception? FallaAlListar { get; set; }

    public Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId) =>
        FallaAlListar is not null ? Task.FromException<IReadOnlyList<AdjuntoDto>>(FallaAlListar)
            : Task.FromResult<IReadOnlyList<AdjuntoDto>>(PorPago.TryGetValue(pagoGastoId, out var l) ? l.ToList() : new List<AdjuntoDto>());

    public Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId) => Task.FromResult<IReadOnlyList<AdjuntoDto>>(new List<AdjuntoDto>());
    public Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException();
    public Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException();
    public Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId) => throw new NotSupportedException();
    public Task QuitarAsync(int adjuntoId) => throw new NotSupportedException();
}
