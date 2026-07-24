using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

public class ConfirmacionImportacionService : IConfirmacionImportacionService
{
    private readonly IImportacionRepository _importacionRepo;
    private readonly IProveedorRepository _proveedores;
    private readonly IRubroGastoRepository _rubros;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ILineaPoaRepository _lineasPoa;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ConfirmacionImportacionService(
        IImportacionRepository importacionRepo,
        IProveedorRepository proveedores,
        IRubroGastoRepository rubros,
        IFuenteFinanciamientoRepository fuentes,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth)
    {
        _importacionRepo = importacionRepo;
        _proveedores = proveedores;
        _rubros = rubros;
        _fuentes = fuentes;
        _lineasPoa = lineasPoa;
        _session = session;
        _auth = auth;
    }

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        await ValidarAsync(dto);

        // El guard de re-importación (spec §2.6, 409 salvo Forzar) se resuelve DENTRO de la
        // transacción del repositorio, no acá: tiene que correr con el advisory lock tomado
        // para no dejar una ventana de carrera entre dos /confirmar concurrentes del mismo
        // ejercicio (ver Task 6). Este servicio solo pasa dto.Forzar tal cual llegó.
        return await _importacionRepo.ConfirmarAsync(dto, _session.UsuarioActual!.Id);
    }

    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        return await _importacionRepo.RevertirAsync(idImportacion, _session.UsuarioActual!.Id);
    }

    public async Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        return await _importacionRepo.ListarHistorialAsync();
    }

    /// <summary>
    /// Regla de cierre (spec §3): toda referencia nominal (Proveedor/Fuente/CodigoRubro/
    /// LineaPoa) tiene que resolver contra un maestro YA existente en la base o contra uno
    /// declarado en dto.MaestrosNuevos (LineaPoa: contra una LineaPoaConfirmarDto.Nombre del
    /// propio payload — no existe un "MaestrosNuevos.LineasPoa" separado). Si no resuelve, es
    /// error con clave "Tipo[índice].Campo". Comparaciones normalizadas (Trim+ToUpperInvariant),
    /// mismo criterio que AnalisisImportacionService.Normalizar (F5b). Proveedores/Rubros
    /// contra TODOS los existentes (sin filtro Activo); Fuentes SOLO contra las ACTIVAS — mismo
    /// criterio ya establecido por AnalisisImportacionService.cs:42-53.
    /// </summary>
    private async Task ValidarAsync(ConfirmarImportacionDto dto)
    {
        var errores = new Dictionary<string, List<string>>();

        var proveedoresExistentes = (await _proveedores.ListarTodosAsync())
            .Select(p => Normalizar(p.Nombre)).ToHashSet();
        var rubrosExistentes = (await _rubros.ListarTodosAsync())
            .Select(r => r.Codigo).ToHashSet();
        var fuentesActivas = (await _fuentes.ListarTodasAsync())
            .Where(f => f.Activo).Select(f => Normalizar(f.Nombre)).ToHashSet();
        var lineasPoaExistentes = (await _lineasPoa.ListarTodasAsync())
            .Where(l => l.Ejercicio == dto.Ejercicio)
            .Select(l => Normalizar(l.Nombre)).ToHashSet();

        var proveedoresNuevos = dto.MaestrosNuevos.Proveedores.Select(Normalizar).ToHashSet();
        var fuentesNuevas = dto.MaestrosNuevos.Fuentes.Select(Normalizar).ToHashSet();
        var rubrosNuevos = dto.MaestrosNuevos.Rubros.Select(r => r.Codigo).ToHashSet();
        var lineasPoaDeclaradas = dto.LineasPoa.Select(l => Normalizar(l.Nombre)).ToHashSet();

        for (var i = 0; i < dto.MaestrosNuevos.Rubros.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(dto.MaestrosNuevos.Rubros[i].Nombre))
                AgregarError(errores, $"MaestrosNuevos.Rubros[{i}].Nombre", "Requerido");
        }

        for (var i = 0; i < dto.Ingresos.Count; i++)
        {
            var ingreso = dto.Ingresos[i];
            if (string.IsNullOrWhiteSpace(ingreso.Concepto))
                AgregarError(errores, $"Ingresos[{i}].Concepto", "Requerido");
            if (!Resuelve(ingreso.Fuente, fuentesActivas, fuentesNuevas))
                AgregarError(errores, $"Ingresos[{i}].Fuente",
                    $"La fuente '{ingreso.Fuente}' no existe ni fue declarada nueva");
        }

        // A.4 (review Important A; F5c amplió la clave con NumeroOrden): dos filas del MISMO
        // payload con la misma (Proveedor, NumeroFactura, NumeroOrden) tienen que dar un 400
        // estructurado acá, no un 500 de Postgres cuando el repositorio intente insertar las dos.
        // El dedupe del repositorio (Important A.1/A.2) resuelve "esta factura+orden ya está en
        // la base"; esto resuelve "esta factura+orden se repite dos veces DENTRO del mismo
        // archivo que se está importando", un caso que el repositorio no puede distinguir sin
        // este chequeo previo (ambas filas son "nuevas" para él). La clave tiene que espejar
        // EXACTAMENTE la del índice único de la base (ImportacionRepository.ProyectarClaveGastoConFactura)
        // — sin NumeroOrden acá, dos renglones reales con la misma factura pero distinto orden
        // (caso real de la planilla 2026: GARAY POZO HERNÁN, factura 82446 — ver
        // docs/finanzas-facturas-duplicadas-planilla-2026.md) se rechazaban por error como
        // "duplicados" cuando en realidad son dos gastos legítimos y distintos.
        var facturasVistas = new Dictionary<(string Proveedor, string NumeroFactura, string? NumeroOrden), int>();

        for (var i = 0; i < dto.Gastos.Count; i++)
        {
            var gasto = dto.Gastos[i];
            if (string.IsNullOrWhiteSpace(gasto.Detalle))
                AgregarError(errores, $"Gastos[{i}].Detalle", "Requerido");
            if (!string.IsNullOrWhiteSpace(gasto.NumeroFactura))
            {
                var claveFactura = (Normalizar(gasto.Proveedor), Normalizar(gasto.NumeroFactura), NormalizarOpcional(gasto.NumeroOrden));
                if (!facturasVistas.TryAdd(claveFactura, i))
                    AgregarError(errores, $"Gastos[{i}].NumeroFactura",
                        $"La factura '{gasto.NumeroFactura}' del proveedor '{gasto.Proveedor}' está duplicada dentro del payload");
            }
            if (!Resuelve(gasto.Proveedor, proveedoresExistentes, proveedoresNuevos))
                AgregarError(errores, $"Gastos[{i}].Proveedor",
                    $"El proveedor '{gasto.Proveedor}' no existe ni fue declarado nuevo");
            if (!Resuelve(gasto.Fuente, fuentesActivas, fuentesNuevas))
                AgregarError(errores, $"Gastos[{i}].Fuente",
                    $"La fuente '{gasto.Fuente}' no existe ni fue declarada nueva");
            if (!rubrosExistentes.Contains(gasto.CodigoRubro) && !rubrosNuevos.Contains(gasto.CodigoRubro))
                AgregarError(errores, $"Gastos[{i}].CodigoRubro",
                    $"El rubro {gasto.CodigoRubro} no existe ni fue declarado nuevo");
            if (!string.IsNullOrWhiteSpace(gasto.LineaPoa)
                && !lineasPoaExistentes.Contains(Normalizar(gasto.LineaPoa))
                && !lineasPoaDeclaradas.Contains(Normalizar(gasto.LineaPoa)))
                AgregarError(errores, $"Gastos[{i}].LineaPoa",
                    $"La línea POA '{gasto.LineaPoa}' no existe ni fue declarada en LineasPoa");
            // Regla SIMÉTRICA de GastoService.cs:272-275 (ValidarAsync). ImportacionRepository no
            // pasa por GastoService, así que este es el único lugar del camino de escritura del
            // importador donde se puede hacer cumplir: Credito la exige, Contado la prohíbe.
            if (gasto.Condicion == CondicionPago.Credito && gasto.FechaVencimiento is null)
                AgregarError(errores, $"Gastos[{i}].FechaVencimiento", "Requerido");
            if (gasto.Condicion == CondicionPago.Contado && gasto.FechaVencimiento is not null)
                AgregarError(errores, $"Gastos[{i}].FechaVencimiento", "No corresponde para gastos de contado");
        }

        for (var i = 0; i < dto.LineasPoa.Count; i++)
        {
            var linea = dto.LineasPoa[i];
            if (string.IsNullOrWhiteSpace(linea.Nombre))
                AgregarError(errores, $"LineasPoa[{i}].Nombre", "Requerido");
            if (string.IsNullOrWhiteSpace(linea.Programa))
                AgregarError(errores, $"LineasPoa[{i}].Programa", "Requerido");

            for (var j = 0; j < linea.Asignaciones.Count; j++)
            {
                if (!Resuelve(linea.Asignaciones[j].Fuente, fuentesActivas, fuentesNuevas))
                    AgregarError(errores, $"LineasPoa[{i}].Asignaciones[{j}].Fuente",
                        $"La fuente '{linea.Asignaciones[j].Fuente}' no existe ni fue declarada nueva");
            }
        }

        if (errores.Count > 0)
            throw new StockApp.Domain.Exceptions.ValidacionImportacionException(
                errores.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
    }

    private static bool Resuelve(string nombre, HashSet<string> existentes, HashSet<string> nuevos)
    {
        var normalizado = Normalizar(nombre);
        return existentes.Contains(normalizado) || nuevos.Contains(normalizado);
    }

    private static void AgregarError(Dictionary<string, List<string>> errores, string clave, string mensaje)
    {
        if (!errores.TryGetValue(clave, out var lista))
        {
            lista = new List<string>();
            errores[clave] = lista;
        }
        lista.Add(mensaje);
    }

    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();

    /// <summary>Mismo criterio que <see cref="Normalizar"/>, pero para NumeroOrden (opcional):
    /// blanco/null colapsa a null, igual que ImportacionRepository.NormalizarOpcional — dos
    /// filas sin orden tienen que matchear entre sí en este chequeo, igual que en el índice de
    /// base (NULLS NOT DISTINCT).</summary>
    private static string? NormalizarOpcional(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : Normalizar(texto);
}
