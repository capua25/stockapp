using StockApp.Application.Logs;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Tests.Logs;

public class ServicioConsultaLogsTests : IDisposable
{
    private readonly string _directorio =
        Path.Combine(Path.GetTempPath(), "StockAppLogsTests_" + Guid.NewGuid());

    private void CrearArchivo(string nombre, string contenido, DateTime escritura)
    {
        Directory.CreateDirectory(_directorio);
        var ruta = Path.Combine(_directorio, nombre);
        File.WriteAllText(ruta, contenido);
        File.SetLastWriteTime(ruta, escritura);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directorio)) Directory.Delete(_directorio, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ObtenerResumen_DirectorioInexistente_DevuelveResumenVacio()
    {
        var servicio = new ServicioConsultaLogs();

        var resumen = servicio.ObtenerResumen(Path.Combine(_directorio, "no-existe"));

        Assert.Equal(0, resumen.CantidadArchivos);
        Assert.Null(resumen.DesdeFecha);
        Assert.Null(resumen.HastaFecha);
        Assert.Equal(0, resumen.TamanioTotalBytes);
    }

    [Fact]
    public void ObtenerResumen_ConTresArchivos_AgregaCantidadTamanioYRango()
    {
        CrearArchivo("stockapp-20260701.log", "aaaa", new DateTime(2026, 7, 1, 10, 0, 0));
        CrearArchivo("stockapp-20260715.log", "bb", new DateTime(2026, 7, 15, 10, 0, 0));
        CrearArchivo("stockapp-20260729.log", "cccccc", new DateTime(2026, 7, 29, 10, 0, 0));
        var servicio = new ServicioConsultaLogs();

        var resumen = servicio.ObtenerResumen(_directorio);

        Assert.Equal(3, resumen.CantidadArchivos);
        Assert.Equal(12, resumen.TamanioTotalBytes);
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0), resumen.DesdeFecha);
        Assert.Equal(new DateTime(2026, 7, 29, 10, 0, 0), resumen.HastaFecha);
    }

    [Fact]
    public void ObtenerResumen_IgnoraArchivosQueNoSonLog()
    {
        CrearArchivo("stockapp-20260701.log", "aaaa", new DateTime(2026, 7, 1, 10, 0, 0));
        CrearArchivo("notas.txt", "esto no es un log", new DateTime(2026, 7, 2, 10, 0, 0));
        var servicio = new ServicioConsultaLogs();

        var resumen = servicio.ObtenerResumen(_directorio);

        Assert.Equal(1, resumen.CantidadArchivos);
        Assert.Equal(4, resumen.TamanioTotalBytes);
    }

    [Fact]
    public void ResolverArchivosParaZip_ConArchivos_LosDevuelveOrdenadosPorNombre()
    {
        CrearArchivo("stockapp-20260729.log", "c", new DateTime(2026, 7, 29, 10, 0, 0));
        CrearArchivo("stockapp-20260701.log", "a", new DateTime(2026, 7, 1, 10, 0, 0));
        var servicio = new ServicioConsultaLogs();

        var archivos = servicio.ResolverArchivosParaZip(_directorio);

        Assert.Equal(2, archivos.Count);
        Assert.EndsWith("stockapp-20260701.log", archivos[0]);
        Assert.EndsWith("stockapp-20260729.log", archivos[1]);
    }

    [Fact]
    public void ResolverArchivosParaZip_SinArchivos_LanzaEntidadNoEncontrada()
    {
        Directory.CreateDirectory(_directorio);
        var servicio = new ServicioConsultaLogs();

        Assert.Throws<EntidadNoEncontradaException>(() => servicio.ResolverArchivosParaZip(_directorio));
    }

    [Fact]
    public void ResolverArchivosParaZip_DirectorioInexistente_LanzaEntidadNoEncontrada()
    {
        var servicio = new ServicioConsultaLogs();

        Assert.Throws<EntidadNoEncontradaException>(
            () => servicio.ResolverArchivosParaZip(Path.Combine(_directorio, "no-existe")));
    }

    [Fact]
    public void ObtenerResumen_DirectorioSinPermiso_LanzaDirectorioLogsInaccesibleConLaRuta()
    {
        Directory.CreateDirectory(_directorio);
        var servicio = new ServicioConsultaLogs();

        // Quitamos el permiso de lectura+ejecución del directorio: en Linux esto hace que
        // Directory.GetFiles falle con UnauthorizedAccessException real de System.IO -- mismo
        // truco de filesystem real (sin fakes) que ServicioBackupTests.
        // LimpiarTmpHuerfanos_ArchivoSinPermisoDeBorrado_NoLanzaYLoDejaEnDisco. Deuda de
        // portabilidad conocida: no corre en Windows (SetUnixFileMode es no-op/no aplica ahí).
        File.SetUnixFileMode(_directorio, UnixFileMode.None);
        try
        {
            var ex = Assert.Throws<DirectorioLogsInaccesibleException>(
                () => servicio.ObtenerResumen(_directorio));

            Assert.Contains(_directorio, ex.Message);
            Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        }
        finally
        {
            File.SetUnixFileMode(_directorio,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void ResolverArchivosParaZip_DirectorioSinPermiso_LanzaDirectorioLogsInaccesibleConLaRuta()
    {
        Directory.CreateDirectory(_directorio);
        var servicio = new ServicioConsultaLogs();

        File.SetUnixFileMode(_directorio, UnixFileMode.None);
        try
        {
            var ex = Assert.Throws<DirectorioLogsInaccesibleException>(
                () => servicio.ResolverArchivosParaZip(_directorio));

            Assert.Contains(_directorio, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(_directorio,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
