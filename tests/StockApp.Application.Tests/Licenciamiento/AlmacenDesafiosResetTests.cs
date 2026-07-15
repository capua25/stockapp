using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class AlmacenDesafiosResetTests
{
    [Fact]
    public void GenerarNuevo_DevuelveNonceNoVacio()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();

        var desafio = almacen.GenerarNuevo();

        Assert.False(string.IsNullOrWhiteSpace(desafio));
    }

    [Fact]
    public void GenerarNuevo_DosVeces_DaNoncesDistintos()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();

        Assert.NotEqual(almacen.GenerarNuevo(), almacen.GenerarNuevo());
    }

    [Fact]
    public void Consumir_DesafioVivo_DevuelveValido()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        var desafio = almacen.GenerarNuevo();

        Assert.Equal(ResultadoDesafio.Valido, almacen.Consumir(desafio));
    }

    [Fact]
    public void Consumir_DosVecesElMismo_LaSegundaEsInexistente()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        var desafio = almacen.GenerarNuevo();

        almacen.Consumir(desafio);

        Assert.Equal(ResultadoDesafio.Inexistente, almacen.Consumir(desafio));
    }

    [Fact]
    public void GenerarNuevo_InvalidaElAnterior()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        var primero = almacen.GenerarNuevo();
        almacen.GenerarNuevo(); // reemplaza al primero

        Assert.Equal(ResultadoDesafio.Inexistente, almacen.Consumir(primero));
    }

    [Fact]
    public void Consumir_Desconocido_DevuelveInexistente()
    {
        var almacen = new AlmacenDesafiosResetEnMemoria();
        almacen.GenerarNuevo();

        Assert.Equal(ResultadoDesafio.Inexistente, almacen.Consumir("no-existe"));
    }

    [Fact]
    public void Consumir_DesafioExpirado_DevuelveExpirado()
    {
        var ahora = new DateTime(2026, 07, 15, 10, 0, 0, DateTimeKind.Utc);
        var reloj = ahora;
        var almacen = new AlmacenDesafiosResetEnMemoria(
            ttl: TimeSpan.FromHours(24), ahora: () => reloj);

        var desafio = almacen.GenerarNuevo();
        reloj = ahora.AddHours(25); // pasó el TTL

        Assert.Equal(ResultadoDesafio.Expirado, almacen.Consumir(desafio));
    }
}
