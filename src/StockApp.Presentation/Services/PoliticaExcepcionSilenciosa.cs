using System;
using System.Linq;

namespace StockApp.Presentation.Services;

/// <summary>
/// Decide si una excepción que llegó a uno de los dos handlers GLOBALES de excepciones no
/// manejadas (Dispatcher.UIThread.UnhandledException en App.axaml.cs y
/// TaskScheduler.UnobservedTaskException en Program.cs) representa un 403/401 legítimo del
/// servidor — y por lo tanto debe tratarse en silencio: sin escribir en crash.log y sin
/// mostrar ningún mensaje nuevo (fix 2026-08-20, diagnóstico de los 16 casos reales de
/// "Ocurrió un error inesperado" en uso real).
///
/// La razón de fondo: <c>AuthTokenHandler.SendAsync</c> YA dispara
/// <c>ApiSession.AccesoRevocado</c> ante CUALQUIER 403 recibido, y App.axaml.cs YA muestra un
/// aviso al usuario en respuesta a ese evento (ver el comentario junto a
/// <c>apiSession.AccesoRevocado += ...</c>). Si el handler global TAMBIÉN loguea/avisa, el
/// usuario ve un crash espurio y potencialmente un segundo modal por el mismo 403. Ver también
/// el comentario en <c>ProveedorListViewModel.CargarAsync</c>, que documenta el mismo criterio
/// para los 18 ViewModels que ya atrapan <see cref="UnauthorizedAccessException"/> a mano.
///
/// <c>ApiErrores.AsegurarExitoAsync</c> es el ÚNICO nacimiento de
/// <see cref="UnauthorizedAccessException"/> del lado cliente (mapea 403 y 401), así que
/// detectar ese tipo alcanza para identificar el caso.
/// </summary>
public static class PoliticaExcepcionSilenciosa
{
    /// <summary>
    /// True si <paramref name="ex"/> es directamente un <see cref="UnauthorizedAccessException"/>,
    /// o si es un <see cref="AggregateException"/> cuyo árbol COMPLETO, una vez aplanado con
    /// <see cref="AggregateException.Flatten"/>, contiene AL MENOS una excepción y TODAS son
    /// <see cref="UnauthorizedAccessException"/>.
    ///
    /// Corrección 2026-08-20 (defecto real hallado por revisión adversarial): la versión
    /// original desenvolvía con <c>ex.InnerException</c>, que en <see cref="AggregateException"/>
    /// devuelve ÚNICAMENTE <c>InnerExceptions[0]</c> -- ignoraba el resto de la colección. Un
    /// <see cref="AggregateException"/> con [UnauthorizedAccessException, NullReferenceException]
    /// se silenciaba ENTERO, tragándose el NullReferenceException (un bug real) sin loguear ni
    /// informar -- y el resultado dependía del orden de inserción, que es azar. Ahora se exige
    /// que TODAS las excepciones aplanadas sean UnauthorizedAccessException: si hay aunque sea
    /// una que no lo es, se loguea normalmente (mejor loguear de más que tragarse un bug).
    ///
    /// <see cref="AggregateException.Flatten"/> hace falta de verdad (no alcanza con desenvolver
    /// manualmente la primera rama): puede haber una rama anidada, no la primera, que contenga
    /// una excepción que no sea UnauthorizedAccessException.
    ///
    /// Caso trampa: un <see cref="AggregateException"/> sin ninguna excepción interna
    /// (<c>InnerExceptions.Count == 0</c>) NO se considera acceso revocado -- <c>All()</c> de
    /// LINQ sobre una colección vacía da <c>true</c> por vacuidad, lo que invertiría el
    /// comportamiento (silenciaría un caso sin ninguna excepción real adentro). Por eso se exige
    /// explícitamente <c>Count &gt; 0</c> antes de mirar <c>All</c>.
    /// </summary>
    public static bool EsAccesoRevocado(Exception ex)
    {
        if (ex is not AggregateException aggregate)
            return ex is UnauthorizedAccessException;

        var planas = aggregate.Flatten().InnerExceptions;
        return planas.Count > 0 && planas.All(e => e is UnauthorizedAccessException);
    }
}
