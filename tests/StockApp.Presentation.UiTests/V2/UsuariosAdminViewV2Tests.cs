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
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.Views.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Usuarios v2 con la View REAL: estructura, estados (sin selección, admin, dado de baja, sin conexión, sin permiso),
/// la alta en línea con su error y el pie fijo de permisos. La pantalla es estructuralmente Admin-only: su gate de menú lo
/// custodia ShellMainViewGatesTests.</summary>
public class UsuariosAdminViewV2Tests
{
    private static readonly UsuarioDto Admin = new(1, "mperez", "María Pérez", RolUsuario.Admin, true, DateTime.UtcNow);
    private static readonly UsuarioDto Operador = new(2, "jrodriguez", "Juan Rodríguez", RolUsuario.Operador, true, DateTime.UtcNow);
    private static readonly UsuarioDto DeBaja = new(4, "psosa", "Pedro Sosa", RolUsuario.Operador, false, DateTime.UtcNow);

    private sealed record Montaje(Window Ventana, UsuariosAdminView Vista, UsuariosAdminViewModel Vm, UsuarioServicioFake Servicio, PanelPermisosViewModel Panel);

    private static Montaje Montar(UsuarioServicioFake? servicio = null, double ancho = 1280, double alto = 760)
    {
        servicio ??= new UsuarioServicioFake { Lista = [Admin, Operador, DeBaja] };
        servicio.Permisos[2] = [Permisos.RegistrarMovimientos, Permisos.GestionarTablasMaestras];
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new PanelPermisosViewModel(servicio, confirmacion);
        var vm = new UsuariosAdminViewModel(servicio, confirmacion, panel, new SesionFake(RolUsuario.Admin));
        var vista = new UsuariosAdminView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, panel);
    }

    private static async Task Seleccionar(Montaje m, UsuarioDto usuario)
    {
        m.Vm.UsuarioSeleccionado = m.Vm.Items.Single(u => u.Id == usuario.Id);
        await m.Panel._tareaCarga;
        Dispatcher.UIThread.RunJobs();
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<CheckBox> Casillas(Montaje m) => Parte<ScrollViewer>(m, "ScrollPermisos").GetVisualDescendants().OfType<CheckBox>();

    [AvaloniaFact]
    public async Task Raiz_TieneUi2_EncabezadoUsuarios_GuardarPermisosEsElUnicoPrimario()
    {
        var m = Montar();
        await Seleccionar(m, Operador);
        var encabezado = Parte<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Usuarios", encabezado.Titulo);
        Assert.Equal("Cuentas y permisos de acceso al sistema.", encabezado.Subtitulo);
        Assert.Contains("primario", Parte<Button>(m, "BotonGuardarPermisos").Classes);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
        Assert.Contains("secundario", Parte<Button>(m, "BotonCrear").Classes);
    }

    [AvaloniaFact]
    public void SinSeleccion_MuestraElVacioYNoElDetalle()
    {
        var m = Montar();

        Assert.True(Visible(Parte<TarjetaContenido>(m, "DetalleVacio")));
        Assert.False(Visible(Parte<TarjetaContenido>(m, "Detalle")));
        var vacio = Parte<TarjetaContenido>(m, "DetalleVacio").GetVisualDescendants().OfType<EstadoVacio>().Single();
        Assert.Equal("Elegí un usuario", vacio.Titulo);
        Assert.Equal("Seleccioná un usuario de la lista para ver sus permisos.", vacio.Mensaje);
    }

    [AvaloniaFact]
    public async Task OperadorSeleccionado_DetalleConRolAccionesYPermisosHabilitados()
    {
        var m = Montar();
        await Seleccionar(m, Operador);

        Assert.True(Visible(Parte<TarjetaContenido>(m, "Detalle")));
        Assert.False(Visible(Parte<TarjetaContenido>(m, "DetalleVacio")));
        Assert.Equal("jrodriguez", Parte<TextBlock>(m, "TituloUsuario").Text);
        Assert.Equal("Operador", Parte<Insignia>(m, "InsigniaRol").Texto);
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaInactivo")));
        Assert.True(Visible(Parte<Button>(m, "BotonHacerAdmin")));
        Assert.False(Visible(Parte<Button>(m, "BotonHacerOperador")));
        Assert.Contains("peligro", Parte<Button>(m, "BotonBaja").Classes);
        Assert.False(Visible(Parte<Alerta>(m, "AlertaAccesoTotal")));
        Assert.Equal(14, Casillas(m).Count());
        Assert.All(Casillas(m), c => Assert.True(c.IsEffectivelyEnabled));
        Assert.Equal(2, Casillas(m).Count(c => c.IsChecked == true));
        Assert.Equal("●", Parte<TextBox>(m, "CajaNuevaContrasenaSeleccionado").PasswordChar.ToString());
        Assert.True(Parte<Button>(m, "BotonGuardarPermisos").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task AdminSeleccionado_AccesoTotalYCasillasDeshabilitadas_HacerOperadorVisible()
    {
        var m = Montar();
        await Seleccionar(m, Admin);

        Assert.True(Visible(Parte<Alerta>(m, "AlertaAccesoTotal")));
        Assert.Equal("Acceso total: los administradores tienen todos los permisos.", Parte<Alerta>(m, "AlertaAccesoTotal").Texto);
        Assert.Equal("Administrador", Parte<Insignia>(m, "InsigniaRol").Texto);
        Assert.All(Casillas(m), c => Assert.False(c.IsEffectivelyEnabled));
        Assert.True(Visible(Parte<Button>(m, "BotonHacerOperador")));
        Assert.False(Visible(Parte<Button>(m, "BotonHacerAdmin")));
        Assert.False(Parte<Button>(m, "BotonGuardarPermisos").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task InactivoSeleccionado_SoloLecturaYAccionesDeshabilitadas()
    {
        var m = Montar();
        await Seleccionar(m, DeBaja);

        Assert.True(Visible(Parte<Insignia>(m, "InsigniaInactivo")));
        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.All(Casillas(m), c => Assert.False(c.IsEffectivelyEnabled));
        Assert.False(Parte<Button>(m, "BotonBaja").IsEffectivelyEnabled);
        Assert.False(Parte<Button>(m, "BotonHacerAdmin").IsEffectivelyEnabled);
        Assert.False(Parte<Button>(m, "BotonCambiarContrasena").IsEffectivelyEnabled);
        Assert.False(Parte<Button>(m, "BotonGuardarPermisos").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Alta_ConError_MuestraLaAlertaEnLaCardDeAlta()
    {
        var servicio = new UsuarioServicioFake { Lista = [Admin, Operador], AltaFalla = new StockApp.Domain.Exceptions.ReglaDeNegocioException("Ya existe un usuario con ese nombre.") };
        var m = Montar(servicio);
        Assert.False(Visible(Parte<Alerta>(m, "AlertaAlta")));
        m.Vm.NuevoNombreUsuario = "jrodriguez";
        m.Vm.NuevaContrasenaPlan = "Clave1234";

        await m.Vm.AltaCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var alerta = Parte<Alerta>(m, "AlertaAlta");
        Assert.True(Visible(alerta));
        Assert.Equal("Ya existe un usuario con ese nombre.", alerta.Texto);
        Assert.True(alerta.GetVisualAncestors().Contains(Parte<TarjetaContenido>(m, "CardNuevoUsuario")));
        // Mismo carácter que Login y ResetAdmin ("●"), en las dos contraseñas de la pantalla.
        Assert.Equal("●", Parte<TextBox>(m, "CajaNuevaContrasena").PasswordChar.ToString());
    }

    [AvaloniaFact]
    public void Grilla_FilaInactiva_InsigniaYTextoTerciario()
    {
        var m = Montar();
        var grilla = Parte<DataGrid>(m, "GrillaUsuarios");
        Assert.Equal(new object?[] { "USUARIO", "ROL", "ESTADO" }, grilla.Columns.Select(c => c.Header).ToArray());

        var fila = m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, m.Vm.Items.Single(u => u.Id == 4)));
        var nombre = fila.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "psosa");

        Assert.Contains("inactivo", nombre.Classes);
        Assert.Single(fila.GetVisualDescendants().OfType<Insignia>(), i => Visible(i));
        Assert.Contains(fila.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Operador");
        var activa = m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, m.Vm.Items.Single(u => u.Id == 1)));
        Assert.Contains(activa.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Administrador");
        Assert.Empty(activa.GetVisualDescendants().OfType<Insignia>().Where(i => Visible(i)));
    }

    [AvaloniaFact]
    public void SinConexion_EstadoConReintentar()
    {
        var m = Montar(new UsuarioServicioFake { Falla = new ServidorNoDisponibleException() });

        Assert.True(Visible(Parte<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Parte<Button>(m, "BotonReintentar")));
        Assert.False(Visible(Parte<TarjetaContenido>(m, "CardNuevoUsuario")));
        Assert.Equal("No se pudo conectar con el servidor", Parte<EstadoVacio>(m, "EstadoSinConexion").Titulo);
    }

    [AvaloniaFact]
    public void SinPermiso_ReemplazaElContenido()
    {
        var m = Montar(new UsuarioServicioFake { Falla = new UnauthorizedAccessException() });

        Assert.True(Visible(Parte<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.False(Visible(Parte<TarjetaContenido>(m, "CardNuevoUsuario")));
        Assert.False(Visible(Parte<TarjetaContenido>(m, "DetalleVacio")));
    }
}
