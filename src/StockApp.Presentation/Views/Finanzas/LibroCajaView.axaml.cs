using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LibroCajaView : UserControl
{
    private LibroCajaViewModel? _vmObservado;

    public LibroCajaView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            ObservarModo(DataContext as LibroCajaViewModel);
            if (DataContext is LibroCajaViewModel vm)
                await vm.CargarAsync();
        };
    }

    private void ObservarModo(LibroCajaViewModel? vm)
    {
        if (_vmObservado is not null)
            _vmObservado.PropertyChanged -= AlCambiarVm;
        _vmObservado = vm;
        if (vm is not null)
            vm.PropertyChanged += AlCambiarVm;
    }

    /// <summary>
    /// En Año completo el campo Mes se oculta, pero un control oculto sigue en el árbol: si quedó con un texto inválido,
    /// su error bloquearía Actualizar sin que se vea por qué (Review Focus 4, D13). Reponer el mes del ViewModel en
    /// el Text alcanza: el behavior de validación limpia el error al cambiar el texto.
    /// </summary>
    private void AlCambiarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibroCajaViewModel.VerAnioCompleto) || sender is not LibroCajaViewModel vm)
            return;

        if (vm.VerAnioCompleto)
            SelectorMes.Text = vm.Mes.ToString(CultureInfo.InvariantCulture);
    }
}
