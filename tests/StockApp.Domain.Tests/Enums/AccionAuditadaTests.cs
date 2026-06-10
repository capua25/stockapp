using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Enums;

/// <summary>
/// Verifica que AccionAuditada es append-only:
/// los valores existentes (0-6) NO cambiaron, y los nuevos valores
/// para el catálogo existen con índices >= 7.
/// </summary>
public class AccionAuditadaTests
{
    // ── Valores existentes NO reordenados (integridad de datos persistidos) ──

    [Fact]
    public void CambioPrecio_EsIndice0()
    {
        Assert.Equal(0, (int)AccionAuditada.CambioPrecio);
    }

    [Fact]
    public void AltaProducto_EsIndice1()
    {
        Assert.Equal(1, (int)AccionAuditada.AltaProducto);
    }

    [Fact]
    public void BajaProducto_EsIndice2()
    {
        Assert.Equal(2, (int)AccionAuditada.BajaProducto);
    }

    [Fact]
    public void AltaUsuario_EsIndice3()
    {
        Assert.Equal(3, (int)AccionAuditada.AltaUsuario);
    }

    [Fact]
    public void BajaUsuario_EsIndice4()
    {
        Assert.Equal(4, (int)AccionAuditada.BajaUsuario);
    }

    [Fact]
    public void CambioRol_EsIndice5()
    {
        Assert.Equal(5, (int)AccionAuditada.CambioRol);
    }

    [Fact]
    public void CambioContrasena_EsIndice6()
    {
        Assert.Equal(6, (int)AccionAuditada.CambioContrasena);
    }

    // ── Nuevos valores para catálogo (índices 7-16) ───────────────────────────

    [Fact]
    public void AltaCategoria_EsIndice7()
    {
        Assert.Equal(7, (int)AccionAuditada.AltaCategoria);
    }

    [Fact]
    public void BajaCategoria_EsIndice8()
    {
        Assert.Equal(8, (int)AccionAuditada.BajaCategoria);
    }

    [Fact]
    public void ModificacionCategoria_EsIndice9()
    {
        Assert.Equal(9, (int)AccionAuditada.ModificacionCategoria);
    }

    [Fact]
    public void AltaProveedor_EsIndice10()
    {
        Assert.Equal(10, (int)AccionAuditada.AltaProveedor);
    }

    [Fact]
    public void BajaProveedor_EsIndice11()
    {
        Assert.Equal(11, (int)AccionAuditada.BajaProveedor);
    }

    [Fact]
    public void ModificacionProveedor_EsIndice12()
    {
        Assert.Equal(12, (int)AccionAuditada.ModificacionProveedor);
    }

    [Fact]
    public void AltaUnidadMedida_EsIndice13()
    {
        Assert.Equal(13, (int)AccionAuditada.AltaUnidadMedida);
    }

    [Fact]
    public void BajaUnidadMedida_EsIndice14()
    {
        Assert.Equal(14, (int)AccionAuditada.BajaUnidadMedida);
    }

    [Fact]
    public void ModificacionUnidadMedida_EsIndice15()
    {
        Assert.Equal(15, (int)AccionAuditada.ModificacionUnidadMedida);
    }

    [Fact]
    public void ModificacionProducto_EsIndice16()
    {
        Assert.Equal(16, (int)AccionAuditada.ModificacionProducto);
    }

    // ── Triangulación: todos los nuevos valores están por encima de 6 ─────────

    [Theory]
    [InlineData(AccionAuditada.AltaCategoria)]
    [InlineData(AccionAuditada.BajaCategoria)]
    [InlineData(AccionAuditada.ModificacionCategoria)]
    [InlineData(AccionAuditada.AltaProveedor)]
    [InlineData(AccionAuditada.BajaProveedor)]
    [InlineData(AccionAuditada.ModificacionProveedor)]
    [InlineData(AccionAuditada.AltaUnidadMedida)]
    [InlineData(AccionAuditada.BajaUnidadMedida)]
    [InlineData(AccionAuditada.ModificacionUnidadMedida)]
    [InlineData(AccionAuditada.ModificacionProducto)]
    public void ValoresCatalogo_SonMayoresDeSeis(AccionAuditada accion)
    {
        Assert.True((int)accion >= 7,
            $"{accion} debería tener índice >= 7, pero tiene {(int)accion}");
    }

    // ── Incremento 5 — Movimientos de Stock (append-only a partir de 17) ──────

    [Fact]
    public void AccionAuditada_ValoresNumericos_SonExactos()
    {
        Assert.Equal(17, (int)AccionAuditada.RegistroMovimiento);
        Assert.Equal(18, (int)AccionAuditada.RecalculoStock);
    }
}
