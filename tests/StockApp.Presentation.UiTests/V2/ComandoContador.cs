using System;
using System.Windows.Input;

namespace StockApp.Presentation.UiTests.V2;

internal sealed class ComandoContador : ICommand
{
    public int Ejecuciones { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => Ejecuciones++;
}
