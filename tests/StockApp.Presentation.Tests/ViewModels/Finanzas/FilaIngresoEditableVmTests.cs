using System;
using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaIngresoEditableVmTests
{
    private static IngresoAnalizadoDto DtoCompleto() => new(
        HojaOrigen: "ENERO", NumeroFila: 3,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 1, 15), Monto: 5000m,
        Concepto: "Venta de entradas",
        Fuente: "Rentas Generales", FuenteDesconocida: false);

    [Fact]
    public void Desde_MapeaTodosLosCamposDelDto()
    {
        var fila = FilaIngresoEditableVm.Desde(DtoCompleto());

        Assert.Equal("ENERO", fila.HojaOrigen);
        Assert.Equal(3, fila.NumeroFila);
        Assert.Equal(new DateOnly(2026, 1, 15), fila.Fecha);
        Assert.Equal(5000m, fila.Monto);
        Assert.Equal("Venta de entradas", fila.Concepto);
        Assert.Equal("Rentas Generales", fila.Fuente);
        Assert.False(fila.Desbloqueada);
        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void Desde_ConceptoNulo_TieneErrorDeValidacion()
    {
        var dto = DtoCompleto() with { Concepto = null };

        var fila = FilaIngresoEditableVm.Desde(dto);

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Concepto)).Cast<object>());
    }

    [Fact]
    public void EsEditableConcepto_ConceptoCompletoYNoDesbloqueada_EsFalse()
    {
        var fila = FilaIngresoEditableVm.Desde(DtoCompleto());

        Assert.False(fila.EsEditableConcepto);
    }

    [Fact]
    public void Desbloquear_HabilitaLaEdicionDeTodasLasCeldasCompletas()
    {
        var fila = FilaIngresoEditableVm.Desde(DtoCompleto());

        fila.DesbloquearCommand.Execute(null);

        Assert.True(fila.Desbloqueada);
        Assert.True(fila.EsEditableConcepto);
        Assert.True(fila.EsEditableFuente);
        Assert.True(fila.EsEditableFecha);
        Assert.True(fila.EsEditableMonto);
    }
}
