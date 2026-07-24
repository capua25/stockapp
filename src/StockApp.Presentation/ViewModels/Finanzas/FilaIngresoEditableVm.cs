using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>VM de fila editable para la grilla de Ingresos del Paso 2 (F5d Entrega 2). Mismo
/// patrón que FilaGastoEditableVm, con menos campos (IngresoAnalizadoDto es más chico que
/// GastoAnalizadoDto — sin condición de pago, sin rubro, sin reconciliación POA).</summary>
public partial class FilaIngresoEditableVm : ObservableValidator
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
    [Required(ErrorMessage = "El concepto es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableConcepto))]
    private string? _concepto;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fuente de financiamiento es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    private string? _fuente;

    [ObservableProperty] private bool _fuenteDesconocida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    [NotifyPropertyChangedFor(nameof(EsEditableConcepto))]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    private bool _desbloqueada;

    public bool EsEditableFecha => Fecha is null || Desbloqueada;
    public bool EsEditableMonto => Monto is null || Desbloqueada;
    public bool EsEditableConcepto => Concepto is null || Desbloqueada;
    public bool EsEditableFuente => Fuente is null || Desbloqueada;

    [RelayCommand]
    private void Desbloquear() => Desbloqueada = true;

    public static FilaIngresoEditableVm Desde(IngresoAnalizadoDto dto)
    {
        var fila = new FilaIngresoEditableVm
        {
            HojaOrigen = dto.HojaOrigen,
            NumeroFila = dto.NumeroFila,
            Estado = dto.Estado,
            Motivos = dto.Motivos,
            Fecha = dto.Fecha,
            Monto = dto.Monto,
            Concepto = dto.Concepto,
            Fuente = dto.Fuente,
            FuenteDesconocida = dto.FuenteDesconocida,
        };
        fila.ValidateAllProperties();
        return fila;
    }
}
