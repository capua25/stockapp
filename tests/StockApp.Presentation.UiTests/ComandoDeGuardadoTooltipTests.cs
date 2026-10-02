using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El tema (Themes/Controls.axaml, style global de Button) le pone a TODO botón cuyo Command es un
/// comando de guardado (<c>ViewModelBase.ComandoDeGuardado</c>) el tooltip del bloqueo, visible
/// aun deshabilitado, sin tocar el XAML de la vista. Las vistas reales se cubren en
/// <see cref="GuardarBloqueadoConErroresDeEntradaTests"/>; acá se fija el contrato del behavior.
/// </summary>
public class ComandoDeGuardadoTooltipTests
{
    private sealed class VmDePrueba : ViewModelBase
    {
        public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(() => Task.CompletedTask);
        public IRelayCommand CancelarCommand => field ??= new RelayCommand(() => { });
    }

    private static (Window Window, VmDePrueba Vm, Button Guardar, Button Cancelar, Button ConTipPropio) Montar()
    {
        var vm = new VmDePrueba();
        var guardar = new Button { Content = "Guardar", Command = vm.GuardarCommand };
        var cancelar = new Button { Content = "Cancelar", Command = vm.CancelarCommand };
        var conTipPropio = new Button { Content = "Guardar 2", Command = vm.GuardarCommand };
        ToolTip.SetTip(conTipPropio, "Tooltip propio de la vista");
        var window = new Window
        {
            DataContext = vm,
            Content = new StackPanel { Children = { guardar, cancelar, conTipPropio } },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, guardar, cancelar, conTipPropio);
    }

    [AvaloniaFact]
    public void BotonDeGuardado_ConErrores_QuedaDeshabilitadoYExplicaElMotivo()
    {
        var (_, vm, guardar, _, _) = Montar();
        Assert.True(guardar.IsEffectivelyEnabled);
        Assert.Null(ToolTip.GetTip(guardar));

        vm.HayErroresDeEntrada = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(guardar.IsEffectivelyEnabled);
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(guardar));
        Assert.True(ToolTip.GetShowOnDisabled(guardar));

        vm.HayErroresDeEntrada = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(guardar.IsEffectivelyEnabled);
        Assert.Null(ToolTip.GetTip(guardar));
    }

    [AvaloniaFact]
    public void BotonQueNoEsDeGuardado_NoSeBloqueaNiRecibeTooltip()
    {
        var (_, vm, _, cancelar, _) = Montar();

        vm.HayErroresDeEntrada = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(cancelar.IsEffectivelyEnabled);
        Assert.Null(ToolTip.GetTip(cancelar));
    }

    [AvaloniaFact]
    public void UnTooltipPuestoEnLaVista_GanaSobreElDelTema()
    {
        var (_, vm, _, _, conTipPropio) = Montar();

        vm.HayErroresDeEntrada = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(conTipPropio.IsEffectivelyEnabled);
        Assert.Equal("Tooltip propio de la vista", ToolTip.GetTip(conTipPropio));
    }
}
