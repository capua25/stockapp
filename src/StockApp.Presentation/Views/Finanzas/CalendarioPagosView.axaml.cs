using System.ComponentModel;
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class CalendarioPagosView : UserControl
{
    private CalendarioPagosViewModel? _vm;

    public CalendarioPagosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= AlCambiarElVm;
            _vm = DataContext as CalendarioPagosViewModel;
            if (_vm is null)
                return;
            _vm.PropertyChanged += AlCambiarElVm;
            SincronizarColumnasDePago();
            await _vm.CargarAsync();
        };
    }

    /// <summary>Un 403 que cambia los permisos llega como PropertyChanged con nombre vacío
    /// (NotificarPermisosActualizados): la columna se re-evalúa sin navegar.</summary>
    private void AlCambiarElVm(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(CalendarioPagosViewModel.PuedeRegistrarPagos))
            SincronizarColumnasDePago();
    }

    /// <summary>
    /// La columna "Registrar pago" se oculta entera sin el permiso. Una DataGridColumn no está en el árbol lógico y no
    /// recibe el DataContext, así que un binding de IsVisible en la columna no se re-evaluaría; el botón de la celda
    /// conserva su propio IsVisible sobre el mismo gate.
    /// </summary>
    private void SincronizarColumnasDePago()
    {
        var visible = _vm?.PuedeRegistrarPagos == true;
        GrillaVencidas.Columns[^1].IsVisible = visible;
        Grilla7Dias.Columns[^1].IsVisible = visible;
        Grilla30Dias.Columns[^1].IsVisible = visible;
    }
}
