using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Finanzas;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Fila del tablero "Maestros nuevos" para un rubro nuevo (F5d Entrega 2 Task 9). El análisis
/// deja NombreSugerido en null (el Código sí lo conoce, viene de la planilla) — Entrega 1 lo
/// mandaba como "" a confirmar, violando RubroNuevoConfirmarDto.Nombre no-vacío (bug documentado
/// en el diseño §5); esta fila exige el nombre acá, antes de llegar a Confirmar.
/// </summary>
public partial class FilaRubroNuevoVm : ObservableValidator
{
    [ObservableProperty] private int _codigo;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El nombre del rubro nuevo es obligatorio.")]
    private string? _nombre;

    public static FilaRubroNuevoVm Desde(CodigoRubroNuevoDto dto)
    {
        var fila = new FilaRubroNuevoVm { Codigo = dto.Codigo, Nombre = dto.NombreSugerido };
        fila.ValidateAllProperties();
        return fila;
    }
}
