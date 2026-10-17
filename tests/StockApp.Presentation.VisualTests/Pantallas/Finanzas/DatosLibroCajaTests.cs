using StockApp.Application.Finanzas;
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups del libro caja escriben estos números a mano: si un dato no cuadra (un saldo corrido mal
/// sumado, un total que no es la suma), el mockup copia el error y la comparación no lo ve.</summary>
public class DatosLibroCajaTests
{
    private static void CuadraElMes(LibroCajaMesDto mes)
    {
        var corrido = mes.SaldoInicial;
        foreach (var m in mes.Movimientos)
        {
            corrido += m.Ingreso - m.Egreso;
            Assert.Equal(corrido, m.SaldoCorrido);
        }
        Assert.Equal(corrido, mes.SaldoFinal);
        Assert.Equal(mes.Movimientos.Sum(m => m.Egreso), mes.TotalesPorRubro.Sum(t => t.Total));
        Assert.Equal(mes.Movimientos.Sum(m => m.Ingreso - m.Egreso), mes.TotalesPorFuente.Sum(t => t.Total));
    }

    [Fact]
    public void LosMeses_CuadranSaldosYTotales()
    {
        CuadraElMes(DatosLibroCaja.Octubre);
        CuadraElMes(DatosLibroCaja.OctubreSaldoNegativo);
        Assert.Empty(DatosLibroCaja.OctubreVacio.Movimientos);
    }

    [Fact]
    public void Octubre_TextosDelMockup()
    {
        var o = DatosLibroCaja.Octubre;
        Assert.Equal(6, o.Movimientos.Count);
        Assert.Equal("02/10/2026", FechaCortaConverter.Formatear(o.Movimientos[0].Fecha));
        Assert.Equal("$ 2.350.000,00", FormatoEsUy.Moneda(o.SaldoInicial));
        Assert.Equal("$ 2.639.460,00", FormatoEsUy.Moneda(o.SaldoFinal));
        Assert.Equal("-$ 44.000,00", FormatoEsUy.Moneda(o.TotalesPorFuente[1].Total));
        Assert.Equal("—", MonedaOGuionConverter.Instance.Convert(o.Movimientos[0].Egreso, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SaldoNegativo_TextosDelMockup()
    {
        Assert.Equal("-$ 51.540,00", FormatoEsUy.Moneda(DatosLibroCaja.OctubreSaldoNegativo.SaldoFinal));
        Assert.Equal("-$ 101.540,00", FormatoEsUy.Moneda(DatosLibroCaja.OctubreSaldoNegativo.Movimientos[0].SaldoCorrido));
    }

    [Fact]
    public void Anio_NetosRubrosYDeficit()
    {
        var a = DatosLibroCaja.Anio2026;
        Assert.Equal(12, a.TotalesPorMes.Count);
        Assert.All(a.TotalesPorMes, t => Assert.Equal(t.Ingresos - t.Egresos, t.Neto));
        Assert.Equal(a.TotalesPorMes.Sum(t => t.Egresos), a.TotalesPorRubro.Sum(t => t.Total));
        Assert.Equal(new[] { "Marzo", "Junio" },
            a.TotalesPorMes.Where(t => t.Neto < 0m).Select(t => NombreMesConverter.Nombre(t.Mes)));
    }

    [Fact]
    public void Anio_ES_Coherente_ConOctubre()
    {
        var a = DatosLibroCaja.Anio2026;
        var inicialEnero = DatosLibroCaja.Enero2026.SaldoInicial;
        var netosEneroSetiembre = a.TotalesPorMes.Where(t => t.Mes < 10).Sum(t => t.Neto);
        Assert.Equal(DatosLibroCaja.Octubre.SaldoInicial, inicialEnero + netosEneroSetiembre);
        Assert.Equal(DatosLibroCaja.Octubre.SaldoFinal, inicialEnero + a.TotalesPorMes.Sum(t => t.Neto));
        Assert.Equal(a.TotalesPorMes.Single(t => t.Mes == 10).Neto,
            DatosLibroCaja.Octubre.SaldoFinal - DatosLibroCaja.Octubre.SaldoInicial);
    }

    [Fact]
    public async Task Vm_ConMovimientos_CargaOctubreConSusSaldos()
    {
        var vm = DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre));

        await vm.CargarAsync();

        Assert.Equal(2026, vm.Anio);
        Assert.Equal(10, vm.Mes);
        Assert.Equal(6, vm.Movimientos.Count);
        Assert.Equal(2_639_460m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Vm_AnioCompleto_SaldosDelAnioYMesesConMovimientos()
    {
        var vm = DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre));
        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;

        await vm.CargarAsync();

        Assert.Equal("$ 949.350,00", FormatoEsUy.Moneda(vm.SaldoInicial));
        Assert.Equal("$ 2.639.460,00", FormatoEsUy.Moneda(vm.SaldoFinal));
        Assert.Equal(10, vm.MesesConMovimientos);
    }

    /// <summary>Con el debounce real (300 ms), si el servidor tarda más que eso el reloj de pared decide quién gana: el
    /// debounce del cambio de modo dispara una carga que le roba el turno a la explícita del test, y esta termina
    /// descartada con los saldos en cero. Es lo que pasa en una máquina cargada (suite completa con otros builds).</summary>
    [Fact]
    public async Task Vm_AnioCompleto_ConServidorLento_NoDependeDelDebounceReal()
    {
        var vm = DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre),
            async () => { await Task.Delay(500); return DatosLibroCaja.Anio2026; });
        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;

        await vm.CargarAsync();

        Assert.Equal("$ 949.350,00", FormatoEsUy.Moneda(vm.SaldoInicial));
        Assert.Equal(10, vm.MesesConMovimientos);
    }
}
