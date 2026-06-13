using System;
using System.Threading.Tasks;
using StockApp.Application.Actualizaciones;

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
    /// Evalúa si hay actualización disponible y decide la acción de UI.
    /// Para severity <c>critical</c>, intenta descargar inmediatamente para determinar
    /// si el modo es BloqueoCritico o ModoDegradado (descarga fallida).
    /// </summary>
    public async Task EvaluarEnArranqueAsync()
    {
        var resultado = await _updateService.BuscarAsync();

        if (!resultado.HayUpdate)
        {
            // AccionUxActual ya es Ninguno; no hay nada que hacer.
            return;
        }

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
}
