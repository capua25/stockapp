using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Enums;

public class MotivoMovimientoTests
{
    /// <summary>
    /// Guardián: el motivo se persiste como int (sin HasConversion) y viaja como número en la API.
    /// Reordenar o insertar miembros reinterpretaría en silencio los movimientos ya guardados.
    /// </summary>
    [Theory]
    [InlineData(MotivoMovimiento.Compra, 0)]
    [InlineData(MotivoMovimiento.UsoOConsumo, 1)]
    [InlineData(MotivoMovimiento.Ajuste, 2)]
    [InlineData(MotivoMovimiento.Merma, 3)]
    public void Valores_NumericosPersistidos_NoCambian(MotivoMovimiento motivo, int esperado)
        => Assert.Equal(esperado, (int)motivo);

    [Fact]
    public void Enum_NoTieneMasMiembrosQueLosConocidos()
        => Assert.Equal(4, Enum.GetValues<MotivoMovimiento>().Length);

    [Theory]
    [InlineData(MotivoMovimiento.Compra, "Compra")]
    [InlineData(MotivoMovimiento.UsoOConsumo, "Uso o consumo")]
    [InlineData(MotivoMovimiento.Ajuste, "Ajuste")]
    [InlineData(MotivoMovimiento.Merma, "Merma")]
    public void Nombre_DevuelveElTextoVisible(MotivoMovimiento motivo, string esperado)
        => Assert.Equal(esperado, motivo.Nombre());

    // Tabla tipo × motivo (incluye combinaciones inválidas, que el servicio rechaza antes).
    [Theory]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Compra, true)]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, false)]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Merma, false)]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.UsoOConsumo, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.Compra, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.Ajuste, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.Merma, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, false)]
    public void PrecioEsObligatorio_SoloEntradaPorCompra(TipoMovimiento tipo, MotivoMovimiento motivo, bool esperado)
        => Assert.Equal(esperado, motivo.PrecioEsObligatorio(tipo));

    [Theory]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Compra, true)]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, true)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.Compra, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.Ajuste, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.Merma, false)]
    [InlineData(TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, false)]
    public void PrecioAplica_SoloEnEntradas(TipoMovimiento tipo, MotivoMovimiento motivo, bool esperado)
        => Assert.Equal(esperado, motivo.PrecioAplica(tipo));

    [Fact]
    public void Nombre_CubreTodosLosMiembros_SinCaerAlToString()
    {
        foreach (var motivo in Enum.GetValues<MotivoMovimiento>())
            Assert.DoesNotContain("UsoOConsumo", motivo.Nombre());
    }
}
