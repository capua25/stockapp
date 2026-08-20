using System;
using System.IO;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Verifica RegistroFallosArchivo (implementación de producción de IRegistroFallos, fix
/// 2026-08-20): formato de la entrada escrita y la rotación simple que evita que crash.log
/// crezca sin techo (4 MB / 5175 entradas acumuladas en 3 semanas de uso real, sin rotación).
/// </summary>
public class RegistroFallosArchivoTests : IDisposable
{
    private readonly string _logPath;

    public RegistroFallosArchivoTests()
    {
        _logPath = Path.Combine(
            Path.GetTempPath(),
            $"stockapp-crashlog-tests-{Guid.NewGuid():N}",
            "crash.log");
    }

    public void Dispose()
    {
        var carpeta = Path.GetDirectoryName(_logPath);
        if (carpeta is not null && Directory.Exists(carpeta))
            Directory.Delete(carpeta, recursive: true);
    }

    [Fact]
    public void LogFatal_EscribeLaEntradaConOrigenTipoYMensaje()
    {
        var registro = new RegistroFallosArchivo(_logPath);

        registro.LogFatal("test", new InvalidOperationException("boom"));

        var contenido = File.ReadAllText(_logPath);
        Assert.Contains("origen=test", contenido);
        Assert.Contains("tipo=System.InvalidOperationException", contenido);
        Assert.Contains("mensaje=boom", contenido);
    }

    [Fact]
    public void LogFatal_VariasLlamadas_AcumulaEntradasEnVezDePisar()
    {
        var registro = new RegistroFallosArchivo(_logPath);

        registro.LogFatal("primero", new InvalidOperationException("uno"));
        registro.LogFatal("segundo", new InvalidOperationException("dos"));

        var contenido = File.ReadAllText(_logPath);
        Assert.Contains("origen=primero", contenido);
        Assert.Contains("origen=segundo", contenido);
    }

    // ── Rotación (fix 2026-08-20) ──────────────────────────────────────────────

    [Fact]
    public void LogFatal_ArchivoPorDebajoDelUmbral_NoRota()
    {
        var registro = new RegistroFallosArchivo(_logPath, tamanioMaximoBytes: 1024);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        File.WriteAllText(_logPath, new string('x', 100));

        registro.LogFatal("test", new InvalidOperationException("boom"));

        Assert.False(File.Exists(_logPath + ".1"));
    }

    [Fact]
    public void LogFatal_ArchivoSuperaElUmbral_RotaAPunto1YArrancaUnCrashLogNuevo()
    {
        var registro = new RegistroFallosArchivo(_logPath, tamanioMaximoBytes: 1024);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        var contenidoViejo = new string('x', 2000);
        File.WriteAllText(_logPath, contenidoViejo);

        registro.LogFatal("nuevo", new InvalidOperationException("boom"));

        Assert.True(File.Exists(_logPath + ".1"));
        Assert.Equal(contenidoViejo, File.ReadAllText(_logPath + ".1"));

        var contenidoNuevo = File.ReadAllText(_logPath);
        Assert.DoesNotContain(contenidoViejo, contenidoNuevo);
        Assert.True(contenidoNuevo.Length < contenidoViejo.Length, "el crash.log rotado debería arrancar de cero, no seguir acumulando sobre el contenido viejo");
        Assert.Contains("origen=nuevo", contenidoNuevo);
    }

    [Fact]
    public void LogFatal_RotacionPisaUnaRotacionAnterior()
    {
        var registro = new RegistroFallosArchivo(_logPath, tamanioMaximoBytes: 1024);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        File.WriteAllText(_logPath + ".1", "rotacion-anterior-vieja");
        var contenidoActual = new string('y', 2000);
        File.WriteAllText(_logPath, contenidoActual);

        registro.LogFatal("nuevo", new InvalidOperationException("boom"));

        var rotado = File.ReadAllText(_logPath + ".1");
        Assert.DoesNotContain("rotacion-anterior-vieja", rotado);
        Assert.Equal(contenidoActual, rotado);
    }

    [Fact]
    public void LogFatal_SiFallaLaEscritura_NuncaTira()
    {
        // Ruta con un caracter nulo: inválida en cualquier filesystem, fuerza la excepción
        // interna que LogFatal debe tragarse (contrato: "nunca debe tirar").
        var registro = new RegistroFallosArchivo("ruta-invalida\0/crash.log");

        var ex = Record.Exception(() => registro.LogFatal("test", new InvalidOperationException("boom")));

        Assert.Null(ex);
    }
}
