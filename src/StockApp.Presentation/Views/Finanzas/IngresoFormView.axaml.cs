using Avalonia.Controls;
using Avalonia.Input;
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
        TocarAlSalir(ComboFuente, nameof(IngresoFormViewModel.FuenteSeleccionada));
    }

    /// <summary>Marca el campo como tocado cuando el foco sale del control entero, no al pasar entre sus partes (la
    /// caja y el botón del selector de fecha). La lista abierta de un combo cuenta como foco adentro: abrirla para
    /// elegir no lo pinta de rojo.</summary>
    private void TocarAlSalir(Control control, string propiedad) =>
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property != InputElement.IsKeyboardFocusWithinProperty || control.IsKeyboardFocusWithin)
                return;
            (DataContext as IngresoFormViewModel)?.MarcarTocado(propiedad);
        };
}
