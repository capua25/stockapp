using Avalonia.Controls;
using Avalonia.Input;

namespace StockApp.Presentation.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    // Fix UX (Tarea 6, UI Kit): Button.IsDefault no dispara con foco en un TextBox
    // porque TextBox maneja Key.Enter internamente y no lo deja llegar al mecanismo
    // de "botón por defecto" de la ventana (ver AvaloniaUI/Avalonia#860). Se intercepta
    // explícitamente acá en los campos de Usuario/Contraseña.
    private void OnCampoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is not ViewModels.LoginViewModel vm)
            return;

        if (vm.EntrarCommand.CanExecute(null))
            vm.EntrarCommand.Execute(null);

        e.Handled = true;
    }
}
