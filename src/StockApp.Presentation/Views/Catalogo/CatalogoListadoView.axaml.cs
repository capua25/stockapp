using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

/// <summary>
/// Detalle genérico de un catálogo. NO carga el listado (lo dispara CatalogosViewModel): solo sincroniza con el VM lo que
/// la grilla de XAML fijo no puede bindear, que es el encabezado y la visibilidad de las columnas medidas y el lugar de NOMBRE.
/// </summary>
public partial class CatalogoListadoView : UserControl
{
    private CatalogoListadoViewModel? _vm;

    public CatalogoListadoView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Enganchar();
    }

    private void Enganchar()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= AlCambiarElVm;
        _vm = DataContext as CatalogoListadoViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += AlCambiarElVm;
        SincronizarColumnas();
    }

    private void AlCambiarElVm(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
            SincronizarColumnas();
    }

    private void SincronizarColumnas()
    {
        if (_vm is null)
            return;
        // Orden fijo del XAML: NOMBRE, las cuatro medidas y ESTADO.
        var medidas = new[] { Grilla.Columns[1], Grilla.Columns[2], Grilla.Columns[3], Grilla.Columns[4] };
        for (var i = 0; i < medidas.Length; i++)
        {
            var visible = i < _vm.Columnas.Count;
            medidas[i].IsVisible = visible;
            medidas[i].Header = visible ? _vm.Columnas[i].Encabezado : string.Empty;
        }
        Grilla.Columns[0].DisplayIndex = System.Math.Min(_vm.PosicionNombre, Grilla.Columns.Count - 1);
    }

    /// <summary>Doble click en una fila = Editar (solo sobre una fila: en el encabezado ordena) y solo si Editar se puede ejecutar.</summary>
    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;
        if (DataContext is CatalogoListadoViewModel vm && vm.EditarCommand.CanExecute(null))
            vm.EditarCommand.Execute(null);
    }
}
