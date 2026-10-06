using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Inicio v2 (spec §5.3, §8.3 "Gates de las tarjetas KPI de Inicio"). Gates SIEMPRE con Operador de permisos
/// mixtos: Admin cortocircuita el OR y no prueba nada (Admin_VeLasTresMetricas es solo control). View real.
/// </summary>
public class InicioViewV2Tests : IDisposable
{
    private readonly List<Window> _ventanas = [];

    public void Dispose()
    {
        foreach (var ventana in _ventanas)
        {
            ventana.Close();
        }
    }

    private sealed class FinanzasFake : IFinanzasVistasService
    {
        private static readonly FacturaCalendarioDto Factura = new(1, "Proveedor", "A-0001", 1000m, new DateOnly(2026, 10, 9), "Pendiente");

        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes) => throw new NotSupportedException();
        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotSupportedException();
        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio) => throw new NotSupportedException();

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) =>
            Task.FromResult(new CalendarioPagosDto([Factura], [Factura, Factura], [], []));
    }

    /// <summary>Como el mockup de Administrador: 3 facturas a vencer y ninguna vencida (sin AlertaFacturasVencidas).</summary>
    private sealed class FinanzasSinVencidasFake : IFinanzasVistasService
    {
        private static readonly FacturaCalendarioDto Factura = new(1, "Proveedor", "A-0001", 1000m, new DateOnly(2026, 10, 9), "Pendiente");

        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes) => throw new NotSupportedException();
        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotSupportedException();
        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio) => throw new NotSupportedException();

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) =>
            Task.FromResult(new CalendarioPagosDto([], [Factura, Factura, Factura], [], []));
    }

    private sealed class BackupsFake : IBackupsService
    {
        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) =>
            Task.FromResult(new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26));
        public Task IniciarAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1040" Height="760">
            <vistas:InicioView />
        </Window>
        """;

    private static readonly UsuarioSesion Operador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");
    private static readonly UsuarioSesion Admin = new(1, "mperez", RolUsuario.Admin, "María Pérez");

    private (Window Ventana, InicioViewModel Vm) Montar(UsuarioSesion usuario, string[] permisos, IAuthService? auth = null,
        List<Tarea>? tareas = null, Size? tamano = null, IFinanzasVistasService? finanzas = null)
    {
        var sesion = new SesionFake(usuario, permisos);
        var vm = new InicioViewModel(sesion, new NavigationServiceFake(), finanzas ?? new FinanzasFake(), new BackupsFake(),
            new TareaServiceFake(tareas), auth ?? new AuthServiceFake());
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        if (tamano is { } t)
        {
            ventana.Width = t.Width;
            ventana.Height = t.Height;
        }

        _ventanas.Add(ventana);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return (ventana, vm);
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static bool SeVe(Window ventana, string nombre) => ArbolVisual.EsVisibleEnArbol(Nombrado<Control>(ventana, nombre));

    [AvaloniaFact]
    public void Raiz_TieneUi2() =>
        Assert.Contains("ui2", Montar(Operador, [Permisos.VerFinanzas]).Ventana.GetVisualDescendants().OfType<InicioView>().Single().Classes);

    [AvaloniaFact]
    public void OperadorMixto_VeFacturasYTareasPeroNoElBackup()
    {
        var (ventana, _) = Montar(Operador,
            [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas]);

        Assert.True(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.True(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.False(SeVe(ventana, "MetricaUltimoBackup"));
    }

    [AvaloniaFact]
    public void OperadorSoloConVerFinanzas_VeSoloFacturas()
    {
        var (ventana, _) = Montar(Operador, [Permisos.VerFinanzas]);

        Assert.True(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.False(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.False(SeVe(ventana, "MetricaUltimoBackup"));
        Assert.Equal("2", Nombrado<TarjetaMetrica>(ventana, "MetricaFacturasAVencer").Valor);
    }

    [AvaloniaFact]
    public void OperadorSoloConGestionarTareas_VeSoloTareas()
    {
        var (ventana, _) = Montar(Operador, [Permisos.GestionarTareas]);

        Assert.False(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.True(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.False(SeVe(ventana, "MetricaUltimoBackup"));
    }

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_NoMuestraLaFilaDeMetricas()
    {
        var (ventana, _) = Montar(Operador, [Permisos.GestionarProductos, Permisos.VerReportes]);

        Assert.False(SeVe(ventana, "FilaMetricas"));
        Assert.False(SeVe(ventana, "BotonCalendarioPagos"));
    }

    [AvaloniaFact]
    public void Admin_VeLasTresMetricas()
    {
        // Control, no prueba de gate: Admin cortocircuita el OR.
        var (ventana, _) = Montar(Admin, []);

        Assert.True(SeVe(ventana, "MetricaFacturasAVencer"));
        Assert.True(SeVe(ventana, "MetricaTareasVencidas"));
        Assert.True(SeVe(ventana, "MetricaUltimoBackup"));
    }

    /// <summary>
    /// Refresco de permisos que se resuelve cuando el test lo pide. Con AuthServiceFake(sesion, permisos) el
    /// refresco termina sincrónicamente dentro de DataContextChanged, ANTES de que los bindings de la vista
    /// lean los gates: la vista nace ya revocada y el test no prueba la revocación en caliente (verificado
    /// quitando la notificación de PuedeVerCalendarioPagos en el VM: quedaba verde).
    /// </summary>
    private sealed class AuthServiceDiferido(SesionFake sesion, IReadOnlySet<string> permisosNuevos) : IAuthService
    {
        private readonly TaskCompletionSource<IReadOnlySet<string>> _respuesta = new();

        public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena) => throw new NotSupportedException();
        public Task LogoutAsync() => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync() => _respuesta.Task;

        /// <summary>Como AuthApiClient: aplica los permisos a la sesión y recién ahí responde.</summary>
        public void Responder()
        {
            sesion.EstablecerPermisos(permisosNuevos);
            _respuesta.SetResult(permisosNuevos);
        }
    }

    [AvaloniaFact]
    public void RevocacionEnCaliente_OcultaLaMetricaYLaOtraOcupaLaFila()
    {
        // Review Focus 5: el refresco de permisos saca VerFinanzas con la vista ya mostrada.
        var sesion = new SesionFake(Operador, Permisos.VerFinanzas, Permisos.GestionarTareas);
        var auth = new AuthServiceDiferido(sesion, new HashSet<string> { Permisos.GestionarTareas });
        var vm = new InicioViewModel(sesion, new NavigationServiceFake(), new FinanzasFake(), new BackupsFake(),
            new TareaServiceFake(), auth);
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        _ventanas.Add(ventana);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(SeVe(ventana, "MetricaFacturasAVencer")); // precondición: antes del refresco se ve

        auth.Responder();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm._tareaRefrescoPermisos.IsCompleted);
        Dispatcher.UIThread.RunJobs();

        Assert.False(SeVe(ventana, "MetricaFacturasAVencer"));
        var tareas = Nombrado<TarjetaMetrica>(ventana, "MetricaTareasVencidas");
        Assert.True(ArbolVisual.EsVisibleEnArbol(tareas));
        Assert.Equal(Nombrado<GrillaUniformeV2>(ventana, "FilaMetricas").Bounds.Width, tareas.Bounds.Width);
    }

    [AvaloniaFact]
    public void BotonCalendarioDePagos_ConVerFinanzas_UsaElComandoDelVm()
    {
        var (ventana, vm) = Montar(Operador, [Permisos.VerFinanzas]);
        var boton = Nombrado<Button>(ventana, "BotonCalendarioPagos");

        Assert.True(ArbolVisual.EsVisibleEnArbol(boton));
        Assert.Equal("Ver calendario de pagos", boton.Content);
        Assert.Contains("secundario", boton.Classes);
        Assert.Same(vm.IrACalendarioPagosCommand, boton.Command);
    }

    [AvaloniaFact]
    public void Accesos_OperadorSoloConRegistrarMovimientos_VeHistorialPeroNoEntradaNiSalida()
    {
        // Gate AND: Registrar Entrada/Salida exigen RegistrarMovimientos Y GestionarProductos.
        var (ventana, _) = Montar(Operador, [Permisos.RegistrarMovimientos]);

        Assert.True(SeVe(ventana, "BotonAccesoHistorialMovimientos"));
        Assert.False(SeVe(ventana, "BotonAccesoRegistrarEntrada"));
        Assert.False(SeVe(ventana, "BotonAccesoRegistrarSalida"));
        Assert.False(SeVe(ventana, "BotonAccesoProductos"));
    }

    [AvaloniaFact]
    public void Accesos_ConservanTitulosDescripcionesEIconosReales()
    {
        // Reemplaza el test de wrap del título de la vista vieja: el recorte lo custodia el guardián de
        // texto recortado en cada comparación, a los dos tamaños.
        var (ventana, _) = Montar(Admin, []);
        var esperado = new (string Nombre, string Titulo, string Descripcion, string Icono)[]
        {
            ("BotonAccesoProductos", "Productos", "Ver y administrar el catálogo", "mdi-package-variant"),
            ("BotonAccesoRegistrarEntrada", "Registrar Entrada", "Cargar entradas de stock", "mdi-tray-arrow-down"),
            ("BotonAccesoRegistrarSalida", "Registrar Salida", "Cargar salidas de stock", "mdi-tray-arrow-up"),
            ("BotonAccesoHistorialMovimientos", "Historial de movimientos", "Consultar movimientos registrados", "mdi-history"),
            ("BotonAccesoValorizacion", "Valorización de inventario", "Ver el valor total del stock", "mdi-currency-usd"),
            ("BotonAccesoAuditoria", "Log de auditoría", "Revisar la actividad del sistema", "mdi-shield-search"),
        };

        foreach (var (nombre, titulo, descripcion, icono) in esperado)
        {
            var acceso = Nombrado<TarjetaAcceso>(ventana, nombre);
            Assert.Equal(titulo, acceso.Titulo);
            Assert.Equal(descripcion, acceso.Descripcion);
            Assert.Equal(icono, acceso.Icono);
        }
    }

    [AvaloniaFact]
    public void ConFacturasPorVencer_NoMuestraElTextoDeLaSemana()
    {
        // U7 (decisión del usuario 2026-10-06, D4): "N facturas por vencer esta semana" no aparece en Inicio;
        // lo reemplaza la métrica. Control: el VM SIGUE produciendo ese texto (se borra en la Fase 9), así que
        // su ausencia en el árbol visual (plantillas incluidas) prueba que la vista no lo muestra. Con la vista
        // vieja este test da rojo: la card de vencimientos lo bindeaba.
        var (ventana, vm) = Montar(Operador, [Permisos.VerFinanzas]);

        Assert.Equal("2 facturas por vencer esta semana", vm.TextoAVencer7Dias);
        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(textos, t => t.Contains("por vencer esta semana"));
        Assert.Equal("2", Nombrado<TarjetaMetrica>(ventana, "MetricaFacturasAVencer").Valor);
    }
    /// <summary>"Hoy" del calendario local etiquetado UTC, como persiste FechaLimite (ver InicioPanelTareasTests).</summary>
    private static DateTime HoyLocal() =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local).Date, DateTimeKind.Utc);

    /// <summary>
    /// Área de contenido del shell (ventana menos el sidebar de 240) a 1280x760 y a 1366x768. Con 7 tareas
    /// vencidas (lo que encontró la verificación orgánica) Inicio necesitaba scroll y los accesos quedaban bajo
    /// el pliegue: el panel muestra 4 filas y el resto queda en "Ver todas las tareas". Admin con los datos del
    /// mockup aprobado (tres métricas, la alerta de backup vencido de BackupsFake, sin facturas vencidas); no es
    /// un gate.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1040, 760)]
    [InlineData(1126, 768)]
    public void ConSieteTareasVencidas_MuestraCuatroFilasYNoNecesitaScroll(double ancho, double alto)
    {
        var tareas = Enumerable.Range(1, 7)
            .Select(i => new Tarea
            {
                Id = i, Titulo = $"Tarea vencida {i}", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
                CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = HoyLocal().AddDays(-i),
            })
            .ToList();

        var (ventana, _) = Montar(Admin, [], tareas: tareas, tamano: new Size(ancho, alto),
            finanzas: new FinanzasSinVencidasFake());

        var filas = Nombrado<ItemsControl>(ventana, "ListaTareasVencidas").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(4, filas.Count);
        Assert.Equal("7", Nombrado<TarjetaMetrica>(ventana, "MetricaTareasVencidas").Valor);
        Assert.True(SeVe(ventana, "EnlaceVerTodasLasTareas"));
        var desplazamiento = Nombrado<ScrollViewer>(ventana, "Desplazamiento");
        Assert.True(desplazamiento.Extent.Height <= desplazamiento.Viewport.Height,
            $"Inicio necesita scroll: contenido {desplazamiento.Extent.Height} px en {desplazamiento.Viewport.Height} px");
    }
    /// <summary>
    /// Peor caso aceptado por el usuario (2026-10-06, opción A): Admin con DOS alertas (backup vencido de
    /// BackupsFake y facturas vencidas de FinanzasFake) y 7 tareas vencidas. Con el tope de 4 filas el contenido
    /// mide 788 px: excede el área visible en 28 px a 1280x760 y en 20 px a 1366x768, y los accesos rápidos se
    /// alcanzan con el scroll de la página. Medido; un crecimiento del contenido lo pone en rojo.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1040, 760, 28)]
    [InlineData(1126, 768, 20)]
    public void ConDosAlertasYSieteTareas_ElScrollNoPasaDelLimiteYLosAccesosSeAlcanzan(double ancho, double alto, double excesoMaximo)
    {
        var tareas = Enumerable.Range(1, 7)
            .Select(i => new Tarea
            {
                Id = i, Titulo = $"Tarea vencida {i}", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
                CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = HoyLocal().AddDays(-i),
            })
            .ToList();

        var (ventana, _) = Montar(Admin, [], tareas: tareas, tamano: new Size(ancho, alto));

        Assert.True(SeVe(ventana, "AlertaBackupVencido")); // precondición: las dos alertas a la vez
        Assert.True(SeVe(ventana, "AlertaFacturasVencidas"));
        var desplazamiento = Nombrado<ScrollViewer>(ventana, "Desplazamiento");
        var exceso = desplazamiento.Extent.Height - desplazamiento.Viewport.Height;
        Assert.True(exceso <= excesoMaximo,
            $"Inicio excede el área visible en {exceso} px (límite aceptado: {excesoMaximo} px)");

        desplazamiento.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        var ultimoAcceso = Nombrado<Control>(ventana, "BotonAccesoAuditoria");
        var abajo = ultimoAcceso.TranslatePoint(new Point(0, ultimoAcceso.Bounds.Height), desplazamiento)!.Value.Y;
        Assert.True(abajo <= desplazamiento.Viewport.Height,
            $"El último acceso termina en y={abajo}, fuera del área visible de {desplazamiento.Viewport.Height} px");
    }
}
