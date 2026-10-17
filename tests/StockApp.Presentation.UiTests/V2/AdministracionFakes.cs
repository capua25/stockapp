using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Servicios de catálogo de la Fase 7. Devuelven lo mismo que el contrato real (Task de IReadOnlyList) y fallan como falla
/// ApiErrores: Falla lanza lo que se le ponga (UnauthorizedAccessException, ServidorNoDisponibleException,
/// ReglaDeNegocioException, EntidadNoEncontradaException); Pendiente deja la consulta colgada hasta completarla.
/// </summary>
internal abstract class CatalogoServicioFake<T> where T : class
{
    public List<T> Lista { get; set; } = new();
    public Exception? Falla { get; set; }
    public Exception? FallaBaja { get; set; }
    public TaskCompletionSource<IReadOnlyList<T>>? Pendiente { get; set; }
    public List<int> Bajas { get; } = new();
    public int Consultas { get; private set; }
    public List<T> Altas { get; } = new();
    public List<T> Modificaciones { get; } = new();

    protected Task<IReadOnlyList<T>> Listar()
    {
        Consultas++;
        if (Falla is not null) return Task.FromException<IReadOnlyList<T>>(Falla);
        if (Pendiente is not null) return Pendiente.Task;
        return Task.FromResult<IReadOnlyList<T>>(Lista.ToList());
    }

    protected Task<IReadOnlyList<T>> Activas() =>
        Falla is not null ? Task.FromException<IReadOnlyList<T>>(Falla) : Task.FromResult<IReadOnlyList<T>>(Lista.ToList());

    protected Task<int> Alta(T x) { Altas.Add(x); return Task.FromResult(1); }

    protected Task Modificar(T x) { Modificaciones.Add(x); return Task.CompletedTask; }

    protected Task Baja(int id)
    {
        if (FallaBaja is not null) return Task.FromException(FallaBaja);
        Bajas.Add(id);
        return Task.CompletedTask;
    }
}

internal sealed class CategoriaServicioFake : CatalogoServicioFake<Categoria>, ICategoriaService
{
    public Task<int> AltaAsync(Categoria x) => Alta(x);
    public Task ModificarAsync(Categoria x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<Categoria>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<Categoria>> ListarActivasAsync() => Activas();
}

internal sealed class ProveedorServicioFake : CatalogoServicioFake<Proveedor>, IProveedorService
{
    public Task<int> AltaAsync(Proveedor x) => Alta(x);
    public Task ModificarAsync(Proveedor x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() => Listar();
    public Task<IReadOnlyList<Proveedor>> ListarActivasAsync() => Activas();
}

internal sealed class UnidadMedidaServicioFake : CatalogoServicioFake<UnidadMedida>, IUnidadMedidaService
{
    public Task<int> AltaAsync(UnidadMedida x) => Alta(x);
    public Task ModificarAsync(UnidadMedida x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync() => Activas();
    public Task<UnidadMedida> GarantizarUnidadPorDefectoAsync() => throw new NotSupportedException();
}

internal sealed class ZonaServicioFake : CatalogoServicioFake<Zona>, IZonaService
{
    public Task<int> AltaAsync(Zona x) => Alta(x);
    public Task ModificarAsync(Zona x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<Zona>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<Zona>> ListarActivasAsync() => Activas();
}

internal sealed class DimensionTematicaServicioFake : CatalogoServicioFake<DimensionTematica>, IDimensionTematicaService
{
    public Task<int> AltaAsync(DimensionTematica x) => Alta(x);
    public Task ModificarAsync(DimensionTematica x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync() => Activas();
}

internal sealed class OrganismoResponsableServicioFake : CatalogoServicioFake<OrganismoResponsable>, IOrganismoResponsableService
{
    public Task<int> AltaAsync(OrganismoResponsable x) => Alta(x);
    public Task ModificarAsync(OrganismoResponsable x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync() => Activas();
}

internal sealed class OrigenFinanciamientoServicioFake : CatalogoServicioFake<OrigenFinanciamiento>, IOrigenFinanciamientoService
{
    public Task<int> AltaAsync(OrigenFinanciamiento x) => Alta(x);
    public Task ModificarAsync(OrigenFinanciamiento x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync() => Activas();
}

internal sealed class FuenteFinanciamientoServicioFake : CatalogoServicioFake<FuenteFinanciamiento>, IFuenteFinanciamientoService
{
    public Task<int> AltaAsync(FuenteFinanciamiento x) => Alta(x);
    public Task ModificarAsync(FuenteFinanciamiento x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() => Activas();
}

internal sealed class RubroGastoServicioFake : CatalogoServicioFake<RubroGasto>, IRubroGastoService
{
    public Task<int> AltaAsync(RubroGasto x) => Alta(x);
    public Task ModificarAsync(RubroGasto x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() => Listar();
    public Task<IReadOnlyList<RubroGasto>> ListarActivosAsync() => Activas();
}

internal sealed class LineaPoaServicioFake : CatalogoServicioFake<LineaPoa>, ILineaPoaService
{
    public Task<int> AltaAsync(LineaPoa x) => Alta(x);
    public Task ModificarAsync(LineaPoa x) => Modificar(x);
    public Task BajaLogicaAsync(int id) => Baja(id);
    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => Listar();
    public Task<IReadOnlyList<LineaPoa>> ListarActivasAsync() => Activas();
}
