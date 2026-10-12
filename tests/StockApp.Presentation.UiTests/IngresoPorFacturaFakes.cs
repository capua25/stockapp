using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fakes minimos de las dependencias de IngresoPorFacturaViewModel/AdjuntosPanelViewModel, mismo
/// criterio que TareaFakes.cs/NuevaImportacionFakes.cs/MovimientoRegistroFakes.cs (este proyecto
/// no referencia Moq). Reusa ProveedorServiceFake/FuenteFinanciamientoServiceFake/
/// RubroGastoServiceFake/LineaPoaServiceFake (NuevaImportacionFakes.cs), ConfirmacionServiceFake
/// (MovimientoRegistroFakes.cs) y ServicioSeleccionArchivoFake (NuevaImportacionFakes.cs) --
/// mismo namespace, no se redeclaran.
/// </summary>
internal sealed class IngresoPorFacturaServiceFake : IIngresoPorFacturaService
{
    private int _proximoGastoId = 1;

    public List<IngresoPorFacturaDto> Registrados { get; } = new();

    /// <summary>Si se setea, RegistrarAsync la relanza en vez de "guardar" -- para ejercitar los
    /// catch de GuardarInternoAsync desde la vista real.</summary>
    public Exception? ExcepcionARelanzar { get; set; }

    public Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto)
    {
        if (ExcepcionARelanzar is not null)
            throw ExcepcionARelanzar;

        Registrados.Add(dto);
        var suma = 0m;
        foreach (var renglon in dto.Renglones)
            suma += renglon.Cantidad * renglon.PrecioUnitario;

        return Task.FromResult(new IngresoPorFacturaResultadoDto(
            _proximoGastoId++, new List<int> { 1 }, suma, dto.MontoTotal - suma));
    }

    public Task AnularLoteAsync(int gastoId, bool confirmarAnulacionDePagoAutomatico = false) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");
}

/// <summary>A diferencia de ProductoServiceFake (MovimientoRegistroFakes.cs, siempre vacío), este
/// fake devuelve una lista configurable -- necesario para poblar ProductosDisponibles y poder
/// ejercitar la selección real del ComboBox "Producto" del renglón.</summary>
internal sealed class ProductoServiceIngresoFake : IProductoService
{
    private readonly IReadOnlyList<ProductoDto> _productos;

    public ProductoServiceIngresoFake(IReadOnlyList<ProductoDto> productos) => _productos = productos;

    public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(Producto producto) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) => Task.FromResult(_productos);
    public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) => Task.FromResult(_productos);
}

internal sealed class CategoriaServiceFake : ICategoriaService
{
    private readonly IReadOnlyList<Categoria> _categorias;

    public CategoriaServiceFake(IReadOnlyList<Categoria> categorias) => _categorias = categorias;

    public Task<int> AltaAsync(Categoria categoria) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(Categoria categoria) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<Categoria>> ListarTodasAsync() => Task.FromResult(_categorias);
    public Task<IReadOnlyList<Categoria>> ListarActivasAsync() => Task.FromResult(_categorias);
}

internal sealed class UnidadMedidaServiceFake : IUnidadMedidaService
{
    private readonly IReadOnlyList<UnidadMedida> _unidades;

    public UnidadMedidaServiceFake(IReadOnlyList<UnidadMedida> unidades) => _unidades = unidades;

    public Task<int> AltaAsync(UnidadMedida unidadMedida) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(UnidadMedida unidadMedida) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync() => Task.FromResult(_unidades);
    public Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync() => Task.FromResult(_unidades);

    /// <summary>No ejercitado por IngresoPorFacturaViewModel (nunca la llama): pertenece a la
    /// interfaz por el alta de producto desde otras pantallas (ver ProductoFormViewModel).
    /// Se implementa igual, devolviendo la primera unidad de la lista configurada, para que la
    /// interfaz quede completa sin lanzar si algún test futuro la ejercitara.</summary>
    public Task<UnidadMedida> GarantizarUnidadPorDefectoAsync() =>
        Task.FromResult(_unidades.Count > 0
            ? _unidades[0]
            : new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u" });
}

/// <summary>Fake mínimo de IAdjuntoService, sólo para poder construir AdjuntosPanelViewModel real
/// (embebido en IngresoPorFacturaViewModel) sin explotar cuando GuardarInternoAsync llama
/// InicializarAsync(gastoId, null) tras un guardado exitoso.</summary>
internal sealed class AdjuntoServiceFake : IAdjuntoService
{
    /// <summary>Lo que devuelven los listados (por gasto y por pago). Vacía por defecto.</summary>
    public List<AdjuntoDto> Items { get; } = new();
    public List<int> Quitados { get; } = new();

    public Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId) => Task.FromResult<IReadOnlyList<AdjuntoDto>>(Items.ToList());
    public Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId) => Task.FromResult<IReadOnlyList<AdjuntoDto>>(Items.ToList());
    public Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId) => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task QuitarAsync(int adjuntoId)
    {
        Quitados.Add(adjuntoId);
        Items.RemoveAll(a => a.Id == adjuntoId);
        return Task.CompletedTask;
    }
}

internal sealed class ServicioAperturaArchivoFake : IServicioAperturaArchivo
{
    public Task AbrirAsync(string nombreArchivo, byte[] contenido) => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class AuthorizationServiceFake : IAuthorizationService
{
    public void Verificar(ICurrentSession sesion, string accion) { }
}

/// <summary>Alta rápida sin ventana: devuelve <see cref="Respuesta"/> (null = el usuario canceló) y registra lo recibido.</summary>
internal sealed class AltaProductoRapidaDialogServiceFake : IAltaProductoRapidaDialogService
{
    public ProductoNuevoDto? Respuesta { get; set; }
    public int Llamadas { get; private set; }
    public IReadOnlyList<Categoria>? CategoriasRecibidas { get; private set; }
    public IReadOnlyList<UnidadMedida>? UnidadesRecibidas { get; private set; }

    public Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades)
    {
        Llamadas++;
        CategoriasRecibidas = categorias;
        UnidadesRecibidas = unidades;
        return Task.FromResult(Respuesta);
    }
}

/// <summary>Confirmación de precios sin ventana: responde con <see cref="Responder"/> o deja la confirmación "abierta"
/// con <see cref="Pendiente"/>.</summary>
internal sealed class ConfirmacionPreciosDialogServiceFake : IConfirmacionPreciosDialogService
{
    public Func<IReadOnlyList<ItemConfirmacionPrecioVm>, IReadOnlyList<bool>?> Responder { get; set; } = _ => null;
    public TaskCompletionSource<IReadOnlyList<bool>?>? Pendiente { get; set; }
    public int Llamadas { get; private set; }
    public IReadOnlyList<ItemConfirmacionPrecioVm>? Recibidos { get; private set; }

    public Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios)
    {
        Llamadas++;
        Recibidos = cambios;
        return Pendiente?.Task ?? Task.FromResult(Responder(cambios));
    }
}
