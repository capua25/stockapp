using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fakes minimos de las dependencias de NuevaImportacionViewModel (F5d Entrega 2), mismo criterio
/// que MovimientoRegistroFakes.cs: este proyecto no referencia Moq, asi que se escriben a mano y
/// lanzan NotSupportedException en los miembros no ejercitados por los tests headless de la
/// grilla. Reutiliza ConfirmacionServiceFake (ya existe en MovimientoRegistroFakes.cs, mismo
/// namespace, misma interfaz IConfirmacionService).
/// </summary>
internal sealed class ImportacionServiceFake : IImportacionService
{
    private readonly ResultadoAnalisisDto _resultado;

    public ImportacionServiceFake(ResultadoAnalisisDto resultado) => _resultado = resultado;

    public Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds, string nombreArchivoPoa, byte[] poaOds, int ejercicio)
        => Task.FromResult(_resultado);

    public Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
        => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class ServicioSeleccionArchivoFake : IServicioSeleccionArchivo
{
    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoOdsAsync()
        => Task.FromResult<(string NombreArchivo, byte[] Contenido)?>(("archivo.ods", new byte[] { 1 }));
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
}
