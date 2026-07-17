namespace StockApp.Application.Finanzas;

/// <summary>
/// Vistas calculadas del módulo Finanzas (spec §7.3-7.5): libro caja, control POA y
/// calendario de pagos. Ningún saldo se persiste — todo se calcula en memoria sobre lo
/// que traen los repositorios. Fail-closed: cada método exige Permisos.VerFinanzas.
/// </summary>
public interface IFinanzasVistasService
{
    /// <summary>Libro caja de un mes puntual (1-12). Lanza ArgumentException si mes está fuera de rango.</summary>
    Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes);

    /// <summary>Libro caja anual: totales por mes y por rubro, sin gráficos.</summary>
    Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio);

    /// <summary>Control presupuestal POA de un ejercicio: una fila por línea.</summary>
    Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio);

    /// <summary>
    /// Calendario de pagos (vencidas, a vencer en 7/30 días, pagos recientes de los últimos
    /// 7 días). <paramref name="fechaReferencia"/> es SOLO para tests determinísticos (no hay
    /// IClock en el proyecto); si es null usa DateTime.UtcNow. El servidor HTTP y
    /// FinanzasVistasApiClient nunca lo envían: el servidor es la única autoridad de "hoy".
    /// </summary>
    Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null);
}
