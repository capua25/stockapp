using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class EventoDocumentoTests
{
    private static DocumentoAdministrativo NuevoDocumento() => new()
    {
        Numero = "0087",
        Anio = 2026,
        Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow,
        Descripcion = "Solicitud de poda de árbol en vereda",
        RegistradoPorUsuarioId = 1,
        FechaRegistro = DateTime.UtcNow,
    };

    [Fact]
    public void AgregarEvento_NotaManual_QuedaEnLaColeccionConLosDatosCorrectos()
    {
        var doc = NuevoDocumento();

        doc.AgregarEvento(usuarioId: 2, texto: "El vecino trajo la documentación faltante", esAutomatico: false);

        var evento = Assert.Single(doc.Eventos);
        Assert.Equal(2, evento.UsuarioId);
        Assert.Equal("El vecino trajo la documentación faltante", evento.Texto);
        Assert.False(evento.EsAutomatico);
        Assert.Null(evento.EstadoAnterior);
        Assert.Null(evento.EstadoNuevo);
        Assert.True((DateTime.UtcNow - evento.Fecha).TotalSeconds < 5);
    }

    [Fact]
    public void AgregarEvento_CambioDeEstadoAutomatico_QuedaConLosEstadosCompletos()
    {
        var doc = NuevoDocumento();

        doc.AgregarEvento(
            usuarioId: 1, texto: "Cambio de estado: Pendiente → EnProceso", esAutomatico: true,
            anterior: EstadoDocumento.Pendiente, nuevo: EstadoDocumento.EnProceso);

        var evento = Assert.Single(doc.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoNuevo);
    }

    [Fact]
    public void AgregarEvento_VariasVeces_EsAppendOnly_SumaSinReemplazar()
    {
        var doc = NuevoDocumento();

        doc.AgregarEvento(usuarioId: 1, texto: "primero", esAutomatico: false);
        doc.AgregarEvento(usuarioId: 1, texto: "segundo", esAutomatico: false);
        doc.AgregarEvento(usuarioId: 1, texto: "tercero", esAutomatico: false);

        Assert.Equal(3, doc.Eventos.Count);
        Assert.Equal(new[] { "primero", "segundo", "tercero" }, doc.Eventos.Select(e => e.Texto));
    }

    [Fact]
    public void EventoDocumento_NoTieneMetodoDeBorradoNiDeEdicion()
    {
        // Append-only en espíritu: la única forma de sumar eventos es
        // DocumentoAdministrativo.AgregarEvento. La clase EventoDocumento no expone ningún
        // método propio (solo propiedades) — este test documenta la intención revisando el
        // tipo en runtime, no reemplaza una revisión de código.
        var metodosPropios = typeof(EventoDocumento).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName); // descarta get_/set_ de las propiedades

        Assert.Empty(metodosPropios);
    }
}
