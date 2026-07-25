using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// VM de fila editable para la grilla de Gastos del Paso 2 (F5d Entrega 2). Reemplaza el binding
/// directo de GastoAnalizadoDto (record inmutable, Entrega 1) — necesario para two-way binding de
/// celda, validación por campo (ObservableValidator, PRIMER uso en el repo) y CancelEdit del
/// DataGrid. Condicion/FechaVencimiento parten de la heurística de Entrega 1 (compromiso POA =>
/// Crédito con vencimiento = Fecha) como valor SUGERIDO, siempre editable — a diferencia de las
/// demás celdas, que se bloquean si ya tienen valor (ver EsEditable* más abajo).
/// </summary>
public partial class FilaGastoEditableVm : FilaImportacionEditableVmBase
{
    [ObservableProperty] private string _hojaOrigen = string.Empty;
    [ObservableProperty] private int _numeroFila;
    [ObservableProperty] private EstadoFila _estado;
    [ObservableProperty] private IReadOnlyList<MotivoEstado> _motivos = new List<MotivoEstado>();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    private DateOnly? _fecha;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El monto es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    private decimal? _monto;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El proveedor es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableProveedor))]
    private string? _proveedor;

    [ObservableProperty] private bool _proveedorNuevo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroFactura))]
    private string? _numeroFactura;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroOrden))]
    private string? _numeroOrden;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El detalle es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableDetalle))]
    private string? _detalle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableDestino))]
    private string? _destino;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fuente de financiamiento es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    private string? _fuente;

    [ObservableProperty] private bool _fuenteDesconocida;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El rubro es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableRubro))]
    private int? _codigoRubro;

    [ObservableProperty] private string? _rubro;
    [ObservableProperty] private bool _rubroDesconocido;
    [ObservableProperty] private string? _lineaPoaAsignada;

    [ObservableProperty] private CondicionPago _condicion;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [VencimientoCondicional]
    private DateOnly? _fechaVencimiento;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableProveedor))]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    [NotifyPropertyChangedFor(nameof(EsEditableRubro))]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    [NotifyPropertyChangedFor(nameof(EsEditableDetalle))]
    [NotifyPropertyChangedFor(nameof(EsEditableDestino))]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroFactura))]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroOrden))]
    private bool _desbloqueada;

    public bool EsEditableProveedor => Proveedor is null || Desbloqueada;
    public bool EsEditableFuente => Fuente is null || Desbloqueada;
    public bool EsEditableRubro => CodigoRubro is null || Desbloqueada;
    public bool EsEditableFecha => Fecha is null || Desbloqueada;
    public bool EsEditableMonto => Monto is null || Desbloqueada;
    public bool EsEditableDetalle => Detalle is null || Desbloqueada;
    public bool EsEditableDestino => Destino is null || Desbloqueada;
    public bool EsEditableNumeroFactura => NumeroFactura is null || Desbloqueada;
    public bool EsEditableNumeroOrden => NumeroOrden is null || Desbloqueada;

    /// <summary>Re-valida FechaVencimiento cuando cambia Condicion (VencimientoCondicional lee
    /// Condicion desde ValidationContext.ObjectInstance, pero sólo se dispara al setear
    /// FechaVencimiento — este hook cubre el caso de cambiar Condicion primero).</summary>
    partial void OnCondicionChanged(CondicionPago value) => ValidateProperty(FechaVencimiento, nameof(FechaVencimiento));

    public IReadOnlyList<CondicionPago> CondicionesDisponibles { get; } = Enum.GetValues<CondicionPago>();

    [RelayCommand]
    private void Desbloquear() => Desbloqueada = true;

    public static FilaGastoEditableVm Desde(GastoAnalizadoDto dto)
    {
        var esCompromisoPoa = dto.LineaPoaAsignada is not null;
        var fila = new FilaGastoEditableVm
        {
            HojaOrigen = dto.HojaOrigen,
            NumeroFila = dto.NumeroFila,
            Estado = dto.Estado,
            Motivos = dto.Motivos,
            Fecha = dto.Fecha,
            Monto = dto.Monto,
            Proveedor = dto.Proveedor,
            ProveedorNuevo = dto.ProveedorNuevo,
            NumeroFactura = dto.NumeroFactura,
            NumeroOrden = dto.NumeroOrden,
            Detalle = dto.Detalle,
            Destino = dto.Destino,
            Fuente = dto.Fuente,
            FuenteDesconocida = dto.FuenteDesconocida,
            CodigoRubro = dto.CodigoRubro,
            Rubro = dto.Rubro,
            RubroDesconocido = dto.RubroDesconocido,
            LineaPoaAsignada = dto.LineaPoaAsignada,
            Condicion = esCompromisoPoa ? CondicionPago.Credito : CondicionPago.Contado,
            FechaVencimiento = esCompromisoPoa ? dto.Fecha : null,
        };
        fila.ValidateAllProperties();
        return fila;
    }
}

/// <summary>
/// Vencimiento obligatorio si Condicion==Credito, prohibido si Condicion==Contado — espejo
/// exacto de la regla del backend (GastoConfirmarDto.FechaVencimiento condicional, ver
/// ConfirmacionImportacionService). Cruza contra Condicion vía ValidationContext.ObjectInstance
/// porque DataAnnotations no tiene un atributo condicional de dos campos nativo.
/// </summary>
public sealed class VencimientoCondicionalAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var fila = (FilaGastoEditableVm)validationContext.ObjectInstance;
        if (fila.Condicion == CondicionPago.Credito && value is null)
            return new ValidationResult("El vencimiento es obligatorio para un gasto a crédito.");
        if (fila.Condicion == CondicionPago.Contado && value is not null)
            return new ValidationResult("Un gasto contado no debe tener fecha de vencimiento.");
        return ValidationResult.Success;
    }
}
