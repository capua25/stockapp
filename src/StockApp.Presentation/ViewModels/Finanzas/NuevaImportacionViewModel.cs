using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
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
/// Paso 2 (Revisar) es SOLO LECTURA en esta entrega — Entrega 2 agrega la edición de celda.
/// </summary>
public partial class NuevaImportacionViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IConfirmacionService _confirmacion;

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

    // ── Paso 2: Revisar (solo lectura, Entrega 1) ───────────────────────────
    private ResultadoAnalisisDto? _analisis;

    public ObservableCollection<GastoAnalizadoDto> GastosAnalizados { get; } = new();
    public DataGridCollectionView GastosAnalizadosView { get; }

    public ObservableCollection<IngresoAnalizadoDto> IngresosAnalizados { get; } = new();
    public DataGridCollectionView IngresosAnalizadosView { get; }

    public ObservableCollection<LineaPoaAnalizadaDto> LineasPoaAnalizadas { get; } = new();
    public DataGridCollectionView LineasPoaAnalizadasView { get; }

    public ObservableCollection<string> ProveedoresNuevos { get; } = new();
    public ObservableCollection<string> FuentesNuevas { get; } = new();
    public ObservableCollection<CodigoRubroNuevoDto> RubrosNuevos { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmar))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private ResumenAnalisisDto? _resumen;

    public bool PuedeConfirmar => Resumen is { Errores: 0 };

    // ── Paso 3: Resultado ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertirCommand))]
    private ResultadoConfirmacionDto? _resultadoConfirmacion;

    public ObservableCollection<ConflictoGastoFila> Conflictos { get; } = new();

    public NuevaImportacionViewModel(
        IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion)
    {
        _service = service;
        _seleccion = seleccion;
        _confirmacion = confirmacion;

        GastosAnalizadosView = new DataGridCollectionView(GastosAnalizados);
        IngresosAnalizadosView = new DataGridCollectionView(IngresosAnalizados);
        LineasPoaAnalizadasView = new DataGridCollectionView(LineasPoaAnalizadas);
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

            GastosAnalizados.Clear();
            foreach (var g in _analisis.Gastos) GastosAnalizados.Add(g);
            IngresosAnalizados.Clear();
            foreach (var i in _analisis.Ingresos) IngresosAnalizados.Add(i);
            LineasPoaAnalizadas.Clear();
            foreach (var l in _analisis.LineasPoa) LineasPoaAnalizadas.Add(l);
            ProveedoresNuevos.Clear();
            foreach (var p in _analisis.MaestrosNuevos.Proveedores) ProveedoresNuevos.Add(p);
            FuentesNuevas.Clear();
            foreach (var f in _analisis.MaestrosNuevos.Fuentes) FuentesNuevas.Add(f);
            RubrosNuevos.Clear();
            foreach (var r in _analisis.MaestrosNuevos.Rubros) RubrosNuevos.Add(r);

            Resumen = _analisis.Resumen;
            PasoActual = PasoWizardImportacion.Revisar;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
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
    /// Mapeo directo (sin edición) análisis→confirmación, válido SOLO cuando Resumen.Errores == 0
    /// (único caso en que ConfirmarCommand se puede ejecutar) — todo campo requerido de las filas
    /// mapeadas ya viene no-nulo del análisis en ese caso.
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
            .Select(i => new IngresoConfirmarDto(i.Fecha!.Value, i.Concepto ?? string.Empty, i.Monto!.Value, i.Fuente!))
            .ToList();

        var gastos = analisis.Gastos.Select(g =>
        {
            var esCompromisoPoa = g.LineaPoaAsignada is not null;
            return new GastoConfirmarDto(
                Proveedor: g.Proveedor!,
                NumeroFactura: g.NumeroFactura,
                NumeroOrden: g.NumeroOrden,
                Detalle: g.Detalle ?? string.Empty,
                Destino: g.Destino,
                Fecha: g.Fecha!.Value,
                MontoTotal: g.Monto!.Value,
                Fuente: g.Fuente!,
                CodigoRubro: g.CodigoRubro!.Value,
                LineaPoa: g.LineaPoaAsignada,
                Condicion: esCompromisoPoa ? CondicionPago.Credito : CondicionPago.Contado,
                FechaVencimiento: esCompromisoPoa ? g.Fecha!.Value : null);
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

    private void ReiniciarWizard()
    {
        PasoActual = PasoWizardImportacion.Cargar;
        GastosNombreArchivo = null;
        _gastosContenido = null;
        PoaNombreArchivo = null;
        _poaContenido = null;
        Forzar = false;
        GastosAnalizados.Clear();
        IngresosAnalizados.Clear();
        LineasPoaAnalizadas.Clear();
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
