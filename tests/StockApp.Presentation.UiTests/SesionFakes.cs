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
/// Sesion de prueba con rol Y permisos explicitos.
///
/// Reemplaza el CurrentSessionFake privado no-op de GastosViewTests, IngresosViewTests,
/// PagosGastoViewTests (Task 8.0 de la Fase B), InicioViewTests e InicioPanelTareasTests, y
/// TareaSessionFake de TareaFakes.cs (Task 13.3 de B3, ya cerrada). La migracion real,
/// verificada con grep sobre EstablecerPermisos sin resolver contra codigo viejo (Ruling B-19),
/// encontro SEIS archivos con un EstablecerPermisos no-op; los seis quedaron migrados a este
/// fake. Cero EstablecerPermisos no-op en tests/StockApp.Presentation.UiTests.
///
/// Usar SIEMPRE RolUsuario.Operador con permisos explicitos para testear un gate: Admin
/// cortocircuita el chequeo en AuthorizationService.cs:65-66 y el test pasa sin probar nada.
/// </summary>
internal sealed class SesionFake : ICurrentSession
{
    private readonly UsuarioSesion? _usuario;
    private IReadOnlySet<string> _permisos;

    public SesionFake(RolUsuario rol, params string[] permisos)
    {
        RolActual = rol;
        _permisos = new HashSet<string>(permisos);
    }

    /// <summary>
    /// Sobrecarga para los bancos de prueba que necesitan un UsuarioSesion con nombre real (la
    /// vista lo muestra: InicioViewModel deriva Saludo y RolTexto de el, Task 13.3). El
    /// constructor de rol suelto hardcodea (1, "prueba", ..., "Usuario de prueba"), que sirve
    /// para gates pero no para texto.
    /// </summary>
    public SesionFake(UsuarioSesion usuario, params string[] permisos)
    {
        _usuario = usuario;
        RolActual = usuario.Rol;
        _permisos = new HashSet<string>(permisos);
    }

    public bool EstaAutenticado => true;
    public UsuarioSesion? UsuarioActual => _usuario ?? new(1, "prueba", RolActual!.Value, "Usuario de prueba");
    public RolUsuario? RolActual { get; }
    public IReadOnlySet<string> PermisosActuales => _permisos;

    /// <summary>
    /// Ruling 6: antes era un no-op. Ahora aplica de verdad el set recibido, igual que
    /// ApiSession/InMemorySession en producción, para poder simular una revocación de permiso
    /// en caliente (AuthServiceFake la llama tras "refrescar" desde el servidor).
    /// </summary>
    public void EstablecerPermisos(IReadOnlySet<string> permisos) => _permisos = permisos;

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
    private readonly ICurrentSession? _session;
    private readonly IReadOnlySet<string>? _permisosARefrescar;

    public AuthServiceFake()
    {
    }

    /// <summary>
    /// Ruling 6: variante que simula el refresco real de permisos. AuthApiClient.ObtenerPermisosPropiosAsync
    /// (producción) llama _session.EstablecerPermisos(permisos) como efecto de borde tras pegarle
    /// al servidor; este fake reproduce exactamente eso para poder simular una revocación en
    /// caliente sin un ApiClient real.
    /// </summary>
    public AuthServiceFake(ICurrentSession session, IReadOnlySet<string> permisosARefrescar)
    {
        _session = session;
        _permisosARefrescar = permisosARefrescar;
    }

    public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task LogoutAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync()
    {
        var permisos = _permisosARefrescar ?? new HashSet<string>();
        _session?.EstablecerPermisos(permisos);
        return Task.FromResult(permisos);
    }
}

/// <summary>
/// Fake escrito a mano (StockApp.Presentation.UiTests no tiene Moq) del sexto parametro de
/// ShellMainViewModel (Task 5.2). Siempre arranca sin preferencias guardadas: los 11 tests de
/// ShellMainViewGatesTests.cs prueban gates de permiso, no persistencia de expansion de grupos —
/// esa persistencia ya tiene su propia cobertura en ServicioPreferenciasSidebarTests.cs y
/// ShellMainViewModelGruposTests.cs.
/// </summary>
internal sealed class PreferenciasSidebarFake : IServicioPreferenciasSidebar
{
    public PreferenciasSidebar? Cargar() => null;

    public void Guardar(PreferenciasSidebar preferencias) { }
}
