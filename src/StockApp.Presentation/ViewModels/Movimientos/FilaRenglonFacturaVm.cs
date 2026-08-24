using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Catalogo;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>Fila editable de la grilla de renglones de la factura (Task 8). El alta en línea de
/// producto nuevo (Task 9) usa los campos ProductoNuevo*; en ese caso Producto queda null.</summary>
public partial class FilaRenglonFacturaVm : ObservableObject
{
    [ObservableProperty] private ProductoDto? _producto;
    [ObservableProperty] private bool _esProductoNuevo;
    [ObservableProperty] private string? _productoNuevoCodigo;
    [ObservableProperty] private string? _productoNuevoNombre;
    [ObservableProperty] private int? _productoNuevoCategoriaId;
    [ObservableProperty] private int _productoNuevoUnidadMedidaId;
    [ObservableProperty] private decimal _cantidad;
    [ObservableProperty] private decimal _precioUnitario;
    [ObservableProperty] private bool _actualizarPrecioCosto;

    public decimal Subtotal => Cantidad * PrecioUnitario;

    partial void OnCantidadChanged(decimal value) => OnPropertyChanged(nameof(Subtotal));
    partial void OnPrecioUnitarioChanged(decimal value) => OnPropertyChanged(nameof(Subtotal));

    /// <summary>Nombre a mostrar en la grilla, exista o no todavía el producto (alta en línea).</summary>
    public string NombreMostrado => EsProductoNuevo
        ? (ProductoNuevoNombre ?? "(producto nuevo)")
        : (Producto?.Nombre ?? string.Empty);

    partial void OnProductoChanged(ProductoDto? value) => OnPropertyChanged(nameof(NombreMostrado));
    partial void OnEsProductoNuevoChanged(bool value) => OnPropertyChanged(nameof(NombreMostrado));
    partial void OnProductoNuevoNombreChanged(string? value) => OnPropertyChanged(nameof(NombreMostrado));
}
