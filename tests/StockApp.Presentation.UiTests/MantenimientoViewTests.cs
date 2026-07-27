using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Backups;
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
        public BackupsServiceFake(IReadOnlyList<CorridaBackupDto> corridas) => _corridas = corridas;

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => Task.FromResult(_corridas);
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(new BackupDescargaDto("x.dump", new MemoryStream()));
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => Task.FromResult(new SaludBackupDto(null, true, 26));
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

    private static (Window Window, MantenimientoViewModel Vm) Montar(IReadOnlyList<CorridaBackupDto> corridas)
    {
        var vm = new MantenimientoViewModel(
            new BackupsServiceFake(corridas), new ServicioGuardadoArchivoFake(), new ConfirmacionServiceFake());

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
}
