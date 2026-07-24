using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class HistorialImportacionesViewModelTests
{
    private static (HistorialImportacionesViewModel vm, Mock<IImportacionService> svc, Mock<IConfirmacionService> confirm)
        Crear(IReadOnlyList<ImportacionHistorialDto>? historial = null)
    {
        var svc = new Mock<IImportacionService>();
        svc.Setup(s => s.ListarHistorialAsync()).ReturnsAsync(historial ?? new List<ImportacionHistorialDto>());

        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new HistorialImportacionesViewModel(svc.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaFilasDesdeElServicio()
    {
        var historial = new List<ImportacionHistorialDto>
        {
            new(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
            new(Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), 2025, "admin", true),
        };
        var (vm, _, _) = Crear(historial);

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
    }

    [Fact]
    public async Task RevertirAsync_FilaActiva_LlamaAlServicioYRecarga()
    {
        var id = Guid.NewGuid();
        var historial = new List<ImportacionHistorialDto> { new(id, DateTime.UtcNow, 2026, "admin", false) };
        var (vm, svc, _) = Crear(historial);
        svc.Setup(s => s.RevertirAsync(id))
            .ReturnsAsync(new ResultadoReversionDto(id, 1, 0, 0, 0, 0));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.RevertirCommand.ExecuteAsync(null);

        svc.Verify(s => s.RevertirAsync(id), Times.Once);
        svc.Verify(s => s.ListarHistorialAsync(), Times.Exactly(2)); // carga inicial + refresco post-revertir
    }

    [Fact]
    public void PuedeRevertir_FilaYaRevertida_False()
    {
        var (vm, _, _) = Crear();
        vm.FilaSeleccionada = new ImportacionHistorialDto(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", true);

        Assert.False(vm.RevertirCommand.CanExecute(null));
    }
}
