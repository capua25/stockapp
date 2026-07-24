using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaLineaPoaEditableVmTests
{
    private static LineaPoaAnalizadaDto Dto(string fuente, decimal presupuesto, decimal saldo, bool esNueva = true) =>
        new(Hoja: "RAMBLA", Ejercicio: 2026, EsNueva: esNueva,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: fuente, FuenteDesconocida: false,
            Presupuesto: presupuesto, SaldoPlanilla: saldo,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());

    [Fact]
    public void DesdeGrupo_UnaSolaAsignacion_MapeaHojaYAsignacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("Rentas Generales", 100000m, 50000m) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.Equal("RAMBLA", fila.Hoja);
        Assert.Equal(2026, fila.Ejercicio);
        Assert.True(fila.EsNueva);
        var asignacion = Assert.Single(fila.Asignaciones);
        Assert.Equal("Rentas Generales", asignacion.Fuente);
        Assert.Equal(100000m, asignacion.Presupuesto);
        Assert.Equal(50000m, asignacion.SaldoPlanilla);
    }

    [Fact]
    public void DesdeGrupo_FinanciamientoMixto_AgrupaLasDosAsignacionesEnUnaSolaFila()
    {
        var lineas = new List<LineaPoaAnalizadaDto>
        {
            Dto("C", 1407252m, 1407252m),
            Dto("B", 92748m, 92748m),
        };
        var grupo = lineas.GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.Equal(2, fila.Asignaciones.Count);
        Assert.Equal(1407252m, fila.Asignaciones[0].Presupuesto);
        Assert.Equal(92748m, fila.Asignaciones[1].Presupuesto);
    }

    [Fact]
    public void DesdeGrupo_EsNueva_ProgramaVacio_TieneErrorDeValidacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("B", 1000m, 500m, esNueva: true) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Programa)).Cast<object>());
    }

    [Fact]
    public void DesdeGrupo_NoEsNueva_ProgramaVacio_NoTieneErrorDeValidacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("B", 1000m, 500m, esNueva: false) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void DesdeGrupo_EsNueva_ProgramaCompleto_NoTieneErrorDeValidacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("B", 1000m, 500m, esNueva: true) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);
        fila.Programa = "Obras públicas";

        Assert.False(fila.HasErrors);
    }
}
