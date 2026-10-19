using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Orquesta el flujo de actualización al arranque: busca → para critical intenta descargar
/// → aplica la política → expone la <see cref="AccionUx"/> resultante.
/// Se dispara desde <see cref="ViewModels.ShellViewModel.InicializarAsync"/> en background.
/// No bloquea el arranque salvo <c>critical</c> (que se procesa in-place pero sin demorar la UI).
/// </summary>
public sealed class CoordinadorActualizacion
{
    private readonly IUpdateService          _updateService;
    private readonly PoliticaUxActualizacion _politica;

    /// <summary>Cada cuánto se repite la búsqueda con la app abierta.</summary>
    public static readonly TimeSpan IntervaloChequeoHorario = TimeSpan.FromHours(1);

    // Un único cerrojo para arranque, chequeo horario y aplicar: nunca dos BuscarAsync/DescargarAsync
    // a la vez (el UltimoFeed de la fuente es compartido). Todo acceso a _notificadas y
    // _ultimaVersionBuscada ocurre con el cerrojo tomado.
    private readonly SemaphoreSlim      _cerrojo = new(1, 1);
    private readonly HashSet<string>    _notificadas = new(StringComparer.Ordinal);
    private string?                     _ultimaVersionBuscada;
    private volatile bool               _aplicacionSolicitada;

    /// <summary>
    /// Resultado de la evaluación: qué modo de UI debe mostrarse.
    /// Starts as Ninguno. Se actualiza tras <see cref="EvaluarEnArranqueAsync"/>.
    /// </summary>
    public AccionUx AccionUxActual { get; private set; } =
        new(ModoUx.Ninguno, null, false, false);

    public CoordinadorActualizacion(
        IUpdateService          updateService,
        PoliticaUxActualizacion politica)
    {
        _updateService = updateService;
        _politica      = politica;
    }

    /// <summary>
    /// Mapea una <see cref="AccionUx"/> al ViewModel de overlay correspondiente, o null si no
    /// hay overlay (ModoUx.Ninguno). Método estático y puro: facilita pruebas unitarias.
    /// </summary>
    public static ViewModelBase? ResolverOverlayViewModel(AccionUx accion) =>
        accion.Modo switch
        {
            ModoUx.Ninguno          => null,
            ModoUx.BannerDiscreto   => new ActualizacionBannerViewModel(accion),
            ModoUx.ModalPosponible  => new ActualizacionModalViewModel(accion),
            ModoUx.BloqueoCritico   => new ActualizacionBloqueoViewModel(accion),
            ModoUx.ModoDegradado    => new ActualizacionBloqueoViewModel(accion),
            ModoUx.AvisoCriticoEnSesion => new ActualizacionAvisoCriticoViewModel(accion),
            _                       => null,
        };

    /// <summary>
    /// Evalúa si hay actualización disponible y decide la acción de UI.
    /// Para severity <c>critical</c>, intenta descargar inmediatamente para determinar
    /// si el modo es BloqueoCritico o ModoDegradado (descarga fallida).
    /// </summary>
    public async Task EvaluarEnArranqueAsync()
    {
        await _cerrojo.WaitAsync();
        try
        {
            var resultado = await _updateService.BuscarAsync();

            if (!resultado.HayUpdate)
            {
                // AccionUxActual ya es Ninguno; no hay nada que hacer.
                return;
            }

            RegistrarNotificada(resultado.Version);

            bool descargaPosible = true;

            if (resultado.Severity == UpdateSeverity.Critical)
            {
                try
                {
                    await _updateService.DescargarAsync();
                }
                catch
                {
                    descargaPosible = false;
                }
            }

            AccionUxActual = _politica.Decidir(resultado, descargaPosible);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <summary>
    /// Chequeo horario con la app abierta. Devuelve la acción a mostrar, o <c>null</c> si no
    /// hay nada que mostrar: ya se pidió aplicar, hay otra operación en curso (ese tick se
    /// saltea), la búsqueda falló (en silencio), no hay update o esa versión ya se notificó.
    /// </summary>
    public async Task<AccionUx?> EvaluarEnSesionAsync()
    {
        if (_aplicacionSolicitada) return null;

        if (!await _cerrojo.WaitAsync(0)) return null;
        try
        {
            UpdateCheckResult resultado;
            try
            {
                resultado = await _updateService.BuscarAsync();
            }
            catch
            {
                return null;
            }

            if (!resultado.HayUpdate) return null;

            if (!RegistrarNotificada(resultado.Version)) return null;

            return _politica.DecidirEnSesion(resultado);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    // Debe llamarse con el cerrojo tomado. Devuelve false si la versión ya estaba notificada.
    private bool RegistrarNotificada(string? version)
    {
        _ultimaVersionBuscada = version;
        return version is null || _notificadas.Add(version);
    }

    /// <summary>
    /// Ejecuta el flujo real de "aplicar actualización" disparado por el usuario desde
    /// cualquiera de los overlays (banner, modal o bloqueo): descarga y luego aplica +
    /// reinicia. Para <see cref="ModoUx.BloqueoCritico"/> la descarga ya pudo haber
    /// ocurrido en <see cref="EvaluarEnArranqueAsync"/>; <see cref="IUpdateService"/>
    /// mantiene su propio estado interno del update pendiente, por lo que volver a
    /// invocar <c>DescargarAsync</c> aquí es seguro.
    /// No debe tumbar la UI: cualquier error de descarga o aplicación se atrapa y se
    /// reporta como <c>false</c> en vez de propagar la excepción.
    /// </summary>
    /// <returns><c>true</c> si el flujo se ejecutó sin errores; <c>false</c> si falló.</returns>
    public async Task<bool> AplicarActualizacionAsync()
    {
        _aplicacionSolicitada = true;
        await _cerrojo.WaitAsync();
        try
        {
            await _updateService.DescargarAsync();
            _updateService.AplicarYReiniciar();
            return true;
        }
        catch
        {
            // La versión vuelve a ser notificable y el chequeo horario vuelve a correr.
            if (_ultimaVersionBuscada is not null) _notificadas.Remove(_ultimaVersionBuscada);
            _aplicacionSolicitada = false;
            return false;
        }
        finally
        {
            _cerrojo.Release();
        }
    }
}
