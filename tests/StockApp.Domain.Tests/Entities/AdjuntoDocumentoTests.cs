using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class AdjuntoDocumentoTests
{
    [Fact]
    public void Activo_PorDefecto_EsTrue()
    {
        var adjunto = new AdjuntoDocumento();

        Assert.True(adjunto.Activo);
    }

    [Fact]
    public void AdjuntoDocumento_SeAsociaAUnDocumentoPorId()
    {
        var adjunto = new AdjuntoDocumento
        {
            DocumentoAdministrativoId = 7,
            NombreArchivo = "factura.pdf",
            ContentType = "application/pdf",
            TamanoBytes = 1024,
            FechaAltaUtc = DateTime.UtcNow,
        };

        Assert.Equal(7, adjunto.DocumentoAdministrativoId);
        Assert.Equal("factura.pdf", adjunto.NombreArchivo);
        Assert.Equal("application/pdf", adjunto.ContentType);
    }

    [Fact]
    public void AdjuntoDocumentoContenido_ContenidoPorDefecto_EsArrayVacio()
    {
        var contenido = new AdjuntoDocumentoContenido();

        Assert.Empty(contenido.Contenido);
    }
}
