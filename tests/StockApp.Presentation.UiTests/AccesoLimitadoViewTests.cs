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
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fix (MINOR, tercer review final E1): AccesoLimitadoView no tenía ninguna cobertura -- ni acá
/// (ver ViewLocatorTests) ni un montaje headless. Es la pantalla que el admin necesita cuando la
/// licencia venció (modo acotado, FIX 1 re-review final E1): hostea MantenimientoView adentro
/// via <c>DataContext="{Binding Mantenimiento}"</c> (a diferencia de MantenimientoViewTests, que
/// monta MantenimientoView directo con DataContext heredado de la Window). Este test verifica
/// específicamente que ese binding anidado efectivamente dispara el DataContextChanged de
/// MantenimientoView y carga la lista -- el bug recurrente número uno de este proyecto es que
/// las Views de Avalonia acá no se auto-inicializan.
/// </summary>
public class AccesoLimitadoViewTests
{
    private sealed class BackupsServiceFake : IBackupsService
    {
        private readonly IReadOnlyList<CorridaBackupDto> _corridas;
        public BackupsServiceFake(IReadOnlyList<CorridaBackupDto> corridas) => _corridas = corridas;

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => Task.FromResult(_corridas);
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(new BackupDescargaDto("x.dump", new MemoryStream()));
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => Task.FromResult(new SaludBackupDto(null, true, 26));
        public Task IniciarAsync(CancellationToken ct = default) => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default) => Task.FromResult(true);
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:acc="clr-namespace:StockApp.Presentation.Views;assembly=StockApp.Presentation"
                Width="700" Height="500">
            <acc:AccesoLimitadoView />
        </Window>
        """;

    private static (Window Window, AccesoLimitadoViewModel Vm) Montar(IReadOnlyList<CorridaBackupDto> corridas)
    {
        var mantenimiento = new MantenimientoViewModel(
            new BackupsServiceFake(corridas), new ServicioGuardadoArchivoFake(), new ConfirmacionServiceFake(), new LogsServiceFake());
        var vm = new AccesoLimitadoViewModel(mantenimiento);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged anidado

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ConCorridas_LaMantenimientoViewAnidadaCargaLaLista()
    {
        var corridas = new List<CorridaBackupDto>
        {
            new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null),
            new(2, DateTime.UtcNow.AddHours(-12), "Fallida", null, null, "pg_dump falló"),
        };

        var (window, vm) = Montar(corridas);

        // Verificación real del binding anidado, no solo del VM: si DataContext="{Binding
        // Mantenimiento}" no propagara o DataContextChanged no disparara CargarAsync(), esto
        // sería 0 -- el VM en sí ya tiene los datos por otro camino (CargarAsync manual), pero
        // acá lo que se prueba es el wiring de la vista, igual que MantenimientoViewTests.
        Assert.Equal(2, vm.Mantenimiento.Corridas.Count);

        var botones = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content as string == "Descargar").ToList();
        Assert.Equal(2, botones.Count);
    }

    [AvaloniaFact]
    public void Montar_SinCorridas_MuestraElMensajeDeListaVaciaDeLaVistaAnidada()
    {
        var (window, vm) = Montar(new List<CorridaBackupDto>());

        Assert.Empty(vm.Mantenimiento.Corridas);
        Assert.True(vm.Mantenimiento.MostrarListaVacia);
        var texto = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Todavía no hay backups registrados.");
        Assert.NotNull(texto);
        Assert.True(texto!.IsVisible);
    }
}
