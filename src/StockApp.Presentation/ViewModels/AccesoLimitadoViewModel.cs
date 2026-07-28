using StockApp.Presentation.ViewModels.Administracion;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Modo acotado (FIX 1, re-review final E1): con la licencia vencida, el único camino que
/// tiene el admin para llegar a los backups es autenticarse y entrar acá (ver
/// BloqueoLicenciaViewModel.IngresoLimitadoSolicitado → ShellViewModel.MostrarLoginAccesoLimitado
/// → LoginViewModel.SoloAccesoLimitado → ShellViewModel.MostrarAccesoLimitado). A diferencia de
/// <see cref="ShellMainViewModel"/> (shell completo con sidebar y ~20 comandos de navegación
/// sobre INavigationService), este VM hostea EXCLUSIVAMENTE <see cref="MantenimientoViewModel"/>:
/// no hay sidebar, no usa INavigationService, no hay forma de llegar a Productos/Finanzas/
/// Reportes. Es intencionalmente un callejón sin salida más allá de los backups — "no se
/// habilita operar el sistema" (decisión del usuario, ver spec del fix).
/// </summary>
public partial class AccesoLimitadoViewModel : ViewModelBase
{
    /// <summary>VM de Mantenimiento/backups, la única superficie accesible en este modo.</summary>
    public MantenimientoViewModel Mantenimiento { get; }

    public AccesoLimitadoViewModel(MantenimientoViewModel mantenimiento)
    {
        Mantenimiento = mantenimiento;
    }
}
