using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Infrastructure.Tests.Platform;

public class UserDataPathProviderTests
{
    [Fact]
    public void GetDataDirectory_RetornaRutaQueContiene_StockApp()
    {
        var provider = new UserDataPathProvider();
        var path = provider.GetDataDirectory();

        Assert.Contains("StockApp", path);
    }

    [Fact]
    public void GetDataDirectory_RetornaRutaAbsoluta()
    {
        var provider = new UserDataPathProvider();
        var path = provider.GetDataDirectory();

        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void GetDatabasePath_TerminaEnArchivoDB()
    {
        var provider = new UserDataPathProvider();
        var dbPath = provider.GetDatabasePath();

        Assert.EndsWith(".db", dbPath);
    }

    [Fact]
    public void GetDatabasePath_EstaContenidoEnDataDirectory()
    {
        var provider = new UserDataPathProvider();
        var dbPath = provider.GetDatabasePath();
        var dataDir = provider.GetDataDirectory();

        Assert.StartsWith(dataDir, dbPath);
    }

    [Fact]
    public void GetBackupsDirectory_EstaContenidoEnDataDirectory()
    {
        var provider = new UserDataPathProvider();
        var backupsDir = provider.GetBackupsDirectory();
        var dataDir = provider.GetDataDirectory();

        Assert.StartsWith(dataDir, backupsDir);
    }
}
