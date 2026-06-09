namespace StockApp.Application.Auth;

/// <summary>Contrato del servicio de primer arranque. Permite mockear PrimerArranqueService en tests.</summary>
public interface IPrimerArranqueService
{
    Task<bool> RequiereCrearAdminAsync();
    Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana);
}
