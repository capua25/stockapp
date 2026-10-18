using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Invariantes P0-P9 del tema viejo para lo único que sigue en v1, la importación (Fase 8 fuera del rediseño,
/// decisión de Martin 2026-10-17). Las tres vistas conservan HeaderVista, Border.card, Button.primary y demás piezas
/// del tema viejo; este guardián custodia que sigan cumpliendo el patrón con el que nacieron. El resto de las vistas
/// las custodia <see cref="GuardianSistemaV2Tests"/>.
/// </summary>
public class GuardianImportacionV1Tests
{
    /// <summary>Las tres vistas de importación que siguen con el tema viejo.</summary>
    public static readonly HashSet<Type> VistasV1Vivas = new()
    {
        typeof(ImportacionView),
        typeof(NuevaImportacionView),
        typeof(HistorialImportacionesView),
    };

    /// <summary>
    /// Vistas que no aportan cromo de vista propio: están embebidas en <see cref="ImportacionView"/> (pestañas), que ya
    /// puso el HeaderVista y el MargenVista. Corren los invariantes que SÍ les aplican (opacidad, botón primario), más
    /// uno propio e invertido (<see cref="VistaEmbebida_NoDuplicaElMargenDeVista"/>).
    /// </summary>
    public static TheoryData<Type> VistasEmbebidas => new()
    {
        typeof(HistorialImportacionesView),
        typeof(NuevaImportacionView),
    };

    public static TheoryData<Type> TodasLasVistasV1 => new(VistasV1Vivas.OrderBy(t => t.Name));

    [AvaloniaTheory]
    [InlineData(typeof(ImportacionView), "Importar planillas", "FINANZAS")]
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

    [AvaloniaTheory]
    [InlineData(typeof(ImportacionView))]
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
    /// -- el contenedor (ImportacionView) ya lo aplico, y duplicarlo da 48px
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
    [MemberData(nameof(TodasLasVistasV1))]
    public void Vista_NoTieneOpacidadesLiterales(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"{tipoVista.Name} tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    /// <summary>
    /// <see cref="ImportacionView"/> embebe <c>NuevaImportacionView</c> como contenido de su primer
    /// TabItem, y <see cref="PatronHelpers.Montar"/> no asigna ViewModel: los tres
    /// <c>IsVisible="{Binding PasoActual, Converter=...}"</c> de <c>NuevaImportacionView</c>
    /// quedan sin resolver y caen al default `true` de la propiedad. Eso deja 3 botones primarios
    /// "visibles" dentro del arbol de <c>ImportacionView</c> sin que <c>ImportacionView</c> tenga
    /// ninguna culpa: no agrega ningun <c>Classes="primary"</c> propio. Por la misma razon
    /// <c>NuevaImportacionView</c> montada sin VM daria 3 primarios visibles. El reemplazo real es
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

    [AvaloniaTheory]
    [MemberData(nameof(TodasLasVistasV1))]
    public void Vista_NoTieneUnSegundoBotonPrimario(Type tipoVista)
    {
        if (VistasExentasPorEmbeberUnWizardP8.Contains(tipoVista))
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
