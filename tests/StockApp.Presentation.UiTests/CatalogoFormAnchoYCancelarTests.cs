using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardian de las 6 vistas de catálogo de un solo campo (o dos, UnidadMedidaFormView) reportadas
/// como "demasiado angostas y sin botón de cancelar": ZonaFormView, DimensionTematicaFormView,
/// OrganismoResponsableFormView, OrigenFinanciamientoFormView (Tareas), CategoriaFormView y
/// UnidadMedidaFormView (Productos). Antes de este archivo, ningún test de UI verificaba la
/// presencia de un botón Cancelar en estos formularios -- el gap lo detectó el usuario usando la
/// app real, no un test.
///
/// <see cref="BotonCancelar_ExisteVisibleConComandoResueltoYNavegaAlListado"/> también cubre
/// ProductoFormView y ProveedorFormView (<see cref="CasosSoloCancelar"/>): a esas dos NO les
/// faltaba el ancho, solo el botón Cancelar -- por eso no entran al banco <see cref="Casos"/> que
/// también alimenta <see cref="Card_NoQuedaAngostaNiCentrada"/>.
///
/// El patrón de referencia (ya resuelto en el repo) es FuenteFinanciamientoFormView.axaml: el card
/// NO lleva MaxWidth/HorizontalAlignment (queda VerticalAlignment="Top" nomás); el StackPanel
/// INTERNO es el que lleva MaxWidth="420" HorizontalAlignment="Left". Por eso
/// <see cref="PanelDelFormularioDe"/> mide el Child del Border.card, no el Border en sí -- es el
/// contenedor "correspondiente" que el brief pide inspeccionar.
///
/// A diferencia de <see cref="GuardianDePatronTests"/>/<see cref="PatronHelpers.Montar"/> (que monta
/// sin ViewModel a propósito, porque solo mide estructura), acá hace falta un ViewModel REAL con
/// Command resuelto -- si no, no hay forma de distinguir "el botón existe pero el binding está
/// roto" de "el botón no existe". Reusa los fakes de TareaFakes.cs/IngresoPorFacturaFakes.cs
/// (mismo criterio del proyecto: no referencia Moq).
/// </summary>
public class CatalogoFormAnchoYCancelarTests
{
    /// <summary>
    /// Un caso por vista: cómo construirla con un ViewModel real (más su
    /// <see cref="NavigationRecorderFake"/>, para poder verificar a qué pantalla vuelve Cancelar)
    /// y a qué ViewModel debería navegar Cancelar -- el mismo destino al que navega GuardarAsync
    /// cuando termina bien (verificado leyendo cada FormViewModel.cs).
    /// </summary>
    private static readonly (string Nombre, Func<(Control Vista, NavigationRecorderFake Recorder)> Fabrica, Type DestinoEsperado)[] Casos =
    {
        ("ZonaFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new ZonaFormViewModel(new ZonaServiceFake(), recorder);
            return ((Control)new ZonaFormView { DataContext = vm }, recorder);
        }, typeof(CatalogosTareaViewModel)),

        ("DimensionTematicaFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new DimensionTematicaFormViewModel(new DimensionTematicaServiceFake(), recorder);
            return ((Control)new DimensionTematicaFormView { DataContext = vm }, recorder);
        }, typeof(CatalogosTareaViewModel)),

        ("OrganismoResponsableFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new OrganismoResponsableFormViewModel(new OrganismoResponsableServiceFake(), recorder);
            return ((Control)new OrganismoResponsableFormView { DataContext = vm }, recorder);
        }, typeof(CatalogosTareaViewModel)),

        ("OrigenFinanciamientoFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new OrigenFinanciamientoFormViewModel(new OrigenFinanciamientoServiceFake(), recorder);
            return ((Control)new OrigenFinanciamientoFormView { DataContext = vm }, recorder);
        }, typeof(CatalogosTareaViewModel)),

        ("CategoriaFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new CategoriaFormViewModel(new CategoriaServiceFake(new List<Categoria>()), recorder);
            return ((Control)new CategoriaFormView { DataContext = vm }, recorder);
        }, typeof(CategoriaListViewModel)),

        ("UnidadMedidaFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new UnidadMedidaFormViewModel(new UnidadMedidaServiceFake(new List<UnidadMedida>()), recorder);
            return ((Control)new UnidadMedidaFormView { DataContext = vm }, recorder);
        }, typeof(UnidadMedidaListViewModel)),
    };

    /// <summary>
    /// ProductoFormView y ProveedorFormView YA tenían el ancho correcto (MaxWidth 480/460 puesto
    /// directamente en el Border.card, sin HorizontalAlignment=Center) -- no sufren el bug de
    /// "angosta y centrada" que persigue <see cref="Card_NoQuedaAngostaNiCentrada"/>, así que NO se
    /// agregan a <see cref="Casos"/> (ese test fallaría sin motivo: su StackPanel interno no lleva
    /// MaxWidth/HorizontalAlignment propios, la propiedad vive en el Border). Solo les faltaba el
    /// botón Cancelar, por eso arman un segundo banco de casos que <em>solo</em> alimenta
    /// <see cref="BotonCancelar_ExisteVisibleConComandoResueltoYNavegaAlListado"/>.
    /// </summary>
    private static readonly (string Nombre, Func<(Control Vista, NavigationRecorderFake Recorder)> Fabrica, Type DestinoEsperado)[] CasosSoloCancelar =
    {
        ("ProductoFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new ProductoFormViewModel(
                new ProductoServiceFake(),
                new UnidadMedidaServiceFake(new List<UnidadMedida>()),
                new CategoriaServiceFake(new List<Categoria>()),
                recorder);
            return ((Control)new ProductoFormView { DataContext = vm }, recorder);
        }, typeof(ProductoListViewModel)),

        ("ProveedorFormView", () =>
        {
            var recorder = new NavigationRecorderFake();
            var vm = new ProveedorFormViewModel(new ProveedorServiceFake(new List<Proveedor>()), recorder);
            return ((Control)new ProveedorFormView { DataContext = vm }, recorder);
        }, typeof(ProveedorListViewModel)),
    };

    private static Control Montar(Control vista)
    {
        var window = new Window { Width = 1200, Height = 900, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return vista;
    }

    private static Border CardDe(Control vista)
        => vista.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card"));

    /// <summary>El contenedor "correspondiente" del brief: el Child del Border.card, que es donde
    /// vive MaxWidth/HorizontalAlignment en el patrón de referencia (no en el Border).</summary>
    private static Control PanelDelFormularioDe(Control vista)
        => (Control)CardDe(vista).Child!;

    private static Button? BotonCancelar(Control vista)
        => vista.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("secondary") && (b.Content as string) == "Cancelar");

    /// <summary>
    /// Custodia el ancho: el panel del formulario debe estar alineado a la izquierda (no
    /// "flotando" centrado) y con MaxWidth >= 420 (patrón FuenteFinanciamientoFormView, NO los 380
    /// del bug reportado). Además, barre TODO el árbol por si alguien reintrodujera el combo viejo
    /// (MaxWidth<=380 + Center) en cualquier otro control -- así la aserción no depende de que el
    /// refactor futuro mantenga la propiedad exactamente en el mismo nodo que hoy.
    /// </summary>
    [AvaloniaFact]
    public void Card_NoQuedaAngostaNiCentrada()
    {
        var errores = new List<string>();

        foreach (var (nombre, fabrica, _) in Casos)
        {
            var (vistaSinMontar, _) = fabrica();
            var vista = Montar(vistaSinMontar);

            var panel = PanelDelFormularioDe(vista);

            if (panel.HorizontalAlignment != HorizontalAlignment.Left)
                errores.Add($"{nombre}: el panel del formulario tiene HorizontalAlignment={panel.HorizontalAlignment}, se esperaba Left.");

            if (!(panel.MaxWidth >= 420))
                errores.Add($"{nombre}: el panel del formulario tiene MaxWidth={panel.MaxWidth}, se esperaba >= 420.");

            var combosViejos = vista.GetVisualDescendants().OfType<Control>()
                .Where(c => c.HorizontalAlignment == HorizontalAlignment.Center && c.MaxWidth <= 380)
                .ToList();
            if (combosViejos.Count > 0)
                errores.Add($"{nombre}: quedó un control angosto y centrado (patrón viejo MaxWidth<=380 + Center): "
                    + string.Join(", ", combosViejos.Select(c => c.GetType().Name)));
        }

        Assert.True(errores.Count == 0, string.Join("\n", errores));
    }

    /// <summary>
    /// Custodia el botón Cancelar: existe, es visible en el árbol (no solo instanciado), es
    /// secundario (no compite con Guardar) y su Command está REALMENTE resuelto -- no solo que el
    /// botón esté ahí. Además ejecuta el comando y confirma que navega al mismo destino que
    /// GuardarAsync cuando termina bien, para que Cancelar no pueda apuntar a la pantalla
    /// equivocada sin que el test lo note.
    /// </summary>
    [AvaloniaFact]
    public void BotonCancelar_ExisteVisibleConComandoResueltoYNavegaAlListado()
    {
        var errores = new List<string>();

        foreach (var (nombre, fabrica, destinoEsperado) in Casos.Concat(CasosSoloCancelar))
        {
            var (vistaSinMontar, recorder) = fabrica();
            var vista = Montar(vistaSinMontar);

            var boton = BotonCancelar(vista);
            if (boton is null)
            {
                errores.Add($"{nombre}: no tiene un Button Classes=\"secondary\" Content=\"Cancelar\".");
                continue;
            }

            if (!ArbolVisual.EsVisibleEnArbol(boton))
                errores.Add($"{nombre}: el botón Cancelar existe pero no es visible en el árbol.");

            if (boton.Command is null)
            {
                errores.Add($"{nombre}: el botón Cancelar no tiene Command resuelto (binding roto o comando ausente en el ViewModel).");
                continue;
            }

            if (boton.Command.CanExecute(null))
                boton.Command.Execute(null);

            if (recorder.UltimoTipoNavegado != destinoEsperado)
                errores.Add($"{nombre}: Cancelar navegó a {recorder.UltimoTipoNavegado?.Name ?? "(nada)"}, se esperaba {destinoEsperado.Name}.");
        }

        Assert.True(errores.Count == 0, string.Join("\n", errores));
    }
}
