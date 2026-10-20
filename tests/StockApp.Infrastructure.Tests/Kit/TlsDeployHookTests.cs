using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/tls/stockapp-tls-deploy-hook.sh, el hook que certbot llama al renovar el
/// certificado de StockApp. Corre COMO ROOT en un contenedor ubuntu descartable (mismo patrón que
/// InstallEsquemaTests): el usuario 'stockapp' existe de verdad, así que el dueño y el modo de los
/// PEM copiados son los reales. 'systemctl' se stubea (no hay systemd en un contenedor) y deja
/// rastro de cada invocación en /tmp/rastro-systemctl.
/// </summary>
public class TlsDeployHookTests
{
    private static string RaizRepo([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    private static string RutaHook => Path.Combine(RaizRepo(), "deploy", "tls", "stockapp-tls-deploy-hook.sh");

    private static string VersionUbuntuDelKit() =>
        File.ReadAllText(Path.Combine(RaizRepo(), "deploy", "kit", "VERSION")).Trim();

    private const string SystemctlStub = """
        #!/usr/bin/env bash
        { printf '%s' "$1"; shift; for a in "$@"; do printf ' %s' "$a"; done; printf '\n'; } >> /tmp/rastro-systemctl
        exit 0
        """;

    public enum Lineage { Valido, SinPrivkey, SinFullchain, Inexistente, VariableVacia, VariableSinDefinir }

    private sealed record Resultado(int ExitCode, string Salida, string Rastro, string DatosDestino, string Perfiles);

    private static Resultado Ejecutar(Lineage caso, bool conDestinoPrevio = false)
    {
        // Sin esta guarda, docker crearía un DIRECTORIO en la ruta del hook al montarlo.
        Assert.True(File.Exists(RutaHook), $"No existe {RutaHook}");
        var dirTrabajo = Path.Combine(Path.GetTempPath(), "tls-hook-" + Guid.NewGuid());
        Directory.CreateDirectory(dirTrabajo);
        try
        {
            var stub = Path.Combine(dirTrabajo, "systemctl");
            File.WriteAllText(stub, SystemctlStub);
            File.SetUnixFileMode(stub, (UnixFileMode)0b111_101_101);

            var exportar = caso switch
            {
                Lineage.VariableSinDefinir => "unset RENEWED_LINEAGE",
                Lineage.VariableVacia => "export RENEWED_LINEAGE=''",
                Lineage.Inexistente => "export RENEWED_LINEAGE=/etc/letsencrypt/live/no-existe.invalid",
                _ => "export RENEWED_LINEAGE=/etc/letsencrypt/live/ensayo.invalid",
            };
            var lineage = caso switch
            {
                Lineage.Valido => "printf 'CADENA-NUEVA' > \"$L/fullchain.pem\"; printf 'CLAVE-NUEVA' > \"$L/privkey.pem\"",
                Lineage.SinPrivkey => "printf 'CADENA-NUEVA' > \"$L/fullchain.pem\"",
                Lineage.SinFullchain => "printf 'CLAVE-NUEVA' > \"$L/privkey.pem\"",
                _ => ":",
            };
            var previo = conDestinoPrevio
                ? "install -d -o stockapp -g stockapp -m 0750 /var/lib/stockapp/tls; " +
                  "printf 'CADENA-VIEJA' > /var/lib/stockapp/tls/fullchain.pem; printf 'CLAVE-VIEJA' > /var/lib/stockapp/tls/privkey.pem; " +
                  "chown stockapp:stockapp /var/lib/stockapp/tls/*.pem; chmod 0600 /var/lib/stockapp/tls/*.pem"
                : ":";

            var entrypoint = $$"""
                #!/usr/bin/env bash
                set -u
                useradd --system --no-create-home stockapp
                mkdir -p /var/lib/stockapp
                L=/etc/letsencrypt/live/ensayo.invalid
                mkdir -p "$L"
                {{lineage}}
                {{previo}}
                export PATH="/testbin:$PATH"
                {{exportar}}
                bash /hook.sh > /tmp/salida 2>&1
                echo "EXIT=$?" >> /tmp/salida
                cat /tmp/salida
                echo "---RASTRO---"
                cat /tmp/rastro-systemctl 2>/dev/null || echo AUSENTE
                echo "---DESTINO---"
                for f in fullchain.pem privkey.pem; do
                  if [[ -e /var/lib/stockapp/tls/$f ]]; then echo "$f $(stat -c '%U:%G %a' /var/lib/stockapp/tls/$f) $(cat /var/lib/stockapp/tls/$f)"; else echo "$f AUSENTE"; fi
                done
                echo "---DIR---"
                stat -c '%U:%G %a' /var/lib/stockapp/tls 2>/dev/null || echo AUSENTE

                """;
            File.WriteAllText(Path.Combine(dirTrabajo, "run.sh"), entrypoint);
            var testbin = Path.Combine(dirTrabajo, "testbin");
            Directory.CreateDirectory(testbin);
            File.Move(stub, Path.Combine(testbin, "systemctl"));

            var cmd = $"docker run --rm -v \"{RutaHook}:/hook.sh:ro\" -v \"{dirTrabajo}/run.sh:/run.sh:ro\" " +
                      $"-v \"{testbin}:/testbin:ro\" \"ubuntu:{VersionUbuntuDelKit()}\" bash /run.sh";
            var (exitDocker, stdout, stderr) = EjecutorBash.Ejecutar(cmd);
            Assert.True(exitDocker == 0, $"'docker run' falló. stdout={stdout} stderr={stderr}");

            var m = Regex.Match(stdout, @"EXIT=(-?\d+)\s*$", RegexOptions.Multiline);
            Assert.True(m.Success, $"Sin marcador EXIT= en: {stdout}");
            return new Resultado(
                int.Parse(m.Groups[1].Value),
                stdout[..stdout.IndexOf("---RASTRO---", StringComparison.Ordinal)],
                Seccion(stdout, "---RASTRO---", "---DESTINO---"),
                Seccion(stdout, "---DESTINO---", "---DIR---"),
                Seccion(stdout, "---DIR---", null));
        }
        finally
        {
            Directory.Delete(dirTrabajo, recursive: true);
        }
    }

    private static string Seccion(string salida, string encabezado, string? siguiente)
    {
        var inicio = salida.IndexOf(encabezado, StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"Falta la sección '{encabezado}' en: {salida}");
        inicio += encabezado.Length;
        var fin = siguiente is null ? salida.Length : salida.IndexOf(siguiente, inicio, StringComparison.Ordinal);
        Assert.True(fin >= 0, $"Falta la sección '{siguiente}' en: {salida}");
        return salida[inicio..fin].Trim('\n', '\r');
    }

    [Fact]
    public void LineageValido_CopiaLosPemComo0600DeStockappYReinicia()
    {
        var r = Ejecutar(Lineage.Valido);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("fullchain.pem stockapp:stockapp 600 CADENA-NUEVA", r.DatosDestino);
        Assert.Contains("privkey.pem stockapp:stockapp 600 CLAVE-NUEVA", r.DatosDestino);
        Assert.Equal("stockapp:stockapp 750", r.Perfiles);
        Assert.Equal("restart stockapp-api", r.Rastro);
    }

    [Fact]
    public void LineageValido_ConDestinoPrevio_LoReemplazaYReinicia()
    {
        var r = Ejecutar(Lineage.Valido, conDestinoPrevio: true);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("fullchain.pem stockapp:stockapp 600 CADENA-NUEVA", r.DatosDestino);
        Assert.Contains("privkey.pem stockapp:stockapp 600 CLAVE-NUEVA", r.DatosDestino);
        Assert.Equal("restart stockapp-api", r.Rastro);
    }

    [Theory]
    [InlineData(Lineage.VariableSinDefinir)]
    [InlineData(Lineage.VariableVacia)]
    [InlineData(Lineage.Inexistente)]
    [InlineData(Lineage.SinPrivkey)]
    [InlineData(Lineage.SinFullchain)]
    public void LineageInvalido_Aborta2SinCopiarNiReiniciar(Lineage caso)
    {
        var r = Ejecutar(caso, conDestinoPrevio: true);

        Assert.Equal(2, r.ExitCode);
        Assert.Equal("AUSENTE", r.Rastro);
        Assert.Contains("fullchain.pem stockapp:stockapp 600 CADENA-VIEJA", r.DatosDestino);
        Assert.Contains("privkey.pem stockapp:stockapp 600 CLAVE-VIEJA", r.DatosDestino);
    }

    [Fact]
    public void SinLineage_NoCreaNiElDirectorioDeDestino()
    {
        var r = Ejecutar(Lineage.VariableSinDefinir);

        Assert.Equal(2, r.ExitCode);
        Assert.Equal("AUSENTE", r.Perfiles);
        Assert.Contains("RENEWED_LINEAGE", r.Salida);
    }

    [Fact]
    public void ElHook_NoTieneDominioNiIpCableados_ReiniciaSoloStockappApi()
    {
        var texto = File.ReadAllText(RutaHook);

        Assert.DoesNotMatch(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", texto);
        Assert.DoesNotMatch(@"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(dev|com|net|org|uy|ar|io|app)\b", texto);
        Assert.DoesNotContain("/live/", texto.Replace("/etc/letsencrypt/live/<dominio>", ""));

        var reinicios = Regex.Matches(texto, @"^\s*(?:sudo\s+)?systemctl\s+(\S+)\s+(\S+)", RegexOptions.Multiline)
            .Select(x => $"{x.Groups[1].Value} {x.Groups[2].Value}").ToList();
        Assert.Equal(["restart stockapp-api"], reinicios);
    }

    [Fact]
    public void ElEncabezado_DocumentaCertonlyDeployHookYQueDryRunNoLoEjecuta()
    {
        var texto = File.ReadAllText(RutaHook);

        Assert.Contains("certonly --standalone -d <dominio> --deploy-hook /usr/local/sbin/stockapp-tls-deploy-hook.sh", texto);
        Assert.Contains("--dry-run", texto);
    }
}
