using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fakes minimos de servicios de Finanzas/catalogo y de seleccion de archivo, compartidos por los
/// bancos de pruebas headless (este proyecto no referencia Moq, asi que se escriben a mano y
/// lanzan NotSupportedException en los miembros no ejercitados). Heredados del importador de
/// planillas ODS (eliminado 2026-10-21); los reutilizan otras vistas.
/// </summary>
internal sealed class ServicioSeleccionArchivoFake : IServicioSeleccionArchivo
{
    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
        => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class FuenteFinanciamientoServiceFake : IFuenteFinanciamientoService
{
    private readonly IReadOnlyList<FuenteFinanciamiento> _fuentes;

    public FuenteFinanciamientoServiceFake(IReadOnlyList<FuenteFinanciamiento> fuentes) => _fuentes = fuentes;

    public Task<int> AltaAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => Task.FromResult(_fuentes);
    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() => Task.FromResult(_fuentes);
}

internal sealed class RubroGastoServiceFake : IRubroGastoService
{
    private readonly IReadOnlyList<RubroGasto> _rubros;

    public RubroGastoServiceFake(IReadOnlyList<RubroGasto> rubros) => _rubros = rubros;

    public Task<int> AltaAsync(RubroGasto rubro) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(RubroGasto rubro) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() => Task.FromResult(_rubros);
    public Task<IReadOnlyList<RubroGasto>> ListarActivosAsync() => Task.FromResult(_rubros);
}

internal sealed class ProveedorServiceFake : IProveedorService
{
    private readonly IReadOnlyList<Proveedor> _proveedores;

    public ProveedorServiceFake(IReadOnlyList<Proveedor> proveedores) => _proveedores = proveedores;

    public Task<int> AltaAsync(Proveedor proveedor) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(Proveedor proveedor) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() => Task.FromResult(_proveedores);
    public Task<IReadOnlyList<Proveedor>> ListarActivasAsync() => Task.FromResult(_proveedores);
}

internal sealed class LineaPoaServiceFake : ILineaPoaService
{
    private readonly IReadOnlyList<LineaPoa> _lineas;

    public LineaPoaServiceFake(IReadOnlyList<LineaPoa> lineas) => _lineas = lineas;

    public Task<int> AltaAsync(LineaPoa linea) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(LineaPoa linea) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => Task.FromResult(_lineas);
    public Task<IReadOnlyList<LineaPoa>> ListarActivasAsync() => Task.FromResult(_lineas);
}
