using Avalonia.Controls;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class NuevaImportacionView : UserControl
{
    public NuevaImportacionView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is NuevaImportacionViewModel vm)
                await vm.InicializarMaestrosAsync();
        };
    }

    private void RubroComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: RubroGasto rubro } combo
            && combo.DataContext is FilaGastoEditableVm fila)
        {
            fila.CodigoRubro = rubro.Codigo;
            fila.Rubro = rubro.Nombre;
        }
    }
}
