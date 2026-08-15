using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Bug 2026-08-15: AuthTokenHandler dispara AccesoRevocado ante CUALQUIER 403, sin distinguir
/// "te revocaron el permiso mientras trabajabas" (mensaje actual, correcto) de "nunca tuviste
/// ese permiso" (mensaje actual, FALSO -- afirma un cambio que no ocurrió). AvisoAccesoRevocado
/// resuelve cuál mensaje corresponde comparando el snapshot local de permisos ANTES del refresco
/// best-effort contra el snapshot DESPUÉS (ver App.axaml.cs).
/// </summary>
public class AvisoAccesoRevocadoTests
{
    [Fact]
    public void Resolver_PermisosIguales_DevuelveMensajeSinPermiso()
    {
        var antes = new HashSet<string> { "finanzas.ver" };
        var despues = new HashSet<string> { "finanzas.ver" };

        var mensaje = AvisoAccesoRevocado.Resolver(antes, despues);

        Assert.Equal(AvisoAccesoRevocado.MensajeSinPermiso, mensaje);
    }

    [Fact]
    public void Resolver_AmbosVacios_DevuelveMensajeSinPermiso()
    {
        // Caso central del bug: un Operador con permisos mínimos (ej. solo finanzas.ver) toca
        // una sección para la que nunca tuvo acceso -- el refresco confirma el mismo conjunto
        // (vacío de ese permiso) antes y después. Nada cambió; el mensaje de "cambiaron tus
        // permisos" sería falso.
        var antes = new HashSet<string>();
        var despues = new HashSet<string>();

        var mensaje = AvisoAccesoRevocado.Resolver(antes, despues);

        Assert.Equal(AvisoAccesoRevocado.MensajeSinPermiso, mensaje);
    }

    [Fact]
    public void Resolver_PermisosDistintos_DevuelveMensajeCambiaron()
    {
        // Caso de revocación en caliente: el Admin le sacó un permiso al usuario mientras
        // la sesión seguía abierta -- el refresco trae un conjunto distinto (más chico).
        var antes = new HashSet<string> { "finanzas.ver", "tareas.gestionar" };
        var despues = new HashSet<string> { "finanzas.ver" };

        var mensaje = AvisoAccesoRevocado.Resolver(antes, despues);

        Assert.Equal(AvisoAccesoRevocado.MensajeCambiaron, mensaje);
    }

    [Fact]
    public void Resolver_RefrescoFalloYCacheLocalNoSeToco_DevuelveMensajeSinPermiso()
    {
        // Si el refresco best-effort falla (API caída), ApiSession.PermisosActuales no se
        // actualiza -- "antes" y "después" terminan siendo el MISMO objeto/contenido. No se
        // puede verificar un cambio, así que no se debe afirmar uno.
        var permisos = new HashSet<string> { "finanzas.ver" };

        var mensaje = AvisoAccesoRevocado.Resolver(permisos, permisos);

        Assert.Equal(AvisoAccesoRevocado.MensajeSinPermiso, mensaje);
    }

    [Fact]
    public void MensajeCambiaron_MencionaQuePasoYQueHacer()
    {
        Assert.Contains("cambiaron", AvisoAccesoRevocado.MensajeCambiaron);
        Assert.Contains("Administrador", AvisoAccesoRevocado.MensajeCambiaron);
    }

    [Fact]
    public void MensajeSinPermiso_NuncaAfirmaUnCambio()
    {
        Assert.DoesNotContain("cambiaron", AvisoAccesoRevocado.MensajeSinPermiso);
    }
}
