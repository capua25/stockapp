namespace StockApp.Domain.Enums;

/// <summary>
/// Tipo de documento administrativo (spec 2026-08-11, decisión 6). Enum fijo, no tabla
/// maestra configurable: append-only, se persiste como int. Agregar un tipo nuevo el día
/// de mañana es una línea de código, no una migración de datos.
/// </summary>
public enum TipoDocumento
{
    Expediente = 0,
    Oficio = 1,
    Suministro = 2,
}
