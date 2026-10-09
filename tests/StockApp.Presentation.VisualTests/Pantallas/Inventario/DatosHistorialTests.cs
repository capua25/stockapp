using System;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

public class DatosHistorialTests
{
    [Fact]
    public void Movimientos_DoceConUnStockNegativoYUnComentarioLargo()
    {
        var m = DatosHistorial.Movimientos;

        Assert.Equal(12, m.Count);
        Assert.Single(m, x => x.StockNuevo < 0);
        Assert.Equal(-3m, m.Single(x => x.StockNuevo < 0).StockNuevo);
        Assert.Contains(m, x => x.Comentario == DatosHistorial.ComentarioLargo);
        Assert.Equal(4, m.Count(x => x.Tipo == TipoMovimiento.Salida));
        Assert.All(m.Where(x => x.Tipo == TipoMovimiento.Salida), x => Assert.Equal(0m, x.PrecioUnitario));
    }

    [Fact]
    public void Fecha_EsUnaHoraLocalFija_EnCualquierHuso()
    {
        var utc = DatosHistorial.Fecha(10, 7, 14, 35);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 10, 7, 14, 35, 0), utc.ToLocalTime());
    }

    [Fact]
    public void ConTextosLargos_SoloCambianProductoYUsuarioDeLaFila3()
    {
        var cambiadas = DatosHistorial.MovimientosConTextosLargos
            .Zip(DatosHistorial.Movimientos, (nuevo, viejo) => (nuevo, viejo))
            .Where(p => p.nuevo != p.viejo).ToList();

        var (n, v) = Assert.Single(cambiadas);
        Assert.Equal(v with { ProductoNombre = DatosHistorial.ProductoLargo, UsuarioNombre = DatosHistorial.UsuarioLargo }, n);
    }

    [Fact]
    public void Cargando_LaCargaNoTermina()
    {
        var vm = (MovimientoHistorialViewModel)DatosHistorial.Cargando().CurrentContent!;

        var carga = vm.InicializarAsync();

        Assert.False(carga.IsCompleted);
        Assert.True(vm.Cargando);
    }

    [Fact]
    public async Task SinPermiso_LaCargaMarcaSinPermisoConElMensajeDelHistorial()
    {
        var vm = (MovimientoHistorialViewModel)DatosHistorial.SinPermiso().CurrentContent!;

        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(MovimientoHistorialViewModel.MensajeSinPermisoHistorial, vm.MensajeSinPermiso);
    }
}
