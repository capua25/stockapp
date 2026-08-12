namespace StockApp.Domain.Enums;

/// <summary>
/// Estado de trámite de un documento administrativo (spec 2026-08-11, decisión 3). Cuatro
/// estados, no tres como Tarea: Anulado es la salida honesta para el trámite que muere sin
/// completarse, para no falsear la estadística de trámites Finalizados.
/// </summary>
public enum EstadoDocumento
{
    Pendiente = 0,
    EnProceso = 1,
    Finalizado = 2,
    Anulado = 3,
}
