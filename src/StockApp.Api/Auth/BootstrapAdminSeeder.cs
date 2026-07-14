using StockApp.Application.Auth;

namespace StockApp.Api.Auth;

/// <summary>
/// Siembra el usuario Admin inicial en el arranque de la API cuando la base de datos no
/// tiene ningún usuario. Reemplaza al bootstrap HTTP anónimo (endpoints /auth/primer-arranque
/// y /auth/primer-admin, eliminados) que abría una ventana de "admin génesis" explotable en LAN.
/// Idempotente: si ya existe algún usuario, no hace nada y no lee la configuración.
/// Fail-fast: con la BD vacía y sin credenciales configuradas, lanza y la API no arranca
/// (mismo criterio que el fail-fast de Jwt:Secret).
/// </summary>
public sealed class BootstrapAdminSeeder
{
    private readonly IPrimerArranqueService _primerArranque;
    private readonly string? _adminUser;
    private readonly string? _adminPassword;

    public BootstrapAdminSeeder(
        IPrimerArranqueService primerArranque,
        string? adminUser,
        string? adminPassword)
    {
        _primerArranque = primerArranque;
        _adminUser = adminUser;
        _adminPassword = adminPassword;
    }

    public async Task SembrarAsync()
    {
        if (!await _primerArranque.RequiereCrearAdminAsync())
            return;

        if (string.IsNullOrWhiteSpace(_adminUser) || string.IsNullOrWhiteSpace(_adminPassword))
            throw new InvalidOperationException(
                "La base de datos no tiene usuarios y falta configurar el administrador inicial. " +
                "Definí 'Bootstrap:AdminUser' y 'Bootstrap:Password'. En desarrollo: " +
                "dotnet user-secrets set \"Bootstrap:AdminUser\" \"<usuario>\" y " +
                "dotnet user-secrets set \"Bootstrap:Password\" \"<contraseña-de-al-menos-6-caracteres>\".");

        // CrearAdminInicialAsync valida longitud de contraseña y nombre en blanco (ArgumentException),
        // y crea el Admin con el semáforo anti-TOCTOU. Si la contraseña es inválida, la excepción
        // burbujea y la API no arranca (fail-fast).
        await _primerArranque.CrearAdminInicialAsync(_adminUser, _adminPassword);
    }
}
