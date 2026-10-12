using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Control POA v2 con la View REAL: estructura, estados, Ejercicio que recarga, insignia y barra por token,
/// formato es-UY y doble click a Gastos.</summary>
public class ControlPoaViewV2Tests
{
    private static ControlPoaLineaDto L(int id, string nombre, decimal presupuesto, decimal gastado, int ejercicio = 2026) =>
        new(id, nombre, "Obras", ejercicio, presupuesto, gastado, presupuesto - gastado,
            Math.Round(gastado / presupuesto * 100m, 2), gastado > presupuesto);

    private static readonly ControlPoaLineaDto Normal = L(1, "Mantenimiento de espacios públicos", 1_200_000m, 567_600m);
    private static readonly ControlPoaLineaDto Excedida = L(5, "Limpieza de playas", 300_000m, 342_900m);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
    }

    private sealed record Montaje(Window Ventana, ControlPoaView Vista, ControlPoaViewModel Vm, FinanzasVistasFake Servicio,
        NavigationServiceFake Navegacion);

    private static Montaje Montar(FinanzasVistasFake servicio, double ancho = 1040)
    {
        var navegacion = new NavigationServiceFake();
        var vm = new ControlPoaViewModel(servicio, navegacion, new CsvExporterSinUsoFake(), new GuardadoSinUsoFake(),
            new ConfirmacionServiceFake(), new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Operador, StockApp.Application.Authorization.Permisos.VerFinanzas), new RelojFijo())
        { EsperaDebounce = TimeSpan.Zero };
        var vista = new ControlPoaView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => m.Vista.FindControl<T>(nombre)!;
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTitulo_ExportesSecundarios_SinActualizar()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Control POA", Control<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Contains("secundario", Control<Button>(m, "BotonExportarCsv").Classes);
        Assert.Contains("secundario", Control<Button>(m, "BotonExportarPdf").Classes);
        Assert.Same(m.Vm.ExportarPdfCommand, Control<Button>(m, "BotonExportarPdf").Command);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Actualizar");
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void ConLineas_FormatoEsUy_PieDeConteo_YBarra()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });

        var textos = m.Vista.GetVisualDescendants().OfType<TextBlock>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();
        Assert.Contains("$ 1.200.000,00", textos);
        Assert.Contains("47,30 %", textos);
        Assert.Equal("1 línea", Control<TextBlock>(m, "PieConteo").Text);
        var barra = Assert.Single(m.Vista.GetVisualDescendants().OfType<BarraEjecucion>());
        Assert.Equal(30, barra.AnchoRelleno);
    }

    [AvaloniaFact]
    public void Sobreejecutada_InsigniaPeligro_NumeroNegativo_YBarraPeligro_SoloEnEsaFila()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal, Excedida] });

        var insignias = m.Vista.GetVisualDescendants().OfType<Insignia>().Where(i => i.Texto == "Sobreejecutado").ToList();
        var visible = Assert.Single(insignias, i => i.IsVisible);
        Assert.Equal(VarianteInsignia.Peligro, visible.Variante);
        var saldo = m.Vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "-$ 42.900,00");
        Assert.Contains("negativo", saldo.Classes);
        var barras = m.Vista.GetVisualDescendants().OfType<BarraEjecucion>().ToList();
        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(barras.Single(b => b.Porcentaje > 100m).RellenoVigente));
        Assert.Equal(MontajeV2.Hex("#1D4ED8"), MontajeV2.ColorDe(barras.Single(b => b.Porcentaje <= 100m).RellenoVigente));
    }

    [AvaloniaFact]
    public void Cargando_IndicadorEnLaCard_ConElFiltroAMano()
    {
        var m = Montar(new FinanzasVistasFake { PendientePoa = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<CampoFormulario>(m, "CampoEjercicio")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinLineas")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void SinLineas_VacioConSuTexto()
    {
        var m = Montar(new FinanzasVistasFake());

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinLineas")));
        Assert.Equal("No hay líneas POA en este ejercicio", Control<EstadoVacio>(m, "EstadoSinLineas").Titulo);
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new FinanzasVistasFake { FallaPoa = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinLineas")));

        servicio.FallaPoa = null;
        servicio.Lineas = [Normal];
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        m.Servicio.FallaPoa = new UnauthorizedAccessException("403");
        _ = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();

        // Un 403 no limpia las filas del ViewModel: lo que oculta la grilla es la card entera.
        Assert.NotEmpty(m.Vm.Filas);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public async Task CambiarEjercicioEnElCampo_RecargaConEseAnio()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var campo = Control<CampoFormulario>(m, "CampoEjercicio").GetVisualDescendants().OfType<NumericUpDown>().Single();

        campo.Value = 2025;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { 2026, 2025 }, m.Servicio.EjerciciosConsultados);
    }

    [AvaloniaFact]
    public async Task EjercicioInvalido_MarcaElError_YNoRecarga()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var campo = Control<CampoFormulario>(m, "CampoEjercicio").GetVisualDescendants().OfType<NumericUpDown>().Single();
        var texto = campo.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == campo);

        texto.Focus();
        Dispatcher.UIThread.RunJobs();
        texto.Text = "20a5";
        Dispatcher.UIThread.RunJobs();
        Control<Button>(m, "BotonExportarCsv").Focus();
        Dispatcher.UIThread.RunJobs();
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.True(DataValidationErrors.GetHasErrors(campo));
        Assert.Contains(campo.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Ingresá un año válido, entre 2020 y 2100." && Visible(t));
        Assert.Equal("20a5", texto.Text);
        Assert.Equal(2026, m.Vm.Ejercicio);
        Assert.Equal(new[] { 2026 }, m.Servicio.EjerciciosConsultados);
    }

    [AvaloniaFact]
    public async Task EjercicioInvalido_ElMensajeVaDebajoDeLaCaja_YLaCajaConservaSuAlto()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var campo = Control<CampoFormulario>(m, "CampoEjercicio").GetVisualDescendants().OfType<NumericUpDown>().Single();
        var texto = campo.GetVisualDescendants().OfType<TextBox>().First(t => t.TemplatedParent == campo);

        texto.Focus();
        Dispatcher.UIThread.RunJobs();
        texto.Text = "20a5";
        Dispatcher.UIThread.RunJobs();
        Control<Button>(m, "BotonExportarCsv").Focus();
        Dispatcher.UIThread.RunJobs();
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        // La caja es el ButtonSpinner de la plantilla (el que dibuja borde y fondo); el mensaje, el ErrorTemplate.
        var caja = campo.GetVisualDescendants().OfType<ButtonSpinner>().Single();
        var mensaje = campo.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Ingresá un año válido, entre 2020 y 2100.");
        var arribaCaja = caja.TranslatePoint(new Point(0, 0), m.Ventana)!.Value.Y;
        var arribaMensaje = mensaje.TranslatePoint(new Point(0, 0), m.Ventana)!.Value.Y;

        Assert.Equal(36, caja.Bounds.Height);
        Assert.True(arribaMensaje >= arribaCaja + caja.Bounds.Height,
            $"el mensaje empieza en {arribaMensaje} y la caja termina en {arribaCaja + caja.Bounds.Height}");
        Assert.True(campo.Bounds.Height >= caja.Bounds.Height + mensaje.Bounds.Height,
            $"el control mide {campo.Bounds.Height}: no entra caja {caja.Bounds.Height} + mensaje {mensaje.Bounds.Height}");
    }

    [AvaloniaFact]
    public void EjercicioValido_LaCajaMide36_SinMensaje()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var campo = Control<CampoFormulario>(m, "CampoEjercicio").GetVisualDescendants().OfType<NumericUpDown>().Single();

        Assert.Equal(36, campo.Bounds.Height);
        Assert.Equal(36, campo.GetVisualDescendants().OfType<ButtonSpinner>().Single().Bounds.Height);
    }

    [AvaloniaFact]
    public void DobleClickEnUnaFila_AbreGastosDeEsaLinea()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var grilla = Control<DataGrid>(m, "Grilla");
        var celda = grilla.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Normal.Nombre);
        var centro = celda.TranslatePoint(new Avalonia.Point(celda.Bounds.Width / 2, celda.Bounds.Height / 2), m.Ventana)!.Value;

        // Headless no lleva clickCount: el doble toque es la secuencia de dos clicks seguidos.
        for (var i = 0; i < 2; i++)
        {
            m.Ventana.MouseDown(centro, MouseButton.Left);
            m.Ventana.MouseUp(centro, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(typeof(GastosViewModel), m.Navegacion.UltimoDestino);
    }
}
