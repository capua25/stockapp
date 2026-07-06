using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Base del formulario de registro de movimiento de stock. El tipo de movimiento (Entrada/Salida)
/// y los motivos disponibles quedan fijos por subclase (ver <see cref="EntradaRegistroViewModel"/>
/// y <see cref="SalidaRegistroViewModel"/>). Concentra la carga de productos y el registro,
/// incluyendo el manejo de stock insuficiente con confirmación del usuario.
/// </summary>
public abstract partial class MovimientoRegistroViewModelBase : ViewModelBase
{
    private readonly IMovimientoStockService _service;
    private readonly IProductoService        _productoService;
    private readonly INavigationService      _navigation;
    private readonly IConfirmacionService    _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarCommand))]
    protected Producto? _productoSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarCommand))]
    protected decimal _cantidad;

    [ObservableProperty]
    protected MotivoMovimiento _motivo;

    [ObservableProperty]
    protected decimal? _precioUnitario;

    [ObservableProperty]
    protected string? _comentario;

    [ObservableProperty]
    protected string? _mensajeError;

    public ObservableCollection<Producto> Productos { get; } = new();

    /// <summary>Tipo de movimiento fijo de la pantalla concreta (Entrada o Salida).</summary>
    public abstract TipoMovimiento Tipo { get; }

    /// <summary>Motivos habilitados para elegir, filtrados según el tipo fijo de la pantalla.</summary>
    public abstract IReadOnlyList<MotivoMovimiento> MotivosDisponibles { get; }

    /// <summary>Título de la pantalla, mostrado en el encabezado del formulario.</summary>
    public abstract string Titulo { get; }

    protected MovimientoRegistroViewModelBase(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service         = service;
        _productoService = productoService;
        _navigation      = navigation;
        _confirmacion    = confirmacion;
    }

    /// <summary>
    /// Carga los productos activos disponibles para elegir en el combo. IProductoService.BuscarAsync
    /// no filtra por Activo (devuelve todos), así que el filtro se aplica en memoria tras traerlos:
    /// no deben poder registrarse movimientos contra productos dados de baja.
    /// </summary>
    public async Task InicializarAsync()
    {
        var productos = await _productoService.BuscarAsync(null, null, null);

        Productos.Clear();
        foreach (var p in productos.Where(p => p.Activo))
            Productos.Add(p);
    }

    private bool PuedeRegistrar()
        => ProductoSeleccionado != null && Cantidad > 0;

    [RelayCommand(CanExecute = nameof(PuedeRegistrar))]
    private async Task RegistrarAsync()
    {
        MensajeError = null;

        var dto = new RegistrarMovimientoDto(
            ProductoId:     ProductoSeleccionado!.Id,
            Tipo:           Tipo,
            Motivo:         Motivo,
            Cantidad:       Cantidad,
            PrecioUnitario: PrecioUnitario,
            Comentario:     Comentario);

        try
        {
            await _service.RegistrarAsync(dto, forzar: false);
            _navigation.Navegar<MovimientoHistorialViewModel>();
        }
        catch (StockInsuficienteException ex)
        {
            var mensaje = $"El stock quedará en {ex.StockResultante}. ¿Confirmar la salida igual?";
            var confirmar = await _confirmacion.PreguntarAsync(mensaje);

            if (confirmar)
            {
                await _service.RegistrarAsync(dto, forzar: true);
                _navigation.Navegar<MovimientoHistorialViewModel>();
            }
            // Si rechaza, no hace nada — el usuario puede corregir la cantidad
        }
        catch (Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
