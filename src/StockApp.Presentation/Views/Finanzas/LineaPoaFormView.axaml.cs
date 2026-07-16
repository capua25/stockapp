using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LineaPoaFormView : UserControl
{
    public LineaPoaFormView()
    {
        InitializeComponent();

        // InicializarAsync carga el combo de fuentes activas y arma las filas de
        // asignaciones (incluido el modo edición precargado por CargarParaEditar,
        // que corre ANTES vía el overload de Navegar).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is LineaPoaFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
