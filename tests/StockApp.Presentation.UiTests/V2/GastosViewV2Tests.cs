using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Gastos y facturas v2 con la View REAL: estructura, filtros que recargan solos, grilla con la insignia de
/// Estado por tono y la fila anulada por token, pie, estados de pantalla, gates con un Operador de permisos mixtos, Anular
/// sobre una fila anulada, doble click y revocación en caliente por el camino del shell.</summary>
public class GastosViewV2Tests
{
    private static readonly Color Terciario = Color.Parse("#78716C");
    private static readonly Proveedor Barraca = new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true };
    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };
    private static readonly RubroGasto Materiales = new() { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true };
    private static readonly LineaPoa Bacheo = new() { Id = 1, Nombre = "Bacheo y caminería rural", Programa = "Infraestructura", Ejercicio = 2026, Activo = true };

    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto G(int id, string detalle, decimal monto, decimal pagado = 0m, DateTime? vence = null, bool activo = true)
    {
        var gasto = new Gasto
        {
            Id = id, ProveedorId = 1, Proveedor = Barraca, NumeroFactura = $"A-000{id}", Detalle = detalle,
            Fecha = Dia(2026, 10, 6), MontoTotal = monto,
            RubroGastoId = 1, RubroGasto = Materiales, LineaPoaId = 1, LineaPoa = Bacheo,
            CondicionPago = vence is null ? CondicionPago.Contado : CondicionPago.Credito, FechaVencimiento = vence, Activo = activo,
        };
        if (pagado > 0m)
        {
            // Contado: el pago es el automático (con la fuente); crédito: un pago manual.
            var pago = vence is null
                ? PagoAutomaticoDePrueba.Con(Figm, fecha: gasto.Fecha, monto: pagado)
                : new PagoGasto { Fecha = gasto.Fecha, Monto = pagado, FuenteFinanciamientoId = Figm.Id, FuenteFinanciamiento = Figm };
            pago.Id = id * 10;
            pago.GastoId = id;
            gasto.Pagos.Add(pago);
        }
        return gasto;
    }

    private static Gasto Pendiente() => G(1, "Reparación de cordón cuneta", 420_000m, vence: Dia(2030, 12, 31));
    private static Gasto Parcial() => G(2, "Luminarias LED plaza Artigas", 310_000m, pagado: 150_000m, vence: Dia(2030, 11, 30));
    private static Gasto Pagada() => G(3, "Combustible de la flota", 92_300m, pagado: 92_300m);
    private static Gasto Vencida() => G(4, "Arena y pedregullo", 64_800m, vence: Dia(2026, 9, 30));
    private static Gasto Anulada() => G(5, "Combustible duplicado", 92_300m, activo: false);

    private static SesionFake ConTodo() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos, Permisos.RegistrarPagos);
    private static SesionFake SoloVer() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, GastosView Vista, GastosViewModel Vm, GastosFake Servicio, NavigationRecorderFake Navegacion);

    private static GastosViewModel NuevoVm(GastosFake servicio, ICurrentSession sesion, INavigationService navegacion) =>
        new(servicio, sesion, new ProveedoresFake { Activos = [Barraca] }, new FuenteFinanciamientoServiceFake([Figm]),
            new RubroGastoServiceFake([Materiales]), new LineaPoaServiceFake([Bacheo]), navegacion,
            new ConfirmacionServiceFake(), new GuardadoSinUsoFake(), new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake())
        { EsperaDebounce = TimeSpan.Zero };

    private static Montaje Montar(GastosFake servicio, ICurrentSession? sesion = null, double ancho = 1040,
        Action<GastosViewModel>? antesDeMostrar = null)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = NuevoVm(servicio, sesion ?? ConTodo(), navegacion);
        antesDeMostrar?.Invoke(vm);
        var vista = new GastosView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static Montaje Montar(params Gasto[] gastos) => Montar(new GastosFake { Lista = gastos.ToList() });

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);

    private static DataGridRow Fila(Montaje m, int idGasto) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridRow>()
            .Single(r => Visible(r) && r.DataContext is GastoFila f && f.Id == idGasto);

    private static TextBlock CeldaConTexto(Montaje m, string texto) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto && Visible(t));

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Headless no lleva clickCount: el doble toque es la secuencia de dos clicks seguidos, con el puntero que
    /// entra primero (como en IngresosViewV2Tests).</summary>
    private static void DobleClick(Window ventana, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 2; i++)
        {
            ventana.MouseDown(p, MouseButton.Left);
            ventana.MouseUp(p, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    // ── Estructura ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_NuevoGastoEsElUnicoPrimario()
    {
        var m = Montar(Pendiente());
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Gastos y facturas", encabezado.Titulo);
        Assert.Equal("Facturas y compromisos de pago de la caja municipal.", encabezado.Subtitulo);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b));
        Assert.Same(Control<Button>(m, "BotonNuevo"), Assert.Single(primarios));
        Assert.Same(m.Vm.NuevoCommand, Control<Button>(m, "BotonNuevo").Command);
        foreach (var nombre in new[] { "BotonEditar", "BotonPagos", "BotonAnular", "BotonExportarPdf" })
            Assert.Contains("secundario", Control<Button>(m, nombre).Classes);
    }

    [AvaloniaFact]
    public void Filtros_SieteCamposConSusEtiquetas_LimpiarSecundario_SinFiltrar()
    {
        var m = Montar(Pendiente());

        Assert.Equal(new[] { "Desde", "Hasta", "Proveedor", "Fuente", "Rubro", "Línea POA", "Estado" },
            new[] { "CampoDesde", "CampoHasta", "CampoProveedor", "CampoFuente", "CampoRubro", "CampoLineaPoa", "CampoEstado" }
                .Select(n => Control<CampoFormulario>(m, n).Etiqueta));
        var limpiar = Control<Button>(m, "BotonLimpiar");
        Assert.Contains("secundario", limpiar.Classes);
        Assert.Same(m.Vm.LimpiarFiltrosCommand, limpiar.Command);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Filtrar");
        Assert.Equal(GastosViewModel.EstadoTodos, Control<ComboBox>(m, "ComboEstado").SelectedItem);
    }

    [AvaloniaFact]
    public void Grilla_OnceColumnasEnOrdenYEnMayusculas_Ordenable()
    {
        var grilla = Control<DataGrid>(Montar(Pendiente()), "Grilla");

        Assert.Equal(new object?[] { "FECHA", "PROVEEDOR", "DETALLE", "MONTO", "PAGADO", "SALDO", "ESTADO", "FACTURA", "FUENTE", "RUBRO", "LÍNEA POA" },
            grilla.Columns.Select(c => c.Header).ToArray());
        Assert.True(grilla.CanUserSortColumns);
    }

    [AvaloniaFact]
    public void Celdas_FechaEnDdMmAaaa_EImportesEnEsUy()
    {
        var m = Montar(new GastosFake { Lista = [Parcial()] }, ancho: 1600);   // ancha: las once columnas a la vista
        var textos = Fila(m, 2).GetVisualDescendants().OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToList();

        Assert.Contains("06/10/2026", textos);
        Assert.Contains("$ 310.000,00", textos);
        Assert.Contains("$ 150.000,00", textos);
        Assert.Contains("$ 160.000,00", textos);
        Assert.Contains("Bacheo y caminería rural", textos);
    }

    [AvaloniaFact]
    public void Estado_CadaInsigniaConElTonoAprobado()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente(), Parcial(), Pagada(), Vencida(), Anulada()] }, ancho: 1600);

        var esperado = new Dictionary<int, (string Texto, VarianteInsignia Variante)>
        {
            [1] = ("Pendiente", VarianteInsignia.Neutro),
            [2] = ("Parcial", VarianteInsignia.Advertencia),
            [3] = ("Pagada", VarianteInsignia.Exito),
            [4] = ("Vencida", VarianteInsignia.Peligro),
            [5] = ("Anulada", VarianteInsignia.Neutro),
        };
        foreach (var (id, (texto, variante)) in esperado)
        {
            var insignia = Assert.Single(Fila(m, id).GetVisualDescendants().OfType<Insignia>());
            Assert.Equal(texto, insignia.Texto);
            Assert.Equal(variante, insignia.Variante);
        }
    }

    [AvaloniaFact]
    public void FilaAnulada_TextoTerciarioPorToken_YLaActivaNo()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente(), Anulada()] }, ancho: 1600);

        var textos = Fila(m, 5).GetVisualDescendants().OfType<TextBlock>()
            .Where(t => Visible(t) && !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null).ToList();
        Assert.NotEmpty(textos);
        Assert.All(textos, t => Assert.Equal(Terciario, MontajeV2.ColorDe(t.Foreground)));
        Assert.NotEqual(Terciario, MontajeV2.ColorDe(CeldaConTexto(m, "Reparación de cordón cuneta").Foreground));
    }

    [AvaloniaFact]
    public void PieConteo_CuentaLasVisibles_YSigueAlEstado()
    {
        var m = Montar(Pendiente(), Pagada());
        Assert.Equal("2 gastos", Control<TextBlock>(m, "PieConteo").Text);

        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Pagada";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("1 gasto", Control<TextBlock>(m, "PieConteo").Text);
    }

    // ── Estados de pantalla ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Cargando_IndicadorConLosFiltros_SinVaciosNiGrilla()
    {
        var m = Montar(new GastosFake { Pendiente = new TaskCompletionSource<IReadOnlyList<Gasto>>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<BarraFiltros>(m, "Filtros")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new GastosFake { Lista = [Pendiente()], Falla = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<BarraFiltros>(m, "Filtros")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));

        servicio.Falla = null;
        Clickear(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await (m.Vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public async Task SinConexion_TrasUnaRecargaFallida_ConFilasPrevias_OcultaGrillaYPie()
    {
        var servicio = new GastosFake { Lista = [Pendiente(), Pagada()] };
        var m = Montar(servicio);
        Assert.True(Visible(Control<TextBlock>(m, "PieConteo")));

        servicio.Falla = new ServidorNoDisponibleException();
        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
    }

    /// <summary>Espera (con tope) a que la recarga programada por el debounce haya pedido el listado y quede colgada del
    /// Pendiente del fake: es el momento en que Cargando es true con las filas previas todavía a la vista.</summary>
    private static async Task EsperarCargando(Montaje m)
    {
        for (var i = 0; i < 200 && !m.Vm.Cargando; i++)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(m.Vm.Cargando);
    }

    [AvaloniaFact]
    public async Task Recargando_ConFilasYaCargadas_ElIndicadorDeCargaQuedaOculto()
    {
        var servicio = new GastosFake { Lista = [Pendiente(), Pagada()] };
        var m = Montar(servicio);
        servicio.Pendiente = new TaskCompletionSource<IReadOnlyList<Gasto>>();

        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await EsperarCargando(m);

        Assert.True(m.Vm.Cargando);
        Assert.Equal(2, m.Vm.Filas.Count);
        Assert.False(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));

        servicio.Pendiente.SetResult([Pendiente(), Pagada()]);
        await m.Vm._tareaDebounce;
    }

    [AvaloniaFact]
    public async Task SinConexion_ConUnaConsultaConFiltroCargada_NoSeMuestraSinResultados()
    {
        var servicio = new GastosFake { Lista = [Pendiente(), Pagada()] };
        var m = Montar(servicio);
        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();
        Assert.True(m.Vm.HayFiltrosAplicados);

        servicio.Falla = new ServidorNoDisponibleException();
        Control<ComboBox>(m, "ComboFuente").SelectedItem = Figm;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.SinConexion);
        Assert.False(m.Vm.Cargando);
        Assert.Equal(0, m.Vm.CantidadVisible);
        Assert.True(m.Vm.HayFiltrosAplicados);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
    }

    [AvaloniaFact]
    public async Task Recargando_ConUnFiltroAplicadoYNingunaFilaVisible_NoSeMuestraSinResultados()
    {
        var servicio = new GastosFake { Lista = [Pendiente()] };
        var m = Montar(servicio);
        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Pagada";
        Dispatcher.UIThread.RunJobs();
        Assert.True(m.Vm.HayFiltrosAplicados);
        Assert.Equal(0, m.Vm.CantidadVisible);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));

        servicio.Pendiente = new TaskCompletionSource<IReadOnlyList<Gasto>>();
        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await EsperarCargando(m);

        Assert.True(m.Vm.Cargando);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));

        servicio.Pendiente.SetResult([Pendiente()]);
        await m.Vm._tareaDebounce;
    }

    /// <summary>El VM vacía las filas al perder la conexión, así que el término SinConexion del pie queda tapado por el
    /// conteo: por el camino real no se llega a SinConexion con filas. Se fuerza el estado (setter privado de la base)
    /// para custodiar el binding de la View por sí solo.</summary>
    [AvaloniaFact]
    public void SinConexion_ConFilasEnMemoria_ElPieNoSeVe()
    {
        var m = Montar(Pendiente(), Pagada());
        Assert.True(Visible(Control<TextBlock>(m, "PieConteo")));

        typeof(ViewModelBase).GetProperty(nameof(ViewModelBase.SinConexion))!.GetSetMethod(nonPublic: true)!.Invoke(m.Vm, [true]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.SinConexion);
        Assert.Equal(2, m.Vm.CantidadVisible);
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard_YExportarDeshabilitado()
    {
        var m = Montar(new GastosFake { Falla = new UnauthorizedAccessException("403") });

        var estado = Control<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(estado));
        Assert.Equal(GastosViewModel.MensajeSinPermisoGastos, estado.Mensaje);
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SinGastos_VacioConNuevoGasto_YElOperadorSinPermisoNoLoVe()
    {
        var conPermiso = Montar(new GastosFake());
        Assert.True(Visible(Control<EstadoVacio>(conPermiso, "EstadoSinGastos")));
        Assert.False(Visible(Control<EstadoVacio>(conPermiso, "EstadoSinResultados")));
        Assert.True(Visible(Control<Button>(conPermiso, "BotonNuevoVacio")));

        var soloVer = Montar(new GastosFake(), SoloVer());
        Assert.True(Visible(Control<EstadoVacio>(soloVer, "EstadoSinGastos")));
        Assert.False(Visible(Control<Button>(soloVer, "BotonNuevoVacio")));
    }

    [AvaloniaFact]
    public void ConFilasYSinFiltros_NingunVacioSeVe()
    {
        var m = Montar(Pendiente(), Pagada());

        Assert.False(m.Vm.HayFiltrosAplicados);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void EstadoSinCoincidencias_ConFilasCargadas_SinResultados_NoElVacioDeSinGastos()
    {
        var m = Montar(Pagada());
        Assert.Single(m.Vm.Filas);   // Estado filtra en memoria: las filas siguen cargadas, solo se ocultan

        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Vencida";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public void FiltroDeServidorSinCoincidencias_SinResultados()
    {
        var m = Montar(new GastosFake(), antesDeMostrar: vm => vm.FechaDesde = new DateTime(2026, 11, 1));

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
    }

    // ── Gates (Operador de permisos mixtos, nunca Admin) ─────────────────────────

    [AvaloniaFact]
    public void OperadorSoloVer_OcultaNuevoEditarAnularYPagos_MuestraSoloLectura_YExportarSigue()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, SoloVer());

        Assert.True(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
        foreach (var nombre in new[] { "BotonNuevo", "BotonEditar", "BotonAnular", "BotonPagos" })
            Assert.False(Visible(Control<Button>(m, nombre)), nombre);
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
    }

    [AvaloniaFact]
    public void OperadorConPagosSinGastos_VePagos_YNoAnular()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] },
            new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarPagos));

        Assert.True(Visible(Control<Button>(m, "BotonPagos")));
        Assert.False(Visible(Control<Button>(m, "BotonAnular")));
        Assert.True(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
    }

    [AvaloniaFact]
    public void OperadorConGastosSinPagos_VeNuevoYAnular_NoVePagosNiSoloLectura()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] },
            new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos));

        Assert.False(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
        Assert.True(Visible(Control<Button>(m, "BotonNuevo")));
        Assert.True(Visible(Control<Button>(m, "BotonEditar")));
        Assert.True(Visible(Control<Button>(m, "BotonAnular")));
        Assert.False(Visible(Control<Button>(m, "BotonPagos")));
    }

    // ── Acciones ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ClickEnUnaFilaAnulada_AnularDeshabilitado_EditarHabilitado()
    {
        var m = Montar(Pendiente(), Anulada());

        Clickear(m.Ventana, Fila(m, 5));

        Assert.Equal(5, m.Vm.FilaSeleccionada?.Id);
        Assert.False(Control<Button>(m, "BotonAnular").IsEffectivelyEnabled);
        Assert.True(Control<Button>(m, "BotonEditar").IsEffectivelyEnabled);

        Clickear(m.Ventana, Fila(m, 1));

        Assert.True(Control<Button>(m, "BotonAnular").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Recargar_SueltaLaSeleccion_YDeshabilitaEditarPagosYAnular()
    {
        var m = Montar(Pendiente(), Pagada());
        Clickear(m.Ventana, Fila(m, 1));
        Assert.Equal(1, m.Vm.FilaSeleccionada?.Id);
        foreach (var nombre in new[] { "BotonEditar", "BotonPagos", "BotonAnular" })
            Assert.True(Control<Button>(m, nombre).IsEffectivelyEnabled, "Precondición: " + nombre);

        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, m.Servicio.Filtros.Count);
        Assert.Null(m.Vm.FilaSeleccionada);
        foreach (var nombre in new[] { "BotonEditar", "BotonPagos", "BotonAnular" })
            Assert.False(Control<Button>(m, nombre).IsEffectivelyEnabled, nombre);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnUnaFila_ConPermiso_Edita()
    {
        var m = Montar(Pendiente());

        DobleClick(m.Ventana, CeldaConTexto(m, "Reparación de cordón cuneta"));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(typeof(GastoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_OperadorSoloVer_NoEdita()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, SoloVer());

        DobleClick(m.Ventana, CeldaConTexto(m, "Reparación de cordón cuneta"));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnElEncabezado_NoEdita()
    {
        var m = Montar(Pendiente());
        Clickear(m.Ventana, Fila(m, 1));   // con una fila seleccionada, Editar se podría ejecutar
        var encabezado = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridColumnHeader>()
            .First(h => Equals(h.Content, "DETALLE"));

        DobleClick(m.Ventana, encabezado);
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task ElegirUnProveedorEnElCombo_RecargaSolaConEseProveedor()
    {
        var m = Montar(Pendiente());

        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, m.Servicio.Filtros.Count);
        Assert.Equal(1, m.Servicio.Filtros[^1].ProveedorId);
    }

    [AvaloniaFact]
    public async Task Limpiar_ConClickReal_ReponeLosFiltrosYRecargaUnaVez()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, antesDeMostrar: vm => vm.FechaDesde = new DateTime(2026, 10, 1));
        Assert.Equal(Dia(2026, 10, 1), m.Servicio.Filtros[0].FechaDesde);

        Clickear(m.Ventana, Control<Button>(m, "BotonLimpiar"));
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Control<CalendarDatePicker>(m, "SelectorDesde").SelectedDate);
        Assert.Equal(2, m.Servicio.Filtros.Count);
        Assert.Null(m.Servicio.Filtros[^1].FechaDesde);
    }

    [AvaloniaFact]
    public void DesdeControlPoa_ElComboMuestraLaLineaPreseleccionada_YConsultaUnaVez()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, antesDeMostrar: vm =>
            vm.FiltrarPorLineaPoa(new LineaPoa { Id = 1, Nombre = "Bacheo y caminería rural", Programa = "Infraestructura", Ejercicio = 2026 }));

        Assert.Same(Bacheo, Control<ComboBox>(m, "ComboLineaPoa").SelectedItem);
        Assert.Equal(1, Assert.Single(m.Servicio.Filtros).LineaPoaId);
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    private static (Window Ventana, ShellMainViewModel Shell, SesionFake Sesion, HashSet<string> Tras, GastosViewModel Vm)
        MontarEnShell(string[] antes, string[] tras)
    {
        var sesion = new SesionFake(RolUsuario.Operador, antes);
        var permisosTras = new HashSet<string>(tras);
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, permisosTras), new PreferenciasSidebarFake());
        var vm = NuevoVm(new GastosFake { Lista = [Pendiente()] }, sesion, new NavigationRecorderFake());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (ventana, shell, sesion, permisosTras, vm);
    }

    private static async Task Disparar403(ShellMainViewModel shell, SesionFake sesion, HashSet<string> tras)
    {
        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static T EnGastos<T>(Window ventana, string nombre) where T : Control =>
        MontajeV2.Parte<T>(ventana.GetVisualDescendants().OfType<GastosView>().Single(), nombre);

    [AvaloniaFact]
    public async Task RevocacionDePagos_DentroDelShell_OcultaPagos_YElRestoSigue()
    {
        var (ventana, shell, sesion, tras, _) = MontarEnShell(
            [Permisos.VerFinanzas, Permisos.RegistrarGastos, Permisos.RegistrarPagos],
            [Permisos.VerFinanzas, Permisos.RegistrarGastos]);
        Assert.True(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonPagos")), "Precondición: con finanzas.pagos, Pagos se ve.");

        await Disparar403(shell, sesion, tras);

        Assert.False(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonPagos")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonNuevo")));
    }

    [AvaloniaFact]
    public async Task RevocacionDeVerFinanzas_DentroDelShell_SinPermisoEnLugarDeLaCard()
    {
        var (ventana, shell, sesion, tras, vm) = MontarEnShell(
            [Permisos.VerFinanzas, Permisos.RegistrarGastos], [Permisos.RegistrarGastos]);
        Assert.Single(vm.Filas);

        await Disparar403(shell, sesion, tras);

        Assert.True(ArbolVisual.EsVisibleEnArbol(EnGastos<EstadoVacio>(ventana, "EstadoSinPermiso")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnGastos<TarjetaContenido>(ventana, "Tarjeta")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonExportarPdf")));
    }
}
