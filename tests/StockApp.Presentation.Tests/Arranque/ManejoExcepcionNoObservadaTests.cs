using System;
using StockApp.Presentation.Tests;
using Xunit;

namespace StockApp.Presentation.Tests.Arranque;

/// <summary>
/// Fix 2026-08-20: <see cref="Program.ManejarExcepcionNoObservada"/> es el cuerpo extraído del
/// handler real de <c>TaskScheduler.UnobservedTaskException</c> (Program.cs, Main). A diferencia
/// del handler de Dispatcher.UIThread (ver ManejoExcepcionesGlobalesTests), este NUNCA muestra
/// un mensaje al usuario -- solo decide si loguea o no. Recibe IRegistroFallos explícito (no usa
/// el <c>Program.LogFatal</c> de un solo parámetro, que instancia un
/// <c>RegistroFallosArchivo</c> real): sin esto, correr este test escribiría en el crash.log
/// real del usuario (mismo problema de fondo que TestBootstrap/RegistroFallosEnMemoria ya
/// resuelven para RefrescoPermisos).
///
/// El mecanismo por el cual TaskScheduler.UnobservedTaskException entrega la excepción de un
/// [RelayCommand] asíncrono fallido envuelta en un AggregateException NO está explicado en el
/// diagnóstico original -- se documenta acá solo como dato observado, no como explicación.
/// </summary>
public class ManejoExcepcionNoObservadaTests
{
    [Fact]
    public void ManejarExcepcionNoObservada_ConAggregateExceptionEnvolviendoUnauthorized_NoLoguea()
    {
        var registro = new RegistroFallosEnMemoria();
        var envuelta = new AggregateException(new UnauthorizedAccessException());

        StockApp.Presentation.Program.ManejarExcepcionNoObservada("UnobservedTask", envuelta, registro);

        Assert.Empty(registro.Entradas);
    }

    [Fact]
    public void ManejarExcepcionNoObservada_ConOtraExcepcion_LogueaConElOrigenRecibido()
    {
        var registro = new RegistroFallosEnMemoria();
        var ex = new InvalidOperationException("boom");

        StockApp.Presentation.Program.ManejarExcepcionNoObservada("UnobservedTask", ex, registro);

        var entrada = Assert.Single(registro.Entradas);
        Assert.Equal("UnobservedTask", entrada.Origen);
        Assert.Same(ex, entrada.Ex);
    }

    [Fact]
    public void ManejarExcepcionNoObservada_ConAggregateExceptionEnvolviendoOtraExcepcion_Loguea()
    {
        var registro = new RegistroFallosEnMemoria();
        var envuelta = new AggregateException(new InvalidOperationException("boom"));

        StockApp.Presentation.Program.ManejarExcepcionNoObservada("UnobservedTask", envuelta, registro);

        var entrada = Assert.Single(registro.Entradas);
        Assert.Same(envuelta, entrada.Ex);
    }
}
