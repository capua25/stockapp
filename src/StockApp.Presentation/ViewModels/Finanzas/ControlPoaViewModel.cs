using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Control POA" (spec §7.4): una fila por línea con presupuesto, gastado, saldo
/// y % de ejecución. Doble click abre las facturas de esa línea en GastosViewModel filtrado.
/// </summary>
public partial class ControlPoaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly INavigationService     _navigation;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService   _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty] private int _ejercicio;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirGastosDeLaLineaCommand))]
    private ControlPoaLineaDto? _filaSeleccionada;

    /// <summary>Ejercicio de la última carga que llegó a <see cref="Filas"/>: es el que describe lo que hay en pantalla y
    /// lo que sale en los exportes (el campo <see cref="Ejercicio"/> puede ir por delante mientras espera el debounce).</summary>
    private int _ejercicioCargado;

    public ObservableCollection<ControlPoaLineaDto> Filas { get; } = new();
    public DataGridCollectionView FilasView { get; }

    public ControlPoaViewModel(
        IFinanzasVistasService service, INavigationService navigation,
        IServicioGuardadoArchivo guardado, IConfirmacionService confirmacion,
        IPdfExporter pdfExporter, IServicioAperturaArchivo apertura, ICurrentSession session,
        TimeProvider? reloj = null)
    {
        // El año arranca del reloj (inyectable para los datos fijos de los mockups). Se escribe el campo: no dispara la
        // recarga de OnEjercicioChanged, la primera carga la pide la vista.
        _ejercicio = (reloj ?? TimeProvider.System).GetUtcNow().Year;
        _ejercicioCargado = _ejercicio;

        _service     = service;
        _navigation  = navigation;
        _guardado    = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        FilasView = new DataGridCollectionView(Filas);
    }

    /// <summary>Espera antes de recargar al cambiar el Ejercicio con las flechas o tipeando: un año de 4 cifras son
    /// 4 cambios y solo el último tiene que ir al servidor.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista (patrón de Productos).</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private CancellationTokenSource? _debounceCts;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoControlPoa = "No tenés permiso para ver el control POA.";

    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las líneas de otra consulta.</summary>
    protected override void AlPerderConexion() => Filas.Clear();

    /// <summary>Reintentar consulta el Ejercicio que está en pantalla, no el de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    /// <summary>
    /// La dispara la View (DataContextChanged) y el cambio de Ejercicio. Protegida (403 → SinPermiso, servidor caído →
    /// SinConexion, Cargando mientras espera) y con turno: si mientras espera empezó otra carga, su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        var ejercicio = Ejercicio;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var lineas = await _service.ObtenerControlPoaAsync(ejercicio);
            if (!turno.EsLaUltima)
                return;
            Filas.Clear();
            foreach (var l in lineas)
                Filas.Add(l);
            _ejercicioCargado = ejercicio;
        }, MensajeSinPermisoControlPoa, turno);
    }

    /// <summary>Cambiar el Ejercicio recarga sola tras la espera; un cambio nuevo cancela la espera anterior. Un texto
    /// inválido en el NumericUpDown no llega acá (Ejercicio conserva el valor), así que no recarga con el año viejo.</summary>
    partial void OnEjercicioChanged(int value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _tareaDebounce = DebounceCargarAsync(_debounceCts.Token);
    }

    private async Task DebounceCargarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(EsperaDebounce, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await CargarAsync();
    }

    /// <summary>
    /// El export serializa en el cliente las <see cref="Filas"/> ya cargadas, sin llamar al servidor: tras un 403 esas
    /// filas siguen en memoria y mientras carga pueden ser de otra consulta. Sin permiso o cargando no se puede exportar
    /// (mismo criterio que Gastos).
    /// </summary>
    public bool PuedeExportar => !SinPermiso && !Cargando;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SinPermiso) or nameof(Cargando))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private void AbrirGastosDeLaLinea()
    {
        if (FilaSeleccionada is null) return;
        var linea = new LineaPoa
        {
            Id = FilaSeleccionada.LineaPoaId, Nombre = FilaSeleccionada.Nombre,
            Programa = FilaSeleccionada.Programa, Ejercicio = FilaSeleccionada.Ejercicio,
        };
        _navigation.Navegar<GastosViewModel>(vm => vm.FiltrarPorLineaPoa(linea));
    }

    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA --
    /// excluye Ejercicio (ya está en la descripción de filtros del membrete) y
    /// Sobregirada (se ve por color/badge en la grilla, sobra como columna en papel).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(ControlPoaLineaDto.Nombre), "Línea"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Programa), "Programa"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Presupuesto), "Presupuesto"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Gastado), "Gastado"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Saldo), "Saldo"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.PorcentajeEjecucion), "% Ejecución"),
    };

    /// <summary>
    /// Exporta <see cref="Filas"/> a PDF con las columnas de la grilla, membrete institucional
    /// y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace nada si no hay datos
    /// cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private async Task ExportarPdfAsync()
    {
        if (Filas.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Filas.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Control POA",
                DescripcionFiltros: $"Ejercicio: {_ejercicioCargado}.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Filas, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, $"control-poa-{_ejercicioCargado}.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, $"control-poa-{_ejercicioCargado}.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
