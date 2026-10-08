using StockApp.Domain.Formato;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Los mockups de Productos escriben estos textos a mano: si un dato fijo cambia y el mockup no, la
/// comparación falla por un número distinto y el diagnóstico no lo dice. Esta tabla es el contrato.
/// </summary>
public class DatosProductosTests
{
    [Fact]
    public void Productos_TextosFormateados_SonLosDelMockup()
    {
        var esperado = new (string Codigo, string Nombre, string Costo, string Stock, string Unidad, string Categoria, bool Activo)[]
        {
            ("MAT-0142", "Cemento portland 50 kg", "$ 480,00", "320", "Bolsa", "Construcción", true),
            ("MAT-0198", "Arena gruesa", "$ 890,00", "45", "Metro cúbico", "Construcción", true),
            ("PLO-0231", "Caño PVC 110 mm", "$ 650,00", "12", "Unidad", "Plomería", true),
            ("PIN-0305", "Pintura látex blanca 20 L", "$ 2.340,00", "6", "Balde", "Pintura", true),
            ("SEG-0356", "Guantes de trabajo", "$ 95,00", "180", "Par", "Seguridad", true),
            ("ELE-0402", "Lámpara LED 30 W", "$ 410,00", "8", "Unidad", "Electricidad", true),
            ("ELE-0418", "Cable 2x1,5 mm", "$ 38,00", "1.250", "Metro", "Electricidad", true),
            ("LIM-0455", "Hipoclorito de sodio 5 L", "$ 210,00", "-3", "Bidón", "Limpieza", true),
            ("LIM-0470", "Bolsas de residuo 80x110", "$ 175,00", "60", "Paquete", "Limpieza", true),
            ("HER-0512", "Pala de punta", "$ 1.150,00", "14", "Unidad", "—", true),
            ("VIA-0533", "Pintura vial amarilla 4 L", "$ 1.890,50", "22,5", "Lata", "Pintura", true),
            ("HER-0560", "Carretilla reforzada", "$ 3.200,00", "0", "Unidad", "Herramientas", false),
        };

        var real = DatosProductos.Productos.Select(p => (p.Codigo, p.Nombre, FormatoEsUy.Moneda(p.PrecioCosto),
            FormatoEsUy.Cantidad(p.StockActual), p.UnidadMedidaNombre, p.CategoriaNombre ?? "—", p.Activo)).ToArray();

        Assert.Equal(esperado, real);
    }

    [Fact]
    public void ProductosConUnidadLarga_SoloCambiaLaUnidadDeArenaGruesa()
    {
        var cambiadas = DatosProductos.ProductosConUnidadLarga
            .Zip(DatosProductos.Productos, (nuevo, viejo) => (nuevo, viejo))
            .Where(p => p.nuevo != p.viejo)
            .ToList();

        var (n, v) = Assert.Single(cambiadas);
        Assert.Equal("Arena gruesa", n.Nombre);
        Assert.Equal(v with { UnidadMedidaNombre = "Metro cúbico puesto en obra con flete incluido" }, n);
        Assert.Equal(DatosProductos.UnidadLarga, n.UnidadMedidaNombre);
    }

    [Fact]
    public async Task SinPermisoListado_LaCargaMarcaSinPermisoConElMensajeDelListado()
    {
        var vm = (StockApp.Presentation.ViewModels.Catalogo.ProductoListViewModel)DatosProductos.SinPermisoListado().CurrentContent!;

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(StockApp.Presentation.ViewModels.Catalogo.ProductoListViewModel.MensajeSinPermisoListado, vm.MensajeSinPermiso);
    }

    [Fact]
    public void Cargando_LaCargaNoTermina()
    {
        var vm = (StockApp.Presentation.ViewModels.Catalogo.ProductoListViewModel)DatosProductos.Cargando().CurrentContent!;

        var carga = vm.CargarAsync();

        Assert.False(carga.IsCompleted);
        Assert.True(vm.Cargando);
    }
}
