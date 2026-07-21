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
/// Una asignación presupuestal dentro de una línea POA: presupuesto asignado, saldo restante y
/// literal de financiamiento (B o C). Modela el financiamiento mixto (F5b, caso real
/// COMPOSTERAS): una hoja de línea puede repartirse en más de una asignación, cada una con su
/// propio literal, presupuesto y saldo — alineado con <c>AsignacionPresupuestal</c> del dominio.
/// </summary>
public sealed record AsignacionPoaOds(string Literal, decimal Presupuesto, decimal Saldo);

/// <summary>
/// Resumen de una línea POA (una hoja de la planilla): sus asignaciones presupuestales (una o
/// más, financiamiento mixto) y los movimientos (facturas imputadas) de la hoja.
/// </summary>
public sealed record LineaPoaResumenOds(
    string Hoja,
    IReadOnlyList<AsignacionPoaOds> Asignaciones,
    IReadOnlyList<FilaPoaOds> Movimientos);

/// <summary>Saldos consolidados de la hoja "SALDO TOTALES" de la planilla POA.</summary>
public sealed record SaldosTotalesPoaOds(decimal SaldoLiteralB, decimal SaldoLiteralC);

/// <summary>Resultado completo de parsear la planilla POA (.ods, F5a).</summary>
public sealed record PlanillaPoaOds(
    IReadOnlyList<LineaPoaResumenOds> Lineas,
    SaldosTotalesPoaOds SaldosTotales);
