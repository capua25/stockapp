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

    public ConfiguradorViewModel(
        IProbadorConexion probador,
        string? rutaArchivoOverride = null,
        string? rutaAppsettingsOverride = null)
    {
        _probador = probador;
        _rutaArchivo = rutaArchivoOverride ?? RutaConexion.ObtenerRutaArchivo();

        // Bug 2026-09-18: acá antes se leía SOLO conexion.json (ConexionConfigStore.Leer) y se
        // caía directo al default si faltaba, salteándose el appsettings.json del directorio
        // de instalación — la misma precedencia de tres niveles que usa StockApp.Presentation
        // (App.axaml.cs). En una instalación fresca sin conexion.json, el Configurador mostraba
        // localhost:5043 mientras la app real ya usaba la URL de appsettings.json (que viaja en
        // el mismo PublishDir, ver build/pack-win.ps1 y build/pack-testers-win.sh). Ahora
        // ResolucionConexion (StockApp.Configuracion) es el ÚNICO lugar que resuelve esa
        // cadena — este VM no la reimplementa.
        var urlInicial = ResolucionConexion.ResolverUrlInicial(rutaAppsettingsOverride, _rutaArchivo);

        // Uri.TryCreate en vez de asumir que lo guardado es válido: un archivo tocado a mano
        // no debe tirar la ventana abajo, solo cae al default (misma filosofía best-effort
        // que ConexionConfigStore.Leer).
        if (!Uri.TryCreate(urlInicial, UriKind.Absolute, out var uri))
        {
            uri = new Uri(ConexionDefaults.UrlPorDefecto);
        }

        _ip = uri.Host;
        // IsDefaultPort: true tanto si la URL no traía puerto como si traía el default explícito
        // (:80 en http, :443 en https). En ambos casos el campo queda vacío — round-trip: al
        // guardar de nuevo, ConstruirUrl() omite el puerto y el string sale igual al que se
        // cargó cuando no traía puerto explícito.
        _puerto = uri.IsDefaultPort ? string.Empty : uri.Port.ToString();
        _usarHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ruta completa del archivo que va a escribir "Guardar". Se muestra en la ventana.</summary>
    public string RutaArchivo => _rutaArchivo;

    [ObservableProperty]
    private string _ip;

    [ObservableProperty]
    private string _puerto;

    /// <summary>Esquema de la URL a armar. Puerto es opcional; esquema no lo es (default http).</summary>
    [ObservableProperty]
    private bool _usarHttps;

    [ObservableProperty]
    private string _mensajeEstado = string.Empty;

    /// <summary>Nombre de Classes para Border.badge en el XAML: "exito" | "advertencia" | "peligro" | "".</summary>
    [ObservableProperty]
    private string _claseEstado = string.Empty;

    [ObservableProperty]
    private bool _probando;

    /// <summary>
    /// Arma la URL a partir de esquema + host + puerto opcional, y la valida con
    /// Uri.TryCreate antes de devolverla: nunca deja salir un "host:" con dos puntos colgando
    /// (el bug de origen, con Puerto vacío) ni ningún otro string que Uri rechace.
    /// </summary>
    private bool TryConstruirUrl(out string url)
    {
        url = string.Empty;

        var esquema = UsarHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var host = Ip.Trim();
        var puertoTexto = Puerto.Trim();

        // Host vacío no tiene un guard explícito propio: "esquema://:puerto" (o "esquema://")
        // ya es una URI sin autoridad válida y Uri.TryCreate de abajo la rechaza sola —
        // verificado por mutación, agregar el if acá no mata ningún test.
        var candidata = puertoTexto.Length == 0
            ? $"{esquema}://{host}"
            : $"{esquema}://{host}:{puertoTexto}";

        if (!Uri.TryCreate(candidata, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = candidata;
        return true;
    }

    private const string MensajeUrlInvalida =
        "La dirección ingresada no es válida. Verificá el servidor y el puerto.";

    [RelayCommand]
    private async Task ProbarConexionAsync()
    {
        Probando = true;
        MensajeEstado = "Probando…";
        ClaseEstado = string.Empty;

        try
        {
            if (!TryConstruirUrl(out var url))
            {
                MensajeEstado = MensajeUrlInvalida;
                ClaseEstado = "peligro";
                return;
            }

            var resultado = await _probador.ProbarAsync(url);

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
        if (!TryConstruirUrl(out var url))
        {
            MensajeEstado = MensajeUrlInvalida;
            ClaseEstado = "peligro";
            return;
        }

        ConexionConfigStore.Guardar(url, _rutaArchivo);
        MensajeEstado = $"Guardado en {_rutaArchivo}";
        ClaseEstado = "exito";
    }

    [RelayCommand]
    private void Cancelar() => SolicitarCierre?.Invoke(this, EventArgs.Empty);

    /// <summary>La View se suscribe para cerrar la ventana (Cancelar no escribe nada).</summary>
    public event EventHandler? SolicitarCierre;
}
