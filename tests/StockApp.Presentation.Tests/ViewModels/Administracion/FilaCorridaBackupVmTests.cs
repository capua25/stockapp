using StockApp.Application.Backups;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

/// <summary>
/// Fix (IMPORTANT, tercer review final E1): antes de este fix, la vista pintaba en rojo
/// (DangerBrush) CUALQUIER MotivoFallo no nulo, incluida la marca de una fila reconciliada
/// (Exitosa, dump huérfano dado de alta tras un restore) -- el admin veía el ícono verde de
/// éxito y, debajo, un texto rojo diciendo que esa fila "no proviene de una corrida real".
/// Estos tests cubren EsFallo/EsNotaInformativa, las propiedades que separan ambos casos.
/// </summary>
public class FilaCorridaBackupVmTests
{
    [Fact]
    public void Fila_Exitosa_ConMotivoFallo_EsNotaInformativaYNoEsFallo()
    {
        // Caso real: fila reconciliada (ServicioBackup.MarcaFilaReconciliada) -- Resultado
        // Exitosa con MotivoFallo no nulo.
        var dto = new CorridaBackupDto(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024,
            "[Reconciliado] Registro reconstruido desde el archivo en disco (posterior a una restauración).");

        var fila = new FilaCorridaBackupVm(dto);

        Assert.True(fila.EsNotaInformativa);
        Assert.False(fila.EsFallo);
    }

    [Fact]
    public void Fila_Fallida_ConMotivoFallo_EsFalloYNoEsNotaInformativa()
    {
        var dto = new CorridaBackupDto(2, DateTime.UtcNow, "Fallida", null, null, "pg_dump falló");

        var fila = new FilaCorridaBackupVm(dto);

        Assert.True(fila.EsFallo);
        Assert.False(fila.EsNotaInformativa);
    }

    [Fact]
    public void Fila_Exitosa_SinMotivoFallo_NiEsFalloNiEsNotaInformativa()
    {
        var dto = new CorridaBackupDto(3, DateTime.UtcNow, "Exitosa", "backup_3.dump", 2048, null);

        var fila = new FilaCorridaBackupVm(dto);

        Assert.False(fila.EsFallo);
        Assert.False(fila.EsNotaInformativa);
    }
}
