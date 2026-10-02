using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Login v2 (spec §5.4): todos los estados actuales y Enter en los campos.</summary>
public class LoginViewV2Tests
{
    private sealed class AuthQueRechaza : IAuthService
    {
        public int Intentos { get; private set; }

        public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
        {
            Intentos++;
            return Task.FromResult(new LoginResult(false));
        }

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync() =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1280" Height="760">
            <vistas:LoginView />
        </Window>
        """;

    private static (Window Ventana, LoginViewModel Vm, AuthQueRechaza Auth) Montar(bool soloAccesoLimitado = false)
    {
        var auth = new AuthQueRechaza();
        // ShellViewModel solo guarda sus dependencias; el camino de login fallido no las usa.
        var shell = new ShellViewModel(auth, null!, null!, null!, null!, null!, new InfoAppFake("0.4.0"));
        var vm = new LoginViewModel(auth, shell, new InfoAppFake("0.4.0"), soloAccesoLimitado);
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return (ventana, vm, auth);
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static void Enter(TextBox campo)
    {
        campo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Raiz_TieneUi2() =>
        Assert.Contains("ui2", Montar().Ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.LoginView>().Single().Classes);

    [AvaloniaFact]
    public void Marca_LlevaLaLineaMunicipioDeCarmeloBajoElTitulo()
    {
        // P6: bajo "Gestión Municipal" y antes de "Ingresá tus credenciales…".
        var ventana = Montar().Ventana;

        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().ToList();
        var titulo = textos.Single(t => t.Classes.Contains("titulo-login"));
        var subtitulo = Assert.Single(textos, t => t.Classes.Contains("marca-subtitulo"));
        Assert.Equal("Municipio de Carmelo", subtitulo.Text);
        var yTitulo = titulo.TranslatePoint(default, ventana)!.Value.Y;
        var yInstruccion = textos.Single(t => t.Text == "Ingresá tus credenciales para continuar.").TranslatePoint(default, ventana)!.Value.Y;
        var ySubtitulo = subtitulo.TranslatePoint(default, ventana)!.Value.Y;
        Assert.InRange(ySubtitulo, yTitulo + 1, yInstruccion - 1);
    }

    [AvaloniaFact]
    public void MarcaDeAgua_ExisteDetrasDeLaCard() =>
        // El detalle (opacidad, escala, región) lo custodia LoginMarcaAguaTests; acá, que la parte exista.
        Assert.NotNull(Nombrado<Image>(Montar().Ventana, "MarcaAgua").Source);

    [AvaloniaTheory]
    [InlineData("CampoUsuario")]
    [InlineData("CampoContrasena")]
    public void Enter_EnUnCampo_EjecutaEntrar(string campo)
    {
        // Review Focus 4: el KeyDown="OnCampoKeyDown" no puede perderse en la reescritura.
        var (ventana, vm, auth) = Montar();
        vm.NombreUsuario = "mperez";
        vm.Contrasena = "secreto1";
        // Con IsDefault el Enter sintético burbujea hasta la ventana y dispara el botón sin pasar por
        // OnCampoKeyDown: el test no vigilaría nada (verificado por mutación). Se apaga para aislar el handler.
        Nombrado<Button>(ventana, "BotonEntrar").IsDefault = false;

        Enter(Nombrado<TextBox>(ventana, campo));

        Assert.Equal(1, auth.Intentos);
        Assert.Equal("Usuario o contraseña incorrectos.", vm.MensajeError);
    }

    [AvaloniaFact]
    public void ErrorDeLogin_SeMuestraEnLaAlertaDeError()
    {
        var (ventana, vm, _) = Montar();

        vm.MensajeError = "Usuario o contraseña incorrectos.";
        Dispatcher.UIThread.RunJobs();

        var alerta = Nombrado<Alerta>(ventana, "AlertaError");
        Assert.True(alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("Usuario o contraseña incorrectos.", alerta.Texto);
    }

    [AvaloniaFact]
    public void SinError_LaAlertaDeErrorNoSeVe() =>
        Assert.False(Nombrado<Alerta>(Montar().Ventana, "AlertaError").IsVisible);

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AvisoDeAccesoLimitado_SoloEnEseModo(bool soloAccesoLimitado)
    {
        var alerta = Nombrado<Alerta>(Montar(soloAccesoLimitado).Ventana, "AvisoAccesoLimitado");

        Assert.Equal(soloAccesoLimitado, alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Advertencia, alerta.Variante);
    }

    [AvaloniaFact]
    public void BotonesConSusTextosRealesYComandos()
    {
        var (ventana, vm, _) = Montar();

        var entrar = Nombrado<Button>(ventana, "BotonEntrar");
        Assert.Equal("Entrar", entrar.Content);
        Assert.Contains("primario", entrar.Classes);
        Assert.True(entrar.IsDefault);
        Assert.Same(vm.EntrarCommand, entrar.Command);
        var reset = Nombrado<Button>(ventana, "EnlaceReset");
        Assert.Equal("No puedo entrar / resetear Admin", reset.Content);
        Assert.Same(vm.ResetearAdminCommand, reset.Command);
    }

    [AvaloniaFact]
    public void BotonEntrar_ConUnCampoEnRojo_QuedaBloqueadoYElTemaExplicaElMotivo()
    {
        // Convención 3: EntrarCommand es un ComandoDeGuardado; el estilo global de Button (que .ui2
        // no pisa) pone el tooltip. La vista NO declara ToolTip.Tip ni IsEnabled a mano.
        var (ventana, vm, _) = Montar();
        var entrar = Nombrado<Button>(ventana, "BotonEntrar");

        vm.HayErroresDeEntrada = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(entrar.IsEffectivelyEnabled);
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(entrar));
    }
}
