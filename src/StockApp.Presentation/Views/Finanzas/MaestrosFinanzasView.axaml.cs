using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class MaestrosFinanzasView : UserControl
{
    public MaestrosFinanzasView()
    {
        InitializeComponent();
        // La carga de datos la cablea cada sub-vista (XxxListView) en su propio
        // DataContextChanged — acá no hay nada que inicializar.
    }
}
