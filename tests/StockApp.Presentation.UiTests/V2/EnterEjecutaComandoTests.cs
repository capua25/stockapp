using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.Behaviors;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Enter en la fila de carga de Ingreso por factura agrega el artículo y nunca guarda. Banco: un TextBox y un ComboBox
/// con el comportamiento, y un botón IsDefault que cuenta los Enter que se escapan (en la pantalla real no hay
/// IsDefault; acá hace de detector).
/// </summary>
public class EnterEjecutaComandoTests
{
    private sealed class Banco
    {
        public Window Ventana = null!;
        public TextBox Caja = null!;
        public ComboBox Combo = null!;
        public RelayCommand Comando = null!;
        public int Ejecuciones;
        public int EnterQueSeEscapan;
        public bool Puede = true;
    }

    private static Banco Montar(string? elegido = null)
    {
        var b = new Banco();
        b.Comando = new RelayCommand(() => b.Ejecuciones++, () => b.Puede);
        b.Caja = new TextBox();
        b.Combo = new ComboBox { ItemsSource = new[] { "Cemento portland 50 kg", "Arena gruesa" }, SelectedItem = elegido };
        var porDefecto = new Button { Content = "Guardar", IsDefault = true };
        porDefecto.Click += (_, _) => b.EnterQueSeEscapan++;
        EnterEjecutaComando.SetComando(b.Caja, b.Comando);
        EnterEjecutaComando.SetComando(b.Combo, b.Comando);
        b.Ventana = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { b.Caja, b.Combo, porDefecto } } };
        b.Ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return b;
    }

    private static void Tecla(Banco b, InputElement foco, PhysicalKey tecla)
    {
        foco.Focus();
        Dispatcher.UIThread.RunJobs();
        b.Ventana.KeyPressQwerty(tecla, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Detector_SinElComportamiento_ElEnterDelCampoLlegaAlBotonPorDefecto()
    {
        // Control positivo: si esto no da 1, el banco no ve un Enter que se escapa y los demás tests no custodian nada.
        var b = Montar();
        EnterEjecutaComando.SetComando(b.Caja, null);

        Tecla(b, b.Caja, PhysicalKey.Enter);

        Assert.Equal(0, b.Ejecuciones);
        Assert.Equal(1, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void EnUnCampoDeTexto_Ejecuta_YNoSeEscapa()
    {
        var b = Montar();

        Tecla(b, b.Caja, PhysicalKey.Enter);

        Assert.Equal(1, b.Ejecuciones);
        Assert.Equal(0, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void EnUnComboConValor_Ejecuta_NoAbreElDesplegable_YNoSeEscapa()
    {
        var b = Montar(elegido: "Arena gruesa");

        Tecla(b, b.Combo, PhysicalKey.Enter);

        Assert.Equal(1, b.Ejecuciones);
        Assert.False(b.Combo.IsDropDownOpen);
        Assert.Equal(0, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void EnUnComboVacio_NoEjecuta_YConservaLoNativo()
    {
        var b = Montar();

        Tecla(b, b.Combo, PhysicalKey.Enter);

        Assert.Equal(0, b.Ejecuciones);
        Assert.True(b.Combo.IsDropDownOpen, "Lo nativo del ComboBox cerrado con Enter es abrir el desplegable.");
    }

    [AvaloniaFact]
    public void ConElDesplegableAbierto_ElEnterEligeYNoEjecuta()
    {
        var b = Montar(elegido: "Arena gruesa");
        b.Combo.Focus();
        b.Combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        // Verificado: un KeyDown sintético vía RaiseEvent no cierra el ComboBox ni sin el comportamiento; el Enter real
        // por la ventana sí (el foco del teclado está en el popup).
        b.Ventana.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, b.Ejecuciones);
        Assert.False(b.Combo.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void ConElComandoBloqueado_NoEjecuta_YTampocoSeEscapa()
    {
        var b = Montar();
        b.Puede = false;
        b.Comando.NotifyCanExecuteChanged();

        Tecla(b, b.Caja, PhysicalKey.Enter);

        Assert.Equal(0, b.Ejecuciones);
        Assert.Equal(0, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void OtraTecla_NoEjecuta()
    {
        var b = Montar(elegido: "Arena gruesa");

        Tecla(b, b.Caja, PhysicalKey.A);
        Tecla(b, b.Combo, PhysicalKey.Tab);

        Assert.Equal(0, b.Ejecuciones);
    }
}
