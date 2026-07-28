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

    /// <summary>
    /// FIX (IMPORTANT, tercer review final E1): true solo cuando <see cref="MotivoFallo"/>
    /// describe un fallo real (Resultado == Fallida). Antes, la vista mostraba CUALQUIER
    /// MotivoFallo no nulo en rojo con DangerBrush -- incluida la marca de una fila
    /// reconciliada (dump huérfano dado de alta tras un restore, ver
    /// ServicioBackup.MarcaFilaReconciliada), que es Exitosa. El admin veía el ícono verde de
    /// éxito y, debajo, un texto rojo diciendo que esa fila "no proviene de una corrida real"
    /// -- justo sobre el backup que necesitaba para restaurar. Esta propiedad separa "es un
    /// fallo" de "es una nota informativa sobre una corrida exitosa" (ver EsNotaInformativa).
    /// </summary>
    public bool EsFallo => Resultado == "Fallida" && MotivoFallo is not null;

    /// <summary>
    /// FIX (IMPORTANT, tercer review final E1): true cuando MotivoFallo trae contenido pero la
    /// corrida es Exitosa (hoy, únicamente la marca de reconciliación). Se renderiza aparte, sin
    /// DangerBrush, con el mismo tratamiento visual que el resto de los textos secundarios de la
    /// vista.
    /// </summary>
    public bool EsNotaInformativa => Resultado == "Exitosa" && MotivoFallo is not null;

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
