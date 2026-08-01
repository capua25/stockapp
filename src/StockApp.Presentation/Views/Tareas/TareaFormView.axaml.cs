using Avalonia.Controls;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// Sin DataContextChanged: CargarParaCrear/CargarParaVer son síncronos y ya corren ANTES
/// de que INavigationService.Navegar&lt;TVm&gt;(Action&lt;TVm&gt;) publique el VM como
/// DataContext — a diferencia de GastoFormView, acá no hay combos que precargar async.
/// </summary>
public partial class TareaFormView : UserControl
{
    public TareaFormView()
    {
        InitializeComponent();
    }
}
