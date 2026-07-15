using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Orquesta la licencia: verifica firma → deserializa → compara la máquina → decide estado.
/// SCOPED en la API (lo consumen los endpoints de licencia). El estado cacheado (singleton)
/// es lo que el middleware lee; este servicio lo actualiza al arrancar y al activar.
/// </summary>
public sealed class ServicioLicencia
{
    private readonly ValidadorFirma            _validador;
    private readonly IFingerprintMaquina       _fingerprint;
    private readonly IAlmacenLicencia          _almacen;
    private readonly EstadoLicencia            _estado;
    private readonly ILogger<ServicioLicencia> _logger;

    public ServicioLicencia(
        ValidadorFirma            validador,
        IFingerprintMaquina       fingerprint,
        IAlmacenLicencia          almacen,
        EstadoLicencia            estado,
        ILogger<ServicioLicencia> logger)
    {
        _validador   = validador;
        _fingerprint = fingerprint;
        _almacen     = almacen;
        _estado      = estado;
        _logger      = logger;
    }

    /// <summary>Valida una licencia contra esta máquina (puro, sin efectos).</summary>
    public ResultadoValidacionLicencia Validar(string licencia)
    {
        var verificacion = _validador.Verificar(licencia, out var payloadJson);
        if (verificacion == ResultadoVerificacion.FormatoInvalido)
            return ResultadoValidacionLicencia.FormatoInvalido;
        if (verificacion == ResultadoVerificacion.FirmaInvalida)
            return ResultadoValidacionLicencia.FirmaInvalida;

        LicenciaPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicenciaPayload>(payloadJson);
        }
        catch (JsonException)
        {
            return ResultadoValidacionLicencia.FormatoInvalido;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Maquina))
            return ResultadoValidacionLicencia.FormatoInvalido;

        return payload.Maquina == _fingerprint.CodigoAgrupado
            ? ResultadoValidacionLicencia.Valida
            : ResultadoValidacionLicencia.MaquinaDistinta;
    }

    /// <summary>Valida y, si es válida, persiste la licencia y activa el estado cacheado.</summary>
    public async Task<ResultadoValidacionLicencia> ActivarAsync(string licencia)
    {
        var resultado = Validar(licencia);
        if (resultado != ResultadoValidacionLicencia.Valida)
            return resultado;

        await _almacen.GuardarAsync(licencia);
        _estado.CodigoMaquina = _fingerprint.CodigoAgrupado;
        _estado.Activada = true;
        return ResultadoValidacionLicencia.Valida;
    }

    /// <summary>
    /// Al arranque: resuelve el código de máquina y valida la licencia guardada. Si el
    /// fingerprint es ilegible (registro/machine-id inaccesible), deja el estado bloqueado
    /// con código vacío — NUNCA lanza (spec §7: la API arranca igual, en modo bloqueado).
    /// </summary>
    public async Task CargarAlArranqueAsync()
    {
        string codigo;
        try
        {
            codigo = _fingerprint.CodigoAgrupado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer el fingerprint de la máquina; la API queda en modo bloqueado");
            _estado.CodigoMaquina = "";
            _estado.Activada = false;
            return;
        }

        _estado.CodigoMaquina = codigo;

        var licencia = await _almacen.LeerAsync();
        _estado.Activada = licencia is not null
            && Validar(licencia) == ResultadoValidacionLicencia.Valida;
    }
}
