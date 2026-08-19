using System.Collections.Generic;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El fake de sesion es la base de toda la red de seguridad de permisos: si miente, los tests de
/// gate dan falsos verdes en masa. TareaSessionFake devolvia SIEMPRE un set vacio de permisos, y
/// por eso los tests de Documentos montaban con Admin — el unico rol que producia gates en true.
/// </summary>
public class SesionFakesTests
{
    [Fact]
    public void SesionFake_ConPermisosExplicitos_LosDevuelve()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos);

        Assert.Equal(RolUsuario.Operador, sesion.RolActual);
        Assert.Contains(Permisos.VerFinanzas, sesion.PermisosActuales);
        Assert.Contains(Permisos.RegistrarGastos, sesion.PermisosActuales);
        Assert.DoesNotContain(Permisos.GestionarProductos, sesion.PermisosActuales);
    }

    [Fact]
    public void SesionFake_SinPermisos_DevuelveSetVacioPeroUsuarioValido()
    {
        var sesion = new SesionFake(RolUsuario.Operador);

        Assert.True(sesion.EstaAutenticado);
        Assert.NotNull(sesion.UsuarioActual);
        Assert.Empty(sesion.PermisosActuales);
    }

    [Fact]
    public void SesionFake_ComoAdmin_NoNecesitaPermisosExplicitos()
    {
        // Admin cortocircuita el chequeo en AuthorizationService.cs:65-66. El fake refleja eso:
        // el rol es lo que importa, la lista queda vacia a proposito.
        var sesion = new SesionFake(RolUsuario.Admin);

        Assert.Equal(RolUsuario.Admin, sesion.RolActual);
        Assert.Empty(sesion.PermisosActuales);
    }
}
