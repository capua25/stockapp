using Moq;
using StockApp.Application.Finanzas;
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
    private readonly AdjuntosPanelViewModel _vm;

    public AdjuntosPanelViewModelTests()
    {
        _vm = new AdjuntosPanelViewModel(_adjuntos.Object, _seleccion.Object, _apertura.Object, _confirmacion.Object);
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
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _adjuntos.Verify(a => a.QuitarAsync(1), Times.Once);
        Assert.Empty(_vm.Items);
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
}
