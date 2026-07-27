using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Backups;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Envoltorio de fila sobre CorridaBackupDto (mismo criterio que los FilaXxxVm de Finanzas,
/// F5d): el DTO es un record inmutable sin estado de UI; esta fila agrega el ÚNICO campo mutable
/// que la vista necesita — Descargando — para que el botón "Cancelar" de ESA fila se muestre/
/// oculte con un binding directo, sin comparar ids entre la fila y el ViewModel padre en XAML.
/// Cts es propiedad de MantenimientoViewModel durante la descarga (la crea, la cancela, la
/// dispone) — la fila solo la retiene para que CancelarCommand la encuentre sin un diccionario
/// aparte en el padre. Cada fila es dueña de SU PROPIO CancellationTokenSource: dos descargas de
/// filas distintas pueden correr en paralelo sin pisarse (no se restringe a "una a la vez",
/// nadie lo pidió).
/// </summary>
public partial class FilaCorridaBackupVm : ObservableObject
{
    public int Id { get; }
    public DateTime FinalizadaEn { get; }
    public string Resultado { get; }
    public string? NombreArchivo { get; }
    public long? TamanioBytes { get; }
    public string? MotivoFallo { get; }

    [ObservableProperty]
    private bool _descargando;

    internal CancellationTokenSource? Cts { get; set; }

    public FilaCorridaBackupVm(CorridaBackupDto dto)
    {
        Id = dto.Id;
        FinalizadaEn = dto.FinalizadaEn;
        Resultado = dto.Resultado;
        NombreArchivo = dto.NombreArchivo;
        TamanioBytes = dto.TamanioBytes;
        MotivoFallo = dto.MotivoFallo;
    }
}
