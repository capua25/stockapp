namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de validar un token de reset de Admin (spec §5.1, §7). Sin excepciones para flujo.</summary>
public enum ResultadoValidacionReset
{
    Valido,
    FormatoInvalido,
    FirmaInvalido,
    MaquinaDistinta,
    AccionInvalida,
    DesafioInvalido,
    DesafioExpirado,
}
