using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Documentos;
using StockApp.Presentation.Views.Finanzas;
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
    [InlineData(typeof(CategoriaListView), "Categorías", "CATÁLOGO")]
    [InlineData(typeof(ProveedorListView), "Proveedores", "CATÁLOGO")]
    [InlineData(typeof(UnidadMedidaListView), "Unidades de medida", "CATÁLOGO")]
    [InlineData(typeof(CategoriaFormView), null, null)]
    [InlineData(typeof(ProveedorFormView), null, null)]
    [InlineData(typeof(UnidadMedidaFormView), null, null)]
    [InlineData(typeof(MaestrosFinanzasView), "Maestros de finanzas", "FINANZAS")]
    [InlineData(typeof(ImportacionView), "Importar planillas", "FINANZAS")]
    [InlineData(typeof(FuenteFinanciamientoFormView), null, "FINANZAS")]
    [InlineData(typeof(RubroGastoFormView), null, "FINANZAS")]
    [InlineData(typeof(IngresoFormView), null, "FINANZAS")]
    [InlineData(typeof(LineaPoaFormView), null, "FINANZAS")]
    [InlineData(typeof(GastoFormView), null, "FINANZAS")]
    [InlineData(typeof(GastosView), "Gastos y facturas", "FINANZAS")]
    [InlineData(typeof(IngresosView), "Ingresos de caja", "FINANZAS")]
    [InlineData(typeof(ControlPoaView), "Control POA", "FINANZAS")]
    [InlineData(typeof(LibroCajaView), "Libro caja", "FINANZAS")]
    [InlineData(typeof(CalendarioPagosView), "Calendario de pagos", "FINANZAS")]
    [InlineData(typeof(PagosGastoView), "Pagos de la factura", "FINANZAS")]
    [InlineData(typeof(DocumentoListView), "Documentos administrativos", "DOCUMENTOS")]
    [InlineData(typeof(DocumentoFormView), "Nuevo documento", "DOCUMENTOS")]
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
        typeof(CategoriaListView),
        typeof(ProveedorListView),
        typeof(UnidadMedidaListView),
        typeof(CategoriaFormView),
        typeof(ProveedorFormView),
        typeof(UnidadMedidaFormView),
        typeof(MaestrosFinanzasView),
        typeof(ImportacionView),
        typeof(FuenteFinanciamientoFormView),
        typeof(RubroGastoFormView),
        typeof(IngresoFormView),
        typeof(LineaPoaFormView),
        typeof(GastoFormView),
        typeof(GastosView),
        typeof(IngresosView),
        typeof(ControlPoaView),
        typeof(LibroCajaView),
        typeof(CalendarioPagosView),
        typeof(PagosGastoView),
        typeof(DocumentoListView),
        typeof(DocumentoFormView),
    };

    /// <summary>
    /// Vistas que se renderizan como contenido de un TabItem/ContentControl de otra vista (P2-emb,
    /// P1-emb, P5, Task 8.1 de la Fase B). No llevan HeaderVista propio ni MargenVista: el
    /// contenedor ya los puso. Corren los invariantes que SI les aplican (opacidad, boton
    /// primario), mas uno propio e invertido (<see cref="VistaEmbebida_NoDuplicaElMargenDeVista"/>).
    /// </summary>
    public static readonly TheoryData<Type> VistasEmbebidas = new()
    {
        typeof(FuenteFinanciamientoListView),
        typeof(RubroGastoListView),
        typeof(LineaPoaListView),
        typeof(HistorialImportacionesView),
        typeof(NuevaImportacionView),
        typeof(AdjuntosPanelView),
        typeof(AdjuntosDocumentoPanelView),
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

    /// <summary>
    /// Ruling B-17: las vistas embebidas en un TabControl NO pueden traer MargenVista (24) propio
    /// -- el contenedor (MaestrosFinanzasView/ImportacionView) ya lo aplico, y duplicarlo da 48px
    /// de aire (bug C3 del esbozo de B2). Su Margin="0,12,0,0" (separacion visual con el borde de
    /// la pestaña) se conserva: el invariante es "distinto de Thickness(24)", no "sin margen".
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(VistasEmbebidas))]
    public void VistaEmbebida_NoDuplicaElMargenDeVista(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var margen = PatronHelpers.MargenExteriorDe(vista);

        Assert.True(margen != new Thickness(24),
            $"{tipoVista.Name} es embebida y trae MargenVista propio: el contenedor ya lo aplica, "
            + "queda 48px de aire duplicado.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    [MemberData(nameof(VistasEmbebidas))]
    public void Vista_NoTieneOpacidadesLiterales(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"{tipoVista.Name} tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    /// <summary>
    /// GAP DE PLAN encontrado al ejecutar la Task 8.1 (el plan esperaba "exactamente 2 fallos
    /// nuevos" en el Step 2 de esa task; hubo un tercero real). <see cref="ImportacionView"/>
    /// embebe <c>NuevaImportacionView</c> (P8, Ruling B-18 punto 3) como contenido de su primer
    /// TabItem, y <see cref="PatronHelpers.Montar"/> no asigna ViewModel: los tres
    /// <c>IsVisible="{Binding PasoActual, Converter=...}"</c> de <c>NuevaImportacionView</c>
    /// quedan sin resolver y caen al default `true` de la propiedad (mismo comportamiento que
    /// documenta la Task 6.7 para un <c>IsVisible</c> sin resolver). Eso deja 3 botones primarios
    /// "visibles" dentro del arbol de <c>ImportacionView</c> sin que <c>ImportacionView</c> tenga
    /// ninguna culpa: no agrega ningun <c>Classes="primary"</c> propio (verificado: los otros tres
    /// invariantes -- header, margen, opacidad -- SI le aplican y pasan en verde).
    ///
    /// Task 8.4: <c>NuevaImportacionView</c> se agrego a <see cref="VistasEmbebidas"/> (P8) y por
    /// lo tanto ENTRA a este mismo invariante generico -- y por la misma razon (montada sin VM,
    /// los 3 IsVisible sin resolver) tambien daria 3 primarios visibles. Se agrega ella misma
    /// aca, no solo su contenedor. El reemplazo real es
    /// <see cref="NuevaImportacionJerarquiaBotonesTests"/>: monta la vista CON un ViewModel real
    /// y verifica, para cada uno de los 3 valores de <c>PasoWizardImportacion</c>, que hay
    /// EXACTAMENTE un boton primario visible y que es el correcto -- eso custodia mas que este
    /// invariante generico, no menos.
    /// </summary>
    private static readonly HashSet<Type> VistasExentasPorEmbeberUnWizardP8 = new()
    {
        typeof(ImportacionView),
        typeof(NuevaImportacionView),
    };

    /// <summary>
    /// GAP DE PLAN encontrado en la Task 9.2 (Fase B, tanda 9): <c>DocumentoFormView</c> tiene dos
    /// <c>Classes="primary"</c> gateados por estado del ViewModel -- "Guardar" (alta,
    /// <c>IsVisible="{Binding EsNuevoDocumento}"</c>) y "Guardar cambios" (edicion,
    /// <c>IsVisible="{Binding PuedeEditar}"</c>). Verificado en <c>DocumentoFormViewModel.cs:88</c>:
    /// <c>PuedeEditar =&gt; !EsNuevoDocumento &amp;&amp; _documento is { EsActivo: true }</c> -- el
    /// termino <c>!EsNuevoDocumento</c> hace que los dos NUNCA sean true a la vez en produccion.
    /// Pero <see cref="PatronHelpers.Montar"/> no asigna DataContext (mismo mecanismo que el
    /// Ruling B-20 documenta para los dos <c>HeaderVista</c> de esta vista): sin VM real, ambos
    /// <c>IsVisible</c> caen al default `true` de la propiedad y el guardian ve dos primarios. Los
    /// 10 casos de <c>DocumentoFormViewGatesTests</c> (Task 9.0) ya ejercitan las dos ramas con un
    /// VM real via <c>ArbolVisual.EsVisibleEnArbol</c>; no se duplica esa cobertura aca.
    /// </summary>
    private static readonly HashSet<Type> VistasExentasPorPrimariosMutuamenteExcluyentesSinVm = new()
    {
        typeof(DocumentoFormView),
    };

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    [MemberData(nameof(VistasEmbebidas))]
    public void Vista_NoTieneUnSegundoBotonPrimario(Type tipoVista)
    {
        if (VistasExentasPorEmbeberUnWizardP8.Contains(tipoVista))
            return;
        if (VistasExentasPorPrimariosMutuamenteExcluyentesSinVm.Contains(tipoVista))
            return;

        var vista = PatronHelpers.Montar(tipoVista);
        var primarios = vista.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("primary") && ArbolVisual.EsVisibleEnArbol(b))
            .ToList();

        Assert.True(primarios.Count <= 1,
            $"{tipoVista.Name} tiene {primarios.Count} botones primarios visibles a la vez.");
    }
}
