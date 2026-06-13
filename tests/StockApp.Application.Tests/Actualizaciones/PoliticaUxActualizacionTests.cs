using StockApp.Application.Actualizaciones;

namespace StockApp.Application.Tests.Actualizaciones;

public class PoliticaUxActualizacionTests
{
    private readonly PoliticaUxActualizacion _sut = new();

    // ── helpers ───────────────────────────────────────────────────────────────

    private static UpdateCheckResult ResultadoCon(
        bool hayUpdate,
        UpdateSeverity severity = UpdateSeverity.Normal,
        string? notas = "Texto de update") =>
        new(hayUpdate, hayUpdate ? "1.2.0" : null, severity, hayUpdate ? notas : null);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Decidir_SinUpdate_Ninguno()
    {
        var resultado = ResultadoCon(false);

        var accion = _sut.Decidir(resultado, descargaPosible: true);

        Assert.Equal(ModoUx.Ninguno, accion.Modo);
        Assert.False(accion.Posponible);
        Assert.False(accion.ReintentaEnArranque);
    }

    [Fact]
    public void Decidir_Normal_BannerDiscreto()
    {
        var resultado = ResultadoCon(true, UpdateSeverity.Normal);

        var accion = _sut.Decidir(resultado, descargaPosible: true);

        Assert.Equal(ModoUx.BannerDiscreto, accion.Modo);
        Assert.True(accion.Posponible);
        Assert.True(accion.ReintentaEnArranque);
    }

    [Fact]
    public void Decidir_Important_ModalPosponible()
    {
        var resultado = ResultadoCon(true, UpdateSeverity.Important);

        var accion = _sut.Decidir(resultado, descargaPosible: true);

        Assert.Equal(ModoUx.ModalPosponible, accion.Modo);
        Assert.True(accion.Posponible);
        Assert.True(accion.ReintentaEnArranque);
    }

    [Fact]
    public void Decidir_CriticalDescargaOk_BloqueoCritico()
    {
        var resultado = ResultadoCon(true, UpdateSeverity.Critical);

        var accion = _sut.Decidir(resultado, descargaPosible: true);

        Assert.Equal(ModoUx.BloqueoCritico, accion.Modo);
        Assert.False(accion.Posponible);
        Assert.True(accion.ReintentaEnArranque);
    }

    [Fact]
    public void Decidir_CriticalDescargaFalla_ModoDegradado()
    {
        var resultado = ResultadoCon(true, UpdateSeverity.Critical);

        var accion = _sut.Decidir(resultado, descargaPosible: false);

        Assert.Equal(ModoUx.ModoDegradado, accion.Modo);
        Assert.False(accion.Posponible);
        Assert.True(accion.ReintentaEnArranque);
    }

    [Fact]
    public void Decidir_ConUpdate_MarcaReintentaEnArranque()
    {
        // Aplica para los tres niveles de severidad con update presente
        var normal    = _sut.Decidir(ResultadoCon(true, UpdateSeverity.Normal),    true);
        var important = _sut.Decidir(ResultadoCon(true, UpdateSeverity.Important), true);
        var critical  = _sut.Decidir(ResultadoCon(true, UpdateSeverity.Critical),  false);

        Assert.True(normal.ReintentaEnArranque);
        Assert.True(important.ReintentaEnArranque);
        Assert.True(critical.ReintentaEnArranque);
    }
}
