using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de ingresos escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla
/// por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosIngresosTests
{
    [Fact]
    public void Ingresos_TextosDelMockup()
    {
        var i = DatosIngresos.Ingresos;
        Assert.Equal(8, i.Count);
        Assert.Equal(2, i.Count(x => !x.Activo));
        Assert.Equal("06/10/2026", FechaCortaConverter.Formatear(DateOnly.FromDateTime(i[0].Fecha)));
        Assert.Equal("$ 1.250.000,00", FormatoEsUy.Moneda(i[0].Monto));
        Assert.Equal("$ 812.430,55", FormatoEsUy.Moneda(i[^1].Monto));
    }

    [Fact]
    public async Task Pie_PorDefectoYConTodos()
    {
        var vm = DatosIngresos.Listado(() => Task.FromResult(DatosIngresos.Ingresos));
        await vm.CargarAsync();
        Assert.Equal("6 ingresos · Total $ 6.610.730,55", vm.TextoPie);

        vm.MostrarInactivos = true;
        Assert.Equal("8 ingresos · 2 dados de baja · Total $ 6.610.730,55", vm.TextoPie);
    }

    [Fact]
    public void Formulario_AltaConFechaFija_YSinErrores()
    {
        var vm = DatosIngresos.Formulario();
        vm.InicializarAsync().GetAwaiter().GetResult();

        Assert.Equal(new DateTime(2026, 10, 6), vm.FechaSeleccionada);
        Assert.Equal(3, vm.FuentesDisponibles.Count);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Formulario_EdicionConMontoEsUy()
    {
        var vm = DatosIngresos.Formulario();
        vm.CargarParaEditar(DatosIngresos.Ingresos[1]);

        Assert.Equal("48.300,00", vm.MontoTexto);
        Assert.Equal("Editar ingreso", vm.Titulo);
    }

    [Fact]
    public async Task ErrorDelServidor_FormularioCompletoConFuente_YGuardarHabilitado()
    {
        // El servidor solo rechaza lo que el cliente dejó guardar: el mockup muestra el formulario completo.
        var vm = (IngresoFormViewModel)DatosIngresos.ConErrorDelServidor().CurrentContent!;
        await vm.InicializarAsync();

        Assert.Equal("FIGM Literal B", vm.FuenteSeleccionada?.Nombre);
        Assert.Equal("El monto del ingreso debe ser mayor a cero.", vm.MensajeError);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task SinPermisoAlInicializar_AlertaDelFormularioYGuardarDeshabilitado()
    {
        var vm = (IngresoFormViewModel)DatosIngresos.SinPermisoAlInicializar().CurrentContent!;
        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(IngresoFormViewModel.MensajeSinPermisoFormulario, vm.MensajeSinPermiso);
        Assert.Equal(new DateTime(2026, 10, 6), vm.FechaSeleccionada);
        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
