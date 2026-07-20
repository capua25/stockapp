namespace StockApp.Application.Finanzas;

/// <summary>Estado de una fila candidata del análisis (spec §8): OK importa directo;
/// Advertencia importa pero necesita atención (literal vacío, rubro/proveedor nuevo);
/// Error NO se puede importar hasta corregir (fecha/monto ilegible).</summary>
public enum EstadoFila { Ok, Advertencia, Error }

/// <summary>Tipo tipado del motivo, para que la UI de F5c resalte la celda correcta.</summary>
public enum TipoMotivo
{
    LiteralVacio,          // advertencia: fuente sin identificar
    FuenteDesconocida,     // advertencia: literal no matchea ninguna FuenteFinanciamiento
    RubroDesconocido,      // advertencia: código no matchea ningún RubroGasto
    ProveedorNuevo,        // advertencia: nombre no existe en Proveedores (se crearía)
    FechaIlegible,         // error: movimiento sin fecha parseable
    MontoIlegible,         // error: movimiento sin monto (ni ingreso ni egreso) parseable
    ReconciliacionDudosa,  // advertencia: match parcial POA↔Gastos, decisión manual
}

public sealed record MotivoEstado(TipoMotivo Tipo, string Mensaje);

/// <summary>Clasificación de un movimiento POA frente al libro caja (planilla de Gastos).</summary>
public enum ClasificacionReconciliacion
{
    Conciliado,        // matchea 1 gasto por factura+orden: NO se duplica, se le asigna la línea POA
    Dudoso,            // match parcial o múltiple: decisión manual en F5c
    CompromisoSoloPoa, // no matchea ningún gasto: compromiso → gasto pendiente a crear
}

/// <summary>Fila candidata de Ingreso de caja (saldo inicial + filas INGRESO de Gastos).</summary>
public sealed record IngresoAnalizadoDto(
    string HojaOrigen, int NumeroFila,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    DateOnly? Fecha, decimal? Monto,
    string? Concepto,
    string? Fuente, bool FuenteDesconocida);

/// <summary>Fila candidata de Gasto (filas EGRESO de la planilla de Gastos).</summary>
public sealed record GastoAnalizadoDto(
    string HojaOrigen, int NumeroFila,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    DateOnly? Fecha, decimal? Monto,
    string? Proveedor, bool ProveedorNuevo,
    string? NumeroFactura, string? NumeroOrden,
    string? Detalle, string? Destino,
    string? Fuente, bool FuenteDesconocida,
    int? CodigoRubro, string? Rubro, bool RubroDesconocido,
    // Reconciliación: si un movimiento POA matcheó este gasto, acá viaja la línea POA
    // que se le asignaría (en vez de duplicar el gasto). Null si no hay match.
    string? LineaPoaAsignada);

/// <summary>Movimiento (factura imputada) de una línea POA, ya clasificado.</summary>
public sealed record MovimientoPoaAnalizadoDto(
    int NumeroFila,
    string? Factura, string? Orden, string? Proveedor, string? Detalle, decimal? Importe,
    ClasificacionReconciliacion Clasificacion,
    // Índice (en la lista Gastos del resultado) del gasto conciliado, o null.
    int? IndiceGastoConciliado,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos);

/// <summary>Línea POA candidata (una hoja de la planilla POA) + su asignación presupuestal.</summary>
public sealed record LineaPoaAnalizadaDto(
    string Hoja, int Ejercicio,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    string? Literal, bool FuenteDesconocida,
    decimal Presupuesto, decimal SaldoPlanilla,
    IReadOnlyList<MovimientoPoaAnalizadoDto> Movimientos);

/// <summary>Conjuntos DISTINTOS de maestros que la importación crearía (spec §8: "se crean
/// los faltantes"). Se materializan una sola vez en el confirm de F5c, no fila por fila.</summary>
public sealed record MaestrosNuevosDto(
    IReadOnlyList<string> Proveedores,
    IReadOnlyList<string> Fuentes,
    IReadOnlyList<CodigoRubroNuevoDto> Rubros);

public sealed record CodigoRubroNuevoDto(int Codigo, string? NombreSugerido);

public sealed record ResumenAnalisisDto(
    int TotalFilas, int Ok, int Advertencias, int Errores,
    int PoaConciliados, int PoaDudosos, int PoaCompromisos);

/// <summary>Resultado completo del análisis (READ-ONLY): nada de esto está en la base todavía.</summary>
public sealed record ResultadoAnalisisDto(
    IReadOnlyList<IngresoAnalizadoDto> Ingresos,
    IReadOnlyList<GastoAnalizadoDto> Gastos,
    IReadOnlyList<LineaPoaAnalizadaDto> LineasPoa,
    MaestrosNuevosDto MaestrosNuevos,
    ResumenAnalisisDto Resumen);
