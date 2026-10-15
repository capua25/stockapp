using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Libro caja v2 con la View REAL: estructura, filtros, grilla del mes, saldos y totales por token, estados,
/// exporte y, en la segunda parte, el modo año, el selector y los gates.</summary>
public class LibroCajaViewV2Tests
{
    private static readonly Color Peligro = Color.Parse("#B91C1C");

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static MovimientoCajaDto Ingreso(int dia, string concepto, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Ingreso", concepto, null, null, "Rentas generales", null, monto, 0m, corrido);

    private static MovimientoCajaDto Egreso(int dia, string concepto, decimal monto, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), "Egreso", concepto, "Barraca Carmelo S.A.", "A-0004521", "FIGM Literal B",
            "Materiales de construcción", 0m, monto, corrido);

    private static readonly LibroCajaMesDto Octubre = new(2026, 10, 100_000m, 61_460m,
        [Ingreso(2, "Multas de tránsito", 48_300m, 148_300m), Egreso(3, "Materiales para bacheo", 186_840m, -38_540m),
         Ingreso(6, "Partida mensual FIGM", 100_000m, 61_460m)],
        [new TotalPorClaveDto("Materiales de construcción", 186_840m)],
        [new TotalPorClaveDto("Rentas generales", 148_300m), new TotalPorClaveDto("FIGM Literal B", -186_840m)]);

    private static SesionFake Mixto() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, LibroCajaView Vista, LibroCajaViewModel Vm, FinanzasVistasFake Servicio);

    private static FinanzasVistasFake ConOctubre(LibroCajaMesDto? mes = null)
    {
        var servicio = new FinanzasVistasFake();
        servicio.Meses[(2026, 10)] = mes ?? Octubre;
        return servicio;
    }

    private static Montaje Montar(FinanzasVistasFake servicio, SesionFake? sesion = null, double ancho = 1040, double alto = 760)
    {
        var vm = new LibroCajaViewModel(servicio, new GuardadoSinUsoFake(), new ConfirmacionServiceFake(),
            new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(), sesion ?? Mixto(), new RelojFijo())
        { EsperaDebounce = TimeSpan.Zero };
        var vista = new LibroCajaView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => m.Vista.FindControl<T>(nombre)!;
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    private static TextBlock CeldaConTexto(DataGrid grilla, string texto) =>
        grilla.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto && ArbolVisual.EsVisibleEnArbol(t));

    private static Color ColorDe(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    /// <summary>Los DataContext de las filas realizadas, de arriba abajo en pantalla (lo que el usuario ve).</summary>
    private static List<object?> FilasEnPantalla(Window ventana, DataGrid grilla) =>
        grilla.GetVisualDescendants().OfType<DataGridRow>().Where(Visible)
            .OrderBy(r => r.TranslatePoint(new Point(0, 0), ventana)!.Value.Y)
            .Select(r => r.DataContext).ToList();

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTitulo_ActualizarYExportarSecundarios_SinPrimario()
    {
        var m = Montar(ConOctubre());

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Libro caja", Control<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Contains("secundario", Control<Button>(m, "BotonActualizar").Classes);
        Assert.Same(m.Vm.RecargarCommand, Control<Button>(m, "BotonActualizar").Command);
        Assert.Contains("secundario", Control<Button>(m, "BotonExportarPdf").Classes);
        Assert.Same(m.Vm.ExportarPdfCommand, Control<Button>(m, "BotonExportarPdf").Command);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Filtros_AnioPeriodoYMes_ConSusEtiquetas()
    {
        var m = Montar(ConOctubre());

        Assert.Equal("Año", Control<CampoFormulario>(m, "CampoAnio").Etiqueta);
        Assert.Equal("Período", Control<CampoFormulario>(m, "CampoPeriodo").Etiqueta);
        Assert.Equal("Mes", Control<CampoFormulario>(m, "CampoMes").Etiqueta);
        Assert.Equal("Mes", Control<ComboBox>(m, "ComboModo").SelectedItem);
        Assert.True(Visible(Control<CampoFormulario>(m, "CampoMes")));
        Assert.Equal(2026m, Control<NumericUpDown>(m, "SelectorAnio").Value);
        Assert.Equal(10m, Control<NumericUpDown>(m, "SelectorMes").Value);
    }

    /// <summary>Validación del año en la vista (como Control POA): un texto que no parsea o fuera de 2020 a 2100 marca
    /// el error en el campo y no llega al VM, así que no dispara ninguna consulta.</summary>
    [AvaloniaTheory]
    [InlineData("20a5")]
    [InlineData("2200")]
    public async Task AnioInvalido_MarcaElError_YNoRecarga(string tipeado)
    {
        var m = Montar(ConOctubre());
        var campo = Control<NumericUpDown>(m, "SelectorAnio");
        var texto = campo.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == campo);

        texto.Focus();
        Dispatcher.UIThread.RunJobs();
        texto.Text = tipeado;
        Dispatcher.UIThread.RunJobs();
        Control<Button>(m, "BotonActualizar").Focus();
        Dispatcher.UIThread.RunJobs();
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.True(DataValidationErrors.GetHasErrors(campo));
        Assert.Contains(campo.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Ingresá un año válido, entre 2020 y 2100." && Visible(t));
        Assert.Equal(2026, m.Vm.Anio);
        Assert.Equal(new[] { "2026-10" }, m.Servicio.ConsultasLibroCaja);
    }

    /// <summary>Orden de columnas aprobado en el GATE 1 (Q-ORDEN): los importes después de CONCEPTO, así SALDO CORRIDO
    /// se ve entero a 1280 y a 1366; los datos de catálogo al final, en la zona que scrollea.</summary>
    [AvaloniaFact]
    public void GrillaDelMes_DiezColumnasEnOrdenYEnMayusculas_Ordenable()
    {
        var m = Montar(ConOctubre());
        var grilla = Control<DataGrid>(m, "GrillaMovimientos");

        Assert.Equal(new object?[] { "FECHA", "TIPO", "CONCEPTO", "INGRESO", "EGRESO", "SALDO CORRIDO", "PROVEEDOR", "FACTURA", "FUENTE", "RUBRO" },
            grilla.Columns.Select(c => c.Header).ToArray());
        Assert.True(grilla.CanUserSortColumns);
    }

    /// <summary>Q-NOCEDE: FECHA y los tres importes no ceden ancho; el resto scrollea en horizontal.</summary>
    [AvaloniaFact]
    public void GrillaDelMes_FechaYLosImportesNoCeden()
    {
        var m = Montar(ConOctubre());
        var grilla = Control<DataGrid>(m, "GrillaMovimientos");

        var noCeden = grilla.Columns.Where(StockApp.Presentation.Behaviors.ColumnaAuto.GetNoCede).Select(c => c.Header).ToArray();

        Assert.Equal(new object?[] { "FECHA", "INGRESO", "EGRESO", "SALDO CORRIDO" }, noCeden);
    }

    /// <summary>Orden por click real en el header (DataGridCollectionView, Avalonia 12).</summary>
    [AvaloniaFact]
    public void GrillaDelMes_ClickEnElHeader_Ordena()
    {
        var m = Montar(ConOctubre(), ancho: 1366, alto: 900);
        var grilla = Control<DataGrid>(m, "GrillaMovimientos");
        Assert.Same(Octubre.Movimientos[0], FilasEnPantalla(m.Ventana, grilla)[0]);

        GrillaStockHelpers.ClickearHeader(m.Ventana, grilla, "CONCEPTO");

        Assert.Same(Octubre.Movimientos[1], FilasEnPantalla(m.Ventana, grilla)[0]); // "Materiales para bacheo" primero
    }

    [AvaloniaFact]
    public void TotalesPorFuente_ClickEnElHeader_Ordena()
    {
        var m = Montar(ConOctubre(), ancho: 1366, alto: 1200);
        var grilla = Control<DataGrid>(m, "GrillaFuentesMes");
        Assert.Same(Octubre.TotalesPorFuente[0], FilasEnPantalla(m.Ventana, grilla)[0]);

        GrillaStockHelpers.ClickearHeader(m.Ventana, grilla, "FUENTE");

        Assert.Same(Octubre.TotalesPorFuente[1], FilasEnPantalla(m.Ventana, grilla)[0]); // "FIGM Literal B" primero
    }

    [AvaloniaFact]
    public void TotalesPorRubro_ClickEnElHeader_Ordena()
    {
        var dosRubros = Octubre with
        {
            TotalesPorRubro = [new TotalPorClaveDto("Obras viales", 420_000m), new TotalPorClaveDto("Combustibles", 92_300m)],
        };
        var m = Montar(ConOctubre(dosRubros), ancho: 1366, alto: 1200);
        var grilla = Control<DataGrid>(m, "GrillaRubrosMes");
        Assert.Same(dosRubros.TotalesPorRubro[0], FilasEnPantalla(m.Ventana, grilla)[0]);

        GrillaStockHelpers.ClickearHeader(m.Ventana, grilla, "RUBRO");

        Assert.Same(dosRubros.TotalesPorRubro[1], FilasEnPantalla(m.Ventana, grilla)[0]); // "Combustibles" primero
    }

    [AvaloniaFact]
    public void Movimientos_EsUy_GuionSinMonto_YSaldoCorridoNegativoPorToken()
    {
        var m = Montar(ConOctubre(), ancho: 1366, alto: 900);
        var grilla = Control<DataGrid>(m, "GrillaMovimientos");
        var filaEgreso = grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, Octubre.Movimientos[1]));
        var textos = filaEgreso.GetVisualDescendants().OfType<TextBlock>().Where(Visible).ToList();

        Assert.Contains(textos, t => t.Text == "03/10/2026");
        Assert.Contains(textos, t => t.Text == "—");                  // INGRESO en 0
        Assert.Contains(textos, t => t.Text == "$ 186.840,00");
        Assert.Equal(Peligro, ColorDe(textos.Single(t => t.Text == "-$ 38.540,00")));
        Assert.NotEqual(Peligro, ColorDe(CeldaConTexto(grilla, "$ 148.300,00")));
    }

    [AvaloniaFact]
    public void PieConteo_CuentaLosMovimientos()
    {
        var m = Montar(ConOctubre());

        Assert.Equal("3 movimientos", Control<TextBlock>(m, "PieConteoMovimientos").Text);
    }

    [AvaloniaFact]
    public void Saldos_PositivosEnEsUy_SinInsignia()
    {
        var m = Montar(ConOctubre(Octubre with { SaldoFinal = 61_460m }));

        Assert.Equal("$ 100.000,00", Control<TextBlock>(m, "ValorSaldoInicial").Text);
        Assert.Equal("$ 61.460,00", Control<TextBlock>(m, "ValorSaldoFinal").Text);
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSaldoInicialNegativo")));
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSaldoFinalNegativo")));
    }

    [AvaloniaFact]
    public void SaldoFinalNegativo_InsigniaPeligroYTextoPorToken()
    {
        var m = Montar(ConOctubre(Octubre with { SaldoFinal = -38_540m }));

        var insignia = Control<Insignia>(m, "InsigniaSaldoFinalNegativo");
        Assert.True(Visible(insignia));
        Assert.Equal("Saldo negativo", insignia.Texto);
        Assert.Equal(VarianteInsignia.Peligro, insignia.Variante);
        Assert.Equal(Peligro, ColorDe(Control<TextBlock>(m, "ValorSaldoFinal")));
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSaldoInicialNegativo")));
    }

    [AvaloniaFact]
    public void SaldoInicialNegativo_TambienLlevaInsigniaYToken()
    {
        var m = Montar(ConOctubre(Octubre with { SaldoInicial = -5_000m }));

        Assert.True(Visible(Control<Insignia>(m, "InsigniaSaldoInicialNegativo")));
        Assert.Equal(Peligro, ColorDe(Control<TextBlock>(m, "ValorSaldoInicial")));
    }

    [AvaloniaFact]
    public void TotalesPorRubroYPorFuente_EnSusCards_LaFuenteNegativaPorToken()
    {
        var m = Montar(ConOctubre());

        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaRubrosMes")));
        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaFuentesMes")));
        Assert.Equal(new object?[] { "RUBRO", "TOTAL" }, Control<DataGrid>(m, "GrillaRubrosMes").Columns.Select(c => c.Header).ToArray());
        Assert.Equal(new object?[] { "FUENTE", "TOTAL" }, Control<DataGrid>(m, "GrillaFuentesMes").Columns.Select(c => c.Header).ToArray());
        Assert.Equal(Peligro, ColorDe(CeldaConTexto(Control<DataGrid>(m, "GrillaFuentesMes"), "-$ 186.840,00")));
    }

    [AvaloniaFact]
    public void Cargando_IndicadorSinElVacioNiLosSaldos()
    {
        var m = Montar(new FinanzasVistasFake { PendienteMes = new TaskCompletionSource<LibroCajaMesDto>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaFiltros")));
        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaVacio")));
        Assert.False(Visible(Control<StackPanel>(m, "PanelSaldos")));
        Assert.False(Visible(Control<StackPanel>(m, "BloquesMes")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = ConOctubre();
        servicio.FallaLibroCaja = new ServidorNoDisponibleException();
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<StackPanel>(m, "PanelSaldos")));
        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaVacio")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);

        servicio.FallaLibroCaja = null;
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<StackPanel>(m, "BloquesMes")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDelContenido_ActualizarVisible_YExportarDeshabilitado()
    {
        var m = Montar(new FinanzasVistasFake { FallaLibroCaja = new UnauthorizedAccessException("403") });

        var sinPermiso = Control<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(sinPermiso));
        Assert.Equal(LibroCajaViewModel.MensajeSinPermisoLibroCaja, sinPermiso.Mensaje);
        Assert.False(Visible(Control<StackPanel>(m, "Contenido")));
        Assert.True(Visible(Control<Button>(m, "BotonActualizar")));
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SinMovimientos_VacioConSuTexto_SaldosVisibles_YExportarDeshabilitado()
    {
        var m = Montar(ConOctubre(new LibroCajaMesDto(2026, 10, 2_350_000m, 2_350_000m, [], [], [])));

        var vacio = Control<EstadoVacio>(m, "EstadoSinMovimientos");
        Assert.True(Visible(vacio));
        Assert.Equal("No hay movimientos en el período", vacio.Titulo);
        Assert.False(Visible(Control<StackPanel>(m, "BloquesMes")));
        Assert.True(Visible(Control<StackPanel>(m, "PanelSaldos")));
        Assert.Equal("$ 2.350.000,00", Control<TextBlock>(m, "ValorSaldoFinal").Text);
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    /// <summary>El exporte se habilita con PuedeExportar (Task 4) y está a la vista en modo mes (la v1 lo ocultaba
    /// solo en año completo; la v2 lo muestra siempre).</summary>
    [AvaloniaFact]
    public void ConMovimientos_ExportarHabilitado_YMientrasRecargaDeshabilitado()
    {
        var servicio = ConOctubre();
        var m = Montar(servicio);
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
        Assert.True(m.Vm.PuedeExportar);
        Assert.True(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);

        servicio.PendienteMes = new TaskCompletionSource<LibroCajaMesDto>();
        _ = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.Cargando);
        Assert.True(Visible(Control<StackPanel>(m, "BloquesMes")));   // la recarga deja los datos a la vista
        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaCargando")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task CambiarMesEnElCampo_RecargaConEseMes()
    {
        var m = Montar(ConOctubre());

        Control<NumericUpDown>(m, "SelectorMes").Value = 9;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "2026-10", "2026-09" }, m.Servicio.ConsultasLibroCaja);
    }

    [AvaloniaFact]
    public void Grilla_ConSeiscientosMovimientos_NoRealizaTodasLasFilas()
    {
        var movimientos = Enumerable.Range(1, 600).Select(i => Ingreso(1 + i % 28, $"Ingreso {i}", 10m, 10m * i)).ToList();
        var m = Montar(ConOctubre(new LibroCajaMesDto(2026, 10, 0m, 6_000m, movimientos, [], [])));

        var filas = Control<DataGrid>(m, "GrillaMovimientos").GetVisualDescendants().OfType<DataGridRow>().Count();

        Assert.InRange(filas, 1, 40);
    }
}
