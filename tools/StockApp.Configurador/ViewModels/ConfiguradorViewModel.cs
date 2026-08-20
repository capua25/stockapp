using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Configuracion;
using StockApp.Configurador.Servicios;

namespace StockApp.Configurador.ViewModels;

/// <summary>
/// Sin contenedor DI (Program.cs la instancia a mano, dos dependencias). RutaArchivo y el
/// override de constructor existen para que la ventana pueda mostrar en pantalla la ruta
/// real del archivo (requisito explícito del usuario, guía a un colega por teléfono) y para
/// poder testear todo sin tocar el %AppData% real de la máquina que corre los tests.
/// </summary>
public partial class ConfiguradorViewModel : ObservableObject
{
    private readonly IProbadorConexion _probador;
    private readonly string _rutaArchivo;

    public ConfiguradorViewModel(IProbadorConexion probador, string? rutaArchivoOverride = null)
    {
        _probador = probador;
        _rutaArchivo = rutaArchivoOverride ?? RutaConexion.ObtenerRutaArchivo();

        var guardado = ConexionConfigStore.Leer(_rutaArchivo);
        var urlInicial = guardado ?? ConexionDefaults.UrlPorDefecto;

        // Uri.TryCreate en vez de asumir que lo guardado es válido: un archivo tocado a mano
        // no debe tirar la ventana abajo, solo cae al default (misma filosofía best-effort
        // que ConexionConfigStore.Leer).
        if (!Uri.TryCreate(urlInicial, UriKind.Absolute, out var uri))
        {
            uri = new Uri(ConexionDefaults.UrlPorDefecto);
        }

        _ip = uri.Host;
        _puerto = uri.Port.ToString();
    }

    /// <summary>Ruta completa del archivo que va a escribir "Guardar". Se muestra en la ventana.</summary>
    public string RutaArchivo => _rutaArchivo;

    [ObservableProperty]
    private string _ip;

    [ObservableProperty]
    private string _puerto;

    [ObservableProperty]
    private string _mensajeEstado = string.Empty;

    /// <summary>Nombre de Classes para Border.badge en el XAML: "exito" | "advertencia" | "peligro" | "".</summary>
    [ObservableProperty]
    private string _claseEstado = string.Empty;

    [ObservableProperty]
    private bool _probando;

    private string ConstruirUrl() => $"http://{Ip.Trim()}:{Puerto.Trim()}";

    [RelayCommand]
    private async Task ProbarConexionAsync()
    {
        Probando = true;
        MensajeEstado = "Probando…";
        ClaseEstado = string.Empty;

        try
        {
            var resultado = await _probador.ProbarAsync(ConstruirUrl());

            (MensajeEstado, ClaseEstado) = resultado switch
            {
                ResultadoPruebaConexion.Ok =>
                    ("Conectado: es la API de Gestión Municipal.", "exito"),
                ResultadoPruebaConexion.RespondeOtraCosa =>
                    ("Algo respondió en esa dirección, pero no es la API de Gestión Municipal.", "advertencia"),
                ResultadoPruebaConexion.NoResponde =>
                    ("No se pudo conectar. Verificá la IP, el puerto y que el servidor esté encendido.", "peligro"),
                _ => ("Resultado desconocido.", "peligro"),
            };
        }
        finally
        {
            Probando = false;
        }
    }

    [RelayCommand]
    private void Guardar()
    {
        ConexionConfigStore.Guardar(ConstruirUrl(), _rutaArchivo);
        MensajeEstado = $"Guardado en {_rutaArchivo}";
        ClaseEstado = "exito";
    }

    [RelayCommand]
    private void Cancelar() => SolicitarCierre?.Invoke(this, EventArgs.Empty);

    /// <summary>La View se suscribe para cerrar la ventana (Cancelar no escribe nada).</summary>
    public event EventHandler? SolicitarCierre;
}
