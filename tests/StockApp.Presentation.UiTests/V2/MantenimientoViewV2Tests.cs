using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Alertas;
using StockApp.Application.Backups;
using StockApp.Application.Logs;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.Views.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Mantenimiento v2 con la View REAL: encabezado con el único primario, grilla de backups con insignias, detalle y acción
/// por fila, vacío y error de carga excluyentes, las dos cards inferiores lado a lado y el comportamiento con muchas corridas.</summary>
public class MantenimientoViewV2Tests
{
    private sealed class BackupsFake : IBackupsService
    {
        public List<CorridaBackupDto> Corridas { get; set; } = new();
        public Exception? Falla { get; set; }
        public TaskCompletionSource? SenialIniciar { get; set; }

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) =>
            Falla is not null ? Task.FromException<IReadOnlyList<CorridaBackupDto>>(Falla) : Task.FromResult<IReadOnlyList<CorridaBackupDto>>(Corridas.ToList());
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) => Task.FromResult(new BackupDescargaDto("x.dump", new MemoryStream()));
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => Task.FromResult(new SaludBackupDto(null, true, 26));
        public async Task IniciarAsync(CancellationToken ct = default)
        {
            if (SenialIniciar is not null) await SenialIniciar.Task;
        }
    }

    private sealed class GuardadoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default, string? extension = null, string? tipoMime = null) => Task.FromResult(true);
    }

    private static readonly CorridaBackupDto Exitosa = new(3, new DateTime(2026, 10, 6, 11, 0, 12, DateTimeKind.Utc), "Exitosa", "a.dump", 48_234_496, null);
    private static readonly CorridaBackupDto Fallida = new(2, new DateTime(2026, 10, 5, 23, 0, 4, DateTimeKind.Utc), "Fallida", null, null, "pg_dump terminó con código 1: no hay espacio en disco.");
    private static readonly CorridaBackupDto ConNota = new(1, new DateTime(2026, 10, 5, 11, 0, 9, DateTimeKind.Utc), "Exitosa", "b.dump", 47_185_920, "Esta fila no proviene de una corrida real.");

    private sealed record Montaje(Window Ventana, MantenimientoView Vista, MantenimientoViewModel Vm);

    private static Montaje Montar(BackupsFake? backups = null, ConfiguracionAlertasServiceFake? alertas = null, LogsServiceFake? logs = null, double ancho = 1280, double alto = 760)
    {
        var vm = new MantenimientoViewModel(backups ?? new BackupsFake { Corridas = [Exitosa, Fallida, ConNota] }, new GuardadoFake(), new ConfirmacionServiceFake(),
            logs ?? new LogsServiceFake(new ResumenLogsDto(12, new DateTime(2026, 9, 25), new DateTime(2026, 10, 6), 3_355_443)),
            alertas ?? new ConfiguracionAlertasServiceFake(new ConfiguracionAlertasDto("https://hooks.municipio.uy/a", true, null)));
        var vista = new MantenimientoView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static DataGridRow Fila(Montaje m, CorridaBackupDto c) =>
        m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is FilaCorridaBackupVm f && f.Id == c.Id);

    [AvaloniaFact]
    public void Raiz_TieneUi2_HacerBackupEsElUnicoPrimario()
    {
        var m = Montar();

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Mantenimiento", Parte<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Equal("Backups, diagnóstico y alertas del servidor.", Parte<EncabezadoPagina>(m, "Encabezado").Subtitulo);
        Assert.Contains("primario", Parte<Button>(m, "BotonBackup").Classes);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
        Assert.Equal("Hacer backup ahora", Parte<Button>(m, "BotonBackup").Content);
    }

    [AvaloniaFact]
    public void Iniciando_ReemplazaElBotonPorIniciandoDeshabilitado()
    {
        var senal = new TaskCompletionSource();
        var m = Montar(new BackupsFake { Corridas = [Exitosa], SenialIniciar = senal });

        _ = m.Vm.IniciarBackupCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<Button>(m, "BotonBackup")));
        Assert.True(Visible(Parte<Button>(m, "BotonIniciando")));
        Assert.False(Parte<Button>(m, "BotonIniciando").IsEnabled);
        Assert.Equal("Iniciando…", Parte<Button>(m, "BotonIniciando").Content);
        senal.SetResult();
    }

    [AvaloniaFact]
    public void Grilla_ResultadoConInsigniaPorTono_ExitosaExitoFallidaPeligro()
    {
        var m = Montar();
        var grilla = Parte<DataGrid>(m, "GrillaBackups");
        Assert.Equal(new object?[] { "FECHA", "RESULTADO", "DETALLE", "TAMAÑO", null }, grilla.Columns.Select(c => c.Header).ToArray());

        var exito = Fila(m, Exitosa).GetVisualDescendants().OfType<Insignia>().Single(Visible);
        var fallo = Fila(m, Fallida).GetVisualDescendants().OfType<Insignia>().Single(Visible);

        Assert.Equal(("Exitosa", VarianteInsignia.Exito), (exito.Texto, exito.Variante));
        Assert.Equal(("Fallida", VarianteInsignia.Peligro), (fallo.Texto, fallo.Variante));
        Assert.Contains(Fila(m, Exitosa).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "06/10/2026 11:00" || t.Text == "06/10/2026 08:00" || t.Text is { Length: 16 });
        Assert.Contains(Fila(m, Exitosa).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "46 MB");
    }

    [AvaloniaFact]
    public void Grilla_Detalle_FalloEnPeligro_NotaEnSecundario()
    {
        var m = Montar();

        var fallo = Fila(m, Fallida).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Fallida.MotivoFallo && Visible(t));
        var nota = Fila(m, ConNota).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == ConNota.MotivoFallo && Visible(t));

        Assert.Contains("negativo", fallo.Classes);
        Assert.DoesNotContain("negativo", nota.Classes);
        Assert.Empty(Fila(m, Exitosa).GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is { Length: > 30 } && Visible(t)));
    }

    [AvaloniaFact]
    public void Grilla_Descargar_DeshabilitadoSinArchivo_CancelarMientrasDescarga()
    {
        var m = Montar();
        Button Descargar(CorridaBackupDto c) => Fila(m, c).GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");

        Assert.True(Descargar(Exitosa).IsEnabled);
        Assert.False(Descargar(Fallida).IsEnabled);
        Assert.Contains("secundario", Descargar(Exitosa).Classes);

        m.Vm.Corridas.Single(f => f.Id == 3).Descargando = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Descargar(Exitosa)));
        var cancelar = Fila(m, Exitosa).GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancelar");
        Assert.True(Visible(cancelar));
        Assert.True(Visible(Descargar(ConNota)));
    }

    [AvaloniaFact]
    public void ConMuchasCorridas_LaUltimaFilaSeAlcanzaScrolleandoLaGrilla()
    {
        var corridas = Enumerable.Range(1, 40)
            .Select(i => new CorridaBackupDto(i, DateTime.UtcNow.AddHours(-i), "Exitosa", $"backup_{i}.dump", 1024, null)).ToList();
        var m = Montar(new BackupsFake { Corridas = corridas });
        var grilla = Parte<DataGrid>(m, "GrillaBackups");
        var antes = m.Vista.GetVisualDescendants().OfType<DataGridRow>().Count();
        var barra = grilla.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);

        Assert.True(antes < 40 && barra.IsVisible, "Con 40 corridas la grilla virtualiza y tiene barra de scroll vertical propia.");
        grilla.ScrollIntoView(m.Vm.Corridas.Last(), grilla.Columns[0]);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var ultima = Fila(m, corridas.Last());
        var y = ultima.TranslatePoint(new Point(0, 0), m.Ventana)!.Value.Y;
        Assert.InRange(y, 0, m.Ventana.Bounds.Height);
        Assert.True(Visible(Parte<TarjetaContenido>(m, "CardDiagnostico")), "Las cards de abajo no se tapan por scrollear la grilla.");
    }

    [AvaloniaFact]
    public void Vacio_YErrorDeCarga_SonExcluyentes()
    {
        var vacio = Montar(new BackupsFake());
        Assert.True(Visible(Parte<EstadoVacio>(vacio, "VacioBackups")));
        Assert.Equal("Todavía no hay backups registrados.", Parte<EstadoVacio>(vacio, "VacioBackups").Titulo);
        Assert.False(Visible(Parte<Alerta>(vacio, "AlertaErrorBackups")));
        Assert.False(Visible(Parte<DataGrid>(vacio, "GrillaBackups")));

        var error = Montar(new BackupsFake { Falla = new InvalidOperationException("sin servidor") });
        Assert.True(Visible(Parte<Alerta>(error, "AlertaErrorBackups")));
        Assert.Equal("No se pudieron cargar los backups.", Parte<Alerta>(error, "AlertaErrorBackups").Texto);
        Assert.False(Visible(Parte<EstadoVacio>(error, "VacioBackups")));
    }

    [AvaloniaFact]
    public void DiagnosticoYAlertas_LadoALado_DebajoDeBackups()
    {
        var m = Montar();
        Rect En(Control c) => new(c.TranslatePoint(new Point(0, 0), m.Ventana)!.Value, c.Bounds.Size);
        var backups = En(Parte<TarjetaContenido>(m, "CardBackups"));
        var diagnostico = En(Parte<TarjetaContenido>(m, "CardDiagnostico"));
        var alertas = En(Parte<TarjetaContenido>(m, "CardAlertas"));

        Assert.Equal(diagnostico.Y, alertas.Y);
        Assert.Equal(diagnostico.Height, alertas.Height);
        Assert.True(alertas.X >= diagnostico.Right, "Alertas queda a la derecha de Diagnóstico, no debajo.");
        Assert.True(diagnostico.Y >= backups.Bottom, "Las dos cards van debajo de la de Backups.");
        Assert.Equal(16, alertas.X - diagnostico.Right, 1);
        Assert.Equal(backups.Width, alertas.Right - diagnostico.X, 1);
    }

    [AvaloniaFact]
    public void ErrorDeAlertas_AlertaYGuardarProbarDeshabilitados()
    {
        var m = Montar(alertas: new ConfiguracionAlertasServiceFake(excepcionAlObtener: new InvalidOperationException("423 Locked")));

        Assert.True(Visible(Parte<Alerta>(m, "AlertaErrorAlertas")));
        Assert.False(Parte<Button>(m, "BotonGuardarAlertas").IsEnabled);
        Assert.False(Parte<Button>(m, "BotonProbarAlertas").IsEnabled);
        Assert.False(Parte<TextBox>(m, "CajaUrl").IsEnabled);
        Assert.Contains("secundario", Parte<Button>(m, "BotonGuardarAlertas").Classes);
    }
}
