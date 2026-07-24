using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Una asignación (Fuente + Presupuesto/Saldo) dentro de una línea POA. Read-only en
/// Entrega 2 — editar asignaciones individuales está fuera de alcance (diseño F5d Entrega 2
/// §11).</summary>
public sealed record AsignacionLineaPoaVm(string? Fuente, bool FuenteDesconocida, decimal Presupuesto, decimal SaldoPlanilla);

/// <summary>
/// VM de fila editable para la grilla de Líneas POA del Paso 2 (F5d Entrega 2). A diferencia de
/// LineaPoaAnalizadaDto (una fila POR ASIGNACIÓN bajo financiamiento mixto), esta fila representa
/// UNA HOJA completa — DesdeGrupo agrupa las N LineaPoaAnalizadaDto que comparten Hoja (mismo
/// criterio de aplanado/agrupado que el diseño §6 pide para el mapeo a confirmación en Task 10).
/// Nombre de la línea = Hoja (siempre read-only, no se edita: viene de la planilla). Programa es
/// el ÚNICO campo editable, y sólo aplica/es obligatorio si EsNueva (si la línea ya existe en la
/// base, Programa ni se manda a confirmar — ver Task 10).
/// </summary>
public partial class FilaLineaPoaEditableVm : ObservableValidator
{
    [ObservableProperty] private string _hoja = string.Empty;
    [ObservableProperty] private int _ejercicio;
    [ObservableProperty] private bool _esNueva;
    [ObservableProperty] private EstadoFila _estado;
    [ObservableProperty] private IReadOnlyList<MotivoEstado> _motivos = new List<MotivoEstado>();
    [ObservableProperty] private IReadOnlyList<AsignacionLineaPoaVm> _asignaciones = new List<AsignacionLineaPoaVm>();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [ProgramaObligatorioSiNueva]
    private string? _programa;

    public static FilaLineaPoaEditableVm DesdeGrupo(IGrouping<string, LineaPoaAnalizadaDto> grupo)
    {
        var lista = grupo.ToList();
        var primera = lista[0];
        var fila = new FilaLineaPoaEditableVm
        {
            Hoja = grupo.Key,
            Ejercicio = primera.Ejercicio,
            EsNueva = primera.EsNueva,
            Estado = lista.Max(l => l.Estado),
            Motivos = lista.SelectMany(l => l.Motivos).ToList(),
            Asignaciones = lista
                .Select(l => new AsignacionLineaPoaVm(l.Literal, l.FuenteDesconocida, l.Presupuesto, l.SaldoPlanilla))
                .ToList(),
        };
        fila.ValidateAllProperties();
        return fila;
    }
}

/// <summary>Programa es obligatorio SÓLO si la línea es nueva (EsNueva) — una línea existente no
/// manda Programa a confirmar (Task 10), así que no tiene sentido exigirlo acá.</summary>
public sealed class ProgramaObligatorioSiNuevaAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var fila = (FilaLineaPoaEditableVm)validationContext.ObjectInstance;
        if (fila.EsNueva && string.IsNullOrWhiteSpace(value as string))
            return new ValidationResult("El programa es obligatorio para una línea POA nueva.");
        return ValidationResult.Success;
    }
}
