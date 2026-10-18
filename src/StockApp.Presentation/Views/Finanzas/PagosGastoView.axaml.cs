using Avalonia.Controls;
using Avalonia.Input;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class PagosGastoView : UserControl
{
    public PagosGastoView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is PagosGastoViewModel vm)
                await vm.InicializarAsync();
        };

        // Salir del Monto sin completarlo cuenta como tocarlo: su error aparece ahí y no recién al registrar (que con
        // el monto vacío está deshabilitado, así que nunca se llegaría a ver). Fecha no lo necesita: arranca en hoy y
        // vaciarla ya la marca como tocada (el cambio de valor), así que no hay salida de foco que cubrir.
        TocarAlSalir(CajaMonto, nameof(PagosGastoViewModel.MontoTexto));
    }

    /// <summary>Marca el campo como tocado cuando el foco sale del control.</summary>
    private void TocarAlSalir(Control control, string propiedad)
    {
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property != InputElement.IsKeyboardFocusWithinProperty || control.IsKeyboardFocusWithin)
                return;
            MarcarTocado(propiedad);
        };
    }

    private void MarcarTocado(string propiedad) => (DataContext as PagosGastoViewModel)?.MarcarTocado(propiedad);
}
