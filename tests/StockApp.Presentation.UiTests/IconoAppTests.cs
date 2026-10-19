using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardianes del ícono de la app (2026-10-19, variante 1d-i: edificio con cúpula y bandera sobre
/// cuadrado azul). Fuente única: assets/marca/icono-app.ico. Cada test custodia UN punto de
/// cableado distinto; si alguien lo desconecta, el ícono vuelve a ser el de Avalonia / el genérico
/// de Windows sin que ningún otro test lo note (compila y corre igual).
/// </summary>
public class IconoAppTests
{
    private static readonly int[] TamanosEsperados = { 16, 24, 32, 48, 64, 128, 256 };
    private const string RutaIcoRelativa = @"assets\marca\icono-app.ico";

    private static string Repo(params string[] partes) =>
        Path.Combine(new[] { RaizRepo.Ruta }.Concat(partes).ToArray());

    [Fact]
    public void Ico_ExisteConLosSieteFramesEsperados()
    {
        var bytes = File.ReadAllBytes(Repo("assets", "marca", "icono-app.ico"));

        // Cabecera ICONDIR: reservado=0, tipo=1 (ico), cantidad de imágenes.
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var cantidad = BitConverter.ToUInt16(bytes, 4);

        var tamanos = new List<int>();
        for (var i = 0; i < cantidad; i++)
        {
            var ancho = bytes[6 + 16 * i];
            tamanos.Add(ancho == 0 ? 256 : ancho); // 0 codifica 256 en ICONDIRENTRY
        }

        Assert.Equal(TamanosEsperados, tamanos.OrderBy(t => t).ToArray());
    }

    [Theory]
    [InlineData("src", "StockApp.Presentation", "StockApp.Presentation.csproj")]
    [InlineData("tools", "StockApp.Configurador", "StockApp.Configurador.csproj")]
    public void Csproj_DeclaraApplicationIconApuntandoAlIcoDeMarca(params string[] ruta)
    {
        var csproj = File.ReadAllText(Repo(ruta));

        var m = Regex.Match(csproj, @"<ApplicationIcon>(.+?)</ApplicationIcon>");
        Assert.True(m.Success, $"{ruta.Last()} no declara <ApplicationIcon>: el .exe perdería su ícono.");

        var apuntaA = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Repo(ruta))!, m.Groups[1].Value.Replace('\\', '/')));
        Assert.Equal(Repo("assets", "marca", "icono-app.ico"), apuntaA);
    }

    [Theory]
    [InlineData("src", "StockApp.Presentation", "StockApp.Presentation.csproj")]
    [InlineData("tools", "StockApp.Configurador", "StockApp.Configurador.csproj")]
    public void Csproj_EmpaquetaElIcoComoRecursoAvaloniaParaWindowIcon(params string[] ruta)
    {
        var csproj = File.ReadAllText(Repo(ruta));

        Assert.Matches(@"<AvaloniaResource Include=""[^""]*assets\\marca\\icono-app\.ico"" Link=""Assets\\icono-app\.ico""", csproj);
    }

    [Fact]
    public void PackWin_PasaIconAVpkPack_YConservaElBomUtf8()
    {
        var bytes = File.ReadAllBytes(Repo("build", "pack-win.ps1"));
        var texto = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains(RutaIcoRelativa, texto);
        Assert.Matches(@"--icon\s+\$IconPath", texto);

        // Windows PowerShell 5.1 lee un .ps1 sin BOM como ANSI y rompe "Gestión".
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void TodasLasVentanasDeLaAppDeclaranIcon()
    {
        var sinIcon = Directory.GetFiles(Repo("src", "StockApp.Presentation", "Views"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Repo("tools", "StockApp.Configurador", "Views"), "*.axaml", SearchOption.AllDirectories))
            .Where(f => File.ReadAllText(f).TrimStart('﻿').TrimStart().StartsWith("<Window", StringComparison.Ordinal))
            .Where(f => !Regex.IsMatch(File.ReadAllText(f), @"^\s+Icon=""/Assets/icono-app\.ico""", RegexOptions.Multiline))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(sinIcon.Count == 0, "Ventanas sin Icon=\"/Assets/icono-app.ico\": " + string.Join(", ", sinIcon));
    }

    [AvaloniaFact]
    public void MainWindowReal_TieneWindowIconResuelto()
    {
        var window = new MainWindow();

        Assert.NotNull(window.Icon);
    }
}
