using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Proyectos;

namespace StockApp.Presentation.Tests.Helpers;

/// <summary>IProyectoService de prueba para los formularios que solo consultan ListarOpcionesAsync (Moq no inventa una
/// lista vacía para IReadOnlyList: sin Setup devolvería null).</summary>
internal static class ProyectosDeTest
{
    public static IProyectoService Servicio(params OpcionProyectoDto[] abiertos) => Mock(abiertos).Object;

    public static Mock<IProyectoService> Mock(params OpcionProyectoDto[] abiertos)
    {
        var mock = new Mock<IProyectoService>();
        mock.Setup(p => p.ListarOpcionesAsync())
            .ReturnsAsync((IReadOnlyList<OpcionProyectoDto>)abiertos);
        return mock;
    }
}
