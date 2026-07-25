using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Base común de FilaGastoEditableVm/FilaIngresoEditableVm/FilaLineaPoaEditableVm (F5d Entrega 2
/// Task 11): mecanismo de error de servidor, PARALELO a la validación por DataAnnotations de
/// ObservableValidator (ver decisión de diseño en el plan — ObservableValidator no expone API
/// pública para inyectar errores externos al pipeline de ValidationAttribute).
/// </summary>
public abstract partial class FilaImportacionEditableVmBase : ObservableValidator
{
    [ObservableProperty] private bool _tieneErrorServidor;
    [ObservableProperty] private string? _mensajeErrorServidor;

    private readonly List<string> _mensajesServidor = new();

    public void AgregarErrorServidor(string mensaje)
    {
        _mensajesServidor.Add(mensaje);
        MensajeErrorServidor = string.Join(" | ", _mensajesServidor);
        TieneErrorServidor = true;
    }

    public void LimpiarErrorServidor()
    {
        _mensajesServidor.Clear();
        MensajeErrorServidor = null;
        TieneErrorServidor = false;
    }
}
