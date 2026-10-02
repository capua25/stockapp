using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Reset de Admin y activación de licencia v2 (spec §5.4): todos los estados actuales.</summary>
public class ResetYLicenciaV2Tests
{
    private sealed class ResetInerte : IResetAdminService
    {
        public Task<DesafioResetDto> SolicitarDesafioAsync() => Task.FromResult(new DesafioResetDto("D", "C"));
        public Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena) => Task.FromResult(new ResultadoResetDto(true, null));
    }

    private sealed class LicenciaFija : ILicenciaService
    {
        public Task<EstadoLicenciaDto> ObtenerEstadoAsync() => Task.FromResult(new EstadoLicenciaDto(false, "7F3A-91C2-4D8E-B605"));
        public Task<ResultadoActivacionDto> ActivarAsync(string licencia) => Task.FromResult(new ResultadoActivacionDto(false, "x"));
    }

    private static Window Montar(Control vista, object vm)
    {
        var ventana = new Window { Width = 1280, Height = 760, Content = vista };
        vista.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return ventana;
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    [AvaloniaFact]
    public void Reset_Completado_MuestraElAvisoDeExito()
    {
        var vm = new ResetAdminViewModel(new ResetInerte());
        var ventana = Montar(new ResetAdminView(), vm);
        Assert.False(Nombrado<Alerta>(ventana, "AlertaCompletado").IsVisible);

        vm.Completado = true;
        Dispatcher.UIThread.RunJobs();

        var alerta = Nombrado<Alerta>(ventana, "AlertaCompletado");
        Assert.True(alerta.IsVisible);
        Assert.Equal("Listo. Volvé al login e ingresá con la nueva contraseña.", alerta.Texto);
    }

    [AvaloniaFact]
    public void Reset_Error_MuestraLaAlertaDeError()
    {
        var vm = new ResetAdminViewModel(new ResetInerte());
        var ventana = Montar(new ResetAdminView(), vm);

        vm.MensajeError = "El token no es válido.";
        Dispatcher.UIThread.RunJobs();

        var alerta = Nombrado<Alerta>(ventana, "AlertaError");
        Assert.True(alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
    }

    [AvaloniaFact]
    public void Bloqueo_AlAsignarElVm_CargaElCodigoDeMaquina()
    {
        // Review Focus 4: el DataContextChanged del code-behind no puede perderse.
        var ventana = Montar(new BloqueoLicenciaView(), new BloqueoLicenciaViewModel(new LicenciaFija()));

        Assert.Contains(ventana.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "7F3A-91C2-4D8E-B605");
    }

    [AvaloniaFact]
    public void Bloqueo_EnlaceDeAccesoLimitado_UsaSuComando()
    {
        var vm = new BloqueoLicenciaViewModel(new LicenciaFija());
        var ventana = Montar(new BloqueoLicenciaView(), vm);

        var enlace = Nombrado<Button>(ventana, "EnlaceAccesoLimitado");
        Assert.Equal("¿Necesitás bajar un backup? Iniciar sesión", enlace.Content);
        Assert.Same(vm.IrALoginAccesoLimitadoCommand, enlace.Command);
    }

    [AvaloniaFact]
    public void AmbasVistas_TienenUi2EnLaRaiz()
    {
        Assert.Contains("ui2", new ResetAdminView().Classes);
        Assert.Contains("ui2", new BloqueoLicenciaView().Classes);
    }

    [AvaloniaFact]
    public void Bloqueo_ParrafoSecundario_VaAlAltoDeLineaDeControl()
    {
        // Sin LineHeight, el párrafo envuelto salía a 15 px de interlineado y el mockup usa 16.
        var ventana = Montar(new BloqueoLicenciaView(), new BloqueoLicenciaViewModel(new LicenciaFija()));

        var parrafo = ventana.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("texto-secundario"));
        Assert.Equal(16, parrafo.LineHeight);
    }
}
