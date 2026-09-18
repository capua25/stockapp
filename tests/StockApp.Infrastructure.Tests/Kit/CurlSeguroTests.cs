using System.Runtime.CompilerServices;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardián estático cross-script: ningún 'curl' de los scripts que terminan corriendo contra
/// un servidor real (deploy/deploy-vps.sh, contra el VPS compartido con 'pinar';
/// deploy/install.sh y deploy/kit/*.sh, los que corren en la máquina del municipio) puede
/// llevar '-k'/'--insecure' -- eso anularía el sentido de haber puesto TLS.
///
/// Motivo (runbook HTTPS del VPS, 2026-09-18): un review de seguridad reportó un
/// 'curl -fsSk' en la línea de /licencia/estado de deploy-vps.sh -- justo el healthcheck que
/// decide si el deploy fue exitoso. Un '-k' ahí da verde aunque el certificado esté mal
/// emitido, vencido o sea de otro dominio: es el fallo exacto que TLS tenía que detectar. La
/// 'k' pegada al final de un cluster de flags cortas ('-fsSk') se escapa de un chequeo que
/// solo busca un '-k' suelto -- por eso el patrón exige que el cluster de flags TERMINE en
/// 'k', no solo que contenga un '-k' aislado.
///
/// Un solo guardián cross-script, no uno por archivo: los 4 scripts comparten el mismo riesgo
/// (llamadas a curl contra la API real) y el mismo criterio de "nunca -k" (deploy-vps.sh e
/// install.sh ya lo documentan en prosa, ver deploy/install.sh:531-545).
/// </summary>
public class CurlSeguroTests
{
    private static string RutaDeploy([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy"));
    }

    public static IEnumerable<object[]> ScriptsAVerificar()
    {
        var deploy = RutaDeploy();
        var rutas = new List<string>
        {
            Path.Combine(deploy, "deploy-vps.sh"),
            Path.Combine(deploy, "install.sh"),
        };
        rutas.AddRange(Directory.GetFiles(Path.Combine(deploy, "kit"), "*.sh", SearchOption.AllDirectories));

        foreach (var ruta in rutas.OrderBy(r => r, StringComparer.Ordinal))
        {
            yield return new object[] { ruta };
        }
    }

    [Theory]
    [MemberData(nameof(ScriptsAVerificar))]
    public void Script_NingunCurlUsaMenosNiInsecure(string rutaScript)
    {
        Assert.True(File.Exists(rutaScript), $"No existe el script a verificar: {rutaScript}");

        // Solo código real, no comentarios -- varios de estos scripts EXPLICAN en prosa por
        // qué no usan '-k'/'--insecure', y esa explicación menciona las dos flags a propósito.
        var codigo = string.Join('\n', File.ReadAllLines(rutaScript)
            .Where(linea => !linea.TrimStart().StartsWith('#')));

        Assert.DoesNotContain("--insecure", codigo);
        // curl permite combinar flags cortas en un solo cluster (ej. '-fsSk') -- un '-k'
        // pegado al final de '-fsS' es tan inseguro como uno suelto.
        Assert.DoesNotMatch(@"(?m)(?:^|\s)-[A-Za-z]*k(?:\s|$)", codigo);
    }
}
