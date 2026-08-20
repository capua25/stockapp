using System;
using System.IO;
using StockApp.Configuracion;

namespace StockApp.Configuracion.Tests;

/// <summary>
/// La ruta del archivo de conexión es el contrato compartido entre StockApp.Presentation
/// (que lee) y tools/StockApp.Configurador (que escribe). Vive fuera del directorio de
/// instalación porque Velopack reemplaza esa carpeta entera en cada update — ver el
/// comentario de ObtenerRutaArchivo.
/// </summary>
public class RutaConexionTests
{
    [Fact]
    public void ObtenerRutaArchivo_ApuntaA_GestionMunicipal_ConexionJson_DentroDeAppData()
    {
        var esperado = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GestionMunicipal",
            "conexion.json");

        var ruta = RutaConexion.ObtenerRutaArchivo();

        Assert.Equal(esperado, ruta);
    }

    [Fact]
    public void ObtenerRutaArchivo_ConCarpetaBaseExplicita_LaUsaEnVezDeAppData()
    {
        var carpetaBase = Path.Combine(Path.GetTempPath(), "ruta-conexion-test-" + Guid.NewGuid());

        var ruta = RutaConexion.ObtenerRutaArchivo(carpetaBase);

        Assert.Equal(Path.Combine(carpetaBase, "GestionMunicipal", "conexion.json"), ruta);
    }
}
