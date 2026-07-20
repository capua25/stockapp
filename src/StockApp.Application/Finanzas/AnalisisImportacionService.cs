using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

public class AnalisisImportacionService : IAnalisisImportacionService
{
    private static readonly string[] MesesGastos =
    {
        "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE",
    };

    private readonly IPlanillaParser _parser;
    private readonly IProveedorRepository _proveedores;
    private readonly IRubroGastoRepository _rubros;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public AnalisisImportacionService(
        IPlanillaParser parser,
        IProveedorRepository proveedores,
        IRubroGastoRepository rubros,
        IFuenteFinanciamientoRepository fuentes,
        ICurrentSession session,
        IAuthorizationService auth)
    {
        _parser = parser;
        _proveedores = proveedores;
        _rubros = rubros;
        _fuentes = fuentes;
        _session = session;
        _auth = auth;
    }

    public async Task<ResultadoAnalisisDto> AnalizarAsync(Stream planillaGastos, Stream planillaPoa, int ejercicio)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        // Maestros normalizados para clasificar OK vs nuevo. Proveedores/Rubros se comparan
        // contra TODOS los existentes (spec: sin calificador de "activo"); Fuentes SOLO contra
        // las activas (spec explícito: "ninguna FuenteFinanciamiento activa de la base").
        var proveedoresExistentes = (await _proveedores.ListarTodosAsync())
            .Select(p => Normalizar(p.Nombre))
            .ToHashSet();
        var rubrosExistentes = (await _rubros.ListarTodosAsync())
            .ToDictionary(r => r.Codigo);
        var fuentesActivas = (await _fuentes.ListarTodasAsync())
            .Where(f => f.Activo)
            .Select(f => Normalizar(f.Nombre))
            .ToHashSet();

        var gastosOds = ParsearGastosSeguro(planillaGastos);

        var ingresos = new List<IngresoAnalizadoDto>();
        var gastos = new List<GastoAnalizadoDto>();

        var proveedoresNuevosVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proveedoresNuevos = new List<string>();
        var fuentesNuevasVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fuentesNuevas = new List<string>();
        var rubrosNuevosVistos = new HashSet<int>();
        var rubrosNuevos = new List<CodigoRubroNuevoDto>();

        AgregarSaldoInicialEnero(gastosOds, ejercicio, ingresos);

        foreach (var mes in MesesGastos)
        {
            foreach (var fila in gastosOds.FilasPorMes[mes])
            {
                var motivos = new List<MotivoEstado>();

                if (fila.Fecha is null)
                    motivos.Add(new MotivoEstado(TipoMotivo.FechaIlegible, "La fila no tiene una fecha legible."));

                var (fuente, fuenteDesconocida) = ClasificarFuente(fila.Literal, fuentesActivas, motivos);
                if (fuenteDesconocida)
                    RegistrarNuevo(fuentesNuevasVistas, fuentesNuevas, fila.Literal!);

                if (fila.Ingreso is not null)
                {
                    var concepto = PrimerTextoNoVacio(fila.Proveedor, fila.Destino, fila.Gasto);
                    ingresos.Add(new IngresoAnalizadoDto(
                        HojaOrigen: fila.Hoja, NumeroFila: fila.NumeroFila,
                        Estado: EstadoMasSevero(motivos), Motivos: motivos,
                        Fecha: fila.Fecha, Monto: fila.Ingreso,
                        Concepto: concepto,
                        Fuente: fuente, FuenteDesconocida: fuenteDesconocida));
                }
                else if (fila.Egreso is not null)
                {
                    var proveedorNuevo = false;
                    if (!string.IsNullOrWhiteSpace(fila.Proveedor)
                        && !proveedoresExistentes.Contains(Normalizar(fila.Proveedor)))
                    {
                        proveedorNuevo = true;
                        motivos.Add(new MotivoEstado(
                            TipoMotivo.ProveedorNuevo,
                            $"El proveedor '{fila.Proveedor}' no existe en el catálogo: se crearía al confirmar la importación."));
                        RegistrarNuevo(proveedoresNuevosVistos, proveedoresNuevos, fila.Proveedor);
                    }

                    var rubroDesconocido = false;
                    string? rubroNombre = fila.Rubro;
                    if (fila.Codigo is { } codigo && !rubrosExistentes.ContainsKey(codigo))
                    {
                        rubroDesconocido = true;
                        motivos.Add(new MotivoEstado(
                            TipoMotivo.RubroDesconocido,
                            $"El rubro con código {codigo} no existe en el catálogo: se crearía al confirmar la importación."));
                        if (rubrosNuevosVistos.Add(codigo))
                            rubrosNuevos.Add(new CodigoRubroNuevoDto(codigo, fila.Rubro));
                    }

                    var detalle = !string.IsNullOrWhiteSpace(fila.Gasto) ? fila.Gasto : fila.Destino;

                    gastos.Add(new GastoAnalizadoDto(
                        HojaOrigen: fila.Hoja, NumeroFila: fila.NumeroFila,
                        Estado: EstadoMasSevero(motivos), Motivos: motivos,
                        Fecha: fila.Fecha, Monto: fila.Egreso,
                        Proveedor: fila.Proveedor, ProveedorNuevo: proveedorNuevo,
                        NumeroFactura: fila.Factura, NumeroOrden: fila.Orden,
                        Detalle: detalle, Destino: fila.Destino,
                        Fuente: fuente, FuenteDesconocida: fuenteDesconocida,
                        CodigoRubro: fila.Codigo, Rubro: rubroNombre, RubroDesconocido: rubroDesconocido,
                        LineaPoaAsignada: null));
                }
                else
                {
                    // Defensivo (spec §"Estados (Gastos)"): fila-movimiento (sobrevivió al filtro
                    // de F5a por tener algún otro campo no nulo) sin Ingreso NI Egreso parseable.
                    // Nunca observado en las planillas reales; se vuelca como Gasto con Monto null
                    // porque sus campos (Proveedor/Factura/Orden/Destino/Rubro) son gasto-shaped.
                    motivos.Add(new MotivoEstado(
                        TipoMotivo.MontoIlegible, "La fila no tiene ni ingreso ni egreso legible."));

                    gastos.Add(new GastoAnalizadoDto(
                        HojaOrigen: fila.Hoja, NumeroFila: fila.NumeroFila,
                        Estado: EstadoMasSevero(motivos), Motivos: motivos,
                        Fecha: fila.Fecha, Monto: null,
                        Proveedor: fila.Proveedor, ProveedorNuevo: false,
                        NumeroFactura: fila.Factura, NumeroOrden: fila.Orden,
                        Detalle: fila.Gasto ?? fila.Destino, Destino: fila.Destino,
                        Fuente: fuente, FuenteDesconocida: fuenteDesconocida,
                        CodigoRubro: fila.Codigo, Rubro: fila.Rubro, RubroDesconocido: false,
                        LineaPoaAsignada: null));
                }
            }
        }

        // Reconciliación real Gastos↔POA: stub (Task 5 la completa). El mapeo de líneas POA de
        // abajo es completo, pero la clasificación de movimientos-con-factura es PROVISIONAL.
        var poaOds = ParsearPoaSeguro(planillaPoa);
        var lineasPoa = new List<LineaPoaAnalizadaDto>();

        foreach (var lineaOds in poaOds.Lineas)
        {
            var motivosLinea = new List<MotivoEstado>();

            var (literal, fuenteDesconocida) = ClasificarFuente(lineaOds.Literal, fuentesActivas, motivosLinea);
            if (fuenteDesconocida)
                RegistrarNuevo(fuentesNuevasVistas, fuentesNuevas, lineaOds.Literal!);

            var movimientos = lineaOds.Movimientos
                .Select(MapearMovimientoPoaProvisional)
                .ToList();

            lineasPoa.Add(new LineaPoaAnalizadaDto(
                Hoja: lineaOds.Hoja, Ejercicio: ejercicio,
                Estado: EstadoMasSevero(motivosLinea), Motivos: motivosLinea,
                Literal: literal, FuenteDesconocida: fuenteDesconocida,
                Presupuesto: lineaOds.Presupuesto, SaldoPlanilla: lineaOds.Saldo,
                Movimientos: movimientos));
        }

        var maestrosNuevos = new MaestrosNuevosDto(proveedoresNuevos, fuentesNuevas, rubrosNuevos);

        var todosLosMovimientos = lineasPoa.SelectMany(l => l.Movimientos).ToList();
        var resumen = new ResumenAnalisisDto(
            TotalFilas: ingresos.Count + gastos.Count,
            Ok: ingresos.Count(i => i.Estado == EstadoFila.Ok) + gastos.Count(g => g.Estado == EstadoFila.Ok),
            Advertencias: ingresos.Count(i => i.Estado == EstadoFila.Advertencia)
                + gastos.Count(g => g.Estado == EstadoFila.Advertencia),
            Errores: ingresos.Count(i => i.Estado == EstadoFila.Error) + gastos.Count(g => g.Estado == EstadoFila.Error),
            PoaConciliados: todosLosMovimientos.Count(mv => mv.Clasificacion == ClasificacionReconciliacion.Conciliado),
            PoaDudosos: todosLosMovimientos.Count(mv => mv.Clasificacion == ClasificacionReconciliacion.Dudoso),
            PoaCompromisos: todosLosMovimientos.Count(mv => mv.Clasificacion == ClasificacionReconciliacion.CompromisoSoloPoa));

        return new ResultadoAnalisisDto(ingresos, gastos, lineasPoa, maestrosNuevos, resumen);
    }

    /// <summary>
    /// Saldo inicial de enero (spec §8, resolución pre-flight #6). La fila "SALDO ANTERIOR" de
    /// la planilla real NO sobrevive el parseo de F5a: no tiene Fecha/Factura/Orden/Proveedor/
    /// Destino/Gasto/Ingreso/Egreso (solo texto en la columna SALDO y, dos columnas más allá, el
    /// número), así que <c>esMovimiento</c> la descarta por completo — es invisible para este
    /// servicio. Por eso SIEMPRE se usa el fallback documentado en el plan: el saldo antes del
    /// primer movimiento real de ENERO = Saldo - Ingreso + Egreso de esa primera fila. Verificado
    /// contra PlanillaGastos2026.ods: primera fila real (Ingreso=150000, Saldo=194524) da
    /// saldo inicial = 44524, que coincide EXACTO con la celda "SALDO ANTERIOR" de la planilla.
    /// </summary>
    private static void AgregarSaldoInicialEnero(
        PlanillaGastosOds gastosOds, int ejercicio, List<IngresoAnalizadoDto> ingresos)
    {
        var filasEnero = gastosOds.FilasPorMes["ENERO"];
        if (filasEnero.Count == 0)
            return;

        var primera = filasEnero[0];
        if (primera.Saldo is null)
            return;

        var saldoInicial = primera.Saldo.Value - (primera.Ingreso ?? 0m) + (primera.Egreso ?? 0m);

        // NumeroFila=0: sentinel — no corresponde a ninguna fila real de la planilla (la fila
        // "SALDO ANTERIOR" original ni siquiera llega a este servicio, ver comentario de arriba).
        ingresos.Add(new IngresoAnalizadoDto(
            HojaOrigen: "ENERO", NumeroFila: 0,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Fecha: new DateOnly(ejercicio, 1, 1), Monto: saldoInicial,
            Concepto: $"Saldo inicial {ejercicio}",
            Fuente: null, FuenteDesconocida: false));
    }

    private static (string? Fuente, bool Desconocida) ClasificarFuente(
        string? literal, HashSet<string> fuentesActivas, List<MotivoEstado> motivos)
    {
        if (string.IsNullOrWhiteSpace(literal))
        {
            motivos.Add(new MotivoEstado(
                TipoMotivo.LiteralVacio, "La fuente de financiamiento (columna LITERAL) está vacía."));
            return (null, false);
        }

        if (!fuentesActivas.Contains(Normalizar(literal)))
        {
            motivos.Add(new MotivoEstado(
                TipoMotivo.FuenteDesconocida,
                $"La fuente de financiamiento '{literal}' no existe en el catálogo: se crearía al confirmar la importación."));
            return (literal, true);
        }

        return (literal, false);
    }

    private static bool RegistrarNuevo(HashSet<string> vistos, List<string> destino, string valor)
    {
        var trimeado = valor.Trim();
        if (!vistos.Add(trimeado))
            return false;

        destino.Add(trimeado);
        return true;
    }

    private static string? PrimerTextoNoVacio(params string?[] candidatos) =>
        candidatos.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static EstadoFila EstadoMasSevero(IReadOnlyList<MotivoEstado> motivos) =>
        motivos.Count == 0 ? EstadoFila.Ok : motivos.Max(m => Severidad(m.Tipo));

    private static EstadoFila Severidad(TipoMotivo tipo) => tipo switch
    {
        TipoMotivo.FechaIlegible or TipoMotivo.MontoIlegible => EstadoFila.Error,
        _ => EstadoFila.Advertencia,
    };

    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();

    /// <summary>
    /// Task 4: mapeo PROVISIONAL de un movimiento POA, sin cruzarlo aún contra los Gastos
    /// (eso es la reconciliación real de Task 5). Pre-flight #7: sin Factura no hay clave de
    /// match posible → es un compromiso, no una ambigüedad, así que va directo a
    /// <see cref="ClasificacionReconciliacion.CompromisoSoloPoa"/> con <see cref="EstadoFila.Ok"/>.
    /// Con Factura, Task 4 todavía no sabe si conciliará contra algún gasto real, así que lo
    /// deja marcado <see cref="ClasificacionReconciliacion.Dudoso"/> (Advertencia +
    /// <see cref="TipoMotivo.ReconciliacionDudosa"/>) como placeholder explícito; Task 5
    /// reemplaza esta clasificación por la real (Conciliado / Dudoso definitivo) cruzando
    /// contra los Gastos parseados arriba.
    /// </summary>
    private static MovimientoPoaAnalizadoDto MapearMovimientoPoaProvisional(FilaPoaOds movimiento)
    {
        if (string.IsNullOrWhiteSpace(movimiento.Factura))
        {
            return new MovimientoPoaAnalizadoDto(
                NumeroFila: movimiento.NumeroFila,
                Factura: movimiento.Factura, Orden: movimiento.Orden, Proveedor: movimiento.Proveedor,
                Detalle: movimiento.Gasto, Importe: movimiento.Importe,
                Clasificacion: ClasificacionReconciliacion.CompromisoSoloPoa,
                IndiceGastoConciliado: null,
                Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>());
        }

        var motivos = new List<MotivoEstado>
        {
            new(TipoMotivo.ReconciliacionDudosa,
                "Reconciliación provisional: la referencia contra los Gastos aún no se calculó."),
        };

        return new MovimientoPoaAnalizadoDto(
            NumeroFila: movimiento.NumeroFila,
            Factura: movimiento.Factura, Orden: movimiento.Orden, Proveedor: movimiento.Proveedor,
            Detalle: movimiento.Gasto, Importe: movimiento.Importe,
            Clasificacion: ClasificacionReconciliacion.Dudoso,
            IndiceGastoConciliado: null,
            Estado: EstadoFila.Advertencia, Motivos: motivos);
    }

    /// <summary>
    /// Resolución pre-flight #10, análoga a <see cref="ParsearGastosSeguro"/> pero para la
    /// planilla POA.
    /// </summary>
    private PlanillaPoaOds ParsearPoaSeguro(Stream planillaPoa)
    {
        try
        {
            return _parser.ParsearPoa(planillaPoa);
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException($"La planilla POA no se pudo leer: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolución pre-flight #10: un .ods inválido o con hoja faltante hace que F5a lance
    /// <see cref="InvalidOperationException"/>; acá se re-lanza como <see cref="ArgumentException"/>
    /// para que el <c>DomainExceptionHandler</c> existente lo traduzca a 400 sin tocarlo.
    /// </summary>
    private PlanillaGastosOds ParsearGastosSeguro(Stream planillaGastos)
    {
        try
        {
            return _parser.ParsearGastos(planillaGastos);
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException($"La planilla de Gastos no se pudo leer: {ex.Message}", ex);
        }
    }
}
