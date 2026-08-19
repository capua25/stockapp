using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Sesion de prueba con rol Y permisos explicitos. Reemplaza los CurrentSessionFake duplicados
/// en GastosViewTests, PagosGastoViewTests, IngresosViewTests e InicioViewTests.
///
/// Usar SIEMPRE RolUsuario.Operador con permisos explicitos para testear un gate: Admin
/// cortocircuita el chequeo en AuthorizationService.cs:65-66 y el test pasa sin probar nada.
/// </summary>
internal sealed class SesionFake : ICurrentSession
{
    private readonly IReadOnlySet<string> _permisos;

    public SesionFake(RolUsuario rol, params string[] permisos)
    {
        RolActual = rol;
        _permisos = new HashSet<string>(permisos);
    }

    public bool EstaAutenticado => true;
    public UsuarioSesion? UsuarioActual => new(1, "prueba", RolActual!.Value, "Usuario de prueba");
    public RolUsuario? RolActual { get; }
    public IReadOnlySet<string> PermisosActuales => _permisos;

    public void EstablecerPermisos(IReadOnlySet<string> permisos) { }

    public void IniciarSesion(Usuario usuario)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public void CerrarSesion()
        => throw new NotSupportedException("No usado en este banco de pruebas.");
}

/// <summary>IInfoApp solo expone Version; ShellMainViewModel la usa para VersionTexto.</summary>
internal sealed class InfoAppFake : IInfoApp
{
    public InfoAppFake(string version = "0.0.0") => Version = version;

    public string Version { get; }
}

/// <summary>
/// Promovido desde las dos copias privadas identicas de InicioPanelTareasTests.cs:64-72 e
/// InicioViewTests.cs:67-75.
/// </summary>
internal sealed class AuthServiceFake : IAuthService
{
    public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task LogoutAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync()
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
}
