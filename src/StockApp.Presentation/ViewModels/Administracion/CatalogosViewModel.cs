using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Pantalla Catálogos: master a la izquierda (grupos con su permiso) y el listado del catálogo elegido a la derecha.
/// La carga del listado la dispara este VM (al abrir y al elegir), nunca la vista del detalle.
/// </summary>
public partial class CatalogosViewModel : ViewModelBase
{
    /// <summary>Texto del estado "Sin permiso" cuando no queda ningún grupo visible.</summary>
    public const string TextoSinPermiso = "No tenés permiso para ver los catálogos.";

    private readonly ICurrentSession _session;
    private ClaveCatalogo? _pendiente;

    public CatalogosViewModel(
        ICurrentSession session,
        CategoriaListViewModel categorias,
        ProveedorListViewModel proveedores,
        UnidadMedidaListViewModel unidadesMedida,
        FuenteFinanciamientoListViewModel fuentes,
        RubroGastoListViewModel rubros,
        LineaPoaListViewModel lineasPoa,
        ZonaListViewModel zonas,
        DimensionTematicaListViewModel dimensiones,
        OrganismoResponsableListViewModel organismos,
        OrigenFinanciamientoListViewModel origenes)
    {
        _session = session;
        Grupos =
        [
            new GrupoCatalogo("INVENTARIO",
            [
                new EntradaCatalogo(ClaveCatalogo.Categorias, categorias.Titulo, "mdi-shape", categorias),
                new EntradaCatalogo(ClaveCatalogo.Proveedores, proveedores.Titulo, "mdi-truck", proveedores),
                new EntradaCatalogo(ClaveCatalogo.UnidadesMedida, unidadesMedida.Titulo, "mdi-ruler", unidadesMedida),
            ], () => PuedeGestionarTablasMaestras),
            new GrupoCatalogo("FINANZAS",
            [
                new EntradaCatalogo(ClaveCatalogo.FuentesFinanciamiento, fuentes.Titulo, "mdi-cash-multiple", fuentes),
                new EntradaCatalogo(ClaveCatalogo.RubrosGasto, rubros.Titulo, "mdi-format-list-numbered", rubros),
                new EntradaCatalogo(ClaveCatalogo.LineasPoa, lineasPoa.Titulo, "mdi-chart-donut", lineasPoa),
            ], () => PuedeGestionarMaestrosFinanzas),
            new GrupoCatalogo("TAREAS",
            [
                new EntradaCatalogo(ClaveCatalogo.Zonas, zonas.Titulo, "mdi-map-marker", zonas),
                new EntradaCatalogo(ClaveCatalogo.Dimensiones, dimensiones.Titulo, "mdi-tag-multiple", dimensiones),
                new EntradaCatalogo(ClaveCatalogo.Organismos, organismos.Titulo, "mdi-domain", organismos),
                new EntradaCatalogo(ClaveCatalogo.Origenes, origenes.Titulo, "mdi-bank", origenes),
            ], () => PuedeGestionarTablasMaestras),
        ];
    }

    public IReadOnlyList<GrupoCatalogo> Grupos { get; }

    [ObservableProperty]
    private CatalogoListadoViewModel? _catalogoActual;

    private bool PuedeGestionarTablasMaestras =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTablasMaestras);

    private bool PuedeGestionarMaestrosFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarMaestrosFinanzas);

    private IEnumerable<EntradaCatalogo> EntradasVisibles =>
        Grupos.Where(g => g.EsVisible).SelectMany(g => g.Entradas);

    /// <summary>Marca el catálogo que abre el próximo <see cref="CargarAsync"/>; no carga nada. Lo usan los formularios al volver.</summary>
    public void Seleccionar(ClaveCatalogo clave) => _pendiente = clave;

    /// <summary>Abre el catálogo pendiente (o el primero visible si no hay, o si ya no se puede ver) y carga su listado.</summary>
    public async Task CargarAsync()
    {
        // Los listados ya protegen su propia carga; esto cubre el camino que no pasa por ellos (y a la propia pantalla ante un 403).
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var entrada = EntradasVisibles.FirstOrDefault(e => e.Clave == _pendiente) ?? EntradasVisibles.FirstOrDefault();
            _pendiente = null;
            if (entrada is null)
            {
                CatalogoActual = null;
                MarcarSinPermiso(TextoSinPermiso);
                return;
            }
            await AbrirAsync(entrada);
        }, TextoSinPermiso);
    }

    [RelayCommand]
    private async Task SeleccionarAsync(EntradaCatalogo? entrada)
    {
        if (entrada is null) return;
        entrada.Listado.Busqueda = "";
        await AbrirAsync(entrada);
    }

    private async Task AbrirAsync(EntradaCatalogo entrada)
    {
        foreach (var e in Grupos.SelectMany(g => g.Entradas))
            e.EstaSeleccionada = ReferenceEquals(e, entrada);
        CatalogoActual = entrada.Listado;
        await entrada.Listado.CargarAsync();
    }

    /// <summary>
    /// Los permisos cambiaron en caliente: se recalculan los grupos. Si el catálogo abierto ya no se puede ver se abre
    /// el primero visible; sin ninguno, "Sin permiso" en lugar del master-detail. Si sigue visible, el listado abierto
    /// también se entera (sus bindings dependen de la sesión).
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        base.NotificarPermisosActualizados();
        foreach (var grupo in Grupos)
            grupo.Recalcular();

        var visibles = EntradasVisibles.ToList();
        if (CatalogoActual is not null && visibles.Any(e => ReferenceEquals(e.Listado, CatalogoActual)))
        {
            CatalogoActual.NotificarPermisosActualizados();
            return;
        }

        if (visibles.FirstOrDefault() is { } primera)
        {
            LimpiarSinPermiso();
            _ = AbrirAsync(primera);
            return;
        }

        CatalogoActual = null;
        foreach (var e in Grupos.SelectMany(g => g.Entradas))
            e.EstaSeleccionada = false;
        MarcarSinPermiso(TextoSinPermiso);
    }
}
