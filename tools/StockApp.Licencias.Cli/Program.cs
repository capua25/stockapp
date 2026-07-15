using StockApp.Application.Licenciamiento;
using StockApp.Licencias.Cli;

// CLI interna del desarrollador. Reutiliza el MISMO firmador que valida Application, así el
// formato no puede divergir. La clave privada vive fuera del repo; nunca se commitea.
if (args.Length == 0)
{
    ImprimirAyuda();
    return 1;
}

try
{
    switch (args[0])
    {
        case "generar-claves":
        {
            var salida = LeerOpcion(args, "--salida") ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(salida);
            var (privadaPem, publicaBase64) = GeneradorClaves.Generar();

            var rutaPrivada = Path.Combine(salida, "clave-privada.pem");
            File.WriteAllText(rutaPrivada, privadaPem);

            Console.WriteLine($"Clave privada escrita en: {rutaPrivada}");
            Console.WriteLine("GUARDALA FUERA DEL REPO. No la compartas ni la commitees.");
            Console.WriteLine();
            Console.WriteLine("Clave pública (pegar en OpcionesLicencia.ClavePublicaBase64Default):");
            Console.WriteLine(publicaBase64);
            return 0;
        }

        case "emitir-licencia":
        {
            var privada = LeerClave(args);
            var cliente = LeerOpcionObligatoria(args, "--cliente");
            var maquina = LeerOpcionObligatoria(args, "--maquina");
            var payload = new LicenciaPayload(1, cliente, maquina, DateTime.UtcNow.ToString("yyyy-MM-dd"));
            Console.WriteLine(FirmadorLicencias.EmitirLicencia(payload, privada));
            return 0;
        }

        case "emitir-reset":
        {
            var privada = LeerClave(args);
            var maquina = LeerOpcionObligatoria(args, "--maquina");
            var desafio = LeerOpcionObligatoria(args, "--desafio");
            var payload = new TokenResetPayload(1, "reset-admin", maquina, desafio);
            Console.WriteLine(FirmadorLicencias.EmitirTokenReset(payload, privada));
            return 0;
        }

        default:
            ImprimirAyuda();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string LeerClave(string[] args)
{
    var ruta = LeerOpcionObligatoria(args, "--clave");
    if (!File.Exists(ruta))
        throw new FileNotFoundException($"No se encontró la clave privada en: {ruta}");
    return File.ReadAllText(ruta);
}

static string? LeerOpcion(string[] args, string nombre)
{
    var i = Array.IndexOf(args, nombre);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string LeerOpcionObligatoria(string[] args, string nombre)
    => LeerOpcion(args, nombre)
       ?? throw new ArgumentException($"Falta la opción obligatoria {nombre}.");

static void ImprimirAyuda()
{
    Console.WriteLine("StockApp.Licencias.Cli — herramienta interna (no distribuir).");
    Console.WriteLine();
    Console.WriteLine("  generar-claves  --salida <dir>");
    Console.WriteLine("  emitir-licencia --clave <clave-privada.pem> --cliente \"Ferretería X\" --maquina A3F2-9B41-...");
    Console.WriteLine("  emitir-reset    --clave <clave-privada.pem> --maquina A3F2-9B41-... --desafio <nonce>");
}
