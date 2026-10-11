using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Enter dentro de un campo ejecuta un comando, desde XAML o desde el code-behind. Ejemplos: en Ingreso por factura,
/// Enter en la fila de carga agrega el artículo para cargar muchos renglones seguidos sin el mouse; en el alta rápida
/// de producto, Enter en un combo con valor confirma el diálogo (el botón IsDefault solo no llega: el combo se come
/// el Enter). Va en túnel: un ComboBox cerrado abre su desplegable con Enter
/// y marca el evento antes de que lo vea un manejador de burbuja. En un ComboBox solo actúa con un ítem elegido y el
/// desplegable cerrado (vacío, Enter abre el desplegable como siempre; abierto, Enter elige el ítem). Ejecuta solo si
/// el comando puede (un campo en rojo lo bloquea igual que al botón) y marca el Enter como manejado, así no llega a
/// ningún botón por defecto.
/// </summary>
public static class EnterEjecutaComando
{
    public static readonly AttachedProperty<ICommand?> ComandoProperty =
        AvaloniaProperty.RegisterAttached<InputElement, InputElement, ICommand?>("Comando");

    static EnterEjecutaComando()
    {
        ComandoProperty.Changed.AddClassHandler<InputElement>(AlCambiarComando);
    }

    public static ICommand? GetComando(InputElement elemento) => elemento.GetValue(ComandoProperty);

    public static void SetComando(InputElement elemento, ICommand? valor) => elemento.SetValue(ComandoProperty, valor);

    private static void AlCambiarComando(InputElement elemento, AvaloniaPropertyChangedEventArgs e)
    {
        elemento.RemoveHandler(InputElement.KeyDownEvent, AlPresionarTecla);
        if (e.NewValue is ICommand)
            elemento.AddHandler(InputElement.KeyDownEvent, AlPresionarTecla, RoutingStrategies.Tunnel);
    }

    private static void AlPresionarTecla(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled || sender is not InputElement elemento)
            return;
        if (elemento is ComboBox combo && (combo.SelectedItem is null || combo.IsDropDownOpen))
            return;
        var comando = GetComando(elemento);
        if (comando is null)
            return;
        if (comando.CanExecute(null))
            comando.Execute(null);
        e.Handled = true;
    }
}
