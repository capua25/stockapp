using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes estáticos (leen el TEXTO, comentarios incluidos) de los scripts de la migración de
/// datos al VPS nuevo. Custodian dos cosas que un test de ejecución no ve:
/// - ningún host, IP ni dominio cableado: los parámetros salen del .env del operador, y un host
///   horneado en un script haría que el corte apunte al VPS equivocado;
/// - ningún comando de alcance global: el VPS viejo es COMPARTIDO con otra aplicación.
/// </summary>
public class MigracionVpsSincroniaTests
{
    private static string RaizRepo([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    /// <summary>Scripts nuevos de la migración (rutas relativas a la raíz del repo).</summary>
    public static readonly string[] ScriptsNuevos =
    [
        "deploy/restaurar-backup.sh",
    ];

    public static TheoryData<string> CasosScriptsNuevos()
    {
        var datos = new TheoryData<string>();
        foreach (var s in ScriptsNuevos) datos.Add(s);
        return datos;
    }

    private static string Texto(string relativo) => File.ReadAllText(Path.Combine(RaizRepo(), relativo));

    private static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
    private static readonly Regex Dominio = new(@"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(dev|com|net|org|uy|ar|io|app)\b");

    [Theory]
    [MemberData(nameof(CasosScriptsNuevos))]
    public void ScriptsNuevos_SinHostsNiIpsCableados(string script)
    {
        var texto = Texto(script);

        var ips = Ipv4.Matches(texto).Select(m => m.Value)
            .Where(ip => ip is not "127.0.0.1" and not "0.0.0.0").ToList();
        Assert.True(ips.Count == 0, $"{script} tiene IPs cableadas: {string.Join(", ", ips)}");

        var dominios = Dominio.Matches(texto).Select(m => m.Value).ToList();
        Assert.True(dominios.Count == 0, $"{script} tiene dominios cableados: {string.Join(", ", dominios)}");
    }

    [Theory]
    [MemberData(nameof(CasosScriptsNuevos))]
    public void ScriptsNuevos_SinComandosGlobalesProhibidos(string script)
    {
        var texto = Texto(script);

        // La lista de DeployVpsTests.ElScript_NuncaContieneUnComandoDeAlcanceGlobalProhibido...
        Assert.DoesNotContain("docker system prune", texto);
        Assert.DoesNotContain("image prune", texto);
        Assert.DoesNotContain("volume prune", texto);
        Assert.DoesNotContain("container prune", texto);
        Assert.DoesNotContain("systemctl restart docker", texto);
        Assert.DoesNotContain("systemctl stop docker", texto);
        Assert.DoesNotContain("ufw", texto);
        Assert.DoesNotContain("apt-get upgrade", texto);
        Assert.DoesNotContain("dist-upgrade", texto);
        Assert.DoesNotContain("autoremove", texto);
        Assert.DoesNotContain("reboot", texto);
        Assert.DoesNotContain("shutdown", texto);
        Assert.DoesNotMatch(@"docker\s+stop\s+\$\(", texto);
        Assert.DoesNotContain("pkill", texto);
        Assert.DoesNotMatch(@"docker compose down(?!.*(-f|-p))", texto);
        // ...más lo propio de la migración: ni el proxy ni los certificados de la otra app, y
        // ningún borrado recursivo sobre una ruta que sale de una variable.
        Assert.DoesNotContain("nginx", texto);
        Assert.DoesNotContain("certbot", texto);
        Assert.DoesNotContain("rm -rf \"$", texto);
    }
}
