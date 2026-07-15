using StockApp.Application.Licenciamiento;
using StockApp.Infrastructure.Platform;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>
/// Persiste la licencia como texto plano en `licencia.lic` dentro del directorio de datos.
/// Los updates de Velopack no tocan ese directorio, así que la licencia sobrevive upgrades.
/// </summary>
public sealed class AlmacenLicenciaArchivo : IAlmacenLicencia
{
    private readonly IUserDataPathProvider _paths;

    public AlmacenLicenciaArchivo(IUserDataPathProvider paths) => _paths = paths;

    public async Task<string?> LeerAsync()
    {
        var ruta = _paths.GetLicenciaPath();
        if (!File.Exists(ruta))
            return null;

        var contenido = (await File.ReadAllTextAsync(ruta)).Trim();
        return string.IsNullOrWhiteSpace(contenido) ? null : contenido;
    }

    public async Task GuardarAsync(string licencia)
    {
        var ruta = _paths.GetLicenciaPath();
        var dir = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(ruta, licencia.Trim());
    }
}
