using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastoFormView : UserControl
{
    public GastoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastoFormViewModel vm)
                await vm.InicializarAsync();
        };

        // Salir de un campo sin completarlo cuenta como tocarlo: su error aparece ahí y no recién al guardar (que con el
        // campo vacío está deshabilitado, así que nunca se llegaría a ver).
        TocarAlSalir(ComboProveedor, nameof(GastoFormViewModel.ProveedorSeleccionado));
        TocarAlSalir(SelectorFecha, nameof(GastoFormViewModel.FechaSeleccionada));
        TocarAlSalir(CajaDetalle, nameof(GastoFormViewModel.Detalle));
        TocarAlSalir(CajaMonto, nameof(GastoFormViewModel.MontoTexto));
        TocarAlSalir(ComboFuente, nameof(GastoFormViewModel.FuenteSeleccionada));
        TocarAlSalir(ComboRubro, nameof(GastoFormViewModel.RubroSeleccionado));
        TocarAlSalir(SelectorVencimiento, nameof(GastoFormViewModel.FechaVencimientoSeleccionada));
    }

    /// <summary>Marca el campo como tocado cuando el foco sale del control entero, no al pasar entre sus partes (la caja y
    /// el botón del selector de fecha). La lista del combo y el calendario viven en un popup, que es otra raíz de foco:
    /// mientras está abierto no se marca; al cerrarse se mira si el foco quedó afuera (mismo criterio que IngresoFormView).</summary>
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

    private void MarcarTocado(string propiedad) => (DataContext as GastoFormViewModel)?.MarcarTocado(propiedad);
}
