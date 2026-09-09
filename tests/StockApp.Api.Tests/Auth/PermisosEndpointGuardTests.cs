using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class PermisosEndpointGuardTests : ApiTestBase
{
    public PermisosEndpointGuardTests(ApiFactory factory) : base(factory) { }

    /// <summary>
    /// Fixture congelada (Metodo, Ruta, Permiso) — construida a partir del código real de
    /// src/StockApp.Api/Endpoints/*.cs al momento de escribir esta task. Cualquier fila que
    /// deje de matchear tras un cambio en un archivo de Endpoints es una regresión real.
    /// </summary>
    private static readonly (string Metodo, string Ruta, string Permiso)[] EndpointsYPermisos =
    [
        ("POST",   "/finanzas/gastos/{id}/adjuntos", Permisos.RegistrarGastos),
        ("POST",   "/finanzas/pagos/{id}/adjuntos", Permisos.RegistrarPagos),
        ("GET",    "/finanzas/gastos/{id}/adjuntos", Permisos.VerFinanzas),
        ("GET",    "/finanzas/pagos/{id}/adjuntos", Permisos.VerFinanzas),
        ("GET",    "/finanzas/adjuntos/{id}/contenido", Permisos.VerFinanzas),

        ("GET",    "/auditoria", Permisos.VerReportes),

        ("GET",    "/backups", Permisos.GestionarDiagnostico),
        ("GET",    "/backups/{id}/contenido", Permisos.GestionarDiagnostico),
        ("GET",    "/backups/salud", Permisos.GestionarDiagnostico),
        ("POST",   "/backups", Permisos.GestionarDiagnostico),

        ("GET",    "/categorias", Permisos.GestionarTablasMaestras),
        ("POST",   "/categorias", Permisos.GestionarTablasMaestras),
        ("PUT",    "/categorias/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/categorias/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/categorias/activas", Permisos.GestionarProductos),

        ("GET",    "/configuracion/alertas", Permisos.GestionarDiagnostico),
        ("PUT",    "/configuracion/alertas", Permisos.GestionarDiagnostico),
        ("POST",   "/configuracion/alertas/probar", Permisos.GestionarDiagnostico),

        ("GET",    "/dimensiones-tematicas", Permisos.GestionarTablasMaestras),
        ("POST",   "/dimensiones-tematicas", Permisos.GestionarTablasMaestras),
        ("PUT",    "/dimensiones-tematicas/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/dimensiones-tematicas/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/dimensiones-tematicas/activas", Permisos.GestionarTareas),

        ("GET",    "/documentos/activos", Permisos.GestionarDocumentos),
        ("GET",    "/documentos/historial", Permisos.GestionarDocumentos),
        ("GET",    "/documentos/{id}", Permisos.GestionarDocumentos),
        ("POST",   "/documentos", Permisos.GestionarDocumentos),
        ("PUT",    "/documentos/{id}", Permisos.GestionarDocumentos),
        ("POST",   "/documentos/{id}/iniciar", Permisos.GestionarDocumentos),
        ("POST",   "/documentos/{id}/volver-a-pendiente", Permisos.GestionarDocumentos),
        ("POST",   "/documentos/{id}/finalizar", Permisos.GestionarDocumentos),
        ("POST",   "/documentos/{id}/notas", Permisos.GestionarDocumentos),
        ("POST",   "/documentos/{id}/anular", Permisos.AdministrarDocumentos),
        ("POST",   "/documentos/{id}/reabrir", Permisos.AdministrarDocumentos),
        ("POST",   "/documentos/{id}/adjuntos", Permisos.GestionarDocumentos),
        ("GET",    "/documentos/{id}/adjuntos", Permisos.GestionarDocumentos),
        ("GET",    "/documentos/adjuntos/{adjuntoId}/contenido", Permisos.GestionarDocumentos),
        ("DELETE", "/documentos/adjuntos/{adjuntoId}", Permisos.AdministrarDocumentos),

        ("GET",    "/finanzas/libro-caja", Permisos.VerFinanzas),
        ("GET",    "/finanzas/control-poa", Permisos.VerFinanzas),
        ("GET",    "/finanzas/calendario-pagos", Permisos.VerFinanzas),

        ("GET",    "/finanzas/fuentes", Permisos.GestionarMaestrosFinanzas),
        ("POST",   "/finanzas/fuentes", Permisos.GestionarMaestrosFinanzas),
        ("PUT",    "/finanzas/fuentes/{id}", Permisos.GestionarMaestrosFinanzas),
        ("DELETE", "/finanzas/fuentes/{id}", Permisos.GestionarMaestrosFinanzas),
        ("GET",    "/finanzas/fuentes/activas", Permisos.VerFinanzas),

        ("GET",    "/finanzas/gastos", Permisos.VerFinanzas),
        ("GET",    "/finanzas/gastos/{id}", Permisos.VerFinanzas),
        ("GET",    "/finanzas/gastos/por-factura", Permisos.VerFinanzas),
        ("POST",   "/finanzas/gastos", Permisos.RegistrarGastos),
        ("PUT",    "/finanzas/gastos/{id}", Permisos.RegistrarGastos),
        ("DELETE", "/finanzas/gastos/{id}", Permisos.RegistrarGastos),
        ("POST",   "/finanzas/gastos/{id}/pagos", Permisos.RegistrarPagos),
        ("DELETE", "/finanzas/gastos/{id}/pagos/{pagoId}", Permisos.RegistrarPagos),
        ("POST",   "/finanzas/gastos/{id}/movimientos", Permisos.RegistrarGastos),

        ("POST",   "/finanzas/importar/analizar", Permisos.ImportarPlanillas),
        ("POST",   "/finanzas/importar/confirmar", Permisos.ImportarPlanillas),
        ("POST",   "/finanzas/importar/revertir/{id}", Permisos.ImportarPlanillas),
        ("GET",    "/finanzas/importar/historial", Permisos.ImportarPlanillas),

        // Bug de coherencia (2026-08-15): la policy HTTP solo exigía RegistrarMovimientos
        // mientras IngresoPorFacturaService (RegistrarAsync/AnularLoteAsync) exige además
        // RegistrarGastos sin condición — el 403 llegaba recién desde Application, más adentro.
        ("POST",   "/movimientos/ingreso-factura", Permisos.RegistrarMovimientos),
        ("POST",   "/movimientos/ingreso-factura", Permisos.RegistrarGastos),
        ("POST",   "/movimientos/ingreso-factura/{gastoId}/anular", Permisos.RegistrarMovimientos),
        ("POST",   "/movimientos/ingreso-factura/{gastoId}/anular", Permisos.RegistrarGastos),

        ("GET",    "/finanzas/ingresos", Permisos.VerFinanzas),
        ("POST",   "/finanzas/ingresos", Permisos.RegistrarIngresos),
        ("PUT",    "/finanzas/ingresos/{id}", Permisos.RegistrarIngresos),
        ("DELETE", "/finanzas/ingresos/{id}", Permisos.RegistrarIngresos),

        ("GET",    "/finanzas/lineas-poa", Permisos.GestionarMaestrosFinanzas),
        ("POST",   "/finanzas/lineas-poa", Permisos.GestionarMaestrosFinanzas),
        ("PUT",    "/finanzas/lineas-poa/{id}", Permisos.GestionarMaestrosFinanzas),
        ("DELETE", "/finanzas/lineas-poa/{id}", Permisos.GestionarMaestrosFinanzas),
        ("GET",    "/finanzas/lineas-poa/activas", Permisos.VerFinanzas),

        ("GET",    "/logs", Permisos.GestionarDiagnostico),
        ("GET",    "/logs/contenido", Permisos.GestionarDiagnostico),

        ("POST",   "/movimientos", Permisos.RegistrarMovimientos),
        ("GET",    "/movimientos/historial", Permisos.RegistrarMovimientos),

        ("GET",    "/organismos-responsables", Permisos.GestionarTablasMaestras),
        ("POST",   "/organismos-responsables", Permisos.GestionarTablasMaestras),
        ("PUT",    "/organismos-responsables/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/organismos-responsables/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/organismos-responsables/activas", Permisos.GestionarTareas),

        ("GET",    "/origenes-financiamiento", Permisos.GestionarTablasMaestras),
        ("POST",   "/origenes-financiamiento", Permisos.GestionarTablasMaestras),
        ("PUT",    "/origenes-financiamiento/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/origenes-financiamiento/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/origenes-financiamiento/activas", Permisos.GestionarTareas),

        ("GET",    "/productos", Permisos.GestionarProductos),
        ("POST",   "/productos", Permisos.GestionarProductos),
        ("PUT",    "/productos/{id}", Permisos.GestionarProductos),
        ("DELETE", "/productos/{id}", Permisos.GestionarProductos),
        ("PUT",    "/productos/{id}/precio", Permisos.GestionarProductos),
        ("POST",   "/productos/{id}/recalcular-stock", Permisos.RecalcularStock),

        ("GET",    "/proveedores", Permisos.GestionarTablasMaestras),
        ("POST",   "/proveedores", Permisos.GestionarTablasMaestras),
        ("PUT",    "/proveedores/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/proveedores/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/proveedores/activas", Permisos.VerFinanzas),

        ("GET",    "/reportes/valorizacion", Permisos.VerReportes),
        ("GET",    "/reportes/stock-por-categoria", Permisos.VerReportes),
        ("GET",    "/reportes/mas-movidos", Permisos.VerReportes),
        ("GET",    "/reportes/historial-producto/{productoId}", Permisos.VerReportes),

        ("GET",    "/finanzas/rubros", Permisos.GestionarMaestrosFinanzas),
        ("POST",   "/finanzas/rubros", Permisos.GestionarMaestrosFinanzas),
        ("PUT",    "/finanzas/rubros/{id}", Permisos.GestionarMaestrosFinanzas),
        ("DELETE", "/finanzas/rubros/{id}", Permisos.GestionarMaestrosFinanzas),
        ("GET",    "/finanzas/rubros/activos", Permisos.VerFinanzas),

        ("POST",   "/tareas", Permisos.GestionarTareas),
        ("GET",    "/tareas", Permisos.GestionarTareas),
        ("GET",    "/tareas/{id}", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/tomar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/soltar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/terminar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/cancelar", Permisos.AdministrarTareas),
        ("POST",   "/tareas/{id}/prioridad", Permisos.AdministrarTareas),
        ("PUT",    "/tareas/{id}/clasificacion", Permisos.AdministrarTareas),
        ("POST",   "/tareas/{id}/notas", Permisos.GestionarTareas),

        ("GET",    "/unidades-medida", Permisos.GestionarTablasMaestras),
        ("POST",   "/unidades-medida", Permisos.GestionarTablasMaestras),
        ("PUT",    "/unidades-medida/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/unidades-medida/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/unidades-medida/activas", Permisos.GestionarProductos),
        ("POST",   "/unidades-medida/garantizar-por-defecto", Permisos.GestionarProductos),

        ("GET",    "/usuarios", Permisos.GestionarUsuarios),
        ("POST",   "/usuarios", Permisos.GestionarUsuarios),
        ("DELETE", "/usuarios/{id}", Permisos.GestionarUsuarios),
        ("PUT",    "/usuarios/{id}/rol", Permisos.GestionarUsuarios),
        ("PUT",    "/usuarios/{id}/contrasena", Permisos.GestionarUsuarios),
        ("GET",    "/usuarios/{id}/permisos", Permisos.GestionarUsuarios),
        ("PUT",    "/usuarios/{id}/permisos", Permisos.GestionarUsuarios),

        ("GET",    "/zonas", Permisos.GestionarTablasMaestras),
        ("POST",   "/zonas", Permisos.GestionarTablasMaestras),
        ("PUT",    "/zonas/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/zonas/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/zonas/activas", Permisos.GestionarTareas),
    ];

    [Fact]
    public void CadaEndpointDeLaLista_SigueExigiendoElMismoPermisoQueAntes()
    {
        var endpointDataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var endpointsReales = endpointDataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        var faltantes = new List<string>();
        var incorrectos = new List<string>();

        foreach (var (metodo, ruta, permisoEsperado) in EndpointsYPermisos)
        {
            // RoutePattern.RawText conserva las restricciones de ruta tal cual se escribieron
            // (ej. "/productos/{id:int}", no "/productos/{id}") — la fixture de arriba usa la
            // forma sin restricción por legibilidad, así que se normalizan ambos lados quitando
            // el sufijo ":tipo" antes de comparar.
            var candidato = endpointsReales.FirstOrDefault(e =>
            {
                var rutaSinRestriccion = System.Text.RegularExpressions.Regex.Replace(
                    e.RoutePattern.RawText ?? string.Empty, @":[a-zA-Z]+(?=\}|\?)", string.Empty);
                return rutaSinRestriccion.TrimEnd('/') == ruta.TrimEnd('/') &&
                    e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(metodo) == true;
            });

            if (candidato is null)
            {
                faltantes.Add($"{metodo} {ruta}");
                continue;
            }

            var policyNames = candidato.Metadata
                .OfType<IAuthorizeData>()
                .Select(a => a.Policy)
                .Where(p => p is not null)
                .ToList();

            if (!policyNames.Contains(permisoEsperado))
                incorrectos.Add($"{metodo} {ruta}: esperaba '{permisoEsperado}', encontró [{string.Join(", ", policyNames)}]");
        }

        Assert.True(faltantes.Count == 0, $"Endpoints no encontrados: {string.Join("; ", faltantes)}");
        Assert.True(incorrectos.Count == 0, $"Permisos cambiados: {string.Join("; ", incorrectos)}");
    }

    /// <summary>
    /// El test de arriba solo custodia lo que YA está en <see cref="EndpointsYPermisos"/>: si un
    /// endpoint nuevo (o uno viejo que nadie migró a la fixture) queda afuera, no falla por
    /// omisión — el guardián queda en silencio. Este test invierte la mirada: recorre el
    /// <see cref="EndpointDataSource"/> real y exige que TODA ruta con al menos una policy de
    /// autorización (IAuthorizeData.Policy no nulo) esté declarada en la fixture, sin importar
    /// qué permiso le corresponda — esa verificación fina la hace el test de arriba.
    ///
    /// Quedan fuera del alcance, sin necesidad de lista de exclusión, los endpoints anónimos
    /// (ej. "/", "/licencia/estado", "/auth/reset-admin/*") y los que exigen autenticación pero
    /// sin policy concreta (RequireAuthorization() sin argumentos, ej. GET /auth/permisos y
    /// DELETE /finanzas/adjuntos/{id}) — ninguno de esos produce un IAuthorizeData con Policy
    /// no nulo, así que el filtro los descarta naturalmente.
    /// </summary>
    [Fact]
    public void TodoEndpointConPermisoDeclarado_EstaEnLaFixtureDelGuardian()
    {
        var endpointDataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var endpointsReales = endpointDataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        var declarados = EndpointsYPermisos
            .Select(e => (e.Metodo, Ruta: e.Ruta.TrimEnd('/')))
            .ToHashSet();

        var faltantes = new List<string>();

        foreach (var endpoint in endpointsReales)
        {
            var metodos = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (metodos is null)
                continue;

            var tienePermisoConcreto = endpoint.Metadata
                .OfType<IAuthorizeData>()
                .Any(a => a.Policy is not null);

            if (!tienePermisoConcreto)
                continue;

            var ruta = System.Text.RegularExpressions.Regex.Replace(
                endpoint.RoutePattern.RawText ?? string.Empty, @":[a-zA-Z]+(?=\}|\?)", string.Empty)
                .TrimEnd('/');

            foreach (var metodo in metodos)
            {
                if (!declarados.Contains((metodo, ruta)))
                    faltantes.Add($"{metodo} {ruta}");
            }
        }

        Assert.True(faltantes.Count == 0,
            $"Endpoints con permiso declarado pero ausentes de la fixture del guardián ({faltantes.Distinct().Count()}): " +
            string.Join("; ", faltantes.Distinct().OrderBy(f => f)));
    }
}
