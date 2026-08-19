using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

public partial class AuditoriaLogView : UserControl
{
    public AuditoriaLogView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializan los usuarios del filtro y se carga el log de auditoría
        // (bugfix 2026-08-19, mismo patrón que MovimientoHistorialView.axaml.cs).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is AuditoriaLogViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
