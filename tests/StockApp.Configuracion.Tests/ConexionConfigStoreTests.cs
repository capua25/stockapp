using System;
using System.IO;
using StockApp.Configuracion;

namespace StockApp.Configuracion.Tests;

/// <summary>
/// Round-trip Guardar/Leer del archivo de conexión. Mismo formato de clave que
/// appsettings.json ("Api:BaseUrl") a propósito: así el ConfigurationBuilder del desktop
/// puede tratar este archivo como una fuente más, sin lógica de fusión custom.
/// </summary>
public class ConexionConfigStoreTests : IDisposable
{
    private readonly string _rutaArchivo =
        Path.Combine(Path.GetTempPath(), "conexion-config-store-test-" + Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_rutaArchivo))
        {
            File.Delete(_rutaArchivo);
        }
    }

    [Fact]
    public void Guardar_CreaElArchivo_ConLaClave_Api_BaseUrl()
    {
        ConexionConfigStore.Guardar("http://192.168.1.50:5080", _rutaArchivo);

        var contenido = File.ReadAllText(_rutaArchivo);

        Assert.Contains("\"Api\"", contenido);
        Assert.Contains("\"BaseUrl\"", contenido);
        Assert.Contains("http://192.168.1.50:5080", contenido);
    }

    [Fact]
    public void Guardar_CreaLaCarpetaContenedora_SiNoExiste()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rutaArchivo)!); // ya existe (temp), no-op

        ConexionConfigStore.Guardar("http://localhost:5000", _rutaArchivo);

        Assert.True(File.Exists(_rutaArchivo));
    }

    [Fact]
    public void Leer_DespuesDeGuardar_DevuelveElMismoValor()
    {
        ConexionConfigStore.Guardar("http://10.0.0.5:5080", _rutaArchivo);

        var leido = ConexionConfigStore.Leer(_rutaArchivo);

        Assert.Equal("http://10.0.0.5:5080", leido);
    }

    [Fact]
    public void Leer_SiElArchivoNoExiste_DevuelveNull()
    {
        var leido = ConexionConfigStore.Leer(_rutaArchivo);

        Assert.Null(leido);
    }

    [Fact]
    public void Leer_SiElArchivoEstaCorrupto_DevuelveNull_NoLanza()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rutaArchivo)!);
        File.WriteAllText(_rutaArchivo, "{ esto no es json valido");

        var leido = ConexionConfigStore.Leer(_rutaArchivo);

        Assert.Null(leido);
    }
}
