using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class DocumentoAdministrativoTests
{
    private static DocumentoAdministrativo NuevoDocumento(EstadoDocumento estado = EstadoDocumento.Pendiente) => new()
    {
        Numero = "0087",
        Anio = 2026,
        Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow,
        Descripcion = "Solicitud de poda de árbol en vereda",
        RegistradoPorUsuarioId = 1,
        FechaRegistro = DateTime.UtcNow,
        Estado = estado,
    };

    // ── Transiciones válidas (D4 del spec) ────────────────────────────────────

    [Fact]
    public void CambiarEstado_PendienteAEnProceso_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.Pendiente);
        doc.CambiarEstado(EstadoDocumento.EnProceso);
        Assert.Equal(EstadoDocumento.EnProceso, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_PendienteAAnulado_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.Pendiente);
        doc.CambiarEstado(EstadoDocumento.Anulado);
        Assert.Equal(EstadoDocumento.Anulado, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_EnProcesoAPendiente_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.CambiarEstado(EstadoDocumento.Pendiente);
        Assert.Equal(EstadoDocumento.Pendiente, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_EnProcesoAFinalizado_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.CambiarEstado(EstadoDocumento.Finalizado);
        Assert.Equal(EstadoDocumento.Finalizado, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_EnProcesoAAnulado_Permitido()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.CambiarEstado(EstadoDocumento.Anulado);
        Assert.Equal(EstadoDocumento.Anulado, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_FinalizadoAEnProceso_Permitido_PorReapertura()
    {
        var doc = NuevoDocumento(EstadoDocumento.Finalizado);
        doc.CambiarEstado(EstadoDocumento.EnProceso);
        Assert.Equal(EstadoDocumento.EnProceso, doc.Estado);
    }

    [Fact]
    public void CambiarEstado_AnuladoAEnProceso_Permitido_PorReapertura()
    {
        var doc = NuevoDocumento(EstadoDocumento.Anulado);
        doc.CambiarEstado(EstadoDocumento.EnProceso);
        Assert.Equal(EstadoDocumento.EnProceso, doc.Estado);
    }

    // ── Transiciones inválidas, incluida la identidad ─────────────────────────

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Pendiente)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Finalizado)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.EnProceso)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Finalizado)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Pendiente)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Anulado)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Anulado)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Pendiente)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Finalizado)]
    public void CambiarEstado_TransicionNoListada_LanzaReglaDeNegocioYNoMuta(
        EstadoDocumento origen, EstadoDocumento destino)
    {
        var doc = NuevoDocumento(origen);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => doc.CambiarEstado(destino));

        Assert.Contains(origen.ToString(), ex.Message);
        Assert.Contains(destino.ToString(), ex.Message);
        Assert.Equal(origen, doc.Estado);
    }

    // ── EsActivo / EsCerrado para los 4 estados ───────────────────────────────

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, true, false)]
    [InlineData(EstadoDocumento.EnProceso, true, false)]
    [InlineData(EstadoDocumento.Finalizado, false, true)]
    [InlineData(EstadoDocumento.Anulado, false, true)]
    public void EsActivo_EsCerrado_ReflejanElEstado(EstadoDocumento estado, bool esperadoActivo, bool esperadoCerrado)
    {
        var doc = NuevoDocumento(estado);

        Assert.Equal(esperadoActivo, doc.EsActivo);
        Assert.Equal(esperadoCerrado, doc.EsCerrado);
    }

    // ── CambiarEstado no toca FechaCierre (D8: la sella el servicio, no la entidad) ──

    [Fact]
    public void CambiarEstado_NoTocaFechaCierre()
    {
        var doc = NuevoDocumento(EstadoDocumento.EnProceso);
        doc.FechaCierre = null;

        doc.CambiarEstado(EstadoDocumento.Finalizado);

        Assert.Null(doc.FechaCierre);
    }

    // ── PuedeTransicionarA: misma tabla que CambiarEstado, de solo lectura ────

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.EnProceso, true)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Anulado, true)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.Pendiente, EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.Pendiente, true)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.Finalizado, true)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.Anulado, true)]
    [InlineData(EstadoDocumento.EnProceso, EstadoDocumento.EnProceso, false)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.EnProceso, true)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Anulado, false)]
    [InlineData(EstadoDocumento.Finalizado, EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.EnProceso, true)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.Anulado, EstadoDocumento.Anulado, false)]
    public void PuedeTransicionarA_ReflejaExactamenteLaMismaTablaQueCambiarEstado(
        EstadoDocumento origen, EstadoDocumento destino, bool esperado)
    {
        var doc = NuevoDocumento(origen);

        Assert.Equal(esperado, doc.PuedeTransicionarA(destino));
        // De solo lectura: consultar no debe mutar el estado.
        Assert.Equal(origen, doc.Estado);
    }
}
