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

        ("POST",   "/movimientos/ingreso-factura", Permisos.RegistrarMovimientos),
        ("POST",   "/movimientos/ingreso-factura/{gastoId}/anular", Permisos.RegistrarMovimientos),

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
        ("POST",   "/tareas/{id}/tomar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/soltar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/terminar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/cancelar", Permisos.AdministrarTareas),
        ("POST",   "/tareas/{id}/prioridad", Permisos.AdministrarTareas),
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
}
