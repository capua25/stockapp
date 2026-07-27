using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Reemplaza a UserDataPathProvider en los tests de integración: sin este fake,
/// BackupsEndpoints (que resuelve GetBackupsDirectory() para leer archivos) apuntaría al
/// directorio REAL de datos de usuario de la máquina que corre los tests (%LOCALAPPDATA%\StockApp\
/// / ~/.local/share/StockApp/), ensuciando el filesystem real. Directorio temporal único por
/// instancia de ApiFactory (compartida por toda la colección "Api") — mismo criterio que
/// AlmacenLicenciaEnMemoria reemplaza el almacén real de licencia.
/// </summary>
public sealed class UserDataPathProviderFake : IUserDataPathProvider
{
    private readonly string _directorioDatos =
        Path.Combine(Path.GetTempPath(), "StockAppApiTests_" + Guid.NewGuid());

    public string GetDataDirectory() => _directorioDatos;
    public string GetDatabasePath() => Path.Combine(_directorioDatos, "stockapp.db");
    public string GetBackupsDirectory() => Path.Combine(_directorioDatos, "backups");
    public string GetLicenciaPath() => Path.Combine(_directorioDatos, "licencia.lic");
}
