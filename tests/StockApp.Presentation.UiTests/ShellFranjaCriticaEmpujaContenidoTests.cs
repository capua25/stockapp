using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Decisión de Martin (2026-10-19): la franja crítica en sesión ocupa su PROPIA fila arriba de
/// MainWindow y empuja el contenido 56px hacia abajo, en vez de tapar los botones del encabezado
/// de las pantallas de lista. Estos tests usan la MainWindow REAL: un test de VM no custodia el
/// XAML (ver memoria "Test de VM no custodia gate de UI"). Los demás overlays no cambian.
/// </summary>
public class ShellFranjaCriticaEmpujaContenidoTests
{
    private const double AltoFranja = 56;

    /// <summary>Contenido representativo: una pantalla de lista con su botón de acción arriba.</summary>
    private sealed class PantallaListaFalsaViewModel : ViewModelBase { }

    private sealed class DispatcherInline : IUiDispatcher
    {
        public void Post(Action accion) => accion();
    }

    private static ShellViewModel CrearShell()
    {
        var info = Mock.Of<IInfoApp>(x => x.Version == "0.0.0");
        var coordinador = new CoordinadorActualizacion(Mock.Of<IUpdateService>(), new PoliticaUxActualizacion());
        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(),
            Mock.Of<ILicenciaService>(),
            Mock.Of<IResetAdminService>(),
            new NavigationService(t => throw new InvalidOperationException(t.Name)),
            coordinador,
            new DispatcherInline(),
            info);
        shell.CurrentViewModel = new PantallaListaFalsaViewModel();
        return shell;
    }

    [SuppressMessage("Trimming", "IL2026", Justification = "Test headless.")]
    private static (MainWindow Window, Button BotonDelEncabezado) Montar(ShellViewModel shell)
    {
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = shell };
        window.DataTemplates.Add(new FuncDataTemplate<PantallaListaFalsaViewModel>(
            (_, _) => new Button
            {
                Name = "BotonEncabezado",
                Content = "Nuevo",
                Width = 120,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 28, 0),
            }));
        window.DataTemplates.Add(new ViewLocator());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, Boton(window));
    }

    private static Button Boton(MainWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BotonEncabezado");

    private static double Y(Visual v, Visual raiz) => v.TranslatePoint(new Point(0, 0), raiz)!.Value.Y;

    private static void Asentar(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // El hit-test lee la escena del compositor: sin un tick de render ve el layout viejo.
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool BotonRecibeElClick(MainWindow window, Button boton)
    {
        // La escena del compositor puede ir uno o dos ticks atrasada respecto del layout; si algo
        // TAPA el botón, lo sigue tapando después de todos los reintentos.
        for (var intento = 0; intento < 10; intento++)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var centro = boton.TranslatePoint(new Point(boton.Bounds.Width / 2, boton.Bounds.Height / 2), window)!.Value;
            var tocado = window.InputHitTest(centro) as Visual;
            if (tocado is not null && (ReferenceEquals(tocado, boton) || tocado.GetVisualAncestors().Contains(boton)))
                return true;
        }
        return false;
    }

    [AvaloniaFact]
    public void SinOverlay_ElContenidoEmpiezaArriba()
    {
        var (window, boton) = Montar(CrearShell());

        Assert.Equal(12, Y(boton, window), 1);
        Assert.True(BotonRecibeElClick(window, boton));
    }

    [AvaloniaFact]
    public void FranjaCritica_EmpujaElContenidoExactamenteElAltoDeLaFranja()
    {
        var shell = CrearShell();
        var (window, boton) = Montar(shell);
        var yAntes = Y(boton, window);

        shell.OverlayActualizacion = DatosFranja();
        Asentar(window);

        Assert.Equal(yAntes + AltoFranja, Y(boton, window), 1);
    }

    [AvaloniaFact]
    public void FranjaCritica_NoTapaElBotonDelEncabezado()
    {
        var shell = CrearShell();
        var (window, boton) = Montar(shell);

        shell.OverlayActualizacion = DatosFranja();
        Asentar(window);

        Assert.True(Y(boton, window) >= AltoFranja, "El contenido quedó debajo de la franja.");
        Assert.True(BotonRecibeElClick(window, boton), "La franja tapa el botón del encabezado.");
    }

    [AvaloniaFact]
    public void FranjaCritica_ElContenidoSeAcortaPorElAltoDeLaFranja()
    {
        var shell = CrearShell();
        var (window, boton) = Montar(shell);
        var contenedor = boton.GetVisualAncestors().OfType<ContentControl>().First();
        var altoAntes = contenedor.Bounds.Height;

        shell.OverlayActualizacion = DatosFranja();
        Asentar(window);

        Assert.Equal(altoAntes - AltoFranja, contenedor.Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void FranjaCritica_AlDesaparecer_ElContenidoRecuperaTodoElAlto()
    {
        var shell = CrearShell();
        var (window, boton) = Montar(shell);
        var yAntes = Y(boton, window);

        shell.OverlayActualizacion = DatosFranja();
        Asentar(window);
        shell.OverlayActualizacion = null;
        Asentar(window);

        Assert.Equal(yAntes, Y(boton, window), 1);
    }

    [AvaloniaFact]
    public void Banner_SigueFlotandoSobreElContenido_SinEmpujarlo()
    {
        var shell = CrearShell();
        var (window, boton) = Montar(shell);
        var yAntes = Y(boton, window);

        shell.OverlayActualizacion = new ActualizacionBannerViewModel(
            new AccionUx(ModoUx.BannerDiscreto, "Mejoras.", true, false, "0.5.0"));
        Asentar(window);

        Assert.Equal(yAntes, Y(boton, window), 1);
        Assert.Contains(window.GetVisualDescendants(), v => v is Avalonia.Controls.Control c
            && c.GetType().Name == "ActualizacionBannerView");
    }

    [AvaloniaFact]
    public void FranjaCritica_SeDibujaUnaSolaVezYArribaDeTodo()
    {
        var shell = CrearShell();
        var (window, _) = Montar(shell);

        shell.OverlayActualizacion = DatosFranja();
        Asentar(window);

        var vistas = window.GetVisualDescendants().OfType<StockApp.Presentation.Actualizaciones.Views.ActualizacionAvisoCriticoView>().ToList();
        var vista = Assert.Single(vistas);
        Assert.Equal(0, Y(vista, window), 1);
        Assert.Equal(AltoFranja, vista.Bounds.Height, 1);
    }

    private static ActualizacionAvisoCriticoViewModel DatosFranja() =>
        new(new AccionUx(ModoUx.AvisoCriticoEnSesion, "Corrige un problema de seguridad.", false, true, "0.5.0"));
}
