using Moq;
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/gestion/documento*.html y documentos*.html (textos fijados en DatosGestionTests).
/// Los activos vienen por fecha de emisión descendente; el historial es de 2026.
/// </summary>
public static class DatosDocumentos
{
    public static readonly UsuarioSesion UsuarioAdmin = new(1, "mperez", RolUsuario.Admin, "María Pérez");
    public static readonly UsuarioSesion UsuarioOperador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    private static readonly Usuario Maria = new() { Id = 1, NombreUsuario = "mperez" };
    private static readonly Usuario Juan = new() { Id = 2, NombreUsuario = "jrodriguez" };
    private static readonly Usuario Sosa = new() { Id = 3, NombreUsuario = "jsosa" };

    /// <summary>Hora LOCAL fija de 2026 convertida a UTC: el evento (UTC a local) muestra lo mismo en cualquier huso.</summary>
    public static DateTime Hora(int mes, int dia, int hora, int minuto) =>
        new DateTime(2026, mes, dia, hora, minuto, 0, DateTimeKind.Local).ToUniversalTime();

    private static DateTime Dia(int mes, int dia) => new(2026, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static DocumentoAdministrativo D(int id, TipoDocumento tipo, string numero, int mes, int dia, string descripcion,
        EstadoDocumento estado, Usuario registradoPor) => new()
    {
        Id = id, Tipo = tipo, Numero = numero, Anio = 2026, FechaEmision = Dia(mes, dia), Descripcion = descripcion, Estado = estado,
        RegistradoPorUsuarioId = registradoPor.Id, RegistradoPor = registradoPor, FechaRegistro = Hora(mes, dia, 9, 0),
        FechaCierre = estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado ? Hora(mes, dia, 12, 0) : null,
    };

    public static readonly IReadOnlyList<DocumentoAdministrativo> Activos =
    [
        D(12, TipoDocumento.Expediente, "0112", 10, 9, "Contratación de luminarias LED para la rambla", EstadoDocumento.Pendiente, Juan),
        D(11, TipoDocumento.Oficio, "0231", 10, 5, "Solicitud de maquinaria a Vialidad Departamental", EstadoDocumento.EnProceso, Maria),
        D(10, TipoDocumento.Suministro, "0098", 9, 29, "Compra de cemento portland para el corralón municipal", EstadoDocumento.EnProceso, Sosa),
        D(9, TipoDocumento.Expediente, "45", 9, 12, "Bacheo de calles céntricas", EstadoDocumento.EnProceso, Maria),
        D(8, TipoDocumento.Oficio, "0204", 9, 2, "Respuesta a la Junta Departamental por el alumbrado de la costanera", EstadoDocumento.Pendiente, Juan),
        D(7, TipoDocumento.Suministro, "0087", 8, 20, "Arena y pedregullo para la caminería rural", EstadoDocumento.EnProceso, Sosa),
    ];

    public static readonly IReadOnlyList<DocumentoAdministrativo> Historial =
    [
        D(6, TipoDocumento.Expediente, "0031", 6, 10, "Habilitación de la feria vecinal de los domingos", EstadoDocumento.Finalizado, Maria),
        D(5, TipoDocumento.Oficio, "0150", 5, 22, "Pedido de informes duplicado a Rentas", EstadoDocumento.Anulado, Juan),
        D(4, TipoDocumento.Suministro, "0042", 4, 14, "Compra de uniformes para la cuadrilla de obras", EstadoDocumento.Finalizado, Sosa),
        D(3, TipoDocumento.Expediente, "0017", 2, 3, "Convenio de uso del predio del club, reemplazado por el de marzo", EstadoDocumento.Anulado, Maria),
    ];

    private static EventoDocumento E(int id, int documentoId, DateTime fecha, Usuario usuario, string texto, bool auto,
        EstadoDocumento? anterior = null, EstadoDocumento? nuevo = null) => new()
    {
        Id = id, DocumentoAdministrativoId = documentoId, Fecha = fecha, Usuario = usuario, UsuarioId = usuario.Id,
        Texto = texto, EsAutomatico = auto, EstadoAnterior = anterior, EstadoNuevo = nuevo,
    };

    public static readonly IReadOnlyList<EventoDocumento> Eventos =
    [
        E(1, 9, Hora(9, 12, 9, 0), Maria, "Documento registrado.", true),
        E(2, 9, Hora(9, 15, 10, 30), Maria, "Pasó de Pendiente a EnProceso.", true, EstadoDocumento.Pendiente, EstadoDocumento.EnProceso),
        E(3, 9, Hora(10, 2, 15, 45), Juan, "Se pidió presupuesto a tres proveedores; faltan dos respuestas.", false),
    ];

    public static readonly IReadOnlyList<AdjuntoDocumentoDto> Adjuntos =
    [
        new(1, 9, "oficio-firmado.pdf", "application/pdf", 245_760, Hora(9, 15, 11, 0)),
    ];

    public static readonly IReadOnlyList<Tarea> TareasVinculadas = [DatosTareas.Tareas[1], DatosTareas.Tareas[2]];

    static DatosDocumentos()
    {
        Activos[3].Eventos.AddRange(Eventos);
        Historial[0].Eventos.AddRange(
        [
            E(4, 6, Hora(6, 10, 9, 0), Maria, "Documento registrado.", true),
            E(5, 6, Hora(6, 10, 12, 0), Maria, "Pasó de EnProceso a Finalizado.", true, EstadoDocumento.EnProceso, EstadoDocumento.Finalizado),
        ]);
    }

    private static ICurrentSession Sesion(bool operador)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(operador ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(operador
            ? new HashSet<string> { Permisos.GestionarDocumentos }
            : new HashSet<string>());
        sesion.Setup(s => s.UsuarioActual).Returns(operador ? UsuarioOperador : UsuarioAdmin);
        return sesion.Object;
    }

    public static DocumentoListViewModel ListadoVm(Func<Task<IReadOnlyList<DocumentoAdministrativo>>> activos,
        Func<Task<IReadOnlyList<DocumentoAdministrativo>>>? historial = null, bool operador = false)
    {
        var servicio = new Mock<IDocumentoAdministrativoService>();
        servicio.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>())).Returns(activos);
        servicio.Setup(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()))
            .Returns(historial ?? (() => Task.FromResult(Historial)));
        return new DocumentoListViewModel(servicio.Object, Sesion(operador), Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    public static DocumentoFormViewModel Formulario(bool operador = false)
    {
        var sesion = Sesion(operador);
        var servicio = new Mock<IDocumentoAdministrativoService>();
        var adjuntos = new Mock<IAdjuntoDocumentoService>();
        adjuntos.Setup(s => s.ListarPorDocumentoAsync(It.IsAny<int>())).ReturnsAsync(Adjuntos);
        var tareas = new Mock<ITareaService>();
        tareas.Setup(s => s.ListarPorDocumentoAsync(It.IsAny<int>())).ReturnsAsync(TareasVinculadas);
        var panel = new AdjuntosDocumentoPanelViewModel(adjuntos.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion);
        return new DocumentoFormViewModel(servicio.Object, sesion, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(),
            panel, tareas.Object);
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm, bool operador = false) =>
        operador
            ? DatosShell.Crear(UsuarioOperador, [Permisos.GestionarDocumentos], "Documentos", vm)
            : DatosShell.Crear(UsuarioAdmin, [], "Documentos", vm);

    private static Task<IReadOnlyList<DocumentoAdministrativo>> ListaActivos() => Task.FromResult(Activos);

    public static ShellMainViewModel Listado() => EnShell(ListadoVm(ListaActivos));

    /// <summary>Vista Historial puesta ANTES de la carga (la recarga automática todavía no está armada) y año fijo.</summary>
    public static ShellMainViewModel ListadoHistorial()
    {
        var vm = ListadoVm(ListaActivos);
        vm.FiltroHistorialAnio = 2026;
        vm.VistaSeleccionada = vm.Vistas[1];
        return EnShell(vm);
    }

    public static ShellMainViewModel ListadoOperador() => EnShell(ListadoVm(ListaActivos, operador: true), operador: true);
    public static ShellMainViewModel Cargando() =>
        EnShell(ListadoVm(() => new TaskCompletionSource<IReadOnlyList<DocumentoAdministrativo>>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(ListadoVm(() => Task.FromException<IReadOnlyList<DocumentoAdministrativo>>(new ServidorNoDisponibleException())));
    public static ShellMainViewModel SinPermiso() =>
        EnShell(ListadoVm(() => Task.FromException<IReadOnlyList<DocumentoAdministrativo>>(new UnauthorizedAccessException())));
    public static ShellMainViewModel SinDocumentos() =>
        EnShell(ListadoVm(() => Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>([])));

    /// <summary>Hay activos pero el filtro de Tipo (Oficio) con texto que no coincide con nada: sin resultados.</summary>
    public static ShellMainViewModel SinResultados()
    {
        var vm = ListadoVm(() => Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>([]));
        vm.FiltroActivosTexto = "licitación";
        return EnShell(vm);
    }

    public static ShellMainViewModel Nuevo()
    {
        var vm = Formulario();
        vm.CargarParaCrear();
        vm.AnioSeleccionado = 2026;
        vm.FechaEmisionSeleccionada = new DateTime(2026, 10, 6);
        return EnShell(vm);
    }

    /// <summary>Número y Descripción tocados y vacíos: los dos errores debajo de su campo y Guardar deshabilitado.</summary>
    public static ShellMainViewModel NuevoConErrorDeCampo()
    {
        var vm = Formulario();
        vm.CargarParaCrear();
        vm.AnioSeleccionado = 2026;
        vm.FechaEmisionSeleccionada = new DateTime(2026, 10, 6);
        vm.MarcarTocado(nameof(DocumentoFormViewModel.Numero));
        vm.MarcarTocado(nameof(DocumentoFormViewModel.Descripcion));
        return EnShell(vm);
    }

    /// <summary>El expediente 45/2026 En proceso, con tres eventos, un adjunto y dos tareas vinculadas.</summary>
    public static ShellMainViewModel Detalle()
    {
        var vm = Formulario();
        vm.CargarParaVerAsync(Activos[3]).GetAwaiter().GetResult();
        return EnShell(vm);
    }

    /// <summary>El expediente 31/2026 Finalizado: campos deshabilitados, solo Reabrir.</summary>
    public static ShellMainViewModel DetalleCerrado()
    {
        var vm = Formulario();
        vm.CargarParaVerAsync(Historial[0]).GetAwaiter().GetResult();
        return EnShell(vm);
    }

    /// <summary>Operador con documentos.gestionar y sin tareas.gestionar: sin Anular, sin Quitar y sin Tareas vinculadas.</summary>
    public static ShellMainViewModel DetalleOperador()
    {
        var vm = Formulario(operador: true);
        vm.CargarParaVerAsync(Activos[3]).GetAwaiter().GetResult();
        return EnShell(vm, operador: true);
    }
}
