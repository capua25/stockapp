using Avalonia.Controls;
using Avalonia.Interactivity;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// Diálogo modal con formulario real (spec 2026-09-08) — molde de PedirTextoDialog:
/// "Aceptar" cierra devolviendo DatosClasificacionTarea (leído del panel embebido);
/// "Cancelar" devuelve null. Usá Window.ShowDialog&lt;TResult&gt; con
/// TResult=DatosClasificacionTarea? para obtener el resultado.
/// </summary>
public partial class ReclasificarTareaDialog : Window
{
    public ReclasificarTareaDialog()
    {
        InitializeComponent();
    }

    private void OnAceptarClick(object? sender, RoutedEventArgs e)
    {
        var vm = (ReclasificarTareaDialogViewModel)DataContext!;
        Close(vm.Panel.ObtenerDatos());
    }

    private void OnCancelarClick(object? sender, RoutedEventArgs e) => Close(null);
}
