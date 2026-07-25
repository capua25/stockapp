using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Paso actual del wizard de importación (F5d §5).</summary>
public enum PasoWizardImportacion { Cargar, Revisar, Resultado }

/// <summary>Fila de solo lectura de la grilla de conflictos del Paso 3 (F5d §5): aplana
/// ConflictoGastoDto.CamposDivergentes a una sola línea legible.</summary>
public sealed record ConflictoGastoFila(string Proveedor, string NumeroFactura, string CamposTexto)
{
    public static ConflictoGastoFila Desde(ConflictoGastoDto dto) => new(
        dto.Proveedor, dto.NumeroFactura,
        string.Join("; ", dto.CamposDivergentes.Select(c => $"{c.Campo}: {c.ValorAnterior} → {c.ValorNuevo}")));
}

/// <summary>
/// Tab "Nueva importación" (F5d §5): wizard de 3 pasos como UNA sola VM con estado PasoActual.
/// Paso 2 (Revisar) es editable desde Entrega 2: cada DTO de análisis se proyecta a una fila VM
/// (FilaGastoEditableVm/FilaIngresoEditableVm/FilaLineaPoaEditableVm) que valida por campo.
/// </summary>
public partial class NuevaImportacionViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IConfirmacionService _confirmacion;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService _rubrosService;
    private readonly IProveedorService _proveedoresService;
    private readonly ILineaPoaService _lineasPoaService;

    [ObservableProperty]
    private PasoWizardImportacion _pasoActual = PasoWizardImportacion.Cargar;

    // ── Paso 1: Cargar ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _gastosNombreArchivo;
    private byte[]? _gastosContenido;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _poaNombreArchivo;
    private byte[]? _poaContenido;

    [ObservableProperty]
    private int _ejercicio = DateTime.UtcNow.Year;

    [ObservableProperty]
    private bool _forzar;

    // ── Paso 2: Revisar (editable, Entrega 2) ───────────────────────────────
    private ResultadoAnalisisDto? _analisis;

    public ObservableCollection<FilaGastoEditableVm> FilasGasto { get; } = new();
    public DataGridCollectionView FilasGastoView { get; }

    public ObservableCollection<FilaIngresoEditableVm> FilasIngreso { get; } = new();
    public DataGridCollectionView FilasIngresoView { get; }

    public ObservableCollection<FilaLineaPoaEditableVm> FilasLineaPoa { get; } = new();
    public DataGridCollectionView FilasLineaPoaView { get; }

    public ObservableCollection<string> ProveedoresNuevos { get; } = new();
    public ObservableCollection<string> FuentesNuevas { get; } = new();
    public ObservableCollection<FilaRubroNuevoVm> RubrosNuevos { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmar))]
    [NotifyPropertyChangedFor(nameof(MensajeConfirmarBloqueado))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private ResumenAnalisisDto? _resumen;

    /// <summary>Confirmar sólo puede ejecutarse si NINGUNA fila (Gasto/Ingreso/LineaPoa) tiene
    /// errores de validación pendientes (F5d Entrega 2 §7) — reemplaza el gate de Entrega 1
    /// (Resumen.Errores==0 && ContarFilasIncompletas()==0), ahora redundante: los campos que antes
    /// dejaban a una fila "incompleta" o en EstadoFila.Error son exactamente los que [Required]
    /// valida en las filas VM.</summary>
    public bool PuedeConfirmar => !HayFilasConErrores();

    private bool HayFilasConErrores() =>
        FilasGasto.Any(f => f.HasErrors) || FilasIngreso.Any(f => f.HasErrors) || FilasLineaPoa.Any(f => f.HasErrors)
        || RubrosNuevos.Any(r => r.HasErrors);

    /// <summary>Cuenta de filas con errores de validación pendientes — null/vacío si Confirmar está
    /// habilitado.</summary>
    public string? MensajeConfirmarBloqueado
    {
        get
        {
            var conErrores = FilasGasto.Count(f => f.HasErrors)
                + FilasIngreso.Count(f => f.HasErrors)
                + FilasLineaPoa.Count(f => f.HasErrors)
                + RubrosNuevos.Count(r => r.HasErrors);
            return conErrores == 0
                ? null
                : $"Hay {conErrores} fila(s) con errores de validación pendientes.";
        }
    }

    // ── Paso 3: Resultado ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertirCommand))]
    private ResultadoConfirmacionDto? _resultadoConfirmacion;

    public ObservableCollection<ConflictoGastoFila> Conflictos { get; } = new();

    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<string> ProgramasExistentes { get; } = new();

    public NuevaImportacionViewModel(
        IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion,
        IFuenteFinanciamientoService fuentesService, IRubroGastoService rubrosService, IProveedorService proveedoresService,
        ILineaPoaService lineasPoaService)
    {
        _service = service;
        _seleccion = seleccion;
        _confirmacion = confirmacion;
        _fuentesService = fuentesService;
        _rubrosService = rubrosService;
        _proveedoresService = proveedoresService;
        _lineasPoaService = lineasPoaService;

        FilasGastoView = new DataGridCollectionView(FilasGasto);
        FilasIngresoView = new DataGridCollectionView(FilasIngreso);
        FilasLineaPoaView = new DataGridCollectionView(FilasLineaPoa);
    }

    /// <summary>Carga los combos de maestros existentes. La dispara la View (DataContextChanged),
    /// mismo contrato que GastoFormViewModel.InicializarAsync.</summary>
    public async Task InicializarMaestrosAsync()
    {
        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes) FuentesDisponibles.Add(f);

        var rubros = await _rubrosService.ListarActivosAsync();
        RubrosDisponibles.Clear();
        foreach (var r in rubros) RubrosDisponibles.Add(r);

        var proveedores = await _proveedoresService.ListarTodosAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores.Where(p => p.Activo)) ProveedoresDisponibles.Add(p);

        var lineas = await _lineasPoaService.ListarTodasAsync();
        ProgramasExistentes.Clear();
        foreach (var programa in lineas.Select(l => l.Programa).Distinct().OrderBy(p => p))
            ProgramasExistentes.Add(programa);
    }

    [RelayCommand]
    private async Task SeleccionarGastosAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (GastosNombreArchivo, _gastosContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SeleccionarPoaAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (PoaNombreArchivo, _poaContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeAnalizar() => _gastosContenido is not null && _poaContenido is not null;

    [RelayCommand(CanExecute = nameof(PuedeAnalizar))]
    private async Task AnalizarAsync()
    {
        try
        {
            _analisis = await _service.AnalizarAsync(
                GastosNombreArchivo!, _gastosContenido!, PoaNombreArchivo!, _poaContenido!, Ejercicio);

            FilasGasto.Clear();
            foreach (var g in _analisis.Gastos)
            {
                var fila = FilaGastoEditableVm.Desde(g);
                fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
                fila.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(fila.Proveedor)) RegistrarSiEsNuevo(ProveedoresDisponibles.Select(p => p.Nombre), ProveedoresNuevos, fila.Proveedor);
                    if (e.PropertyName == nameof(fila.Fuente)) RegistrarSiEsNuevo(FuentesDisponibles.Select(f => f.Nombre), FuentesNuevas, fila.Fuente);
                };
                FilasGasto.Add(fila);
            }

            FilasIngreso.Clear();
            foreach (var i in _analisis.Ingresos)
            {
                var fila = FilaIngresoEditableVm.Desde(i);
                fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
                fila.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(fila.Fuente)) RegistrarSiEsNuevo(FuentesDisponibles.Select(f => f.Nombre), FuentesNuevas, fila.Fuente);
                };
                FilasIngreso.Add(fila);
            }

            FilasLineaPoa.Clear();
            foreach (var grupo in _analisis.LineasPoa.GroupBy(l => l.Hoja))
            {
                var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);
                fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
                FilasLineaPoa.Add(fila);
            }

            ProveedoresNuevos.Clear();
            foreach (var p in _analisis.MaestrosNuevos.Proveedores) ProveedoresNuevos.Add(p);
            FuentesNuevas.Clear();
            foreach (var f in _analisis.MaestrosNuevos.Fuentes) FuentesNuevas.Add(f);
            RubrosNuevos.Clear();
            foreach (var r in _analisis.MaestrosNuevos.Rubros)
            {
                var fila = FilaRubroNuevoVm.Desde(r);
                fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
                RubrosNuevos.Add(fila);
            }

            Resumen = _analisis.Resumen;
            PasoActual = PasoWizardImportacion.Revisar;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>Se dispara cuando cualquier fila (Gasto/Ingreso/LineaPoa) cambia su estado de
    /// validación — el gating de Confirmar depende de HasErrors de TODAS las filas, no sólo de la
    /// que cambió, así que se recalculan las dos propiedades computadas completas.</summary>
    private void NotificarGatingCambio()
    {
        OnPropertyChanged(nameof(PuedeConfirmar));
        OnPropertyChanged(nameof(MensajeConfirmarBloqueado));
        ConfirmarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Auto-declaración de maestro nuevo (F5d Entrega 2 Task 9): si el texto no matchea
    /// (case-insensitive) ningún nombre existente Y todavía no está declarado, se agrega a la lista
    /// de nuevos. Sin remoción: si el usuario corrige el typo después, el nombre viejo queda
    /// declarado (aceptable para Entrega 2 — el usuario revisa la pestaña Maestros nuevos antes de
    /// Confirmar).</summary>
    private static void RegistrarSiEsNuevo(IEnumerable<string> existentes, ObservableCollection<string> nuevos, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;
        var normalizado = texto.Trim();
        if (existentes.Any(e => string.Equals(e, normalizado, StringComparison.OrdinalIgnoreCase))) return;
        if (!nuevos.Any(n => string.Equals(n, normalizado, StringComparison.OrdinalIgnoreCase)))
            nuevos.Add(normalizado);
    }

    [RelayCommand(CanExecute = nameof(PuedeConfirmar))]
    private async Task ConfirmarAsync()
    {
        if (_analisis is null) return;

        var dto = MapearAConfirmacion(_analisis, Ejercicio, Forzar);

        try
        {
            ResultadoConfirmacion = await _service.ConfirmarAsync(dto);
            Conflictos.Clear();
            foreach (var c in ResultadoConfirmacion.Conflictos)
                Conflictos.Add(ConflictoGastoFila.Desde(c));
            PasoActual = PasoWizardImportacion.Resultado;
        }
        catch (ValidacionImportacionException vex)
        {
            await _confirmacion.InformarAsync(FormatearErroresValidacion(vex));
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>
    /// Formatea ValidacionImportacionException.Errores (F5c/F5d Task 4: diccionario "Tipo[i].Campo"
    /// → mensajes, reconstruido por el ApiClient desde el 400 estructurado) como texto legible —
    /// una línea por campo. Entrega 1 es SOLO texto: resaltar la celda/pestaña exacta es Entrega 2.
    /// </summary>
    private static string FormatearErroresValidacion(ValidacionImportacionException vex)
        => string.Join(
            Environment.NewLine,
            vex.Errores.Select(par => $"{par.Key}: {string.Join("; ", par.Value)}"));

    /// <summary>
    /// Mapeo directo (sin edición) análisis→confirmación. Precondición real (garantizada por
    /// PuedeConfirmar, no por Resumen.Errores == 0 solo): ninguna fila a importar tiene Fuente,
    /// CodigoRubro, Proveedor (Gastos) ni Fuente, Concepto (Ingresos) en null — PuedeConfirmar
    /// verifica esto explícitamente porque Errores == 0 NO lo garantiza (una fuente vacía es
    /// Advertencia, no Error). RequeridoNoNulo debajo es un segundo cinturón de seguridad: si esta
    /// precondición se violara igual (p.ej. un futuro cambio que llame a este método sin pasar por
    /// el gating), falla con un mensaje explícito en vez de una NullReferenceException muda.
    ///
    /// Gap documentado (Entrega 1, contradice el diseño F5d §8 en la letra chica): GastoConfirmarDto
    /// exige CondicionPago no-nullable, pero GastoAnalizadoDto NO expone ese campo — el análisis
    /// (F5b/F5c) nunca lo calculó. Se infiere con el mismo criterio que ya usa el backend para los
    /// compromisos POA (ImportacionRepository, "Los compromisos POA importados van Credito SIN
    /// pago"): LineaPoaAsignada != null ⇒ Credito con FechaVencimiento = la misma Fecha del gasto
    /// (no hay otra fecha disponible sin editar); si no, Contado sin vencimiento. Es una heurística,
    /// no una elección del usuario — Entrega 2 debería exponer Condicion/FechaVencimiento como
    /// celda editable si este supuesto no alcanza en la práctica.
    ///
    /// Segundo gap documentado: LineaPoaConfirmarDto exige Nombre+Programa, que
    /// LineaPoaAnalizadaDto NO expone (solo Hoja/Literal/Presupuesto/SaldoPlanilla — Literal es
    /// el nombre de la FUENTE de esa línea, no el nombre de la línea). Declarar una LineaPoa
    /// NUEVA es, en los hechos, una operación de edición — se difiere a Entrega 2. Acá SIEMPRE se
    /// manda vacía: si algún Gasto referencia una LineaPoa que todavía no existe en la base para
    /// este Ejercicio, el 400 estructurado del servidor se muestra tal cual (catch de
    /// ConfirmarAsync), nunca se inventa un Nombre/Programa.
    /// </summary>
    private static ConfirmarImportacionDto MapearAConfirmacion(
        ResultadoAnalisisDto analisis, int ejercicio, bool forzar)
    {
        var ingresos = analisis.Ingresos
            .Select(i => new IngresoConfirmarDto(
                RequeridoNoNulo(i.Fecha, "Ingreso.Fecha"),
                i.Concepto ?? string.Empty,
                RequeridoNoNulo(i.Monto, "Ingreso.Monto"),
                RequeridoNoNulo(i.Fuente, "Ingreso.Fuente")))
            .ToList();

        var gastos = analisis.Gastos.Select(g =>
        {
            var esCompromisoPoa = g.LineaPoaAsignada is not null;
            var fecha = RequeridoNoNulo(g.Fecha, "Gasto.Fecha");
            return new GastoConfirmarDto(
                Proveedor: RequeridoNoNulo(g.Proveedor, "Gasto.Proveedor"),
                NumeroFactura: g.NumeroFactura,
                NumeroOrden: g.NumeroOrden,
                Detalle: g.Detalle ?? string.Empty,
                Destino: g.Destino,
                Fecha: fecha,
                MontoTotal: RequeridoNoNulo(g.Monto, "Gasto.MontoTotal"),
                Fuente: RequeridoNoNulo(g.Fuente, "Gasto.Fuente"),
                CodigoRubro: RequeridoNoNulo(g.CodigoRubro, "Gasto.CodigoRubro"),
                LineaPoa: g.LineaPoaAsignada,
                Condicion: esCompromisoPoa ? CondicionPago.Credito : CondicionPago.Contado,
                FechaVencimiento: esCompromisoPoa ? fecha : null);
        }).ToList();

        var maestrosNuevos = new MaestrosNuevosConfirmarDto(
            analisis.MaestrosNuevos.Proveedores,
            analisis.MaestrosNuevos.Fuentes,
            analisis.MaestrosNuevos.Rubros
                .Select(r => new RubroNuevoConfirmarDto(r.Codigo, r.NombreSugerido ?? string.Empty))
                .ToList());

        return new ConfirmarImportacionDto(
            ejercicio, forzar, maestrosNuevos, ingresos, gastos, new List<LineaPoaConfirmarDto>());
    }

    /// <summary>Cinturón de seguridad de MapearAConfirmacion: falla con mensaje explícito (en vez de
    /// NRE muda) si la precondición garantizada por PuedeConfirmar se violara igual.</summary>
    private static T RequeridoNoNulo<T>(T? valor, string campo) where T : struct
        => valor ?? throw new InvalidOperationException(
            $"{campo} nulo al mapear a confirmación — violación de la precondición de PuedeConfirmar.");

    private static string RequeridoNoNulo(string? valor, string campo)
        => valor ?? throw new InvalidOperationException(
            $"{campo} nulo al mapear a confirmación — violación de la precondición de PuedeConfirmar.");

    private bool PuedeRevertir() => ResultadoConfirmacion is not null;

    [RelayCommand(CanExecute = nameof(PuedeRevertir))]
    private async Task RevertirAsync()
    {
        if (ResultadoConfirmacion is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma revertir la importación {ResultadoConfirmacion.IdImportacion}? " +
            "Se darán de baja todos los gastos, ingresos y líneas POA que creó.");
        if (!confirmar) return;

        try
        {
            await _service.RevertirAsync(ResultadoConfirmacion.IdImportacion);
            await _confirmacion.InformarAsync("Importación revertida correctamente.");
            ReiniciarWizard();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>Salida limpia del Paso 3 (F5d, hallazgo de review Entrega 1): reinicia el wizard al
    /// Paso 1 SIN revertir la importación — a diferencia de RevertirCommand, no pregunta confirmación
    /// ni llama al servicio.</summary>
    [RelayCommand]
    private void NuevaImportacion() => ReiniciarWizard();

    private void ReiniciarWizard()
    {
        PasoActual = PasoWizardImportacion.Cargar;
        GastosNombreArchivo = null;
        _gastosContenido = null;
        PoaNombreArchivo = null;
        _poaContenido = null;
        Forzar = false;
        FilasGasto.Clear();
        FilasIngreso.Clear();
        FilasLineaPoa.Clear();
        ProveedoresNuevos.Clear();
        FuentesNuevas.Clear();
        RubrosNuevos.Clear();
        Conflictos.Clear();
        Resumen = null;
        ResultadoConfirmacion = null;
        _analisis = null;
        AnalizarCommand.NotifyCanExecuteChanged();
    }
}
