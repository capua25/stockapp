using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class AdjuntosPanelView : UserControl
{
    public AdjuntosPanelView()
    {
        InitializeComponent();

        // A diferencia de las Views "de página" (ver GastosView), este panel es embebido y
        // se inicializa explícitamente desde el form padre (GastoFormViewModel/PagosGastoViewModel)
        // llamando a InicializarAsync con el GastoId/PagoGastoId correspondiente. No engancha
        // DataContextChanged para auto-cargar.
    }
}
