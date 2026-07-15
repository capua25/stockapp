using StockApp.Infrastructure.Licenciamiento;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Infrastructure.Tests.Licenciamiento;

public class AlmacenLicenciaArchivoTests : IDisposable
{
    private readonly string _dirTemp;
    private readonly IUserDataPathProvider _paths;

    public AlmacenLicenciaArchivoTests()
    {
        _dirTemp = Path.Combine(Path.GetTempPath(), "stockapp-lic-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_dirTemp);
        _paths = new PathsFake(_dirTemp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dirTemp)) Directory.Delete(_dirTemp, recursive: true);
    }

    private sealed class PathsFake : IUserDataPathProvider
    {
        private readonly string _dir;
        public PathsFake(string dir) => _dir = dir;
        public string GetDataDirectory() => _dir;
        public string GetDatabasePath() => Path.Combine(_dir, "stockapp.db");
        public string GetBackupsDirectory() => Path.Combine(_dir, "backups");
        public string GetLicenciaPath() => Path.Combine(_dir, "licencia.lic");
    }

    [Fact]
    public async Task LeerAsync_SinArchivo_DevuelveNull()
    {
        var almacen = new AlmacenLicenciaArchivo(_paths);

        Assert.Null(await almacen.LeerAsync());
    }

    [Fact]
    public async Task GuardarAsync_LuegoLeerAsync_DevuelveLoGuardado()
    {
        var almacen = new AlmacenLicenciaArchivo(_paths);

        await almacen.GuardarAsync("payload.firma");

        Assert.Equal("payload.firma", await almacen.LeerAsync());
    }

    [Fact]
    public async Task GuardarAsync_SobrescribeLicenciaAnterior()
    {
        var almacen = new AlmacenLicenciaArchivo(_paths);

        await almacen.GuardarAsync("vieja");
        await almacen.GuardarAsync("nueva");

        Assert.Equal("nueva", await almacen.LeerAsync());
    }

    [Fact]
    public async Task GuardarAsync_CreaElDirectorioSiNoExiste()
    {
        var subdir = Path.Combine(_dirTemp, "no", "existe");
        var almacen = new AlmacenLicenciaArchivo(new PathsFake(subdir));

        await almacen.GuardarAsync("x.y");

        Assert.Equal("x.y", await almacen.LeerAsync());
    }
}
