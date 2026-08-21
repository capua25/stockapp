using System;
using Moq;
using StockApp.ApiClient;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Arranque;

/// <summary>
/// Fix 2026-08-20 (crash.log ensuciado por 403/401 legítimos, 16 casos reales entre 2026-08-01
/// y 2026-08-15): <see cref="StockApp.Presentation.App.ManejarExcepcionUiThread"/> es el cuerpo
/// EXTRAÍDO del handler real de <c>Dispatcher.UIThread.UnhandledException</c> en App.axaml.cs —
/// la lambda de producción ahora es un one-liner que resuelve las dos dependencias por DI y
/// delega acá, sin lógica propia que pueda divergir de lo que este archivo verifica (mismo
/// criterio de seam que <c>App.ResolverApiBaseUrl</c>/<c>App.ConstruirConfiguracion</c>, ver
/// ResolucionApiBaseUrlTests). La decisión de silencio la toma
/// <see cref="PoliticaExcepcionSilenciosa"/> (ver su propia suite); acá se verifica el
/// comportamiento COMPLETO: si loguea, si informa, y con qué mensaje.
/// </summary>
public class ManejoExcepcionesGlobalesTests
{
    private const string MensajeGenerico =
        "Ocurrió un error inesperado. Podés seguir usando la aplicación; " +
        "si el problema persiste, contactá a soporte.";

    [Fact]
    public void ManejarExcepcionUiThread_ConUnauthorizedAccessException_NoLogueaNiInforma()
    {
        var registro = new RegistroFallosEnMemoria();
        var confirmMock = new Mock<IConfirmacionService>();

        StockApp.Presentation.App.ManejarExcepcionUiThread(
            new UnauthorizedAccessException(), registro, confirmMock.Object);

        Assert.Empty(registro.Entradas);
        confirmMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ManejarExcepcionUiThread_ConAggregateExceptionEnvolviendoUnauthorized_NoLogueaNiInforma()
    {
        var registro = new RegistroFallosEnMemoria();
        var confirmMock = new Mock<IConfirmacionService>();

        StockApp.Presentation.App.ManejarExcepcionUiThread(
            new AggregateException(new UnauthorizedAccessException()), registro, confirmMock.Object);

        Assert.Empty(registro.Entradas);
        confirmMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ManejarExcepcionUiThread_ConOtraExcepcion_LogueaConOrigenUIThreadEInformaElMensajeGenerico()
    {
        var registro = new RegistroFallosEnMemoria();
        var confirmMock = new Mock<IConfirmacionService>();
        var ex = new InvalidOperationException("boom");

        StockApp.Presentation.App.ManejarExcepcionUiThread(ex, registro, confirmMock.Object);

        var entrada = Assert.Single(registro.Entradas);
        Assert.Equal("UIThread", entrada.Origen);
        Assert.Same(ex, entrada.Ex);
        confirmMock.Verify(c => c.InformarAsync(MensajeGenerico), Times.Once);
    }

    [Fact]
    public void ManejarExcepcionUiThread_ConServidorNoDisponibleException_InformaSuMensajeAccionableEnVezDelGenerico()
    {
        var registro = new RegistroFallosEnMemoria();
        var confirmMock = new Mock<IConfirmacionService>();
        var ex = new ServidorNoDisponibleException();

        StockApp.Presentation.App.ManejarExcepcionUiThread(ex, registro, confirmMock.Object);

        Assert.Single(registro.Entradas);
        confirmMock.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
        confirmMock.Verify(c => c.InformarAsync(MensajeGenerico), Times.Never);
    }

    [Fact]
    public void ManejarExcepcionUiThread_SinConfirmacionDisponible_NoLanzaYSigueLogueando()
    {
        var registro = new RegistroFallosEnMemoria();
        var ex = new InvalidOperationException("boom");

        var excepcion = Record.Exception(() =>
            StockApp.Presentation.App.ManejarExcepcionUiThread(ex, registro, confirmacion: null));

        Assert.Null(excepcion);
        Assert.Single(registro.Entradas);
    }
}
