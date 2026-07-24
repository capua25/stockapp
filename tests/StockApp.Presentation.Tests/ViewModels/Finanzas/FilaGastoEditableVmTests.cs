using System;
using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaGastoEditableVmTests
{
    private static GastoAnalizadoDto DtoCompleto() => new(
        HojaOrigen: "MARZO", NumeroFila: 5,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 10), Monto: 1500m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-100", NumeroOrden: "O-1",
        Detalle: "Compra de insumos", Destino: "Depósito central",
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 12, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);

    [Fact]
    public void Desde_MapeaTodosLosCamposDelDto()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.Equal("MARZO", fila.HojaOrigen);
        Assert.Equal(5, fila.NumeroFila);
        Assert.Equal(EstadoFila.Ok, fila.Estado);
        Assert.Equal(new DateOnly(2026, 3, 10), fila.Fecha);
        Assert.Equal(1500m, fila.Monto);
        Assert.Equal("ACME SA", fila.Proveedor);
        Assert.Equal("F-100", fila.NumeroFactura);
        Assert.Equal("O-1", fila.NumeroOrden);
        Assert.Equal("Compra de insumos", fila.Detalle);
        Assert.Equal("Depósito central", fila.Destino);
        Assert.Equal("Rentas Generales", fila.Fuente);
        Assert.Equal(12, fila.CodigoRubro);
        Assert.Equal("Materiales", fila.Rubro);
        Assert.False(fila.Desbloqueada);
    }

    [Fact]
    public void Desde_SinCompromisoPoa_CondicionInicialEsContadoSinVencimiento()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.Equal(CondicionPago.Contado, fila.Condicion);
        Assert.Null(fila.FechaVencimiento);
    }

    [Fact]
    public void Desde_ConCompromisoPoa_CondicionInicialEsCreditoConVencimientoIgualAFecha()
    {
        var dto = DtoCompleto() with { LineaPoaAsignada = "RAMBLA" };

        var fila = FilaGastoEditableVm.Desde(dto);

        Assert.Equal(CondicionPago.Credito, fila.Condicion);
        Assert.Equal(fila.Fecha, fila.FechaVencimiento);
    }

    [Fact]
    public void Desde_DtoCompleto_NoTieneErroresDeValidacion()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void Desde_ProveedorNulo_TieneErrorDeValidacionEnProveedor()
    {
        var dto = DtoCompleto() with { Proveedor = null };

        var fila = FilaGastoEditableVm.Desde(dto);

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Proveedor)).Cast<object>());
    }

    [Fact]
    public void Proveedor_SeSeteaAVacio_GeneraErrorDeValidacionEnCaliente()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());
        Assert.False(fila.HasErrors);

        fila.Proveedor = null;

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Proveedor)).Cast<object>());
    }

    [Fact]
    public void EsEditableProveedor_ProveedorNoNuloYFilaNoDesbloqueada_EsFalse()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.False(fila.EsEditableProveedor);
    }

    [Fact]
    public void EsEditableProveedor_ProveedorNulo_EsTrueAunqueNoEsteDesbloqueada()
    {
        var dto = DtoCompleto() with { Proveedor = null };

        var fila = FilaGastoEditableVm.Desde(dto);

        Assert.True(fila.EsEditableProveedor);
    }

    [Fact]
    public void Desbloquear_HabilitaLaEdicionDeTodasLasCeldasCompletas()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());
        Assert.False(fila.EsEditableProveedor);

        fila.DesbloquearCommand.Execute(null);

        Assert.True(fila.Desbloqueada);
        Assert.True(fila.EsEditableProveedor);
        Assert.True(fila.EsEditableFuente);
        Assert.True(fila.EsEditableRubro);
    }

    [Fact]
    public void FechaVencimiento_CreditoSinVencimiento_GeneraErrorDeValidacion()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        fila.Condicion = CondicionPago.Credito;
        fila.FechaVencimiento = null;

        Assert.NotEmpty(fila.GetErrors(nameof(fila.FechaVencimiento)).Cast<object>());
    }

    [Fact]
    public void FechaVencimiento_ContadoConVencimiento_GeneraErrorDeValidacion()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        fila.Condicion = CondicionPago.Contado;
        fila.FechaVencimiento = new DateOnly(2026, 4, 1);

        Assert.NotEmpty(fila.GetErrors(nameof(fila.FechaVencimiento)).Cast<object>());
    }

    [Fact]
    public void FechaVencimiento_CreditoConVencimiento_NoGeneraError()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        fila.FechaVencimiento = new DateOnly(2026, 4, 1);
        fila.Condicion = CondicionPago.Credito;

        Assert.Empty(fila.GetErrors(nameof(fila.FechaVencimiento)).Cast<object>());
    }
}
