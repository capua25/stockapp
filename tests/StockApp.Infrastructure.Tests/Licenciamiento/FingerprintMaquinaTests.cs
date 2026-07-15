using System.Text.RegularExpressions;
using StockApp.Infrastructure.Licenciamiento;
using Xunit;

namespace StockApp.Infrastructure.Tests.Licenciamiento;

public class FingerprintMaquinaTests
{
    // Subclase de prueba: fija el id crudo para verificar el hasheo/agrupado sin depender
    // del OS real (registro / /etc/machine-id).
    private sealed class FingerprintFijo : FingerprintMaquinaBase
    {
        private readonly string _id;
        public FingerprintFijo(string id) => _id = id;
        protected override string ObtenerIdCrudo() => _id;
    }

    [Fact]
    public void CodigoAgrupado_TieneFormatoDeBloquesDe4HexMayuscula()
    {
        var fp = new FingerprintFijo("id-de-maquina-fijo");

        var codigo = fp.CodigoAgrupado;

        // SHA-256 = 64 hex → 16 bloques de 4 unidos por '-'.
        Assert.Matches(new Regex("^[0-9A-F]{4}(-[0-9A-F]{4}){15}$"), codigo);
    }

    [Fact]
    public void CodigoAgrupado_EsDeterministicoParaElMismoId()
    {
        var a = new FingerprintFijo("misma-maquina").CodigoAgrupado;
        var b = new FingerprintFijo("misma-maquina").CodigoAgrupado;

        Assert.Equal(a, b);
    }

    [Fact]
    public void CodigoAgrupado_DifiereEntreIdsDistintos()
    {
        var a = new FingerprintFijo("maquina-1").CodigoAgrupado;
        var b = new FingerprintFijo("maquina-2").CodigoAgrupado;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CodigoAgrupado_NoContieneElIdCrudo()
    {
        var fp = new FingerprintFijo("SECRETO-machine-guid");

        Assert.DoesNotContain("SECRETO", fp.CodigoAgrupado);
    }
}
