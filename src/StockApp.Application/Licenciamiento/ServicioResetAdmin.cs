using System.Linq;
using System.Text.Json;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Valida un token de reset firmado y, si es correcto, resetea la contraseña del Admin (o lo
/// recrea si no queda ninguno) y audita. SCOPED: audita y toca el repositorio.
/// Propiedades: un solo uso (el desafío muere al usarse), no transferible (atado al fingerprint),
/// no pre-generable (el desafío nace en esta máquina en ese momento).
/// </summary>
public sealed class ServicioResetAdmin
{
    private const string AccionEsperada = "reset-admin";

    private readonly ValidadorFirma        _validador;
    private readonly IFingerprintMaquina   _fingerprint;
    private readonly IAlmacenDesafiosReset _desafios;
    private readonly IUsuarioRepository    _usuarios;
    private readonly IPasswordHasher       _hasher;
    private readonly IAuditLogger          _audit;

    public ServicioResetAdmin(
        ValidadorFirma         validador,
        IFingerprintMaquina    fingerprint,
        IAlmacenDesafiosReset  desafios,
        IUsuarioRepository     usuarios,
        IPasswordHasher        hasher,
        IAuditLogger           audit)
    {
        _validador      = validador;
        _fingerprint    = fingerprint;
        _desafios       = desafios;
        _usuarios       = usuarios;
        _hasher         = hasher;
        _audit          = audit;
    }

    public async Task<ResultadoValidacionReset> ResetearAsync(string token, string nuevaContrasena)
    {
        var verificacion = _validador.Verificar(token, out var payloadJson);
        if (verificacion == ResultadoVerificacion.FormatoInvalido)
            return ResultadoValidacionReset.FormatoInvalido;
        if (verificacion == ResultadoVerificacion.FirmaInvalida)
            return ResultadoValidacionReset.FirmaInvalido;

        TokenResetPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenResetPayload>(payloadJson);
        }
        catch (JsonException)
        {
            return ResultadoValidacionReset.FormatoInvalido;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Desafio))
            return ResultadoValidacionReset.FormatoInvalido;

        if (payload.Accion != AccionEsperada)
            return ResultadoValidacionReset.AccionInvalida;

        if (payload.Maquina != _fingerprint.CodigoAgrupado)
            return ResultadoValidacionReset.MaquinaDistinta;

        // La contraseña se valida ANTES de consumir el desafío: si es inválida, no quemamos
        // el nonce. ArgumentException burbujea al endpoint (400 vía DomainExceptionHandler).
        ContrasenaValidator.Validar(nuevaContrasena);

        var consumo = _desafios.Consumir(payload.Desafio);
        if (consumo == ResultadoDesafio.Inexistente)
            return ResultadoValidacionReset.DesafioInvalido;
        if (consumo == ResultadoDesafio.Expirado)
            return ResultadoValidacionReset.DesafioExpirado;

        var adminId = await AplicarResetAsync(nuevaContrasena);

        await _audit.RegistrarAsync(
            adminId, AccionAuditada.ResetAdminFirmado, "Usuario", adminId,
            "Reset de contraseña de Admin vía token firmado.");

        return ResultadoValidacionReset.Valido;
    }

    private async Task<int> AplicarResetAsync(string nuevaContrasena)
    {
        var todos = await _usuarios.ListarTodosAsync();
        var admin = todos
            .Where(u => u.Rol == RolUsuario.Admin)
            .OrderBy(u => u.Id)
            .FirstOrDefault();

        if (admin is not null)
        {
            admin.HashContrasena = _hasher.Hash(nuevaContrasena);
            admin.Activo = true; // recuperación: si estaba deshabilitado, se reactiva
            await _usuarios.ActualizarAsync(admin);
            return admin.Id;
        }

        // No queda ningún Admin. No se puede delegar en PrimerArranqueService.CrearAdminInicialAsync:
        // esa ruta chequea "no existe NINGÚN usuario" (RequiereCrearAdminAsync), y falla si quedan
        // Operadores sin Admins — justamente el caso de borde que este endpoint de último recurso
        // tiene que poder resolver. Se crea el Admin directo con el mismo mecanismo interno
        // (repositorio + hasher), sin pasar por esa precondición. La contraseña ya fue validada
        // en ResetearAsync antes de consumir el desafío.
        var nuevoAdmin = new Usuario
        {
            NombreUsuario  = "admin",
            HashContrasena = _hasher.Hash(nuevaContrasena),
            Rol            = RolUsuario.Admin,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow,
        };
        var id = await _usuarios.AgregarAsync(nuevoAdmin);
        return id;
    }
}
