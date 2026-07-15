namespace StockApp.Infrastructure.Platform;

public class UserDataPathProvider : IUserDataPathProvider
{
    private const string AppName = "StockApp";
    private const string DbFileName = "stockapp.db";
    private const string BackupsSubdir = "backups";
    private const string LicenciaFileName = "licencia.lic";

    public string GetDataDirectory()
    {
        // Windows: %LOCALAPPDATA%\StockApp\
        // Linux:   ~/.local/share/StockApp/
        var baseDir = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(baseDir, AppName);
    }

    public string GetDatabasePath()
        => Path.Combine(GetDataDirectory(), DbFileName);

    public string GetBackupsDirectory()
        => Path.Combine(GetDataDirectory(), BackupsSubdir);

    public string GetLicenciaPath()
        => Path.Combine(GetDataDirectory(), LicenciaFileName);
}
