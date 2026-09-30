using System.Windows.Input;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.VisualTests.Catalogo;

public sealed record ProductoMuestra(string Codigo, string Nombre, string Costo, string Stock, string Estado, VarianteInsignia VarianteEstado);

/// <summary>
/// Datos fijos del catálogo: los MISMOS textos que docs/ui-v2/mockups/componentes/catalogo.html.
/// Montos ya formateados: el catálogo valida componentes, no converters.
/// </summary>
public sealed class CatalogoComponentesViewModel
{
    public string Busqueda { get; set; } = "";

    public IReadOnlyList<string> Categorias { get; } = ["Todas las categorías", "Ferretería", "Pinturería"];

    public string CategoriaFiltro { get; set; } = "Todas las categorías";

    public IReadOnlyList<ProductoMuestra> Productos { get; } =
    [
        new("A-001", "Tornillo 8x40", "12.50", "340", "Activo", VarianteInsignia.Exito),
        new("A-002", "Pintura látex blanca 4 L", "1850.00", "-3", "Stock negativo", VarianteInsignia.Peligro),
        new("A-003", "Guantes de nitrilo", "95.00", "120", "Inactivo", VarianteInsignia.Neutro),
    ];

    public string Nombre { get; set; } = "Tornillo 8x40";

    public string Codigo { get; set; } = "A-001";

    public IReadOnlyList<string> CategoriasProducto { get; } = ["Ferretería", "Pinturería"];

    public string CategoriaSeleccionada { get; set; } = "Ferretería";

    public DateTime? FechaAlta { get; set; } = new DateTime(2026, 9, 28);

    public decimal StockInicial { get; set; } = 340;

    public bool Activo { get; set; } = true;

    public ICommand VolverCommand { get; } = new ComandoInerte();

    private sealed class ComandoInerte : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
