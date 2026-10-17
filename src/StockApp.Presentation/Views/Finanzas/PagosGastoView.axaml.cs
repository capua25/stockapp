using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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

        // Salir de un campo sin completarlo cuenta como tocarlo: su error aparece ahí y no recién al registrar (que con
        // el monto vacío está deshabilitado, así que nunca se llegaría a ver).
        TocarAlSalir(SelectorFecha, nameof(PagosGastoViewModel.FechaSeleccionada));
        TocarAlSalir(CajaMonto, nameof(PagosGastoViewModel.MontoTexto));
    }

    /// <summary>Marca el campo como tocado cuando el foco sale del control entero, no al pasar entre sus partes (la caja y el
    /// botón del selector de fecha). El calendario vive en un popup, que es otra raíz de foco: mientras está abierto no se
    /// marca (el error aparecería mientras el usuario elige); al cerrarse se mira si el foco quedó afuera.</summary>
    private void TocarAlSalir(Control control, string propiedad)
    {
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property != InputElement.IsKeyboardFocusWithinProperty || control.IsKeyboardFocusWithin || PopupAbierto(control))
                return;
            MarcarTocado(propiedad);
        };

        if (control is CalendarDatePicker selector)
            selector.CalendarClosed += (_, _) =>
                // Diferido: al cerrar, el selector recupera el foco después de avisar el cierre.
                Dispatcher.UIThread.Post(() =>
                {
                    if (!control.IsKeyboardFocusWithin)
                        MarcarTocado(propiedad);
                });
    }

    private static bool PopupAbierto(Control control) => control is CalendarDatePicker { IsDropDownOpen: true };

    private void MarcarTocado(string propiedad) => (DataContext as PagosGastoViewModel)?.MarcarTocado(propiedad);
}
