using Avalonia.Controls;

namespace StockApp.Presentation.Views.Catalogo;

public partial class CatalogosTareaView : UserControl
{
    public CatalogosTareaView()
    {
        InitializeComponent();
        // La carga de datos la cablea cada sub-vista (XxxListView) en su propio
        // DataContextChanged (Task 8) — acá no hay nada que inicializar.
    }
}
