using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class ProyectoTests
{
    private static readonly DateTime Ahora = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Proyecto Nuevo(EstadoProyecto estado = EstadoProyecto.Pendiente) => new()
    {
        Titulo = "Plaza del barrio",
        Estado = estado,
    };

    [Theory]
    [InlineData(EstadoProyecto.Pendiente, EstadoProyecto.EnCurso)]
    [InlineData(EstadoProyecto.Pendiente, EstadoProyecto.Cancelado)]
    [InlineData(EstadoProyecto.EnCurso, EstadoProyecto.Pendiente)]
    [InlineData(EstadoProyecto.EnCurso, EstadoProyecto.Terminado)]
    [InlineData(EstadoProyecto.EnCurso, EstadoProyecto.Cancelado)]
    [InlineData(EstadoProyecto.Terminado, EstadoProyecto.EnCurso)]
    [InlineData(EstadoProyecto.Cancelado, EstadoProyecto.EnCurso)]
    public void CambiarEstado_TransicionesValidas_Aplican(EstadoProyecto origen, EstadoProyecto destino)
    {
        var proyecto = Nuevo(origen);

        Assert.True(proyecto.PuedeTransicionarA(destino));
        proyecto.CambiarEstado(destino, Ahora);

        Assert.Equal(destino, proyecto.Estado);
    }

    [Theory]
    [InlineData(EstadoProyecto.Pendiente, EstadoProyecto.Terminado)]
    [InlineData(EstadoProyecto.Terminado, EstadoProyecto.Cancelado)]
    [InlineData(EstadoProyecto.Pendiente, EstadoProyecto.Pendiente)]
    public void CambiarEstado_TransicionInvalida_LanzaReglaDeNegocio(EstadoProyecto origen, EstadoProyecto destino)
    {
        var proyecto = Nuevo(origen);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => proyecto.CambiarEstado(destino, Ahora));

        Assert.Equal($"No se puede pasar el proyecto de '{origen}' a '{destino}'.", ex.Message);
        Assert.Equal(origen, proyecto.Estado);
    }

    [Theory]
    [InlineData(EstadoProyecto.Terminado)]
    [InlineData(EstadoProyecto.Cancelado)]
    public void CambiarEstado_ATerminadoOCancelado_SellaFechaFin(EstadoProyecto destino)
    {
        var proyecto = Nuevo(EstadoProyecto.EnCurso);

        proyecto.CambiarEstado(destino, Ahora);

        Assert.Equal(Ahora, proyecto.FechaFin);
    }

    [Fact]
    public void CambiarEstado_Reabrir_LimpiaFechaFin()
    {
        var proyecto = Nuevo(EstadoProyecto.Terminado);
        proyecto.FechaFin = Ahora;

        proyecto.CambiarEstado(EstadoProyecto.EnCurso, Ahora.AddDays(1));

        Assert.Null(proyecto.FechaFin);
    }

    [Theory]
    [InlineData(EstadoProyecto.Pendiente, false)]
    [InlineData(EstadoProyecto.EnCurso, false)]
    [InlineData(EstadoProyecto.Terminado, true)]
    [InlineData(EstadoProyecto.Cancelado, true)]
    public void EsCerrado_SoloTerminadoYCancelado(EstadoProyecto estado, bool esperado) =>
        Assert.Equal(esperado, Nuevo(estado).EsCerrado);

    [Fact]
    public void Defaults_PendienteYPrioridadMedia()
    {
        var proyecto = new Proyecto();

        Assert.Equal(EstadoProyecto.Pendiente, proyecto.Estado);
        Assert.Equal(PrioridadTarea.Media, proyecto.Prioridad);
        Assert.Null(proyecto.Presupuesto);
    }
}
