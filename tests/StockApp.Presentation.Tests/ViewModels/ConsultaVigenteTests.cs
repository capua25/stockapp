using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ConsultaVigenteTests
{
    [Fact]
    public void UnaSolaConsulta_EsLaUltima()
    {
        var consultas = new ConsultaVigente();

        var turno = consultas.Empezar();

        Assert.True(turno.EsLaUltima);
    }

    [Fact]
    public void AlEmpezarOtra_LaAnteriorDejaDeSerLaUltima_YLaNuevaLoEs()
    {
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();

        var nueva = consultas.Empezar();

        Assert.False(vieja.EsLaUltima);
        Assert.True(nueva.EsLaUltima);
    }

    [Fact]
    public void DosContadores_NoSeMezclan()
    {
        var a = new ConsultaVigente();
        var b = new ConsultaVigente();
        var turnoA = a.Empezar();

        b.Empezar();

        Assert.True(turnoA.EsLaUltima);
    }
}
