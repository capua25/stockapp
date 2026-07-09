using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class PermisosTests
{
    [Fact]
    public void Todos_ContieneLasSeisConstantesExactas()
    {
        var esperados = new[]
        {
            Permisos.GestionarUsuarios,
            Permisos.VerReportes,
            Permisos.GestionarProductos,
            Permisos.GestionarTablasMaestras,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
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
