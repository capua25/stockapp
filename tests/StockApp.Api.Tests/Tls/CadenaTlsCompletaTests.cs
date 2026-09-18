using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StockApp.Api.Tests.Tls;

/// <summary>
/// CANARIO, no guardián de un bug a corregir -- léase con cuidado antes de "arreglarlo".
///
/// QUÉ COMPORTAMIENTO EXTERNO DOCUMENTA: Kestrel, configurado vía
/// "Kestrel:Certificates:Default:Path"/"KeyPath" (la MISMA config que usa
/// stockapp-api.service en el VPS a través de /etc/stockapp-api/api.env), sirve HOY 3 de
/// los 4 certificados de un fullchain.pem de 4 niveles, cuando una CA intermedia de esa
/// cadena TAMBIÉN existe, por separado, como raíz autofirmada de confianza local del
/// servidor. Caso real en el VPS: "ISRG Root X2" firma en cruz a "Root YE" (así arma la
/// cadena hacia ISRG Root X1) Y a la vez es, por sí sola, raíz confiable del sistema
/// (/etc/ssl/certs/ISRG_Root_X2.pem). El chain-building de .NET/OpenSSL encuentra ahí un
/// camino más corto hacia una raíz ya confiable y deja afuera el certificado cross-signed
/// -- sin que el archivo esté mal armado.
///
/// POR QUÉ ESTÁ AFIRMADO "AL REVÉS" (el comportamiento MALO, no el deseado): la
/// investigación completa (engram "stockapp/tls-vps-cadena-truncada", 2026-09-18)
/// concluyó que esto NO es un bug a corregir del lado del servidor. La cadena de 3
/// certificados que Kestrel sirve hoy es VÁLIDA -- Windows la valida y el candado queda
/// OK, confirmado por el usuario abriendo la URL en el navegador real. Lo único que pasa
/// es que la primera vez, Windows necesita un fetch AIA de red (a veces varios segundos)
/// para completar la cadena hasta una raíz que YA tiene cacheada -- no es un problema de
/// validez, es de LATENCIA en el primer contacto. El arreglo correcto para una latencia es
/// un timeout que la tolere, y eso se hizo aparte, en tools/StockApp.Configurador/
/// Servicios/ProbadorConexion.cs (4s -> 12s + mensaje de "probá de nuevo"). Forzar acá que
/// Kestrel mande los 4 certs bajaría por debajo de la API pública de Kestrel (armar el
/// mensaje TLS Certificate a mano) -- invasivo, no soportado, y solo ahorraría el primer
/// contacto. No vale la pena. El servidor queda como está.
///
/// QUÉ SIGNIFICA SI ESTE TEST SE PONE ROJO: que el comportamiento de Kestrel CAMBIÓ (una
/// nueva versión de .NET empezó a mandar los 4 certs, o dejó de mandar los 3). Eso es la
/// señal para volver a mirar si el timeout largo del Configurador todavía hace falta --
/// NO para "arreglar" este test volviendo a poner el Assert al revés sin investigar.
///
/// MEDIDO EN ESTA MÁQUINA (Linux, .NET 10.0.8), no supuesto de un issue de GitHub: se
/// probaron 4 formas de configurar el certificado en Kestrel bajo la MISMA condición
/// (CA intermedia también confiable localmente) y las 4 truncan igual:
///   A) Kestrel:Certificates:Default:Path/KeyPath (la que usa el VPS hoy) -> 3/4
///   B) Kestrel:Endpoints:Https:Certificate:Path/KeyPath                 -> 3/4 (=A)
///   C) .pfx con la cadena completa vía Certificates:Default:Path        -> 1/4 (peor)
///   D) HttpsConnectionAdapterOptions.ServerCertificateChain seteado a mano en código,
///      bypaseando la config -> 3/4 (=A, SslStreamCertificateContext poda igual)
/// dotnet/aspnetcore#52511 y #60709 (https://github.com/dotnet/aspnetcore/issues/52511,
/// https://github.com/dotnet/aspnetcore/issues/60709) describen a B como el fix de A --
/// pero ese comportamiento es WINDOWS-ESPECÍFICO (vía SslStreamCertificateContext.Create
/// agregando intermedios al Windows Certificate Store). En Linux, con OpenSSL detrás, A y
/// B son EQUIVALENTES -- medido acá, no en el issue. No confiar en ese issue para este
/// servidor sin volver a medir.
///
/// LA CONDICIÓN QUE HAY QUE REPRODUCIR (sin esto, "no se puede reproducir" es la
/// conclusión equivocada, no que el bug no exista): una cadena de 3-4 niveles PLANA, sin
/// ningún nivel confiable localmente, NO alcanza -- se probó primero así y Kestrel sirvió
/// los certificados completos, sin truncar nada. Hace falta que una de las CA intermedias
/// exista ADEMÁS, por separado, como raíz autofirmada instalada como confiable en el
/// servidor. Acá se instala esa condición SIN tocar el trust store del sistema (no hace
/// falta sudo, portable en CI) usando X509Store(StoreName.Root, StoreLocation.CurrentUser)
/// -- un store por-usuario que en Linux vive en
/// ~/.dotnet/corefx/cryptography/x509stores/root/, separado de /etc/ssl/certs -- y se
/// retira en Dispose.
/// </summary>
public sealed class CadenaTlsCompletaTests : IDisposable
{
    private X509Certificate2? _midRootInstaladoEnStoreDeUsuario;

    public void Dispose()
    {
        if (_midRootInstaladoEnStoreDeUsuario is null) return;

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Remove(_midRootInstaladoEnStoreDeUsuario);
        store.Close();
    }

    [Fact]
    public async Task Kestrel_Certificates_Default_trunca_la_cadena_si_una_CA_intermedia_tambien_es_raiz_local()
    {
        var cadena = ConstruirCadenaConAtajoDeConfianzaLocal();

        // Reproduce la condición real del VPS (la CA intermedia YA es raíz confiable del
        // sistema) sin tocar el trust store del sistema operativo: alcanza con el store
        // por-usuario, que no requiere sudo.
        using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadWrite);
            store.Add(cadena.MidRootSelfSigned);
            store.Close();
        }
        _midRootInstaladoEnStoreDeUsuario = cadena.MidRootSelfSigned;

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Certificates:Default:Path"] = cadena.FullchainPath,
            ["Kestrel:Certificates:Default:KeyPath"] = cadena.KeyPath,
        });
        builder.WebHost.UseUrls("https://127.0.0.1:0");

        await using var app = builder.Build();
        await app.StartAsync();

        var direccion = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var puerto = int.Parse(direccion.Split(':')[^1]);

        var certificadosRecibidos = await ContarCertificadosServidosPorTls(puerto);

        await app.StopAsync();

        // Afirmación AL REVÉS a propósito (ver el comentario de la clase): esto NO dice
        // "así debería ser", dice "así es HOY, y es aceptado". certsEnArchivo - 1 porque
        // el comportamiento medido es "trunca exactamente el último certificado de la
        // cadena" (el cross-signed hacia la raíz que ya es confiable localmente) -- no
        // "trunca algo, quién sabe cuánto". Si algún día sirve los 4, este Assert falla
        // solo, y ESO es la señal (ver comentario de clase), no un motivo para tocar el
        // número sin investigar por qué cambió.
        Assert.Equal(cadena.CertsEnArchivo - 1, certificadosRecibidos);
    }

    private static async Task<int> ContarCertificadosServidosPorTls(int puerto)
    {
        var certificadosVistos = -1;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, puerto);
        using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, _, chain, _) =>
            {
                // OJO -- esto casi produce un guardián verde por la razón equivocada, igual
                // que el "bloque 0": chain.ChainPolicy.ExtraStore YA incluye el leaf (no solo
                // los intermedios, pase lo que diga cualquier ejemplo por ahí). Verificado
                // empíricamente contra `openssl s_client -showcerts` corriendo al mismo
                // tiempo contra el mismo host: cuando el server mandó 3 certs por el cable
                // (leaf+2), ExtraStore.Count daba 3, NO 2. Sumarle +1 acá duplicaba el leaf y
                // el test pasaba aunque el server estuviera truncando la cadena. NO sumar +1.
                //
                // Tampoco sirve chain.ChainElements: para cuando corre este callback, .NET ya
                // CORRIÓ chain.Build() usando el trust store local del cliente -- en este
                // mismo proceso/usuario, que tiene la CA intermedia instalada a propósito para
                // reproducir el bug -- así que ChainElements queda "completo" aunque el server
                // haya mandado menos. Eso también daría un falso verde.
                certificadosVistos = chain?.ChainPolicy.ExtraStore.Count ?? 0;
                return true; // no interesa la validación acá, solo contar lo recibido
            });

        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        });

        return certificadosVistos;
    }

    private sealed record CadenaDePrueba(
        string FullchainPath,
        string KeyPath,
        int CertsEnArchivo,
        X509Certificate2 MidRootSelfSigned);

    /// <summary>
    /// Arma, con APIs puras de .NET (sin depender de openssl externo -- portable en
    /// cualquier runner de CI), una cadena de 4 niveles análoga a la real del VPS:
    ///   leaf (localhost) → Int2 (análogo YE2) → IntCA (análogo Root YE)
    ///     → MidRoot cross-signed por TopRoot (análogo ISRG Root X2 cross-signed por X1)
    /// donde MidRoot EXISTE ADEMÁS en forma self-signed (análogo a que ISRG Root X2 sea,
    /// por sí sola, una raíz confiable del sistema).
    /// </summary>
    private static CadenaDePrueba ConstruirCadenaConAtajoDeConfianzaLocal()
    {
        using var topRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // CreateSelfSigned ya devuelve el certificado CON la clave privada asociada -- a
        // diferencia de req.Create(issuer, ...) mas abajo, que NO la asocia. Llamar
        // CopyWithPrivateKey acá de nuevo tira "The certificate already has an associated
        // private key.".
        var topRootConClave = AutoFirmada("Repro Top Root (analogo ISRG Root X1)", topRootKey, pathLen: 2);

        using var midRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var midRootSelfSigned = AutoFirmada("Repro Mid Root (analogo ISRG Root X2)", midRootKey, pathLen: 1);
        var midRootCrossSigned = FirmadaPorCa(
            "Repro Mid Root (analogo ISRG Root X2)", midRootKey, topRootConClave, pathLen: 1);

        using var intCaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var intCaPub = FirmadaPorCa("Repro IntCA (analogo Root YE)", intCaKey, midRootSelfSigned, pathLen: 0);
        var intCaConClave = intCaPub.CopyWithPrivateKey(intCaKey);

        using var int2Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var int2Pub = FirmadaPorCa("Repro Int2 (analogo YE2)", int2Key, intCaConClave, pathLen: 0);
        var int2ConClave = int2Pub.CopyWithPrivateKey(int2Key);

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafPub = FirmadaHoja("localhost", leafKey, int2ConClave);
        var leafConClave = leafPub.CopyWithPrivateKey(leafKey);

        // Orden real de un fullchain.pem: leaf primero, root NUNCA se incluye -- igual
        // forma que el fullchain.pem del VPS (leaf, YE2, RootYE, X2-cross-signed).
        var certsEnOrden = new[] { leafPub, int2Pub, intCaPub, midRootCrossSigned };

        var dir = Directory.CreateTempSubdirectory("stockapp-tls-guardia-");
        var fullchainPath = Path.Combine(dir.FullName, "fullchain.pem");
        File.WriteAllText(fullchainPath,
            string.Concat(certsEnOrden.Select(c => c.ExportCertificatePem() + "\n")));

        var keyPath = Path.Combine(dir.FullName, "privkey.pem");
        File.WriteAllText(keyPath, leafConClave.GetECDsaPrivateKey()!.ExportECPrivateKeyPem() + "\n");

        return new CadenaDePrueba(fullchainPath, keyPath, certsEnOrden.Length, midRootSelfSigned);
    }

    private static X509Certificate2 AutoFirmada(string cn, ECDsa key, int pathLen)
    {
        var req = new CertificateRequest($"CN={cn}", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLen, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    private static X509Certificate2 FirmadaPorCa(string cn, ECDsa key, X509Certificate2 caConClavePrivada, int pathLen)
    {
        var req = new CertificateRequest($"CN={cn}", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLen, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        return req.Create(caConClavePrivada, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5),
            RandomNumberGenerator.GetBytes(8));
    }

    private static X509Certificate2 FirmadaHoja(string cn, ECDsa key, X509Certificate2 caConClavePrivada)
    {
        var req = new CertificateRequest($"CN={cn}", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true)); // serverAuth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(cn);
        req.CertificateExtensions.Add(sanBuilder.Build());

        return req.Create(caConClavePrivada, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1),
            RandomNumberGenerator.GetBytes(8));
    }
}
