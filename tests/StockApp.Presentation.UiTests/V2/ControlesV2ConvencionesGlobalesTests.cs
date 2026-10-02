using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.ViewModels;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Los estilos globales de Themes/Controls.axaml (ExplicarBloqueo en Button; Validar y Reportar en
/// NumericUpDown) siguen aplicando DENTRO del scope .ui2: los estilos v2 los pisan en apariencia
/// pero no pueden dejarlos sin setters. Si un control v2 pierde el comportamiento, un campo en
/// rojo vuelve a dejar guardar el valor viejo en silencio (bug 2026-10-01).
/// </summary>
public partial class ControlesV2ConvencionesGlobalesTests
{
    private sealed partial class VmDePrueba : ViewModelBase
    {
        [ObservableProperty] private int _anio = 2026;
        public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(() => Task.CompletedTask);
    }

    private static (Window Ventana, VmDePrueba Vm, Button Guardar, NumericUpDown Anio) MontarV2()
    {
        var ventana = Montar("""
            <NumericUpDown x:Name="Anio" Minimum="2000" Maximum="2100" FormatString="0" Value="{Binding Anio, Mode=TwoWay}" />
            <TextBox x:Name="Otro" />
            <Button x:Name="Guardar" Classes="primario" Content="Guardar" Command="{Binding GuardarCommand}" />
            """);
        var vm = new VmDePrueba();
        ventana.DataContext = vm;
        Dispatcher.UIThread.RunJobs();
        return (ventana, vm, Buscar<Button>(ventana, "Guardar"), Buscar<NumericUpDown>(ventana, "Anio"));
    }

    [AvaloniaFact]
    public void BotonPrimarioV2_ConUnComandoDeGuardado_QuedaDeshabilitadoYExplicaElMotivo()
    {
        var (_, vm, guardar, _) = MontarV2();
        Assert.True(guardar.IsEffectivelyEnabled);

        vm.HayErroresDeEntrada = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(guardar.IsEffectivelyEnabled);
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(guardar));
        Assert.True(ToolTip.GetShowOnDisabled(guardar));
    }

    [AvaloniaFact]
    public void NumericUpDownV2_ConTexto20a5_MarcaElError_YBloqueaGuardar()
    {
        var (ventana, vm, guardar, anio) = MontarV2();
        var interno = anio.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == anio);

        interno.Focus();
        Dispatcher.UIThread.RunJobs();
        interno.Text = "20a5";
        Dispatcher.UIThread.RunJobs();

        Assert.True(DataValidationErrors.GetHasErrors(anio));
        Assert.True(vm.HayErroresDeEntrada);
        Assert.False(guardar.IsEffectivelyEnabled);
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(guardar));
        Assert.Equal(2026, vm.Anio);
    }
}
