using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>Helpers para tipear en el TextBox interno (PART_TextBox) de un NumericUpDown.</summary>
internal static class NumericUpDownPrueba
{
    public static TextBox Interno(NumericUpDown nud)
        => nud.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == nud);

    public static void Tipear(NumericUpDown nud, string texto)
    {
        var interno = Interno(nud);
        interno.Focus();
        Dispatcher.UIThread.RunJobs();
        interno.Text = texto;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Saca el foco del NumericUpDown (dispara su OnLostFocus, que es el que revertía).</summary>
    public static void SacarElFoco(Window window, NumericUpDown nud)
    {
        var otro = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.IsEffectivelyEnabled && t.IsEffectivelyVisible && t.TemplatedParent is null
                        && !t.GetVisualAncestors().OfType<NumericUpDown>().Any());
        otro.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(nud.IsKeyboardFocusWithin);
    }

    public static void AssertErrorVisible(NumericUpDown nud, string mensaje)
    {
        Assert.True(DataValidationErrors.GetHasErrors(nud));
        Assert.Contains(nud.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == mensaje && ArbolVisual.EsVisibleEnArbol(t));
    }

    public static void AssertBloqueadoConTooltip(Button boton)
    {
        Assert.False(boton.IsEffectivelyEnabled);
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(boton));
        Assert.True(ToolTip.GetShowOnDisabled(boton));
    }

    public static Button BotonPorCommand(Window window, object comando)
        => window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, comando));
}

/// <summary>
/// Los NumericUpDown de las vistas REALES con texto inválido: marcan el error, bloquean el botón
/// que usaría el valor (Guardar, Analizar, Buscar) con su tooltip, y no vuelven en silencio al
/// valor anterior al perder el foco. Ver <see cref="NumericUpDownTextoInvalidoTests"/> para el
/// contrato del behavior; LibroCaja (Actualizar) vive en <see cref="LibroCajaViewTests"/>.
/// </summary>
public class NumericUpDownVistasRealesTests
{
    // ── Documento: "Año" del formulario -> Guardar ─────────────────────────

    private static (Window Window, DocumentoFormViewModel Vm, NumericUpDown Anio) MontarDocumentoNuevo()
    {
        var sesion = new SesionFake(RolUsuario.Admin);
        var adjuntos = new AdjuntosDocumentoPanelViewModel(
            new AdjuntoDocumentoServiceFake(), new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), new ConfirmacionServiceFake(), sesion);
        var vm = new DocumentoFormViewModel(
            new DocumentoServiceFake(), sesion, new NavigationRecorderDocumentosFake(),
            new ConfirmacionServiceFake(), adjuntos, new TareaServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=GestionMunicipal"
                    Width="760" Height="900">
                <docs:DocumentoFormView />
            </Window>
            """, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        vm.CargarParaCrear();
        Dispatcher.UIThread.RunJobs();

        vm.Numero = "PRUEBA ESTRUCTURAL 1";
        vm.Descripcion = "PRUEBA ESTRUCTURAL";
        vm.FechaEmisionSeleccionada = DateTime.Today;
        vm.AnioSeleccionado = 2026;
        Dispatcher.UIThread.RunJobs();

        var anio = window.GetVisualDescendants().OfType<CampoFormulario>().Single(c => c.Etiqueta == "Año")
            .GetVisualDescendants().OfType<NumericUpDown>().Single();
        return (window, vm, anio);
    }

    [AvaloniaTheory]
    [InlineData("20a5")]
    [InlineData("1999")]
    public void DocumentoForm_AnioInvalido_MarcaElError_BloqueaGuardar_YNoVuelveAlValorAnterior(string texto)
    {
        var (window, vm, anio) = MontarDocumentoNuevo();
        var guardar = NumericUpDownPrueba.BotonPorCommand(window, vm.GuardarCommand);
        Assert.True(guardar.IsEffectivelyEnabled);

        NumericUpDownPrueba.Tipear(anio, texto);

        NumericUpDownPrueba.AssertErrorVisible(anio, "Ingresá un año válido, entre 2000 y 2100.");
        NumericUpDownPrueba.AssertBloqueadoConTooltip(guardar);
        Assert.Equal(2026, vm.AnioSeleccionado);

        NumericUpDownPrueba.SacarElFoco(window, anio);

        Assert.Equal(texto, NumericUpDownPrueba.Interno(anio).Text);
        NumericUpDownPrueba.AssertErrorVisible(anio, "Ingresá un año válido, entre 2000 y 2100.");
        NumericUpDownPrueba.AssertBloqueadoConTooltip(guardar);

        NumericUpDownPrueba.Tipear(anio, "2025");

        Assert.False(DataValidationErrors.GetHasErrors(anio));
        Assert.Equal(2025, vm.AnioSeleccionado);
        Assert.True(guardar.IsEffectivelyEnabled);
        Assert.Null(ToolTip.GetTip(guardar));
    }

    // ── Documento: filtro "Año" del historial -> Buscar ─────────────────────

    [AvaloniaFact]
    public void DocumentoList_FiltroAnio_Invalido_BloqueaBuscar_YVacioSignificaTodos()
    {
        var vm = new DocumentoListViewModel(
            new DocumentoServiceFake(), new SesionFake(RolUsuario.Admin),
            new NavigationRecorderDocumentosFake(), new ConfirmacionServiceFake());
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=GestionMunicipal"
                    Width="1100" Height="800">
                <docs:DocumentoListView />
            </Window>
            """, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var anio = window.GetVisualDescendants().OfType<CampoFormulario>().Single(c => c.Etiqueta == "Año")
            .GetVisualDescendants().OfType<NumericUpDown>().Single();
        var buscar = NumericUpDownPrueba.BotonPorCommand(window, vm.BuscarHistorialCommand);

        NumericUpDownPrueba.Tipear(anio, "20a5");

        NumericUpDownPrueba.AssertErrorVisible(anio, "Ingresá un año válido, entre 2000 y 2100.");
        NumericUpDownPrueba.AssertBloqueadoConTooltip(buscar);

        NumericUpDownPrueba.Tipear(anio, "");

        Assert.False(DataValidationErrors.GetHasErrors(anio));
        Assert.Null(vm.FiltroHistorialAnio);
        Assert.True(buscar.IsEffectivelyEnabled);
    }

    // ── Importación: "Ejercicio" -> Analizar ───────────────────────────────

    [AvaloniaFact]
    public async Task NuevaImportacion_EjercicioInvalido_BloqueaAnalizar_YNoVuelveAlValorAnterior()
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(), new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0), new SaldosTotalesPoaOds(0m, 0m)));
        var vm = new NuevaImportacionViewModel(
            service, new ServicioSeleccionArchivoFake(), new ConfirmacionServiceFake(),
            new FuenteFinanciamientoServiceFake(new List<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(new List<RubroGasto>()),
            new ProveedorServiceFake(new List<Proveedor>()),
            new LineaPoaServiceFake(new List<LineaPoa>()));
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                    Width="1000" Height="700">
                <fin:NuevaImportacionView />
            </Window>
            """, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);
        vm.Ejercicio = 2026;
        Dispatcher.UIThread.RunJobs();
        var analizar = NumericUpDownPrueba.BotonPorCommand(window, vm.AnalizarCommand);
        var ejercicio = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.IsEffectivelyVisible);
        Assert.True(analizar.IsEffectivelyEnabled);

        NumericUpDownPrueba.Tipear(ejercicio, "abc");

        NumericUpDownPrueba.AssertErrorVisible(ejercicio, "Ingresá un año válido, entre 2000 y 2100.");
        NumericUpDownPrueba.AssertBloqueadoConTooltip(analizar);
        Assert.Equal(2026, vm.Ejercicio);

        // El paso 1 no tiene otro TextBox: se saca el foco hacia el botón de la planilla.
        window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.SeleccionarPoaCommand)).Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.False(ejercicio.IsKeyboardFocusWithin);
        Assert.Equal("abc", NumericUpDownPrueba.Interno(ejercicio).Text);
        NumericUpDownPrueba.AssertBloqueadoConTooltip(analizar);
    }
}
