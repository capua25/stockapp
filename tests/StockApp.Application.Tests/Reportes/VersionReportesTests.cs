using System.Threading.Tasks;
using StockApp.Application.Reportes;
using Xunit;

namespace StockApp.Application.Tests.Reportes;

public class VersionReportesTests
{
    [Fact]
    public void Actual_AlInicio_EsCero()
    {
        var version = new VersionReportes();
        Assert.Equal(0, version.Actual);
    }

    [Fact]
    public void Invalidar_IncrementaLaVersion()
    {
        var version = new VersionReportes();

        version.Invalidar();

        Assert.Equal(1, version.Actual);
    }

    [Fact]
    public void Invalidar_VariasVeces_IncrementaMonotonicamente()
    {
        var version = new VersionReportes();

        version.Invalidar();
        version.Invalidar();
        version.Invalidar();

        Assert.Equal(3, version.Actual);
    }

    [Fact]
    public async Task Invalidar_Concurrente_NoPierdeIncrementos()
    {
        var version = new VersionReportes();
        var tareas = new Task[100];

        for (var i = 0; i < tareas.Length; i++)
            tareas[i] = Task.Run(() => version.Invalidar());
        await Task.WhenAll(tareas);

        Assert.Equal(100, version.Actual);
    }
}
