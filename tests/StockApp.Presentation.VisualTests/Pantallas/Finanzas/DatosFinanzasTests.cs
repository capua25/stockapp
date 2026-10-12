using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de finanzas escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla
/// por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosFinanzasTests
{
    [Fact]
    public void Calendario_TextosDelMockup()
    {
        var c = DatosCalendario.Calendario;
        Assert.Equal(new[] { 2, 1, 2, 2 }, new[] { c.Vencidas.Count, c.AVencer7Dias.Count, c.AVencer30Dias.Count, c.PagosRecientes.Count });
        Assert.Equal("$ 18.450,00", FormatoEsUy.Moneda(c.Vencidas[0].SaldoPendiente));
        Assert.Equal("28/09/2026", FechaCortaConverter.Formatear(c.Vencidas[0].FechaVencimiento));
        Assert.Equal("—", c.Vencidas[1].NumeroFactura ?? "—");
        Assert.Equal("20/10/2026", FechaCortaConverter.Formatear(c.AVencer30Dias[0].FechaVencimiento));
        Assert.Equal("05/10/2026", FechaCortaConverter.Formatear(c.PagosRecientes[0].FechaPago));
    }

    [Fact]
    public void ControlPoa_TextosDelMockup()
    {
        Assert.Equal("47,30 %", PorcentajeConverter.Formatear(DatosControlPoa.Lineas[0].PorcentajeEjecucion));
        Assert.Equal("94,26 %", PorcentajeConverter.Formatear(DatosControlPoa.Lineas[1].PorcentajeEjecucion));
        Assert.Equal("$ 1.200.000,00", FormatoEsUy.Moneda(DatosControlPoa.Lineas[0].Presupuesto));
        var sobre = DatosControlPoa.LineasConSobreejecutada[^1];
        Assert.Equal("-$ 42.900,00", FormatoEsUy.Moneda(sobre.Saldo));
        Assert.Equal("114,30 %", PorcentajeConverter.Formatear(sobre.PorcentajeEjecucion));
        Assert.True(sobre.Sobregirada);
        foreach (var l in DatosControlPoa.LineasConSobreejecutada)
        {
            Assert.Equal(l.Presupuesto - l.Gastado, l.Saldo);
            Assert.Equal(Math.Round(l.Gastado / l.Presupuesto * 100m, 2), l.PorcentajeEjecucion);
        }
    }

    [Fact]
    public void ControlPoa_ElEjercicioSaleDelRelojFijo()
    {
        Assert.Equal(2026, DatosControlPoa.Vm(() => Task.FromResult(DatosControlPoa.Lineas)).Ejercicio);
    }

    [Fact]
    public void Adjuntos_VacioYConPermiso()
    {
        var vm = DatosAdjuntos.VmVacio();

        Assert.Empty(vm.Items);
        Assert.True(vm.PuedeModificar);
    }
}
