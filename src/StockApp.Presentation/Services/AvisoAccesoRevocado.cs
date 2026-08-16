using System.Collections.Generic;

namespace StockApp.Presentation.Services;

/// <summary>
/// Bug 2026-08-15 (corrección sobre spec 2026-08-10): AuthTokenHandler dispara AccesoRevocado
/// ante CUALQUIER 403, sin distinguir dos situaciones muy distintas -- "te revocaron el permiso
/// mientras trabajabas" (el mensaje de "cambiaron tus permisos" es correcto y útil) de "nunca
/// tuviste ese permiso" (el mismo mensaje es FALSO: afirma un cambio que no ocurrió y manda al
/// Operador a buscar un culpable que no existe).
///
/// Resolver compara el snapshot local de permisos capturado ANTES del refresco best-effort
/// (App.axaml.cs, ApiSession.PermisosActuales) contra el snapshot de DESPUÉS de ese refresco:
/// - Si difieren, el Admin efectivamente cambió algo en caliente -- MensajeCambiaron.
/// - Si no difieren -- porque nunca cambió, o porque el refresco best-effort falló (API caída)
///   y el cache local ni se tocó -- no hay nada que verificar como "cambio", así que no se
///   afirma uno: MensajeSinPermiso, que es verdadero en cualquiera de los dos casos (el 403 ya
///   probó que el usuario no tiene acceso).
/// </summary>
public static class AvisoAccesoRevocado
{
    public const string MensajeCambiaron =
        "Tus permisos cambiaron mientras trabajabas y ya no tenés acceso a esta " +
        "sección. Si no lo esperabas, pedile a un Administrador que te oriente.";

    public const string MensajeSinPermiso =
        "No tenés permiso para esta operación.";

    public static string Resolver(IReadOnlySet<string> permisosAntes, IReadOnlySet<string> permisosDespues) =>
        permisosAntes.SetEquals(permisosDespues) ? MensajeSinPermiso : MensajeCambiaron;
}
