using System;
using System.Collections.ObjectModel;
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
/// Formulario de registro de movimiento de stock.
/// Maneja stock insuficiente con confirmación del usuario.
/// </summary>
public partial class MovimientoRegistroViewModel : ViewModelBase
{
    private readonly IMovimientoStockService _service;
    private readonly IProductoService        _productoService;
    private readonly INavigationService      _navigation;
    private readonly IConfirmacionService    _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarCommand))]
    private Producto? _productoSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarCommand))]
    private decimal _cantidad;

    [ObservableProperty]
    private TipoMovimiento _tipo = TipoMovimiento.Entrada;

    [ObservableProperty]
    private MotivoMovimiento _motivo = MotivoMovimiento.Compra;

    [ObservableProperty]
    private decimal? _precioUnitario;

    [ObservableProperty]
    private string? _comentario;

    [ObservableProperty]
    private string? _mensajeError;

    public ObservableCollection<Producto> Productos { get; } = new();

    public MovimientoRegistroViewModel(
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
