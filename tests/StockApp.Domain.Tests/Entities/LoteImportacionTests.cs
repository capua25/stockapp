using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

/// <summary>
/// fix/integridad-referencial: LoteImportacion reemplaza a las 5 columnas Guid sueltas
/// (Gastos/IngresosCaja/LineasPoa/PagosGasto.IdImportacion + LogAuditoria.IdLote) como entidad
/// real. RevertidaEn/RevertidaPorUsuarioId solo pueden setearse JUNTOS por MarcarRevertida —
/// mismo criterio que PagoGasto.Automatico (ver PagoGastoTests): un lote con RevertidaEn seteado
/// pero RevertidaPorUsuarioId null (o viceversa) queda en un estado ambiguo que ningún código de
/// lectura sabría interpretar.
/// </summary>
public class LoteImportacionTests
{
    [Fact]
    public void Nueva_NoEstaRevertidaPorDefecto()
    {
        var lote = new LoteImportacion
        {
            Id = Guid.NewGuid(),
            Fecha = DateTime.UtcNow,
            UsuarioId = 1,
            Ejercicio = 2026,
        };

        Assert.Null(lote.RevertidaEn);
        Assert.Null(lote.RevertidaPorUsuarioId);
    }

    [Fact]
    public void MarcarRevertida_SeteaFechaYUsuarioJuntos()
    {
        var lote = new LoteImportacion
        {
            Id = Guid.NewGuid(),
            Fecha = DateTime.UtcNow,
            UsuarioId = 1,
            Ejercicio = 2026,
        };
        var fechaReversion = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

        lote.MarcarRevertida(fechaReversion, usuarioId: 7);

        Assert.Equal(fechaReversion, lote.RevertidaEn);
        Assert.Equal(7, lote.RevertidaPorUsuarioId);
    }

    /// <summary>
    /// Minor 7 del review adversarial: antes, llamar MarcarRevertida dos veces pisaba
    /// RevertidaEn/RevertidaPorUsuarioId en silencio con la segunda fecha/usuario. Hoy la
    /// protege también el guard "yaRevertida" de ImportacionRepository.RevertirAsync, pero la
    /// entidad no puede depender de que TODO llamador futuro repita ese guard -- es el único
    /// punto de mutación documentado como tal (ver el XML-doc del método), así que se defiende
    /// sola.
    /// </summary>
    [Fact]
    public void MarcarRevertida_LlamadaDosVeces_TiraYNoPisaLosValoresOriginales()
    {
        var lote = new LoteImportacion
        {
            Id = Guid.NewGuid(),
            Fecha = DateTime.UtcNow,
            UsuarioId = 1,
            Ejercicio = 2026,
        };
        var primeraReversion = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        var segundaReversion = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        lote.MarcarRevertida(primeraReversion, usuarioId: 7);

        Assert.Throws<InvalidOperationException>(() => lote.MarcarRevertida(segundaReversion, usuarioId: 9));

        Assert.Equal(primeraReversion, lote.RevertidaEn);
        Assert.Equal(7, lote.RevertidaPorUsuarioId);
    }
}
