using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Finanzas;

public class ConfirmacionImportacionService : IConfirmacionImportacionService
{
    private readonly IImportacionRepository _importacionRepo;
    private readonly IProveedorRepository _proveedores;
    private readonly IRubroGastoRepository _rubros;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ILineaPoaRepository _lineasPoa;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ConfirmacionImportacionService(
        IImportacionRepository importacionRepo,
        IProveedorRepository proveedores,
        IRubroGastoRepository rubros,
        IFuenteFinanciamientoRepository fuentes,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth)
    {
        _importacionRepo = importacionRepo;
        _proveedores = proveedores;
        _rubros = rubros;
        _fuentes = fuentes;
        _lineasPoa = lineasPoa;
        _session = session;
        _auth = auth;
    }

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        // El guard de re-importación (spec §2.6, 409 salvo Forzar) se resuelve DENTRO de la
        // transacción del repositorio, no acá: tiene que correr con el advisory lock tomado
        // para no dejar una ventana de carrera entre dos /confirmar concurrentes del mismo
        // ejercicio (ver Task 6). Este servicio solo pasa dto.Forzar tal cual llegó.
        return await _importacionRepo.ConfirmarAsync(dto, _session.UsuarioActual!.Id);
    }

    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        return await _importacionRepo.RevertirAsync(idImportacion, _session.UsuarioActual!.Id);
    }
}
