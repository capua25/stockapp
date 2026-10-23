using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Pagos del gasto dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.
/// La selección de un pago se hace con la vista ya cargada: la carga que dispara la vista suelta la selección.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class PagosComparacionTests
{
    // Aprobación puntual (no sube el umbral global) de Martin, 2026-10-17: antialias de Chromium contra Skia en texto (importes del
    // resumen, error de Monto, aviso y nombre del adjunto, vacío de la card y mensaje del modal). Sin diferencia de layout:
    // columnas, anchos y posiciones coinciden con el mockup. Tope = medido. Mismo mecanismo que GastoFormComparacionTests.
    private const string MutacionPalida =
        "TarjetaContenido con V2FondoBrush en vez de V2SuperficieBrush (relleno pálido equivocado): 977 teselas a 1280 y 1053 a 1366, contra 17 y 18 aprobadas";

    private static UmbralAprobado Aprobacion(int teselas, string detalle, string? mutacionPalida = null) => new(
        teselas, "Martin", "2026-10-17",
        $"Antialias Chromium vs Skia en texto ({detalle}); sin corrimiento de layout; teselas solo sobre el texto. Tope = medido.",
        mutacionPalida);

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    private static Action<Control> OcultarSidebarYSeleccionar(int pagoId) => vista =>
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (PagosGastoViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.PagoSeleccionado = vm.Pagos.Single(p => p.Id == pagoId);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    };

    private static Control Modal(string mensaje) => ContenidoDeDialogo.Extraer(new ConfirmacionDialog(mensaje));

    [AvaloniaFact] public void Pagos_IgualAlMockup() => Comparar(DatosPagos.ConPagos(), "finanzas/pagos.html");
    [AvaloniaFact] public void Pagos_IgualAlMockup_1366x768() => Comparar(DatosPagos.ConPagos(), "finanzas/pagos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void PagosSeleccionado_IgualAlMockup() =>
        Comparar(DatosPagos.ConPagos(), "finanzas/pagos-seleccionado.html", preparar: OcultarSidebarYSeleccionar(61));
    [AvaloniaFact] public void PagosSeleccionado_IgualAlMockup_1366x768() =>
        Comparar(DatosPagos.ConPagos(), "finanzas/pagos-seleccionado.html", TamanoLienzo.Municipio, OcultarSidebarYSeleccionar(61));
    [AvaloniaFact] public void PagosSinPagos_IgualAlMockup() => Comparar(DatosPagos.SinPagos(), "finanzas/pagos-sin-pagos.html", aprobado: Aprobacion(12, "importes del resumen y vacío de la card"));
    [AvaloniaFact] public void PagosSinPagos_IgualAlMockup_1366x768() => Comparar(DatosPagos.SinPagos(), "finanzas/pagos-sin-pagos.html", TamanoLienzo.Municipio, aprobado: Aprobacion(15, "importes del resumen y vacío de la card"));
    [AvaloniaFact] public void PagosError_IgualAlMockup() => Comparar(DatosPagos.ConError(), "finanzas/pagos-error.html", aprobado: Aprobacion(14, "importes del resumen y error de Monto"));
    [AvaloniaFact] public void PagosError_IgualAlMockup_1366x768() => Comparar(DatosPagos.ConError(), "finanzas/pagos-error.html", TamanoLienzo.Municipio, aprobado: Aprobacion(15, "importes del resumen y error de Monto"));
    [AvaloniaFact] public void PagosSoloLectura_IgualAlMockup() =>
        Comparar(DatosPagos.SoloLectura(), "finanzas/pagos-solo-lectura.html", preparar: OcultarSidebarYSeleccionar(61));
    [AvaloniaFact] public void PagosSoloLectura_IgualAlMockup_1366x768() =>
        Comparar(DatosPagos.SoloLectura(), "finanzas/pagos-solo-lectura.html", TamanoLienzo.Municipio, OcultarSidebarYSeleccionar(61));
    [AvaloniaFact] public void PagosGastoAnulado_IgualAlMockup() => Comparar(DatosPagos.GastoAnuladoPantalla(), "finanzas/pagos-gasto-anulado.html");
    [AvaloniaFact] public void PagosGastoAnulado_IgualAlMockup_1366x768() =>
        Comparar(DatosPagos.GastoAnuladoPantalla(), "finanzas/pagos-gasto-anulado.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void PagosGastoPoa_IgualAlMockup() => Comparar(DatosPagos.GastoPoaAlContadoPantalla(), "finanzas/pagos-gasto-poa.html");
    [AvaloniaFact] public void PagosGastoPoa_IgualAlMockup_1366x768() =>
        Comparar(DatosPagos.GastoPoaAlContadoPantalla(), "finanzas/pagos-gasto-poa.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void PagosPagoAnulado_IgualAlMockup() =>
        Comparar(DatosPagos.PagoAnulado(), "finanzas/pagos-pago-anulado.html", preparar: OcultarSidebarYSeleccionar(62), aprobado: Aprobacion(17, "importes del resumen, aviso y nombre del adjunto", MutacionPalida));
    [AvaloniaFact] public void PagosPagoAnulado_IgualAlMockup_1366x768() =>
        Comparar(DatosPagos.PagoAnulado(), "finanzas/pagos-pago-anulado.html", TamanoLienzo.Municipio, OcultarSidebarYSeleccionar(62), aprobado: Aprobacion(18, "importes del resumen, aviso y nombre del adjunto", MutacionPalida));
    [AvaloniaFact] public void PagosConfirmarAnular_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Modal(DatosPagos.MensajeConfirmarAnular()), null, "finanzas/pagos-confirmar-anular.html", aprobado: Aprobacion(13, "mensaje del modal"));
    [AvaloniaFact] public void PagosConfirmarAnular_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosPagos.MensajeConfirmarAnular()), null, "finanzas/pagos-confirmar-anular.html",
            tamano: TamanoLienzo.Municipio, aprobado: Aprobacion(14, "mensaje del modal"));
}
