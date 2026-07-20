namespace StockApp.Application.Finanzas;

/// <summary>
/// Parser de las planillas .ods de migración (F5, one-shot): NO evalúa fórmulas, siempre lee
/// el valor cacheado que LibreOffice/Excel dejó guardado en el archivo. Puro: no toca la base
/// de datos ni el estado de la app, solo interpreta bytes de un Stream ya abierto.
/// </summary>
public interface IPlanillaParser
{
    /// <summary>Parsea la planilla de Gastos: 12 hojas mensuales + Variables.</summary>
    PlanillaGastosOds ParsearGastos(Stream odsStream);

    /// <summary>Parsea la planilla POA: una hoja por línea presupuestal + SALDO TOTALES.</summary>
    PlanillaPoaOds ParsearPoa(Stream odsStream);
}
