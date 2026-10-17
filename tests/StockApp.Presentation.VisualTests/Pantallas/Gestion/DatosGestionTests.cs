using Moq;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Presentation.Services;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>Los mockups de Gestión escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla
/// por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosGestionTests
{
    private static string Vence(Tarea t) => FechaCortaConverter.Formatear(DateOnly.FromDateTime(t.FechaLimite!.Value));

    [Fact]
    public void Tareas_FijanEstadoPrioridadVenceYClasificacion()
    {
        var esperado = new (int Id, EstadoTarea Estado, string Prioridad, string Vence, string Clasificacion)[]
        {
            (6, EstadoTarea.Pendiente, "Alta", "15/03/2030", "Costa · Infraestructura"),
            (5, EstadoTarea.Pendiente, "Media", "30/09/2026", "Centro · Infraestructura · Dirección de Obras · FIGM Literal B · Exp. 45/2026"),
            (4, EstadoTarea.EnCurso, "Alta", "01/02/2030", "Centro · Ambiente · Higiene y Ambiente"),
            (3, EstadoTarea.EnCurso, "Media", "20/05/2030", "Barrio Norte · Ambiente · Higiene y Ambiente · Rentas generales"),
            (2, EstadoTarea.Terminada, "Baja", "10/01/2030", "Centro"),
            (1, EstadoTarea.Cancelada, "Media", "04/04/2030", "Centro · Infraestructura"),
        };

        Assert.Equal(esperado.Length, DatosTareas.Tareas.Count);
        for (var i = 0; i < esperado.Length; i++)
        {
            var fila = new TareaFila(DatosTareas.Tareas[i], RolUsuario.Admin);
            Assert.Equal(esperado[i].Id, fila.Id);
            Assert.Equal(esperado[i].Estado, fila.Tarea.Estado);
            Assert.Equal(esperado[i].Prioridad, fila.PrioridadTexto);
            Assert.Equal(esperado[i].Vence, Vence(DatosTareas.Tareas[i]));
            Assert.Equal(esperado[i].Clasificacion, fila.EtiquetasClasificacion);
        }
        Assert.Equal(["mperez", "jsosa"], DatosTareas.Tareas.Where(t => t.Estado == EstadoTarea.EnCurso).Select(t => t.TomadaPor!.NombreUsuario).OrderDescending());
    }

    [Fact]
    public void Tareas_LaVencidaEsLaUnica()
    {
        var vencidas = DatosTareas.Tareas.Select(t => new TareaFila(t, RolUsuario.Admin)).Where(f => f.EsVencida).ToList();

        Assert.Single(vencidas);
        Assert.Equal(5, vencidas[0].Id);
    }

    [Fact]
    public void Documentos_FijanTipoNumeroEstadoYEmision()
    {
        var esperado = new (int Id, TipoDocumento Tipo, string Numero, EstadoDocumento Estado, string Emision, string Registrado)[]
        {
            (12, TipoDocumento.Expediente, "0112/2026", EstadoDocumento.Pendiente, "09/10/2026", "jrodriguez"),
            (11, TipoDocumento.Oficio, "0231/2026", EstadoDocumento.EnProceso, "05/10/2026", "mperez"),
            (10, TipoDocumento.Suministro, "0098/2026", EstadoDocumento.EnProceso, "29/09/2026", "jsosa"),
            (9, TipoDocumento.Expediente, "45/2026", EstadoDocumento.EnProceso, "12/09/2026", "mperez"),
            (8, TipoDocumento.Oficio, "0204/2026", EstadoDocumento.Pendiente, "02/09/2026", "jrodriguez"),
            (7, TipoDocumento.Suministro, "0087/2026", EstadoDocumento.EnProceso, "20/08/2026", "jsosa"),
            (6, TipoDocumento.Expediente, "0031/2026", EstadoDocumento.Finalizado, "10/06/2026", "mperez"),
            (5, TipoDocumento.Oficio, "0150/2026", EstadoDocumento.Anulado, "22/05/2026", "jrodriguez"),
            (4, TipoDocumento.Suministro, "0042/2026", EstadoDocumento.Finalizado, "14/04/2026", "jsosa"),
            (3, TipoDocumento.Expediente, "0017/2026", EstadoDocumento.Anulado, "03/02/2026", "mperez"),
        };

        var filas = DatosDocumentos.Activos.Concat(DatosDocumentos.Historial).Select(d => new DocumentoFila(d, RolUsuario.Admin)).ToList();

        Assert.Equal(6, DatosDocumentos.Activos.Count);
        Assert.Equal(4, DatosDocumentos.Historial.Count);
        for (var i = 0; i < esperado.Length; i++)
        {
            Assert.Equal(esperado[i].Id, filas[i].Id);
            Assert.Equal(esperado[i].Tipo.ToString(), filas[i].TipoTexto);
            Assert.Equal(esperado[i].Numero, filas[i].NumeroCompleto);
            Assert.Equal(esperado[i].Estado, filas[i].Documento.Estado);
            Assert.Equal(esperado[i].Emision, FechaCortaConverter.Formatear(DateOnly.FromDateTime(filas[i].FechaEmision)));
            Assert.Equal(esperado[i].Registrado, filas[i].RegistradoPorNombre);
        }
    }

    [Fact]
    public void Notas_YEventos_FijanTextoYHoraLocal()
    {
        string Local(DateTime utc) => (string)FechaHoraLocalConverter.Instance.Convert(utc, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)!;

        Assert.Equal(["12/10/2026 09:15", "13/10/2026 11:40", "15/10/2026 16:05"], DatosTareas.Notas.Select(n => Local(n.Fecha)));
        Assert.Equal(3, DatosTareas.Tareas[2].Notas.Count);
        Assert.Equal(["12/09/2026 09:00", "15/09/2026 10:30", "02/10/2026 15:45"], DatosDocumentos.Eventos.Select(e => Local(e.Fecha)));
        Assert.Equal(3, DatosDocumentos.Activos[3].Eventos.Count);
        Assert.Equal("15/09/2026", FechaUtcACortaConverter.Instance.Convert(DatosDocumentos.Adjuntos[0].FechaAltaUtc, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MensajesDeConfirmacion_SonLosDelMockup()
    {
        var confirmacion = new Mock<IConfirmacionService>();
        confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        var servicio = new Mock<ITareaService>();
        servicio.Setup(s => s.ListarAsync()).ReturnsAsync(DatosTareas.Tareas);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var vm = new TareaListViewModel(servicio.Object, sesion.Object, Mock.Of<StockApp.Presentation.Navigation.INavigationService>(), confirmacion.Object);
        await vm.CargarAsync();

        await vm.CancelarCommand.ExecuteAsync(vm.EnCurso[0]);

        confirmacion.Verify(c => c.PreguntarAsync("¿Confirma cancelar la tarea \"Poda de los plátanos de la avenida Artigas\"?"), Times.Once);
        Assert.Equal("¿Quitar el adjunto \"oficio-firmado.pdf\"?",
            AdjuntosDocumentoPanelViewModel.MensajeConfirmarQuitar(DatosDocumentos.Adjuntos[0].NombreArchivo));
        Assert.Equal(245_760, DatosDocumentos.Adjuntos[0].TamanoBytes);
    }

    private static T Contenido<T>(StockApp.Presentation.ViewModels.ShellMainViewModel shell) where T : class =>
        (shell.CurrentContent as T) ?? throw new InvalidOperationException("Sin contenido.");

    [Fact]
    public async Task Tareas_FabricasDeEstado()
    {
        var cargando = Contenido<TareaListViewModel>(DatosTareas.Cargando());
        var tarea = cargando.CargarAsync();
        Assert.True(cargando.Cargando);
        Assert.False(tarea.IsCompleted);

        var sinConexion = Contenido<TareaListViewModel>(DatosTareas.SinConexion());
        await sinConexion.CargarAsync();
        Assert.True(sinConexion.SinConexion);

        var sinPermiso = Contenido<TareaListViewModel>(DatosTareas.SinPermiso());
        await sinPermiso.CargarAsync();
        Assert.True(sinPermiso.SinPermiso);
        Assert.Equal(TareaListViewModel.MensajeSinPermisoTareas, ((StockApp.Presentation.ViewModels.ViewModelBase)sinPermiso).MensajeSinPermiso);

        var sinTareas = Contenido<TareaListViewModel>(DatosTareas.SinTareas());
        await sinTareas.CargarAsync();
        Assert.Empty(sinTareas.Tareas);

        var sinResultados = Contenido<TareaListViewModel>(DatosTareas.SinResultados());
        await sinResultados.CargarAsync();
        Assert.Equal(5, sinResultados.Tareas.Count);
        Assert.Equal(0, sinResultados.CantidadVisible);
        Assert.Equal("Canceladas", sinResultados.FiltroEstado.Nombre);

        var listado = Contenido<TareaListViewModel>(DatosTareas.Listado());
        await listado.CargarAsync();
        Assert.Equal(4, listado.CantidadVisible);
    }

    [Fact]
    public async Task Tareas_FormulariosYDialogo()
    {
        var nueva = Contenido<TareaFormViewModel>(DatosTareas.Nueva());
        await nueva.ClasificacionPanel.InicializarAsync();
        Assert.True(nueva.EsNuevaTarea);
        Assert.False(nueva.HasErrors);
        Assert.False(nueva.GuardarCommand.CanExecute(null));
        Assert.Equal("(ninguna)", nueva.ClasificacionPanel.ZonasDisponibles[0].Nombre);

        var conError = Contenido<TareaFormViewModel>(DatosTareas.NuevaConErrorDeCampo());
        Assert.Equal(TareaFormViewModel.MensajeTituloObligatorio, conError.ErrorDe(nameof(TareaFormViewModel.Titulo)));

        var detalle = Contenido<TareaFormViewModel>(DatosTareas.Detalle());
        Assert.False(detalle.EsNuevaTarea);
        Assert.True(detalle.MuestraReclasificar);
        Assert.True(detalle.MuestraCambioPrioridad);
        Assert.Equal(3, detalle.Notas.Count);

        var operador = Contenido<TareaFormViewModel>(DatosTareas.DetalleOperador());
        Assert.False(operador.MuestraReclasificar);
        Assert.False(operador.MuestraCambioPrioridad);

        var dialogo = DatosTareas.Reclasificar();
        Assert.Equal("Centro", dialogo.Panel.ZonaSeleccionada?.Nombre);
        Assert.Equal("Dirección de Obras", dialogo.Panel.OrganismoSeleccionado?.Nombre);
        Assert.Equal("45", dialogo.Panel.DocumentoSeleccionado?.Numero);
    }

    [Fact]
    public async Task Documentos_FabricasDeEstado()
    {
        var cargando = Contenido<DocumentoListViewModel>(DatosDocumentos.Cargando());
        var tarea = cargando.CargarAsync();
        Assert.True(cargando.Cargando);
        Assert.False(tarea.IsCompleted);

        var sinConexion = Contenido<DocumentoListViewModel>(DatosDocumentos.SinConexion());
        await sinConexion.CargarAsync();
        Assert.True(sinConexion.SinConexion);

        var sinPermiso = Contenido<DocumentoListViewModel>(DatosDocumentos.SinPermiso());
        await sinPermiso.CargarAsync();
        Assert.True(sinPermiso.SinPermiso);
        Assert.Equal(DocumentoListViewModel.MensajeSinPermisoDocumentos, ((StockApp.Presentation.ViewModels.ViewModelBase)sinPermiso).MensajeSinPermiso);

        var sinDocumentos = Contenido<DocumentoListViewModel>(DatosDocumentos.SinDocumentos());
        await sinDocumentos.CargarAsync();
        Assert.Empty(sinDocumentos.Activos);
        Assert.False(sinDocumentos.HayFiltrosEnActivos);

        var sinResultados = Contenido<DocumentoListViewModel>(DatosDocumentos.SinResultados());
        await sinResultados.CargarAsync();
        Assert.Empty(sinResultados.Activos);
        Assert.True(sinResultados.HayFiltrosEnActivos);

        var historial = Contenido<DocumentoListViewModel>(DatosDocumentos.ListadoHistorial());
        await historial.CargarAsync();
        Assert.True(historial.MostrandoHistorial);
        Assert.Equal(4, historial.Historial.Count);
        Assert.Empty(historial.Activos);

        var activos = Contenido<DocumentoListViewModel>(DatosDocumentos.Listado());
        await activos.CargarAsync();
        Assert.Equal(6, activos.Activos.Count);
    }

    [Fact]
    public async Task Documentos_FormulariosYDetalles()
    {
        var nuevo = Contenido<DocumentoFormViewModel>(DatosDocumentos.Nuevo());
        Assert.True(nuevo.EsNuevoDocumento);
        Assert.Equal(new DateTime(2026, 10, 6), nuevo.FechaEmisionSeleccionada);
        Assert.False(nuevo.HasErrors);
        Assert.False(nuevo.GuardarCommand.CanExecute(null));

        var conError = Contenido<DocumentoFormViewModel>(DatosDocumentos.NuevoConErrorDeCampo());
        Assert.Equal(DocumentoFormViewModel.MensajeNumeroObligatorio, conError.ErrorDe(nameof(DocumentoFormViewModel.Numero)));
        Assert.Equal(DocumentoFormViewModel.MensajeDescripcionObligatoria, conError.ErrorDe(nameof(DocumentoFormViewModel.Descripcion)));

        var detalle = Contenido<DocumentoFormViewModel>(DatosDocumentos.Detalle());
        Assert.True(detalle.PuedeEditar);
        Assert.True(detalle.PuedeAnular);
        Assert.True(detalle.PuedeVerTareasVinculadas);
        Assert.Equal(3, detalle.Eventos.Count);
        Assert.Single(detalle.AdjuntosPanel.Items);
        Assert.Equal(2, detalle.TareasVinculadas.Count);
        Assert.True(detalle.AdjuntosPanel.PuedeQuitar);

        var cerrado = Contenido<DocumentoFormViewModel>(DatosDocumentos.DetalleCerrado());
        Assert.False(cerrado.PuedeEditarCampos);
        Assert.True(cerrado.PuedeReabrir);
        Assert.False(cerrado.AdjuntosPanel.PuedeAgregar);

        var operador = Contenido<DocumentoFormViewModel>(DatosDocumentos.DetalleOperador());
        Assert.False(operador.PuedeAnular);
        Assert.False(operador.PuedeVerTareasVinculadas);
        Assert.False(operador.AdjuntosPanel.PuedeQuitar);
        Assert.True(operador.AdjuntosPanel.PuedeAgregar);
        await Task.CompletedTask;
    }
}
