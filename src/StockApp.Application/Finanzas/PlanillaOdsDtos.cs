namespace StockApp.Application.Finanzas;

/// <summary>
/// Fila de una hoja mensual de la planilla de Gastos (.ods, F5a). Representa exactamente lo
/// que hay en la fila de la planilla, SIN interpretar contra maestros de la base — Factura/
/// Orden se guardan como texto libre porque la planilla mezcla números y strings en esas
/// columnas. Filas que solo arrastran el SALDO hacia abajo (sin ningún otro dato) no generan
/// FilaGastoOds — no son movimientos.
/// </summary>
public sealed record FilaGastoOds(
    string Hoja,
    int NumeroFila,
    DateOnly? Fecha,
    string? Factura,
    string? Orden,
    string? Proveedor,
    string? Destino,
    string? Gasto,
    decimal? Ingreso,
    decimal? Egreso,
    decimal? Saldo,
    string? Literal,
    int? Codigo,
    string? Rubro);

/// <summary>Fila de la hoja "Variables" de la planilla de Gastos (lookup literal→código→rubro).</summary>
public sealed record LineaVariableOds(string Literal, int Codigo, string Rubro);

/// <summary>
/// Resultado completo de parsear la planilla de Gastos (.ods, F5a): filas por cada hoja
/// mensual (ENERO..DICIEMBRE) más la hoja Variables. NO incluye ANUAL/GRAFICAS: son vistas
/// derivadas dentro de la propia planilla, no datos fuente.
/// </summary>
public sealed record PlanillaGastosOds(
    IReadOnlyDictionary<string, IReadOnlyList<FilaGastoOds>> FilasPorMes,
    IReadOnlyList<LineaVariableOds> Variables);

/// <summary>Fila de movimiento (factura imputada) dentro de una hoja de línea POA.</summary>
public sealed record FilaPoaOds(
    string Hoja,
    int NumeroFila,
    string? Factura,
    string? Orden,
    string? Proveedor,
    string? Gasto,
    decimal? Importe);

/// <summary>
/// Resumen de una línea POA (una hoja de la planilla): presupuesto asignado, saldo restante,
/// literal de financiamiento (B o C) y sus movimientos.
/// </summary>
public sealed record LineaPoaResumenOds(
    string Hoja,
    decimal Presupuesto,
    decimal Saldo,
    string Literal,
    IReadOnlyList<FilaPoaOds> Movimientos);

/// <summary>Saldos consolidados de la hoja "SALDO TOTALES" de la planilla POA.</summary>
public sealed record SaldosTotalesPoaOds(decimal SaldoLiteralB, decimal SaldoLiteralC);

/// <summary>Resultado completo de parsear la planilla POA (.ods, F5a).</summary>
public sealed record PlanillaPoaOds(
    IReadOnlyList<LineaPoaResumenOds> Lineas,
    SaldosTotalesPoaOds SaldosTotales);
