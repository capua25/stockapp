namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Constante embebida con la clave pública del desarrollador para validar licencias/tokens.
/// El valor por defecto es un PLACEHOLDER: se reemplaza por la clave real que imprime
/// `StockApp.Licencias.Cli generar-claves` durante la puesta en producción. En runtime se
/// puede sobrescribir con la config `Licencia:ClavePublicaBase64` (los tests inyectan la
/// clave pública de prueba por ahí). Con el placeholder, toda licencia falla la verificación
/// (fail-closed) y la API queda bloqueada — es el comportamiento correcto hasta pegar la real.
/// </summary>
public static class OpcionesLicencia
{
    public const string ClavePublicaBase64Default = "REEMPLAZAR-CON-CLAVE-PUBLICA-GENERADA-POR-LA-CLI";
}
