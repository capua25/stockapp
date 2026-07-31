using StockApp.Licencias.Cli;
using Xunit;

namespace StockApp.Licencias.Cli.Tests;

public class EscritorClavePrivadaTests
{
    [Fact]
    public void Escribir_DestinoInexistente_GeneraElArchivoYLoDejaEn600()
    {
        var directorio = CrearDirectorioTemporal();
        var (privadaPem, _) = GeneradorClaves.Generar();

        var resultado = EscritorClavePrivada.Escribir(directorio, privadaPem, forzar: false);

        Assert.True(File.Exists(resultado.RutaPrivada));
        Assert.Equal(privadaPem, File.ReadAllText(resultado.RutaPrivada));
        Assert.Null(resultado.RutaRespaldo);

        if (!OperatingSystem.IsWindows())
        {
            var modoArchivo = File.GetUnixFileMode(resultado.RutaPrivada);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, modoArchivo);

            var modoDirectorio = File.GetUnixFileMode(directorio);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                modoDirectorio);
        }
    }

    [Fact]
    public void Escribir_DestinoYaExisteSinForzar_FallaYDejaElArchivoOriginalIntacto()
    {
        var directorio = CrearDirectorioTemporal();
        var (privadaOriginal, _) = GeneradorClaves.Generar();
        var primeraEscritura = EscritorClavePrivada.Escribir(directorio, privadaOriginal, forzar: false);
        var bytesOriginales = File.ReadAllBytes(primeraEscritura.RutaPrivada);

        var (privadaNueva, _) = GeneradorClaves.Generar();
        var ex = Assert.Throws<InvalidOperationException>(
            () => EscritorClavePrivada.Escribir(directorio, privadaNueva, forzar: false));

        Assert.Contains(primeraEscritura.RutaPrivada, ex.Message);
        Assert.Equal(bytesOriginales, File.ReadAllBytes(primeraEscritura.RutaPrivada));

        // No se creó ningún respaldo: el intento fue rechazado antes de tocar el disco.
        var archivosEnDirectorio = Directory.GetFiles(directorio);
        Assert.Single(archivosEnDirectorio);
    }

    [Fact]
    public void Escribir_DestinoYaExisteConForzar_GeneraLaNuevaYRespaldaLaViejaConSuContenidoOriginal()
    {
        var directorio = CrearDirectorioTemporal();
        var (privadaOriginal, _) = GeneradorClaves.Generar();
        var primeraEscritura = EscritorClavePrivada.Escribir(directorio, privadaOriginal, forzar: false);

        var (privadaNueva, _) = GeneradorClaves.Generar();
        var segundaEscritura = EscritorClavePrivada.Escribir(directorio, privadaNueva, forzar: true);

        Assert.Equal(privadaNueva, File.ReadAllText(segundaEscritura.RutaPrivada));
        Assert.NotNull(segundaEscritura.RutaRespaldo);
        Assert.True(File.Exists(segundaEscritura.RutaRespaldo));
        Assert.Equal(privadaOriginal, File.ReadAllText(segundaEscritura.RutaRespaldo!));
        Assert.NotEqual(segundaEscritura.RutaPrivada, segundaEscritura.RutaRespaldo);
    }

    [Fact]
    public void Escribir_DestinoYaExisteSinForzar_ElMensajeDeErrorMencionaLaRutaCompleta()
    {
        var directorio = CrearDirectorioTemporal();
        var (privadaOriginal, _) = GeneradorClaves.Generar();
        var primeraEscritura = EscritorClavePrivada.Escribir(directorio, privadaOriginal, forzar: false);

        var (privadaNueva, _) = GeneradorClaves.Generar();
        var ex = Assert.Throws<InvalidOperationException>(
            () => EscritorClavePrivada.Escribir(directorio, privadaNueva, forzar: false));

        Assert.Contains(primeraEscritura.RutaPrivada, ex.Message);
        Assert.Contains("--forzar", ex.Message);
    }

    private static string CrearDirectorioTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "stockapp-licencias-cli-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directorio);
        return directorio;
    }
}
