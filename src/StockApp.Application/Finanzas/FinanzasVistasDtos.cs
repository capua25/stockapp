namespace StockApp.Application.Finanzas;

/// <summary>Fila cronológica del libro caja (spec §7.3): un ingreso o un egreso, con saldo corrido.</summary>
public record MovimientoCajaDto(
    DateOnly Fecha,
    string Tipo,                 // "Ingreso" | "Egreso"
    string Concepto,
    string? ProveedorNombre,
    string? NumeroFactura,
    string? FuenteNombre,
    string? RubroNombre,
    decimal Ingreso,
    decimal Egreso,
    decimal SaldoCorrido);

/// <summary>Total agregado por una clave (rubro o fuente).</summary>
public record TotalPorClaveDto(string Clave, decimal Total);

/// <summary>Libro caja de un mes puntual (spec §7.3).</summary>
public record LibroCajaMesDto(
    int Anio,
    int Mes,
    decimal SaldoInicial,
    decimal SaldoFinal,
    IReadOnlyList<MovimientoCajaDto> Movimientos,
    IReadOnlyList<TotalPorClaveDto> TotalesPorRubro,
    IReadOnlyList<TotalPorClaveDto> TotalesPorFuente);

/// <summary>Fila de un mes en la vista "Año completo" (spec §7.3): totales sin gráficos.</summary>
public record TotalMensualDto(int Mes, decimal Ingresos, decimal Egresos, decimal Neto);

/// <summary>Libro caja anual (spec §7.3, "año completo"): totales por mes y por rubro.</summary>
public record LibroCajaAnualDto(
    int Anio,
    IReadOnlyList<TotalMensualDto> TotalesPorMes,
    IReadOnlyList<TotalPorClaveDto> TotalesPorRubro);

/// <summary>Fila de control POA (spec §7.4): una línea con presupuesto, gastado, saldo y % de ejecución.</summary>
public record ControlPoaLineaDto(
    int LineaPoaId,
    string Nombre,
    string Programa,
    int Ejercicio,
    decimal Presupuesto,
    decimal Gastado,
    decimal Saldo,
    decimal PorcentajeEjecucion,
    bool Sobregirada);

/// <summary>Factura en alguna de las secciones del calendario de pagos (spec §7.5).</summary>
public record FacturaCalendarioDto(
    int GastoId,
    string ProveedorNombre,
    string? NumeroFactura,
    decimal SaldoPendiente,
    DateOnly? FechaVencimiento,
    string Estado);

/// <summary>Pago efectuado recientemente, para la sección "pagos recientes" del calendario.</summary>
public record PagoRecienteDto(
    int GastoId,
    string ProveedorNombre,
    string? NumeroFactura,
    DateOnly FechaPago,
    decimal Monto);

/// <summary>Calendario de pagos completo (spec §7.5).</summary>
public record CalendarioPagosDto(
    IReadOnlyList<FacturaCalendarioDto> Vencidas,
    IReadOnlyList<FacturaCalendarioDto> AVencer7Dias,
    IReadOnlyList<FacturaCalendarioDto> AVencer30Dias,
    IReadOnlyList<PagoRecienteDto> PagosRecientes);
