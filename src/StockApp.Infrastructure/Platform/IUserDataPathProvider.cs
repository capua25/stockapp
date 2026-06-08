namespace StockApp.Infrastructure.Platform;

public interface IUserDataPathProvider
{
    /// <summary>
    /// Retorna el directorio de datos del usuario para StockApp.
    /// Windows: %LOCALAPPDATA%\StockApp\
    /// Linux:   ~/.local/share/StockApp/
    /// </summary>
    string GetDataDirectory();

    /// <summary>Ruta completa al archivo .db de la base de datos.</summary>
    string GetDatabasePath();

    /// <summary>Ruta completa al subdirectorio de backups.</summary>
    string GetBackupsDirectory();
}
