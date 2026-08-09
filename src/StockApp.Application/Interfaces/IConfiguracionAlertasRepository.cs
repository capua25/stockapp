using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IConfiguracionAlertasRepository
{
    /// <summary>
    /// La fila única de configuración. Si no existe (la base de tests la borra por CASCADE al
    /// truncar Usuarios), devuelve una instancia por defecto NO persistida: Habilitado = false
    /// y UrlWebhook = null. Nunca devuelve null.
    /// </summary>
    Task<ConfiguracionAlertas> ObtenerAsync();

    /// <summary>Upsert de la fila única: inserta con Id = 1 si no existe, actualiza si existe.</summary>
    Task GuardarAsync(ConfiguracionAlertas configuracion);
}
