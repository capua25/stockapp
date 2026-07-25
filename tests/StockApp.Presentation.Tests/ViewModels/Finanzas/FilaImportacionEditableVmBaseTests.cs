using System.Linq;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaImportacionEditableVmBaseTests
{
    public sealed class FilaDePrueba : FilaImportacionEditableVmBase { }

    [Fact]
    public void Nueva_NoTieneErrorServidor()
    {
        var fila = new FilaDePrueba();

        Assert.False(fila.TieneErrorServidor);
        Assert.Null(fila.MensajeErrorServidor);
    }

    [Fact]
    public void AgregarErrorServidor_MarcaLaFilaYAcumulaElMensaje()
    {
        var fila = new FilaDePrueba();

        fila.AgregarErrorServidor("Fecha: la fecha es obligatoria.");
        fila.AgregarErrorServidor("Fuente: la fuente es obligatoria.");

        Assert.True(fila.TieneErrorServidor);
        Assert.Contains("Fecha:", fila.MensajeErrorServidor);
        Assert.Contains("Fuente:", fila.MensajeErrorServidor);
    }

    [Fact]
    public void LimpiarErrorServidor_QuitaElMarcadoYElMensaje()
    {
        var fila = new FilaDePrueba();
        fila.AgregarErrorServidor("Fecha: la fecha es obligatoria.");

        fila.LimpiarErrorServidor();

        Assert.False(fila.TieneErrorServidor);
        Assert.Null(fila.MensajeErrorServidor);
    }
}
