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

    [Theory]
    [InlineData(MotivoMovimiento.Compra, true)]
    [InlineData(MotivoMovimiento.UsoOConsumo, false)]
    [InlineData(MotivoMovimiento.Ajuste, false)]
    [InlineData(MotivoMovimiento.Merma, false)]
    public void PrecioEsObligatorio_SoloParaCompra(MotivoMovimiento motivo, bool esperado)
        => Assert.Equal(esperado, motivo.PrecioEsObligatorio());

    [Theory]
    [InlineData(MotivoMovimiento.Compra, true)]
    [InlineData(MotivoMovimiento.UsoOConsumo, false)]
    [InlineData(MotivoMovimiento.Ajuste, true)]
    [InlineData(MotivoMovimiento.Merma, true)]
    public void PrecioAplica_TodosMenosUsoOConsumo(MotivoMovimiento motivo, bool esperado)
        => Assert.Equal(esperado, motivo.PrecioAplica());

    [Fact]
    public void Nombre_CubreTodosLosMiembros_SinCaerAlToString()
    {
        foreach (var motivo in Enum.GetValues<MotivoMovimiento>())
            Assert.DoesNotContain("UsoOConsumo", motivo.Nombre());
    }
}
