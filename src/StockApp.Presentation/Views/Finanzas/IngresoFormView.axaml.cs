using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresoFormView : UserControl
{
    public IngresoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresoFormViewModel vm)
                await vm.InicializarAsync();
        };

        // Salir de un campo sin completarlo cuenta como tocarlo: su error aparece ahí y no recién al guardar (que con
        // el campo vacío está deshabilitado, así que nunca se llegaría a ver).
        TocarAlSalir(SelectorFecha, nameof(IngresoFormViewModel.FechaSeleccionada));
        TocarAlSalir(CajaMonto, nameof(IngresoFormViewModel.MontoTexto));
        TocarAlSalir(CajaConcepto, nameof(IngresoFormViewModel.Concepto));
        TocarAlSalir(ComboCaja, nameof(IngresoFormViewModel.CajaSeleccionada));
        TocarAlSalir(ComboFuente, nameof(IngresoFormViewModel.FuenteSeleccionada));
    }

    /// <summary>Marca el campo como tocado cuando el foco sale del control entero, no al pasar entre sus partes (la
    /// caja y el botón del selector de fecha). La lista del combo y el calendario viven en un popup, que es otra raíz de
    /// foco: navegar la lista con el teclado o apretar un ítem saca el foco del control. Mientras el popup está abierto
    /// no se marca (el error aparecería mientras el usuario elige); al cerrarse se mira si el foco quedó afuera.</summary>
    private void TocarAlSalir(Control control, string propiedad)
    {
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property != InputElement.IsKeyboardFocusWithinProperty || control.IsKeyboardFocusWithin || PopupAbierto(control))
                return;
            MarcarTocado(propiedad);
        };

        EventHandler? alCerrar = (_, _) =>
            // Diferido: al cerrar, el combo recupera el foco después de avisar el cierre.
            Dispatcher.UIThread.Post(() =>
            {
                if (!control.IsKeyboardFocusWithin)
                    MarcarTocado(propiedad);
            });
        if (control is ComboBox combo)
            combo.DropDownClosed += alCerrar;
        else if (control is CalendarDatePicker selector)
            selector.CalendarClosed += alCerrar;
    }

    private static bool PopupAbierto(Control control) =>
        control is ComboBox { IsDropDownOpen: true } or CalendarDatePicker { IsDropDownOpen: true };

    private void MarcarTocado(string propiedad) => (DataContext as IngresoFormViewModel)?.MarcarTocado(propiedad);
}
