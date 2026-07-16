using System;
using System.IO;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Verifica la persistencia local (JSON) del estado de la ventana principal: round-trip,
/// robustez ante archivo corrupto y creación del directorio si falta.
/// </summary>
public class ServicioEstadoVentanaTests : IDisposable
{
    private readonly string _rutaTemporal;

    public ServicioEstadoVentanaTests()
    {
        _rutaTemporal = Path.Combine(
            Path.GetTempPath(),
            $"stockapp-ventana-tests-{Guid.NewGuid():N}",
            "ventana.json");
    }

    public void Dispose()
    {
        var carpeta = Path.GetDirectoryName(_rutaTemporal);
        if (carpeta is not null && Directory.Exists(carpeta))
            Directory.Delete(carpeta, recursive: true);
    }

    [Fact]
    public void Cargar_SinArchivoPrevio_DevuelveNull()
    {
        var servicio = new ServicioEstadoVentana(_rutaTemporal);

        var resultado = servicio.Cargar();

        Assert.Null(resultado);
    }

    [Fact]
    public void GuardarYCargar_RoundTrip_DevuelveElMismoEstado()
    {
        var servicio = new ServicioEstadoVentana(_rutaTemporal);
        var estado = new EstadoVentana
        {
            Ancho = 1024,
            Alto = 768,
            X = 100,
            Y = 50,
            Maximizada = false,
        };

        servicio.Guardar(estado);
        var recuperado = servicio.Cargar();

        Assert.Equal(estado, recuperado);
    }

    [Fact]
    public void Guardar_DirectorioInexistente_LoCreaYPersiste()
    {
        var servicio = new ServicioEstadoVentana(_rutaTemporal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(_rutaTemporal)));

        servicio.Guardar(new EstadoVentana { Ancho = 800, Alto = 600, X = 0, Y = 0, Maximizada = true });

        Assert.True(Directory.Exists(Path.GetDirectoryName(_rutaTemporal)));
        Assert.True(File.Exists(_rutaTemporal));
    }

    [Fact]
    public void Cargar_ArchivoCorrupto_DevuelveNull()
    {
        var carpeta = Path.GetDirectoryName(_rutaTemporal)!;
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(_rutaTemporal, "{ esto no es json válido ");

        var servicio = new ServicioEstadoVentana(_rutaTemporal);
        var resultado = servicio.Cargar();

        Assert.Null(resultado);
    }

    [Fact]
    public void GuardarYCargar_Maximizada_PersisteElFlag()
    {
        var servicio = new ServicioEstadoVentana(_rutaTemporal);
        var estado = new EstadoVentana { Ancho = 1280, Alto = 720, X = 10, Y = 20, Maximizada = true };

        servicio.Guardar(estado);
        var recuperado = servicio.Cargar();

        Assert.NotNull(recuperado);
        Assert.True(recuperado!.Maximizada);
    }
}
