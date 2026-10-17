using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// GATE 2 de la Fase 4d (2026-10-17): el tooltip del Guardar bloqueado salía como una caja de 316x28 con
/// "Corregí los campos marcados en rojo para" y le faltaba "continuar.". Causa: las vistas de formulario
/// ponen UseLayoutRounding False en su columna (para evitar el pixel de más de MaxWidth), la propiedad es
/// heredable y el popup del tooltip la hereda del botón. Sin redondeo, el popup mide 316,74 x 28,52 y la
/// ventana nativa se crea de 316x28: el texto (298,74) ya no entra en 298 px, baja de línea y la segunda
/// línea queda fuera de la caja.
/// Va en VisualTests porque necesita las métricas REALES de Inter (Skia): con las métricas falsas de UiTests
/// los anchos son enteros y el redondeo no se nota. Se monta el caso real (host con redondeo apagado, botón
/// v2 con ComandoDeGuardado bloqueado) y se abre el tooltip por el camino de Avalonia.
/// </summary>
public class TooltipBloqueoCompletoTests
{
    private sealed class VmDePrueba : ViewModelBase
    {
        public IAsyncRelayCommand GuardarCommand => field ??= ComandoDeGuardado(() => Task.CompletedTask);
    }

    // redondeo=true es el CONTROL: pasa con o sin el arreglo del estilo global de ToolTip (el redondeo queda encendido
    // en los dos casos). Quien custodia el arreglo es redondeo=false, que da rojo si se quita el UseLayoutRounding del estilo.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TooltipDeGuardarBloqueado_MuestraElMensajeCompleto_ConYSinRedondeoDeLayoutHeredado(bool redondeo)
    {
        var vm = new VmDePrueba();
        var guardar = new Button { Classes = { "primario" }, Content = "Guardar", Command = vm.GuardarCommand };
        var raiz = new UserControl { Classes = { "ui2" }, DataContext = vm, Content = guardar };
        var ventana = new Window { Width = 800, Height = 400, UseLayoutRounding = redondeo, Content = raiz };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        vm.HayErroresDeEntrada = true;
        Dispatcher.UIThread.RunJobs();

        ToolTip.SetIsOpen(guardar, true);
        Dispatcher.UIThread.RunJobs();

        var tip = ventana.GetVisualDescendants().OfType<ToolTip>().Single();
        var texto = tip.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, texto.Text);

        // La ventana nativa del popup se crea con el tamaño ENTERO truncado (X11): un tamaño fraccionario
        // (316,74 x 28,52) la deja 1 px más chica que el texto y la última palabra baja de línea y se pierde.
        // En headless no hay ventana nativa que trunque, así que se custodia la causa: tamaño entero.
        var d = tip.DesiredSize;
        Assert.True(d.Width == System.Math.Ceiling(d.Width) && d.Height == System.Math.Ceiling(d.Height),
            $"El tooltip mide {d.Width}x{d.Height} (redondeo heredado={redondeo}): una ventana nativa de popup "
            + "lo trunca y el texto queda cortado.");

    }
}
