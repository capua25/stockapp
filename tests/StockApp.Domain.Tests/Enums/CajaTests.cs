using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Enums;

public class CajaTests
{
    /// <summary>
    /// Guardián: la caja se persiste como int (sin HasConversion) y viaja como número en la API.
    /// Reordenar o insertar miembros reinterpretaría en silencio los movimientos ya guardados.
    /// </summary>
    [Fact]
    public void Nombre_LocalYPoa()
    {
        Assert.Equal("Local", Caja.Local.Nombre());
        Assert.Equal("POA", Caja.Poa.Nombre());
        Assert.Equal(0, (int)Caja.Local);
        Assert.Equal(1, (int)Caja.Poa);
        Assert.Equal(new[] { Caja.Local, Caja.Poa }, CajaExtensions.Todas);
    }

    [Fact]
    public void Nombre_FueraDelEnum_DevuelveElNumero()
    {
        Assert.Equal("7", ((Caja)7).Nombre());
    }
}
