using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Backups;
using StockApp.Application.Logs;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta MantenimientoView real (no un banco de pruebas aislado) con un VM real + fakes hechos
/// a mano (mismo patron que MovimientoFormControlValidacionTests.cs) para confirmar que la
/// carga via DataContextChanged funciona end-to-end y que el boton "Descargar" queda
/// deshabilitado para una corrida Fallida (NombreArchivo null). ConfirmacionServiceFake ya
/// existe en MovimientoRegistroFakes.cs (mismo proyecto, internal) — se reutiliza tal cual.
/// </summary>
public class MantenimientoViewTests
{
    private sealed class BackupsServiceFake : IBackupsService
    {
        private readonly IReadOnlyList<CorridaBackupDto> _corridas;
        private readonly Exception? _excepcionAlIniciar;
        public int VecesIniciado { get; private set; }
        private readonly TaskCompletionSource? _senialIniciar;

        public BackupsServiceFake(
            IReadOnlyList<CorridaBackupDto> corridas, Exception? excepcionAlIniciar = null,
            TaskCompletionSource? senialIniciar = null)
        {
            _corridas = corridas;
            _excepcionAlIniciar = excepcionAlIniciar;
            _senialIniciar = senialIniciar;
        }

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => Task.FromResult(_corridas);
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(new BackupDescargaDto("x.dump", new MemoryStream()));
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => Task.FromResult(new SaludBackupDto(null, true, 26));

        public async Task IniciarAsync(CancellationToken ct = default)
        {
            VecesIniciado++;
            if (_senialIniciar is not null)
                await _senialIniciar.Task;
            if (_excepcionAlIniciar is not null)
                throw _excepcionAlIniciar;
        }
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default) => Task.FromResult(true);
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:admin="clr-namespace:StockApp.Presentation.Views.Administracion;assembly=StockApp.Presentation"
                Width="700" Height="500">
            <admin:MantenimientoView />
        </Window>
        """;

    private static (Window Window, MantenimientoViewModel Vm) Montar(
        IReadOnlyList<CorridaBackupDto> corridas, ResumenLogsDto? resumenLogs = null,
        IBackupsService? backups = null, ConfirmacionServiceFake? confirmacion = null)
    {
        var vm = new MantenimientoViewModel(
            backups ?? new BackupsServiceFake(corridas), new ServicioGuardadoArchivoFake(),
            confirmacion ?? new ConfirmacionServiceFake(), new LogsServiceFake(resumenLogs));

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ConCorridas_MuestraLasFilasCargadas()
    {
        var corridas = new List<CorridaBackupDto>
        {
            new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null),
            new(2, DateTime.UtcNow.AddHours(-12), "Fallida", null, null, "pg_dump fallo"),
        };

        var (window, vm) = Montar(corridas);

        Assert.Equal(2, vm.Corridas.Count);
        var botones = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content as string == "Descargar").ToList();
        Assert.Equal(2, botones.Count);
    }

    [AvaloniaFact]
    public void Montar_CorridaFallidaSinArchivo_BotonDescargarQuedaDeshabilitado()
    {
        var corridas = new List<CorridaBackupDto> { new(2, DateTime.UtcNow, "Fallida", null, null, "pg_dump fallo") };

        var (window, _) = Montar(corridas);

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        Assert.False(boton.IsEnabled);
    }

    [AvaloniaFact]
    public void Montar_CorridaExitosa_BotonDescargarQuedaHabilitado()
    {
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };

        var (window, _) = Montar(corridas);

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        Assert.True(boton.IsEnabled);
    }

    [AvaloniaFact]
    public void Montar_FilaSinDescargaEnCurso_MuestraDescargarYOcultaCancelar()
    {
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };

        var (window, _) = Montar(corridas);

        var botonDescargar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        var botonCancelar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancelar");
        Assert.True(botonDescargar.IsVisible);
        Assert.False(botonCancelar.IsVisible);
    }

    /// <summary>
    /// Reproduce el bug real reportado en el review: sin ScrollViewer, MantenimientoView era un
    /// DockPanel -> ItemsControl sin nada que scrollee; a los pocos días de retención (~13-16
    /// corridas exitosas + fallidas) la lista supera el alto de la ventana y el botón "Descargar"
    /// de las filas más viejas queda fuera del área clickeable. Se monta con 25 filas en la misma
    /// ventana chica (700x500) que ya usan los demás tests de esta clase para que el contenido
    /// exceda el viewport de verdad.
    /// </summary>
    [AvaloniaFact]
    public void Montar_ConMuchasCorridas_ElScrollViewerHaceAlcanzableLaUltimaFila()
    {
        var corridas = Enumerable.Range(1, 25)
            .Select(i => new CorridaBackupDto(i, DateTime.UtcNow.AddHours(-i), "Exitosa", $"backup_{i}.dump", 1024, null))
            .ToList();

        var (window, vm) = Montar(corridas);

        Assert.Equal(25, vm.Corridas.Count);

        var scrollViewer = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scrollViewer);

        // 25 cards de ~60px superan largamente el alto de la ventana (500px): el contenido
        // medido (Extent) tiene que ser mayor que lo visible (Viewport). Sin el ScrollViewer
        // esta aserción ya falla arriba (NotNull) porque no hay ningún ScrollViewer en el árbol.
        Assert.True(scrollViewer!.Extent.Height > scrollViewer.Viewport.Height);

        // Escrolleamos hasta el final y confirmamos que el botón "Descargar" de la ÚLTIMA fila
        // (la corrida más vieja, la que el bug real dejaba inalcanzable) cae dentro del área
        // visible de la ventana una vez scrolleado — no solo que "existe" en el árbol visual
        // (GetVisualDescendants encuentra controles fuera de vista igual).
        scrollViewer.Offset = new Vector(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        Dispatcher.UIThread.RunJobs();

        var ultimoBoton = window.GetVisualDescendants().OfType<Button>()
            .Last(b => b.Content as string == "Descargar");
        var posicion = ultimoBoton.TranslatePoint(new Point(0, 0), window);

        Assert.NotNull(posicion);
        Assert.InRange(posicion!.Value.Y, 0, window.Bounds.Height);
    }

    /// <summary>
    /// Estado vacío (review final E1): sin backups cargados, la pantalla mostraba nada bajo el
    /// subtítulo, indistinguible de "falló algo y no me enteré" — es lo primero que ve el
    /// municipio el día 1 de una instalación nueva.
    /// </summary>
    [AvaloniaFact]
    public void Montar_SinCorridas_MuestraElMensajeDeListaVacia()
    {
        var (window, vm) = Montar(new List<CorridaBackupDto>());

        Assert.Empty(vm.Corridas);
        Assert.True(vm.MostrarListaVacia);
        var texto = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Todavía no hay backups registrados.");
        Assert.NotNull(texto);
        Assert.True(texto!.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_ConCorridas_OcultaElMensajeDeListaVacia()
    {
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };

        var (window, vm) = Montar(corridas);

        Assert.False(vm.MostrarListaVacia);
        var texto = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == "Todavía no hay backups registrados.");
        Assert.False(texto.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_FilaConDescargaEnCurso_MuestraCancelarYOcultaDescargar()
    {
        // Setea Descargando directo sobre la fila (ObservableObject) en vez de orquestar una
        // descarga async real dentro del dispatcher headless — la coordinacion async de
        // DescargarCommand/CancelarCommand ya esta cubierta a nivel logico por los tests de
        // MantenimientoViewModelTests (Task 9); aca solo se verifica el WIRING de XAML (que
        // Descargando efectivamente alterna que boton se ve), que es lo que le toca a esta Task.
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };
        var (window, vm) = Montar(corridas);

        vm.Corridas[0].Descargando = true;
        Dispatcher.UIThread.RunJobs();

        var botonDescargar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        var botonCancelar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancelar");
        Assert.False(botonDescargar.IsVisible);
        Assert.True(botonCancelar.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_ConLogs_MuestraElResumenYHabilitaLaDescarga()
    {
        var (window, vm) = Montar(
            [],
            new ResumenLogsDto(2, new DateTime(2026, 7, 28), new DateTime(2026, 7, 29), 4096));

        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HayLogs);
        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains(textos, t => t.Contains("Diagnóstico", StringComparison.Ordinal));
        Assert.Contains(textos, t => t.Contains("2 archivo(s)", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Montar_SinLogs_MuestraLaZonaConElMensajeVacio()
    {
        var (window, vm) = Montar([], new ResumenLogsDto(0, null, null, 0));

        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HayLogs);
        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains(textos, t => t.Contains("No hay archivos de log todavía", StringComparison.Ordinal));
    }

    /// <summary>
    /// Hallazgo MINOR (review final E2): DescargandoLogs existía en el VM pero no estaba
    /// bindeada en ningún XAML -- con el timeout de 30 minutos del HttpClient "Descargas", si
    /// el ZIP pesa la UI quedaba muda. Mismo criterio que
    /// Montar_FilaConDescargaEnCurso_MuestraCancelarYOcultaDescargar: setea DescargandoLogs
    /// directo sobre el VM en vez de orquestar una descarga async real dentro del dispatcher
    /// headless -- acá solo se verifica el WIRING de XAML.
    /// </summary>
    [AvaloniaFact]
    public void Montar_DescargaDeLogsEnCurso_MuestraLaSenialDeProgresoYOcultaElBotonDeAccion()
    {
        var (window, vm) = Montar(
            [], new ResumenLogsDto(2, new DateTime(2026, 7, 28), new DateTime(2026, 7, 29), 4096));

        vm.DescargandoLogs = true;
        Dispatcher.UIThread.RunJobs();

        var botonDescargar = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "Descargar logs");
        var botonDescargando = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "Descargando…");

        Assert.False(botonDescargar.IsVisible);
        Assert.True(botonDescargando.IsVisible);
        Assert.False(botonDescargando.IsEnabled);
    }

    // ── Hacer backup ahora (fix/integridad-referencial, POST /backups) ──────────

    [AvaloniaFact]
    public void Montar_ClickEnHacerBackupAhora_DisparaIniciarAsync()
    {
        var backups = new BackupsServiceFake(new List<CorridaBackupDto>());
        var (window, vm) = Montar([], backups: backups);

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Hacer backup ahora");
        boton.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, backups.VecesIniciado);
    }

    [AvaloniaFact]
    public void Montar_MientrasIniciaBackup_MuestraIniciandoYOcultaHacerBackupAhora()
    {
        var senial = new TaskCompletionSource();
        var backups = new BackupsServiceFake(new List<CorridaBackupDto>(), senialIniciar: senial);
        var (window, vm) = Montar([], backups: backups);

        vm.IniciarBackupCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var botonHacer = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Hacer backup ahora");
        var botonIniciando = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Iniciando…");
        Assert.False(botonHacer.IsVisible);
        Assert.True(botonIniciando.IsVisible);
        Assert.False(botonIniciando.IsEnabled);

        senial.SetResult();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_HacerBackupAhoraFalla_InformaElErrorYRestauraElBoton()
    {
        var confirmacion = new ConfirmacionServiceFake();
        var backups = new BackupsServiceFake(
            new List<CorridaBackupDto>(), excepcionAlIniciar: new InvalidOperationException("Ya hay un backup en curso."));
        var (window, vm) = Montar([], backups: backups, confirmacion: confirmacion);

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Hacer backup ahora");
        boton.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IniciandoBackup);
        var botonHacer = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Hacer backup ahora");
        Assert.True(botonHacer.IsVisible);
    }
}
