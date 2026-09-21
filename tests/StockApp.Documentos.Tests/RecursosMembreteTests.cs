using StockApp.Documentos;
using Xunit;

namespace StockApp.Documentos.Tests;

public class RecursosMembreteTests
{
    [Fact]
    public void ObtenerLogoNegro_DevuelveBytesDeUnPngValido()
    {
        var bytes = RecursosMembrete.ObtenerLogoNegro();

        Assert.NotEmpty(bytes);
        // Firma PNG: 0x89 'P' 'N' 'G' \r \n 0x1A \n
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }
}
