using System.Reflection;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.VisualTests.Pantallas.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Gestion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.VisualTests.Pantallas;

/// <summary>Las fábricas de datos fijos de los ViewModels con RecargaConDebounce fijan la espera en cero: con los 300 ms
/// reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado
/// depende de cuánto tarde la máquina (el bug de Libro caja, 2de3ad5).</summary>
public class DatosSinDebounceTests
{
    [Fact] public void Productos_Listado_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosProductos.Listado([]).EsperaDebounce);
    [Fact] public void Productos_ListadoQueNoTermina_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosProductos.ListadoQueNoTermina().EsperaDebounce);
    [Fact] public void Productos_ListadoSinPermiso_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosProductos.ListadoSinPermiso().EsperaDebounce);
    [Fact] public void Gastos_Listado_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosGastos.Listado(_ => Task.FromResult<IReadOnlyList<Gasto>>([])).EsperaDebounce);
    [Fact] public void ControlPoa_Vm_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosControlPoa.Vm(() => Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>([])).EsperaDebounce);
    [Fact] public void LibroCaja_Vm_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosLibroCaja.Vm(() => Task.FromResult(DatosLibroCaja.Octubre)).EsperaDebounce);
    [Fact] public void Documentos_ListadoVm_SinEspera() => Assert.Equal(TimeSpan.Zero, DatosDocumentos.ListadoVm(() => Task.FromResult(DatosDocumentos.Activos)).EsperaDebounce);

    [Fact]
    public void TodoViewModelConDebounce_TieneSuFabricaVerificada()
    {
        // Tipos de StockApp.Presentation con una propiedad de instancia NO pública "EsperaDebounce" de tipo TimeSpan.
        var conDebounce = typeof(ViewModelBase).Assembly.GetTypes()
            .Where(t => t.GetProperty("EsperaDebounce", BindingFlags.Instance | BindingFlags.NonPublic)?.PropertyType == typeof(TimeSpan))
            .Select(t => t.Name).OrderBy(n => n).ToArray();
        Assert.True(new[] { "ControlPoaViewModel", "DocumentoListViewModel", "GastosViewModel", "LibroCajaViewModel", "ProductoListViewModel" }.SequenceEqual(conDebounce),
            "un ViewModel con RecargaConDebounce nuevo necesita su fábrica de datos fijos con EsperaDebounce = TimeSpan.Zero y su fila en este archivo; hoy: " + string.Join(", ", conDebounce));
    }
}
