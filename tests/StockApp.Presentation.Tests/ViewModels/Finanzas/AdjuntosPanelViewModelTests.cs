using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class AdjuntosPanelViewModelTests
{
    private readonly Mock<IAdjuntoService> _adjuntos = new();
    private readonly Mock<IServicioSeleccionArchivo> _seleccion = new();
    private readonly Mock<IServicioAperturaArchivo> _apertura = new();
    private readonly Mock<IConfirmacionService> _confirmacion = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly AdjuntosPanelViewModel _vm;

    public AdjuntosPanelViewModelTests()
    {
        // Default: sin permisos configurados (mismo comportamiento que el TienePermiso mockeado
        // sin Setup, que devolvía false). Los tests de PuedeModificar pisan este Setup con el
        // suyo propio.
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());

        _vm = new AdjuntosPanelViewModel(
            _adjuntos.Object, _seleccion.Object, _apertura.Object, _confirmacion.Object,
            _session.Object);
    }

    [Fact]
    public async Task InicializarAsync_ConGastoId_CargaListaDeGasto()
    {
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>
        {
            new(1, "a.pdf", "application/pdf", 10, 5, null, DateTime.UtcNow),
        });

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.Single(_vm.Items);
        _adjuntos.Verify(a => a.ListarPorGastoAsync(5), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_ConPagoGastoId_CargaListaDePago()
    {
        _adjuntos.Setup(a => a.ListarPorPagoAsync(8)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: null, pagoGastoId: 8);

        _adjuntos.Verify(a => a.ListarPorPagoAsync(8), Times.Once);
    }

    [Fact]
    public async Task AgregarAsync_UsuarioCancelaSeleccion_NoLlamaAlServicio()
    {
        _seleccion.Setup(s => s.SeleccionarArchivoAsync()).ReturnsAsync(((string, byte[])?)null);
        await _vm.InicializarAsync(5, null);

        await _vm.AgregarCommand.ExecuteAsync(null);

        _adjuntos.Verify(a => a.AgregarAGastoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_ConGastoId_SubeYRecargaLista()
    {
        _seleccion.Setup(s => s.SeleccionarArchivoAsync()).ReturnsAsync(("factura.pdf", new byte[] { 1, 2 }));
        _adjuntos.Setup(a => a.AgregarAGastoAsync(5, "factura.pdf", It.IsAny<byte[]>()))
            .ReturnsAsync(new AdjuntoDto(1, "factura.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow));
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5))
            .ReturnsAsync(new List<AdjuntoDto> { new(1, "factura.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow) });
        await _vm.InicializarAsync(5, null);

        await _vm.AgregarCommand.ExecuteAsync(null);

        _adjuntos.Verify(a => a.AgregarAGastoAsync(5, "factura.pdf", It.IsAny<byte[]>()), Times.Once);
        Assert.Single(_vm.Items);
    }

    [Fact]
    public async Task QuitarAsync_LlamaAlServicioYRecarga()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.SetupSequence(a => a.ListarPorGastoAsync(5))
            .ReturnsAsync(new List<AdjuntoDto> { item })
            .ReturnsAsync(new List<AdjuntoDto>());
        _confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _adjuntos.Verify(a => a.QuitarAsync(1), Times.Once);
        Assert.Empty(_vm.Items);
    }

    [Fact]
    public async Task QuitarAsync_PreguntaConElNombreDelArchivo()
    {
        var item = new AdjuntoDto(1, "factura-A-0004521.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto> { item });
        _confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _confirmacion.Verify(c => c.PreguntarAsync("¿Quitar el adjunto \"factura-A-0004521.pdf\"?"), Times.Once);
        Assert.Equal("¿Quitar el adjunto \"factura-A-0004521.pdf\"?", AdjuntosPanelViewModel.MensajeConfirmarQuitar("factura-A-0004521.pdf"));
    }

    [Fact]
    public async Task QuitarAsync_Cancelado_NoQuitaNiRecarga()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto> { item });
        _confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _adjuntos.Verify(a => a.QuitarAsync(It.IsAny<int>()), Times.Never);
        _adjuntos.Verify(a => a.ListarPorGastoAsync(5), Times.Once); // solo la carga inicial
        Assert.Single(_vm.Items);
    }

    [Fact]
    public async Task PuedeModificar_SeRecalculaConLosPermisosActuales_SinVolverAInicializar()
    {
        var permisos = new HashSet<string> { Permisos.RegistrarGastos };
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(() => permisos);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        await _vm.InicializarAsync(5, null);
        Assert.True(_vm.PuedeModificar);

        permisos.Remove(Permisos.RegistrarGastos);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task PuedeModificar_PanelDeGasto_NoDependeDelPermisoDePagos()
    {
        var permisos = new HashSet<string> { Permisos.RegistrarGastos, Permisos.RegistrarPagos };
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(() => permisos);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        await _vm.InicializarAsync(5, null);

        permisos.Remove(Permisos.RegistrarPagos);

        Assert.True(_vm.PuedeModificar);
    }

    [Fact]
    public void PuedeModificar_AntesDeInicializar_EsFalse_AunSiendoAdmin()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_AvisaElCambioDePuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        var avisos = new List<string?>();
        _vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        await _vm.InicializarAsync(5, null);

        Assert.Contains(nameof(AdjuntosPanelViewModel.PuedeModificar), avisos);
    }

    [Fact]
    public async Task VerAsync_DescargaYAbre()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.Setup(a => a.ObtenerContenidoAsync(1))
            .ReturnsAsync(new AdjuntoContenidoDto("a.pdf", "application/pdf", new byte[] { 1, 2 }));

        await _vm.VerCommand.ExecuteAsync(item);

        _apertura.Verify(x => x.AbrirAsync("a.pdf", It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_PanelDeGasto_ConPermisoRegistrarGastos_HabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarGastos });
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.True(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_PanelDeGasto_SinPermisoRegistrarGastos_DeshabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_PanelDePago_ConPermisoRegistrarPagos_HabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarPagos });
        _adjuntos.Setup(a => a.ListarPorPagoAsync(8)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: null, pagoGastoId: 8);

        Assert.True(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_ListarPorGastoAsyncLanzaExcepcion_InformaYNoCrashea()
    {
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ThrowsAsync(new InvalidOperationException("error de red"));

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        _confirmacion.Verify(c => c.InformarAsync("error de red"), Times.Once);
        Assert.Empty(_vm.Items);
    }
}
