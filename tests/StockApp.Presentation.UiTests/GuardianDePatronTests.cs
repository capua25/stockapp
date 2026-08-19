using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardian de bloque para el catalogo de patrones P0-P7 (Task 6.0 del plan de Fase B,
/// 2026-08-19). 42 de las 55 vistas restantes de la Fase B no tienen ni un test de UI propio;
/// escribir uno por vista seria un plan mas grande que el refactor. El compilador de bindings
/// (AvaloniaUseCompiledBindingsByDefault=true) ya cubre los bindings — lo que NO ve es "esta
/// vista tiene un HeaderVista con el titulo correcto", "su margen exterior es el estandar" y
/// "no quedo ningun Opacity literal decorativo". Eso es lo que este guardian mide, sobre N
/// vistas de golpe, con un solo [AvaloniaTheory] por invariante.
///
/// Las tandas 7-12 agregan sus propias filas a las mismas InlineData en vez de escribir un
/// guardian nuevo cada vez.
///
/// Titulo/Eyebrow en null: para las vistas cuyo HeaderVista toma el titulo de un binding a VM
/// (P3: ProductoFormView, MovimientoFormControl; P0: InicioView usa {Binding Saludo}) o que no
/// llevan HeaderVista propio (P5: EntradaRegistroView/SalidaRegistroView, heredan el de
/// MovimientoFormControl), PatronHelpers.Montar no asigna ViewModel, asi que el texto no se
/// puede verificar literal en este banco de pruebas. Se deja en null y el test solo exige que
/// el HeaderVista exista (o, en el caso P5, que el heredado de MovimientoFormControl llegue al
/// arbol visual una vez que la Task 6.4/6.5 lo agregue).
/// </summary>
public class GuardianDePatronTests
{
    [AvaloniaTheory]
    [InlineData(typeof(ProductoListView), "Productos", "CATÁLOGO")]
    [InlineData(typeof(MovimientoHistorialView), "Historial de movimientos", "MOVIMIENTOS")]
    [InlineData(typeof(ProductoFormView), null, null)]
    [InlineData(typeof(MovimientoFormControl), null, null)]
    [InlineData(typeof(InicioView), null, "INICIO")]
    [InlineData(typeof(EntradaRegistroView), null, null)]
    [InlineData(typeof(SalidaRegistroView), null, null)]
    [InlineData(typeof(IngresoPorFacturaView), "Ingreso de stock por factura", "MOVIMIENTOS")]
    public void Vista_TieneHeaderVistaConElTituloEsperado(Type tipoVista, string? titulo, string? eyebrow)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var header = PatronHelpers.HeaderDe(vista);

        Assert.True(header is not null,
            $"{tipoVista.Name} no tiene un HeaderVista en su arbol visual.");

        if (titulo is not null)
            Assert.Equal(titulo, header!.Titulo);
        if (eyebrow is not null)
            Assert.Equal(eyebrow, header!.Eyebrow);
    }

    public static readonly TheoryData<Type> VistasDeLaTanda = new()
    {
        typeof(ProductoListView),
        typeof(MovimientoHistorialView),
        typeof(ProductoFormView),
        typeof(MovimientoFormControl),
        typeof(InicioView),
        typeof(EntradaRegistroView),
        typeof(SalidaRegistroView),
        typeof(IngresoPorFacturaView),
    };

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    public void Vista_TieneMargenExteriorEstandar(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var margen = PatronHelpers.MargenExteriorDe(vista);

        Assert.True(margen is not null,
            $"{tipoVista.Name} no tiene ningun panel con Margin distinto de default.");
        Assert.Equal(new Thickness(24), margen!.Value);
    }

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    public void Vista_NoTieneOpacidadesLiterales(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"{tipoVista.Name} tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    public void Vista_NoTieneUnSegundoBotonPrimario(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var primarios = vista.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("primary") && ArbolVisual.EsVisibleEnArbol(b))
            .ToList();

        Assert.True(primarios.Count <= 1,
            $"{tipoVista.Name} tiene {primarios.Count} botones primarios visibles a la vez.");
    }
}
