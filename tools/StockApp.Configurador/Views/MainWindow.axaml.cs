using Avalonia.Controls;
using StockApp.Configurador.ViewModels;

namespace StockApp.Configurador.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // "Cancelar" cierra sin guardar (ver ConfiguradorViewModel.Cancelar). "Guardar" NO
        // cierra la ventana: deja el mensaje de confirmación visible para que el usuario lo
        // lea antes de cerrar a mano, o siga usando "Probar conexión" sobre el valor recién
        // guardado.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConfiguradorViewModel vm)
            {
                vm.SolicitarCierre += (_, _) => Close();
            }
        };
    }
}
