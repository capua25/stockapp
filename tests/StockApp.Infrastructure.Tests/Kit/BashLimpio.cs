using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Entorno limpio y compartido para los tests que corren bash del kit (ruling F2 y R20 de la
/// migración automatizada). Cada clase de tests de scripts tenía su propia copia de este bloque,
/// y ninguna limpiaba STOCKAPP_ENSAYO_* ni ORQ_*: un STOCKAPP_ENSAYO_FALLAR_EN o un ORQ_ENSAYO
/// heredado de quien corre la suite habría hecho fallar (o peor, pasar) un test al azar.
///
/// Lo que limpia: stdin (nunca se cuelga leyendo el teclado de quien corre la suite),
/// STOCKAPP_ENV_LOCAL, REPO_ROOT y toda variable que empiece con VPS_, ENSAYO_, STOCKAPP_ENSAYO_
/// u ORQ_. Opcionalmente fija HOME en un directorio temporal del test.
/// </summary>
internal static class BashLimpio
{
    public static string RaizRepo([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    public static string Orquestacion => Path.Combine(RaizRepo(), "deploy", "lib", "orquestacion.sh");

    public static (int ExitCode, string Stdout, string Stderr) Ejecutar(string script, string? home = null) =>
        EjecutorBash.Ejecutar(
            "exec </dev/null\n" +
            "unset STOCKAPP_ENV_LOCAL REPO_ROOT\n" +
            "for v in $(compgen -v VPS_) $(compgen -v ENSAYO_) $(compgen -v STOCKAPP_ENSAYO_) $(compgen -v ORQ_); do unset \"$v\"; done\n" +
            (home is null ? "" : $"export HOME='{home}'\n") +
            script);

    public static void EscribirEjecutable(string ruta, string contenido)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
