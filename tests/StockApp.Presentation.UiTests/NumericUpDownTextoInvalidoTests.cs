using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Contrato de <see cref="NumericUpDownValidacionBehavior"/> (global en el style NumericUpDown de
/// Themes/Controls.axaml). Sin él, un NumericUpDown con texto inválido ("20a5", "abc", un año
/// fuera de Minimum/Maximum) NO marcaba ningún error, el ViewModel conservaba el valor ANTERIOR,
/// Guardar seguía habilitado, y al perder el foco el control volvía a mostrar en silencio el
/// valor viejo (NumericUpDown.OnLostFocus -> CommitInput(forceTextUpdate: true), Avalonia 12.0.5).
/// Las vistas reales se cubren en <see cref="NumericUpDownVistasRealesTests"/>.
/// </summary>
public partial class NumericUpDownTextoInvalidoTests
{
    private sealed partial class VmDePrueba : ViewModelBase
    {
        [ObservableProperty] private int _anio = 2026;
        [ObservableProperty] private int? _anioOpcional = 2026;
        public int Guardados { get; private set; }

        public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(() =>
        {
            Guardados++;
            return Task.CompletedTask;
        });
    }

    private sealed record Montaje(Window Window, VmDePrueba Vm, NumericUpDown Anio, NumericUpDown Opcional, TextBox Otro, Button Guardar);

    private static Montaje Montar()
    {
        var vm = new VmDePrueba();
        var anio = new NumericUpDown { Minimum = 2000, Maximum = 2100, FormatString = "0" };
        anio.Bind(NumericUpDown.ValueProperty, new Avalonia.Data.Binding(nameof(VmDePrueba.Anio)) { Mode = Avalonia.Data.BindingMode.TwoWay });
        NumericUpDownValidacionBehavior.SetQue(anio, "año");
        var opcional = new NumericUpDown { Minimum = 2000, Maximum = 2100, FormatString = "0" };
        opcional.Bind(NumericUpDown.ValueProperty, new Avalonia.Data.Binding(nameof(VmDePrueba.AnioOpcional)) { Mode = Avalonia.Data.BindingMode.TwoWay });
        NumericUpDownValidacionBehavior.SetPermitirVacio(opcional, true);
        var otro = new TextBox();
        var guardar = new Button { Content = "Guardar", Command = vm.GuardarCommand };
        var window = new Window
        {
            Width = 600, Height = 600, DataContext = vm,
            Content = new StackPanel { Children = { anio, opcional, otro, guardar } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(window, vm, anio, opcional, otro, guardar);
    }

    private static TextBox Interno(NumericUpDown nud)
        => nud.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == nud);

    private static void Tipear(NumericUpDown nud, string texto)
    {
        var interno = Interno(nud);
        interno.Focus();
        Dispatcher.UIThread.RunJobs();
        interno.Text = texto;
        Dispatcher.UIThread.RunJobs();
    }

    private static void SacarElFoco(Montaje m)
    {
        m.Otro.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertErrorVisible(NumericUpDown nud, string mensaje)
    {
        Assert.True(DataValidationErrors.GetHasErrors(nud));
        Assert.Contains(nud.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == mensaje && ArbolVisual.EsVisibleEnArbol(t));
    }

    [AvaloniaTheory]
    [InlineData("20a5")]
    [InlineData("abc")]
    public void TextoNoNumerico_MarcaElError_YBloqueaGuardarConTooltip(string texto)
    {
        var m = Montar();
        Assert.True(m.Guardar.IsEffectivelyEnabled);

        Tipear(m.Anio, texto);

        AssertErrorVisible(m.Anio, "Ingresá un año válido, entre 2000 y 2100.");
        Assert.True(m.Vm.HayErroresDeEntrada);
        Assert.False(m.Guardar.IsEffectivelyEnabled);
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(m.Guardar));
        Assert.Equal(2026, m.Vm.Anio);
    }

    [AvaloniaTheory]
    [InlineData("1999")]
    [InlineData("2101")]
    [InlineData("2025,5")]
    public void FueraDeRangoONoEntero_MarcaElError(string texto)
    {
        var m = Montar();

        Tipear(m.Anio, texto);

        AssertErrorVisible(m.Anio, "Ingresá un año válido, entre 2000 y 2100.");
        Assert.False(m.Guardar.IsEffectivelyEnabled);
        Assert.Equal(2026, m.Vm.Anio);
    }

    [AvaloniaFact]
    public void AlPerderElFoco_NoVuelveEnSilencioAlValorAnterior()
    {
        var m = Montar();
        Tipear(m.Anio, "20a5");

        SacarElFoco(m);

        Assert.Equal("20a5", Interno(m.Anio).Text);
        AssertErrorVisible(m.Anio, "Ingresá un año válido, entre 2000 y 2100.");
        Assert.False(m.Guardar.IsEffectivelyEnabled);
        Assert.Equal(2026, m.Vm.Anio);
    }

    [AvaloniaFact]
    public void AlCorregir_SeVaElError_LlegaAlViewModel_YGuardarSeHabilita()
    {
        var m = Montar();
        Tipear(m.Anio, "20a5");
        SacarElFoco(m);

        Tipear(m.Anio, "2025");

        Assert.False(DataValidationErrors.GetHasErrors(m.Anio));
        Assert.False(m.Vm.HayErroresDeEntrada);
        Assert.Equal(2025, m.Vm.Anio);
        Assert.True(m.Guardar.IsEffectivelyEnabled);
        Assert.Null(ToolTip.GetTip(m.Guardar));
    }

    [AvaloniaFact]
    public void Vacio_EsErrorSalvoQueElCampoLoPermita()
    {
        var m = Montar();

        Tipear(m.Anio, "");
        AssertErrorVisible(m.Anio, "Ingresá un año válido, entre 2000 y 2100.");

        Tipear(m.Anio, "2026");
        Tipear(m.Opcional, "");

        Assert.False(DataValidationErrors.GetHasErrors(m.Opcional));
        Assert.False(m.Vm.HayErroresDeEntrada);
        Assert.Null(m.Vm.AnioOpcional);
    }

    [AvaloniaFact]
    public void SiElViewModelCambiaElValor_ReemplazaElTextoInvalidoYSeVaElError()
    {
        var m = Montar();
        Tipear(m.Anio, "20a5");
        SacarElFoco(m);

        m.Vm.Anio = 2024;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("2024", Interno(m.Anio).Text);
        Assert.False(DataValidationErrors.GetHasErrors(m.Anio));
        Assert.False(m.Vm.HayErroresDeEntrada);
    }

    [AvaloniaFact]
    public void ConElErrorVisible_ElClickEnGuardarNoGuardaElValorViejo()
    {
        var m = Montar();
        Tipear(m.Anio, "20a5");

        var centro = new Avalonia.Point(m.Guardar.Bounds.Width / 2, m.Guardar.Bounds.Height / 2);
        var punto = m.Guardar.TranslatePoint(centro, m.Window)!.Value;
        m.Window.MouseDown(punto, Avalonia.Input.MouseButton.Left);
        m.Window.MouseUp(punto, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, m.Vm.Guardados);
        Assert.Equal("20a5", Interno(m.Anio).Text);
    }
}
