using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class ApiSessionTests
{
    [Fact]
    public void SinSesion_NoEstaAutenticado()
    {
        var session = new ApiSession();

        Assert.False(session.EstaAutenticado);
        Assert.Null(session.UsuarioActual);
        Assert.Null(session.RolActual);
        Assert.Null(session.Token);
    }

    [Fact]
    public void EstablecerSesion_GuardaSnapshotYToken()
    {
        var session = new ApiSession();

        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, "Ana Admin"), "tok-123");

        Assert.True(session.EstaAutenticado);
        Assert.Equal("admin", session.UsuarioActual!.NombreUsuario);
        Assert.Equal("Ana Admin", session.UsuarioActual.NombreCompleto);
        Assert.Equal(RolUsuario.Admin, session.RolActual);
        Assert.Equal("tok-123", session.Token);
    }

    [Fact]
    public void CerrarSesion_LimpiaSnapshotYToken()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, null), "tok-123");

        session.CerrarSesion();

        Assert.False(session.EstaAutenticado);
        Assert.Null(session.UsuarioActual);
        Assert.Null(session.Token);
    }

    [Fact]
    public void IniciarSesion_ProyectaLaEntidadAUnSnapshot_SinToken()
    {
        // Miembro del contrato ICurrentSession: en modo API el login usa EstablecerSesion,
        // pero la implementación se mantiene funcional (misma proyección que InMemorySession).
        var session = new ApiSession();
        var usuario = new Usuario { Id = 2, NombreUsuario = "oper", Rol = RolUsuario.Operador };

        session.IniciarSesion(usuario);

        Assert.True(session.EstaAutenticado);
        Assert.Equal("oper", session.UsuarioActual!.NombreUsuario);
        Assert.Equal(RolUsuario.Operador, session.RolActual);
        Assert.Null(session.Token);
    }

    [Fact]
    public void DispararSesionVencida_InvocaElEvento()
    {
        var session = new ApiSession();
        var disparado = false;
        session.SesionVencida += () => disparado = true;

        session.DispararSesionVencida();

        Assert.True(disparado);
    }

    [Fact]
    public void DispararSesionVencida_SinSuscriptores_NoLanza()
    {
        var session = new ApiSession();

        session.DispararSesionVencida();
    }

    [Fact]
    public void PermisosActuales_SinSesion_DevuelveConjuntoVacio()
    {
        var session = new ApiSession();

        Assert.Empty(session.PermisosActuales);
    }

    [Fact]
    public void EstablecerPermisos_PueblaPermisosActuales()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "operador", RolUsuario.Operador, null), "token123");

        session.EstablecerPermisos(new HashSet<string> { "finanzas.ver" });

        Assert.Contains("finanzas.ver", session.PermisosActuales);
    }

    [Fact]
    public void CerrarSesion_LimpiaTambienLosPermisos()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "operador", RolUsuario.Operador, null), "token123");
        session.EstablecerPermisos(new HashSet<string> { "finanzas.ver" });

        session.CerrarSesion();

        Assert.Empty(session.PermisosActuales);
    }

    [Fact]
    public void DispararAccesoRevocado_InvocaElEvento()
    {
        // DispararAccesoRevocado es internal — visible acá porque StockApp.ApiClient.csproj
        // declara InternalsVisibleTo hacia StockApp.ApiClient.Tests (mismo mecanismo que ya
        // usan los tests de SesionVencida/LicenciaDesactivada en AuthTokenHandlerTests.cs).
        var session = new ApiSession();
        var disparado = false;
        session.AccesoRevocado += () => disparado = true;

        session.DispararAccesoRevocado();

        Assert.True(disparado);
    }
}
