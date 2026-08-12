using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class PermisosTests
{
    [Fact]
    public void Todos_ContieneLasDiecisieteConstantesExactas()
    {
        var esperados = new[]
        {
            Permisos.GestionarUsuarios,
            Permisos.VerReportes,
            Permisos.GestionarProductos,
            Permisos.GestionarTablasMaestras,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
            Permisos.VerFinanzas,
            Permisos.GestionarMaestrosFinanzas,
            Permisos.RegistrarGastos,
            Permisos.RegistrarPagos,
            Permisos.RegistrarIngresos,
            Permisos.ImportarPlanillas,
            Permisos.GestionarDiagnostico,
            Permisos.GestionarTareas,
            Permisos.AdministrarTareas,
            Permisos.GestionarDocumentos,
            Permisos.AdministrarDocumentos,
        };

        Assert.Equal(esperados.Length, Permisos.Todos.Count);
        foreach (var permiso in esperados)
            Assert.Contains(permiso, Permisos.Todos);
    }

    [Fact]
    public void Todos_NoTieneDuplicados()
    {
        Assert.Equal(Permisos.Todos.Count, Permisos.Todos.Distinct().Count());
    }
}
