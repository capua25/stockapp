using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad de DocumentoFormView.axaml -- Task 9.0 del plan de Fase B, cierra la deuda
/// de las Tasks 4.3/4.4 de la Fase A (Ruling B-5): esta vista NO tenía ningún test de UI antes de
/// esta task. DocumentoFormView.axaml.cs NO tiene DataContextChanged (a diferencia de
/// DocumentoListView) -- CargarParaCrear()/CargarParaVerAsync() se invocan a mano, igual que hace
/// INavigationService.Navegar&lt;TVm&gt;(Action&lt;TVm&gt;) en producción.
///
/// Fórmulas (DocumentoFormViewModel.cs):
/// - EsAdmin              => _session.RolActual == Admin
/// - PuedeEditar           => !EsNuevoDocumento && _documento is { EsActivo: true }
/// - PuedeEditarCampos     => EsNuevoDocumento || PuedeEditar   -- OR de ramas MUTUAMENTE
///   EXCLUYENTES (PuedeEditar exige !EsNuevoDocumento): se cubre en sus dos ramas por separado,
///   más el caso donde ninguna se cumple.
/// - PuedeIniciar          => !EsNuevoDocumento && Estado == Pendiente && PuedeTransicionarA(EnProceso)
/// - PuedeVolverAPendiente => !EsNuevoDocumento && PuedeTransicionarA(Pendiente)
/// - PuedeFinalizar        => !EsNuevoDocumento && PuedeTransicionarA(Finalizado)
/// - PuedeAnular           => EsAdmin && !EsNuevoDocumento && PuedeTransicionarA(Anulado)
/// - PuedeReabrir          => EsAdmin && !EsNuevoDocumento && EsCerrado
///
/// A diferencia de DocumentoListView, acá los 5 gates de visibilidad NO viven en una plantilla
/// (:63-67) -- se verifican montando la vista con el VM directo. Los 5 gates de HABILITACIÓN
/// (:32,36,40,45,50) usan IsEnabled, que ArbolVisual.EsVisibleEnArbol NO detecta (mira IsVisible):
/// se lee control.IsEnabled directo.
///
/// CORRECCIÓN al relevamiento del plan: el plan afirmaba que "IsEnabled en Avalonia es efectivo/
/// heredado". Verificado por diagnóstico directo que es AL REVÉS -- IsEnabled es un valor LOCAL,
/// no heredado: el TextBox interno de un CalendarDatePicker deshabilitado (su template trae uno
/// propio, Watermark "dd/mm/aaaa") reporta IsEnabled=True aunque el CalendarDatePicker que lo
/// contiene tenga IsEnabled=False. Mismo problema con NumericUpDown y ComboBox (también
/// TemplatedControl con partes internas propias). Por eso CamposDelFormulario filtra cualquier
/// TextBox descendiente de esos tres tipos: sin el filtro, indexar "los primeros dos TextBox" da
/// falso verde en 14a/14b (agarra el TextBox interno, siempre habilitado) y falso rojo en 14c.
/// </summary>
public class DocumentoFormViewGatesTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=GestionMunicipal"
                Width="760" Height="900">
            <docs:DocumentoFormView />
        </Window>
        """;

    private static DocumentoAdministrativo DocumentoDe(int id, EstadoDocumento estado) => new()
    {
        Id = id, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = "Descripción",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static DocumentoFormViewModel CrearVm(RolUsuario rol)
    {
        var sesion = new SesionFake(rol);
        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            new AdjuntoDocumentoServiceFake(),
            new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(),
            new ConfirmacionServiceFake(),
            sesion);

        return new DocumentoFormViewModel(
            new DocumentoServiceFake(), sesion, new NavigationRecorderDocumentosFake(),
            new ConfirmacionServiceFake(), adjuntosPanel);
    }

    private static (Window Window, DocumentoFormViewModel Vm) MontarParaCrear(RolUsuario rol)
    {
        var vm = CrearVm(rol);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();

        vm.CargarParaCrear();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    private static async Task<(Window Window, DocumentoFormViewModel Vm)> MontarParaVerAsync(
        RolUsuario rol, DocumentoAdministrativo documento)
    {
        var vm = CrearVm(rol);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();

        await vm.CargarParaVerAsync(documento);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    private static Button BotonPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == texto);

    /// <summary>
    /// Los 5 campos gateados por PuedeEditarCampos (:32 Numero, :36 AnioSeleccionado,
    /// :40 TipoSeleccionado, :45 FechaEmisionSeleccionada, :50 Descripcion).
    ///
    /// TRAMPA descubierta por diagnóstico directo (no documentada en el plan): NumericUpDown,
    /// ComboBox y CalendarDatePicker son TemplatedControl -- cada uno realiza un TextBox INTERNO
    /// propio (p. ej. el de CalendarDatePicker trae Watermark "dd/mm/aaaa"), y ese TextBox interno
    /// reporta IsEnabled=True aunque el control contenedor esté deshabilitado: IsEnabled en
    /// Avalonia es un valor LOCAL, no efectivo/heredado (al revés de lo que decía el relevamiento
    /// del plan). Indexar "los primeros dos TextBox" a ciegas agarra el TextBox interno de
    /// NumericUpDown/ComboBox en vez de Descripcion, y ese SIEMPRE da IsEnabled=True -- falso
    /// verde en 14a/14b y falso rojo en 14c (confirmado con ZZZDiagTests descartable). El fix es
    /// excluir cualquier TextBox que sea descendiente de otro control templated de la lista.
    /// </summary>
    private static IReadOnlyList<Control> CamposDelFormulario(Window window)
    {
        var textBoxesDeNivelSuperior = window.GetVisualDescendants().OfType<TextBox>()
            .Where(tb => !EsDescendienteDeAlguno(tb, typeof(NumericUpDown), typeof(ComboBox), typeof(CalendarDatePicker)))
            .ToList();

        return new Control[]
        {
            textBoxesDeNivelSuperior[0], // Numero
            window.GetVisualDescendants().OfType<NumericUpDown>().First(),      // AnioSeleccionado
            window.GetVisualDescendants().OfType<ComboBox>().First(),           // TipoSeleccionado
            window.GetVisualDescendants().OfType<CalendarDatePicker>().First(), // FechaEmisionSeleccionada
            textBoxesDeNivelSuperior[1], // Descripcion
        };
    }

    private static bool EsDescendienteDeAlguno(Visual visual, params Type[] tiposAncestro)
    {
        for (var actual = visual.GetVisualParent(); actual is not null; actual = actual.GetVisualParent())
            if (tiposAncestro.Any(t => t.IsInstanceOfType(actual)))
                return true;
        return false;
    }

    // ---- Caso 9: Operador, alta (EsNuevoDocumento=true) ----

    [AvaloniaFact]
    public void Alta_Operador_BotonesDeTransicionOcultosYGuardarVisible()
    {
        var (window, _) = MontarParaCrear(RolUsuario.Operador);

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Iniciar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Volver a pendiente")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Finalizar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Reabrir")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Guardar")));
    }

    // ---- Casos 10-11: PuedeIniciar / PuedeAnular sobre documento Pendiente ----

    [AvaloniaFact]
    public async Task Detalle_OperadorPendiente_IniciarVisibleYRestoOculto()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Operador, DocumentoDe(1, EstadoDocumento.Pendiente));

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Iniciar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Volver a pendiente")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Finalizar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular")));
    }

    /// <summary>
    /// GAP encontrado por mutación (no anticipado por el plan, mismo motivo que
    /// Detalle_OperadorEnProceso_IniciarOculto): ningún caso del plan ejercitaba la rama
    /// POSITIVA de PuedeVolverAPendiente/PuedeFinalizar contra el árbol visual real del
    /// formulario -- sin este test, borrar IsVisible="{Binding PuedeVolverAPendiente}" (:64) o
    /// IsVisible="{Binding PuedeFinalizar}" (:65) default a visible=true y ningún test existente
    /// se pone rojo (verificado con las dos mutaciones reales). Mismas dos condiciones que
    /// DocumentoListView (Estado == EnProceso): se cubren juntas, distinguiéndose por botón.
    /// </summary>
    [AvaloniaFact]
    public async Task Detalle_OperadorEnProceso_VolverAPendienteYFinalizarVisiblesYRestoOculto()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Operador, DocumentoDe(1, EstadoDocumento.EnProceso));

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Volver a pendiente")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Finalizar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Iniciar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular")));
    }

    /// <summary>
    /// GAP encontrado por mutación (no anticipado por el plan): el caso 9 ("Iniciar oculto" en
    /// alta) apaga el botón por el panel envolvente IsVisible="{Binding !EsNuevoDocumento}"
    /// (:62), NO por PuedeIniciar -- y el caso 10 solo prueba la rama positiva (Pendiente =>
    /// visible). Sin este caso, borrar IsVisible="{Binding PuedeIniciar}" de :63 default a
    /// visible=true y NINGÚN test existente se pone rojo (verificado: mutación real, suite
    /// completa 8/8 verde). EnProceso es el estado correcto para la rama negativa: el documento
    /// SÍ admite la transición EnProceso->EnProceso... no, PuedeTransicionarA(EnProceso) es false
    /// para un documento que ya está EnProceso (no es una transición propia), así que PuedeIniciar
    /// da false por el chequeo de estado, sin ambigüedad con la reapertura de Finalizado/Anulado.
    /// </summary>
    [AvaloniaFact]
    public async Task Detalle_OperadorEnProceso_IniciarOculto()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Operador, DocumentoDe(1, EstadoDocumento.EnProceso));

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Iniciar")));
    }

    /// <summary>Caso que no existía antes de la Task 9.0: prueba el gate de rol de PuedeAnular
    /// contra el árbol visual real del formulario.</summary>
    [AvaloniaFact]
    public async Task Detalle_AdminPendiente_AnularVisible()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Admin, DocumentoDe(1, EstadoDocumento.Pendiente));

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular")));
    }

    // ---- Casos 12-13: PuedeReabrir sobre documento Finalizado ----

    /// <summary>Caso que no existía antes de la Task 9.0: prueba el gate de rol de PuedeReabrir
    /// contra el árbol visual real del formulario.</summary>
    [AvaloniaFact]
    public async Task Detalle_OperadorFinalizado_ReabrirOculto()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Operador, DocumentoDe(1, EstadoDocumento.Finalizado));

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Reabrir")));
    }

    [AvaloniaFact]
    public async Task Detalle_AdminFinalizado_ReabrirVisible()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Admin, DocumentoDe(1, EstadoDocumento.Finalizado));

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Reabrir")));
    }

    // ---- Casos 14a/14b/14c: las dos ramas del OR de PuedeEditarCampos, y ninguna ----
    // Sin 14a y 14b por separado, un test pasa por la rama equivocada y el OR queda sin custodiar.

    /// <summary>14a: rama izquierda del OR (EsNuevoDocumento=true).</summary>
    [AvaloniaFact]
    public void PuedeEditarCampos_RamaIzquierdaAlta_ControlesHabilitados()
    {
        var (window, _) = MontarParaCrear(RolUsuario.Operador);

        foreach (var control in CamposDelFormulario(window))
            Assert.True(control.IsEnabled);
    }

    /// <summary>14b: rama derecha del OR (EsNuevoDocumento=false, PuedeEditar=true por documento
    /// activo/EnProceso).</summary>
    [AvaloniaFact]
    public async Task PuedeEditarCampos_RamaDerechaDocumentoActivo_ControlesHabilitados()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Operador, DocumentoDe(1, EstadoDocumento.EnProceso));

        foreach (var control in CamposDelFormulario(window))
            Assert.True(control.IsEnabled);
    }

    /// <summary>14c: ninguna rama del OR (EsNuevoDocumento=false, PuedeEditar=false por documento
    /// cerrado/Finalizado).</summary>
    [AvaloniaFact]
    public async Task PuedeEditarCampos_NingunaRamaDocumentoCerrado_ControlesDeshabilitados()
    {
        var (window, _) = await MontarParaVerAsync(RolUsuario.Operador, DocumentoDe(1, EstadoDocumento.Finalizado));

        foreach (var control in CamposDelFormulario(window))
            Assert.False(control.IsEnabled);
    }
}
