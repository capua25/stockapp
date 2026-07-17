using StockApp.Application.Finanzas;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class AdjuntoValidadorTests
{
    private static readonly byte[] MagicPdf = { 0x25, 0x50, 0x44, 0x46 };
    private static readonly byte[] MagicJpg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] MagicPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void DetectarContentType_Pdf_DevuelveApplicationPdf()
    {
        Assert.Equal("application/pdf", AdjuntoValidador.DetectarContentType(MagicPdf));
    }

    [Fact]
    public void DetectarContentType_Jpg_DevuelveImageJpeg()
    {
        Assert.Equal("image/jpeg", AdjuntoValidador.DetectarContentType(MagicJpg));
    }

    [Fact]
    public void DetectarContentType_Png_DevuelveImagePng()
    {
        Assert.Equal("image/png", AdjuntoValidador.DetectarContentType(MagicPng));
    }

    [Fact]
    public void DetectarContentType_BytesNoReconocidos_DevuelveNull()
    {
        Assert.Null(AdjuntoValidador.DetectarContentType(new byte[] { 0x00, 0x01, 0x02 }));
    }

    [Fact]
    public void Validar_ArchivoValido_NoLanza()
    {
        AdjuntoValidador.Validar(MagicPdf, "factura.pdf");
    }

    [Fact]
    public void Validar_MimeNoPermitido_LanzaReglaDeNegocio()
    {
        var ex = Assert.Throws<ReglaDeNegocioException>(
            () => AdjuntoValidador.Validar(new byte[] { 0x00, 0x01 }, "archivo.exe"));

        Assert.Contains("PDF, JPG o PNG", ex.Message);
    }

    [Fact]
    public void Validar_ExcedeTamanoMaximo_LanzaReglaDeNegocio()
    {
        var contenido = new byte[AdjuntoValidador.TamanoMaximoBytes + 1];
        MagicPdf.CopyTo(contenido, 0);

        var ex = Assert.Throws<ReglaDeNegocioException>(
            () => AdjuntoValidador.Validar(contenido, "factura.pdf"));

        Assert.Contains("10", ex.Message);
    }
}
