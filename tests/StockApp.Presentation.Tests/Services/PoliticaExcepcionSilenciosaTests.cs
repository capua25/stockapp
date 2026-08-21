using System;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Fix 2026-08-20 (crash.log ensuciado por 403/401 legítimos): verifica el predicado puro que
/// usan los dos handlers globales de excepciones no manejadas (Dispatcher.UIThread.
/// UnhandledException en App.axaml.cs y TaskScheduler.UnobservedTaskException en Program.cs)
/// para decidir si deben quedarse en silencio. Ver AppManejoExcepcionesGlobalesTests para la
/// verificación de que el handler REAL de App.axaml.cs usa este mismo predicado.
/// </summary>
public class PoliticaExcepcionSilenciosaTests
{
    [Fact]
    public void EsAccesoRevocado_ConUnauthorizedAccessExceptionDirecta_DevuelveTrue()
    {
        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(new UnauthorizedAccessException());

        Assert.True(resultado);
    }

    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionEnvolviendoUnauthorized_DevuelveTrue()
    {
        // Caso real de TaskScheduler.UnobservedTaskException: la Task de un [RelayCommand]
        // asíncrono falla con UnauthorizedAccessException y nadie observa la excepción.
        var envuelta = new AggregateException(new UnauthorizedAccessException());

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envuelta);

        Assert.True(resultado);
    }

    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionAnidadaEnvolviendoUnauthorized_DevuelveTrue()
    {
        var envueltaDosVeces = new AggregateException(
            new AggregateException(new UnauthorizedAccessException()));

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envueltaDosVeces);

        Assert.True(resultado);
    }

    [Fact]
    public void EsAccesoRevocado_ConOtraExcepcion_DevuelveFalse()
    {
        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(new InvalidOperationException("boom"));

        Assert.False(resultado);
    }

    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionEnvolviendoOtraExcepcion_DevuelveFalse()
    {
        var envuelta = new AggregateException(new InvalidOperationException("boom"));

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envuelta);

        Assert.False(resultado);
    }

    /// <summary>
    /// Defecto real encontrado por revisión adversarial (2026-08-20, post-fix): la implementación
    /// original desenvolvía con <c>ex.InnerException</c>, que en <see cref="AggregateException"/>
    /// devuelve ÚNICAMENTE <c>InnerExceptions[0]</c> -- ignora el resto de la colección. Con dos
    /// excepciones internas, [UnauthorizedAccessException, NullReferenceException], la política
    /// vieja silenciaba el AggregateException ENTERO (miraba solo la primera), tragándose un bug
    /// real (NullReferenceException) sin loguear ni informar. Este es el caso que hoy falla
    /// contra la implementación vieja.
    /// </summary>
    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionUnauthorizedYOtra_DevuelveFalse()
    {
        var envuelta = new AggregateException(
            new UnauthorizedAccessException(), new NullReferenceException());

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envuelta);

        Assert.False(resultado);
    }

    /// <summary>
    /// Mismo caso que el anterior con el orden invertido: prueba que la decisión NO depende de
    /// cuál excepción quedó primera en <c>InnerExceptions</c> (con el desenvolvimiento viejo,
    /// basado en <c>ex.InnerException</c>, este caso SÍ pasaba -- por eso el comportamiento era
    /// azar de orden de inserción, no una decisión real).
    /// </summary>
    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionOtraYUnauthorized_DevuelveFalseSinImportarElOrden()
    {
        var envuelta = new AggregateException(
            new NullReferenceException(), new UnauthorizedAccessException());

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envuelta);

        Assert.False(resultado);
    }

    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionDosUnauthorized_DevuelveTrue()
    {
        var envuelta = new AggregateException(
            new UnauthorizedAccessException(), new UnauthorizedAccessException());

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envuelta);

        Assert.True(resultado);
    }

    /// <summary>
    /// Trampa principal de este fix: "todas son Unauthorized" sobre una colección VACÍA da
    /// <c>true</c> por vacuidad si se usa LINQ <c>All()</c> ingenuamente -- invertiría el
    /// comportamiento (silenciaría un AggregateException sin ninguna excepción real adentro).
    /// El comportamiento correcto (y el que ya tenía la implementación vieja, sin querer) es
    /// <c>false</c>: si no hay nada que mirar, no hay nada que justifique el silencio.
    /// </summary>
    [Fact]
    public void EsAccesoRevocado_ConAggregateExceptionVacio_DevuelveFalse()
    {
        var vacia = new AggregateException();

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(vacia);

        Assert.False(resultado);
    }

    /// <summary>
    /// Prueba que hace falta <see cref="AggregateException.Flatten"/> de verdad y no alcanza con
    /// desenvolver manualmente la PRIMERA rama: la primera rama anidada es enteramente
    /// UnauthorizedAccessException, pero la SEGUNDA rama (no la primera) contiene una excepción
    /// que no lo es. Un desenvolvimiento que solo baja por el primer hijo (como
    /// <c>ex.InnerException</c>) nunca llegaría a ver la segunda rama.
    /// </summary>
    [Fact]
    public void EsAccesoRevocado_ConRamaAnidadaNoPrimeraQueNoEsUnauthorized_DevuelveFalse()
    {
        var ramaTodaUnauthorized = new AggregateException(new UnauthorizedAccessException());
        var ramaConOtra          = new AggregateException(new NullReferenceException());
        var envuelta = new AggregateException(ramaTodaUnauthorized, ramaConOtra);

        var resultado = PoliticaExcepcionSilenciosa.EsAccesoRevocado(envuelta);

        Assert.False(resultado);
    }
}
