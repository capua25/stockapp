using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Payload de POST /finanzas/importar/confirmar (F5c, spec §3). Los maestros se referencian
/// por NOMBRE (Proveedor/Fuente) o CÓDIGO (Rubro), no por Id — la mayoría no existe todavía en
/// la base; el servidor resuelve nombre/código → Id con get-or-create dentro de la transacción.
/// Contrato PROPIO, no reutiliza los DTOs de análisis de F5b (ResultadoAnalisisDto y afines):
/// los campos obligatorios del dominio van NO nullable acá aunque el análisis los deje vacíos.
/// </summary>
public sealed record ConfirmarImportacionDto(
    int Ejercicio,
    bool Forzar,
    MaestrosNuevosConfirmarDto MaestrosNuevos,
    IReadOnlyList<IngresoConfirmarDto> Ingresos,
    IReadOnlyList<GastoConfirmarDto> Gastos,
    IReadOnlyList<LineaPoaConfirmarDto> LineasPoa);

/// <summary>Conjuntos de maestros a crear, declarados EXPLÍCITAMENTE por el usuario (F5d). Nada
/// se crea por fuera de lo que aparece acá — es la "regla de cierre" del spec §3.</summary>
public sealed record MaestrosNuevosConfirmarDto(
    IReadOnlyList<string> Proveedores,
    IReadOnlyList<string> Fuentes,
    IReadOnlyList<RubroNuevoConfirmarDto> Rubros);

public sealed record RubroNuevoConfirmarDto(int Codigo, string Nombre);

public sealed record IngresoConfirmarDto(DateOnly Fecha, string Concepto, decimal Monto, string Fuente);

/// <summary>
/// LineaPoa es null cuando el gasto NO está vinculado a ningún proyecto POA (la mayoría de los
/// gastos del libro caja). Cuando no es null, tiene que resolver contra una LineaPoa YA
/// existente en la base para este Ejercicio o contra una declarada en el propio payload
/// (LineasPoa) — NO existe un "MaestrosNuevos.LineasPoa" separado: la lista LineasPoa del
/// payload ES la declaración.
///
/// FechaVencimiento (agregado tras revisión del usuario): obligatoria cuando Condicion ==
/// Credito, valida en Task 3. Sin ella, GastoService.AltaAsync (alta manual) rechaza el gasto
/// (GastoService.cs:272-273) — ImportacionRepository escribe directo contra AppDbContext, sin
/// pasar por GastoService, así que no hay bloqueo técnico, pero un Credito sin vencimiento
/// nunca aparecería en el Calendario de Pagos (F4): sería un compromiso invisible. Va AL FINAL
/// de la lista de parámetros (no en medio) para no reordenar ninguna construcción posicional
/// ya escrita en este plan.
/// </summary>
public sealed record GastoConfirmarDto(
    string Proveedor, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateOnly Fecha, decimal MontoTotal,
    string Fuente, int CodigoRubro, string? LineaPoa, CondicionPago Condicion,
    DateOnly? FechaVencimiento);

public sealed record LineaPoaConfirmarDto(
    string Nombre, string Programa,
    IReadOnlyList<AsignacionConfirmarDto> Asignaciones);

public sealed record AsignacionConfirmarDto(string Fuente, decimal Monto);

/// <summary>Respuesta feliz de /confirmar. IdImportacion es el Guid del lote — necesario para
/// poder revertirlo después con /revertir/{id}. Los campos *Reactivados/*Reactivadas (agregados
/// al FINAL, sin reordenar los existentes: hay construcciones posicionales en tasks posteriores
/// que dependen del orden actual) cuentan maestros/líneas que existían dados de baja (Activo =
/// false) y se reactivaron al ser declarados de nuevo — NO se suman a los contadores de
/// *Creados: un maestro reactivado no es un maestro creado.
///
/// Conflictos (review Important A, agregado AL FINAL — misma regla de no reordenar; F5c amplió
/// la clave con NumeroOrden): gastos CON NumeroFactura cuya clave natural (ProveedorId,
/// NumeroFactura, NumeroOrden) matchea contra un gasto activo ya existente, pero cuyos demás
/// datos (Fecha/MontoTotal) difieren. NO son "omitidos" (esos son duplicados idénticos) ni
/// "creados" — no se escriben, y quedan acá para que un humano decida. Solo aplica al camino CON
/// factura: ese es el único que colisiona contra el índice único parcial
/// IX_Gastos_ProveedorId_NumeroFactura_NumeroOrden (AppDbContext.cs).</summary>
public sealed record ResultadoConfirmacionDto(
    Guid IdImportacion,
    int ProveedoresCreados, int FuentesCreadas, int RubrosCreados,
    int LineasPoaCreadas, int AsignacionesCreadas,
    int IngresosCreados, int IngresosOmitidos,
    int GastosCreados, int GastosOmitidos, int PagosCreados,
    int ProveedoresReactivados, int FuentesReactivadas,
    int RubrosReactivados, int LineasPoaReactivadas,
    IReadOnlyList<ConflictoGastoDto> Conflictos);

/// <summary>Un campo de un gasto existente cuyo valor difiere del que trae el payload para la
/// MISMA clave natural (ProveedorId, NumeroFactura). ValorAnterior/ValorNuevo van como string ya
/// formateados — es solo para que un humano lea el reporte, no para reprocesar nada.</summary>
public sealed record CampoDivergenteDto(string Campo, string ValorAnterior, string ValorNuevo);

/// <summary>Un gasto CON NumeroFactura que matchea por (Proveedor, NumeroFactura) contra uno ya
/// activo en la base, pero con datos distintos (spec review Important A.2) — "ya importado con
/// datos distintos", no un duplicado silencioso. NumeroFactura va sin normalizar (tal cual lo
/// declaró el usuario en el payload) para que el reporte sea legible. Indice (agregado AL FINAL,
/// re-review Minor) es la posición de la fila dentro de dto.Gastos — la misma clave estructurada
/// "Gastos[i]" que ya usa ConfirmacionImportacionService.ValidarAsync (A.4) — para que F5d pueda
/// resaltar la fila exacta en la grilla sin tener que buscarla por proveedor+factura.</summary>
public sealed record ConflictoGastoDto(
    string Proveedor, string NumeroFactura, IReadOnlyList<CampoDivergenteDto> CamposDivergentes, int Indice);

/// <summary>Respuesta feliz de /revertir/{id}: contadores de registros dados de baja por tipo.</summary>
public sealed record ResultadoReversionDto(
    Guid IdImportacion,
    int GastosRevertidos, int PagosRevertidos, int IngresosRevertidos,
    int LineasPoaRevertidas, int AsignacionesRevertidas);
