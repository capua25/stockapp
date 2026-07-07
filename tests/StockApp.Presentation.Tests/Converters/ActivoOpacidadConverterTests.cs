using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica el converter usado para atenuar visualmente las filas de entidades de catálogo
/// dadas de baja (Categoría/Proveedor/UnidadMedida inactivas), Capa C del fix de crash al dar
/// de baja (ver UnidadMedidaListView/CategoriaListView/ProveedorListView).
/// </summary>
public class ActivoOpacidadConverterTests
{
    private static readonly ActivoOpacidadConverter Sut = ActivoOpacidadConverter.Instance;

    [Fact]
    public void Convert_Activo_DevuelveOpacidadCompleta()
    {
        var resultado = Sut.Convert(true, typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(1.0, resultado);
    }

    [Fact]
    public void Convert_Inactivo_DevuelveOpacidadReducida()
    {
        var resultado = Sut.Convert(false, typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(0.55, resultado);
    }

    [Fact]
    public void Convert_ValorNoBooleano_DevuelveOpacidadCompleta()
    {
        var resultado = Sut.Convert(null, typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(1.0, resultado);
    }
}
