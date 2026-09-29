using System.IO;
using System.Runtime.CompilerServices;

namespace StockApp.Tests.Compartido;

/// <summary>
/// Raíz del repo (donde vive StockApp.sln), resuelta subiendo desde la ubicación de este archivo
/// fuente. Compartido vía Link (ver csproj de UiTests/VisualTests): no depende del directorio de
/// trabajo del test runner, que varía entre "dotnet test" local y CI.
/// </summary>
internal static class RaizRepo
{
    public static string Ruta { get; } = Buscar();

    private static string Buscar([CallerFilePath] string archivo = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(archivo)!); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StockApp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new System.InvalidOperationException("No se encontró StockApp.sln.");
    }
}
