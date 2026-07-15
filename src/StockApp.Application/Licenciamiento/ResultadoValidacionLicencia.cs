namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de validar una licencia contra esta máquina (spec §7). Sin excepciones para flujo.</summary>
public enum ResultadoValidacionLicencia
{
    Valida,
    FormatoInvalido,
    FirmaInvalida,
    MaquinaDistinta,
}
