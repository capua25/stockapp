using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

public partial class TareaFormView : UserControl
{
    public TareaFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is TareaFormViewModel vm && vm.EsNuevaTarea)
                await vm.ClasificacionPanel.InicializarAsync();
        };

        // Salir de la caja sin completarla cuenta como tocarla: su error aparece ahí y no recién al guardar (que con el
        // título vacío está deshabilitado, así que nunca se llegaría a ver).
        CajaTitulo.LostFocus += (_, _) => (DataContext as TareaFormViewModel)?.MarcarTocado(nameof(TareaFormViewModel.Titulo));
    }
}
