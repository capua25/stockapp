# UI v2 — Fase 0 Fundaciones Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir las fundaciones de la UI v2: tokens como fuente única con generador, herramienta de comparación visual mockup ↔ app, componentes y estilos v2 que conviven con los viejos sin cambiar la app, y una pantalla catálogo-de-componentes comparada contra su mockup que calibra el umbral de aceptación.

**Architecture:** `docs/ui-v2/tokens.json` es la única fuente de valores; una herramienta de consola (`tools/StockApp.UiV2.Herramientas`) genera `Themes/V2/Tokens.axaml` y `mockups/_base/tokens.css`, extrae Inter local para los mockups, exporta íconos MDI a SVG, captura mockups con Playwright y compara PNG con SkiaSharp. Un proyecto de test nuevo (`tests/StockApp.Presentation.VisualTests`) renderiza vistas reales con Avalonia.Headless **sobre Skia** (métricas de texto reales) y expone `ComparacionVisual.CompararConMockup(...)`. Todo estilo v2 vive bajo el selector de scope `.ui2` y todo componente v2 en el namespace `StockApp.Presentation.Controls.V2`, así ninguna vista existente cambia de aspecto hasta que su fase la migre.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Optris.Icons.Avalonia 12.0.7 (prefijo `mdi`), Microsoft.Playwright 1.63.0 (Chromium rev 1243), SkiaSharp 3.119.4 (transitivo de Avalonia.Skia), xUnit v2 (herramienta) y xUnit v3 + Avalonia.Headless.XUnit (tests de UI).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10).
- Fondo app `#F7F7F5` · Superficie `#FFFFFF` · Borde `#E7E5E4` · Borde input `#D6D3D1` · Separador de grilla `#F0EFED` · Header de grilla `#FAFAF9`.
- Acento `#1D4ED8` · Acento hover `#1E40AF` · Acento suave `#DBEAFE` · Acento oscuro `#1E3A8A`.
- Texto primario `#1C1917` · secundario `#57534E` · terciario `#78716C` · etiqueta `#44403C` · grupo `#A8A29E`.
- Peligro texto `#B91C1C` · Borde error `#DC2626`.
- Insignias (fondo/texto): Neutro `#F5F5F4`/`#44403C` · Advertencia `#FEF3C7`/`#92400E` · Peligro `#FEE2E2`/`#991B1B` · Éxito `#DCFCE7`/`#14532D`.
- Alertas (fondo/borde/texto): Advertencia `#FEF3C7`/`#FDE68A`/`#92400E` · Error `#FEE2E2`/`#FCA5A5`/`#991B1B` · Info `#DBEAFE`/`#DBEAFE`/`#1E3A8A`.
- Radios: 8 (inputs, botones, pastilla activa, alertas), 10 (cards), 999 (insignias).
- Sombra de card: `0 1px 2px rgba(0,0,0,0.05)` — la única sombra del sistema.
- Alturas: input y botón 36; fila de grilla 40; header de grilla 36; ítem de sidebar 32.
- Espaciado base 4: todo margen, padding y gap es múltiplo de 4. Única excepción: padding vertical 2 de la `Insignia` (padding 2×8).
- Anchos fijos: sidebar 240; card de formulario máx. 760; card de login 400.
- Tipografía Inter (`Avalonia.Fonts.Inter`); cifras tabulares y alineadas a la derecha en toda columna numérica.
- Tamaños/pesos: título de página 22/600 alto de línea 26 · título de sección 15/600 · marca 15/700 · valor de TarjetaMetrica 20/600 · título de login 20/600 · cuerpo 14/400 · texto de controles 13 · subtítulo de sección de formulario 13/600 · etiqueta de campo 12/500 · ítem de sidebar 12/500 · insignia 12/500 · error de campo 12 · header de columna y de sección de sidebar 11/600 MAYÚSCULAS letter-spacing 0.6.
- Grillas sin zebra: filas blancas separadas por `#F0EFED`.
- Mockups a 1280×760, solo técnicas que Avalonia replica 1:1 (colores sólidos, bordes de 1px, radios, una única `BoxShadow`, Inter, px enteros, múltiplos de 4). Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`.
- Los valores de tokens de la spec son canónicos; si un mockup de referencia difiere, gana la spec (§2.3 "Precedencia").
- No cambian: `ViewLocator`, `INavigationService`, el wiring de `DataContextChanged`, el seam de validación `ErrorConverter`, la lógica de permisos. No se renombran ni se mueven vistas.
- **Fase 0 no migra ninguna vista:** la app real debe verse y comportarse IGUAL que antes al final de la fase.
- **Scope `.ui2`:** todo estilo de control base v2 lleva el prefijo de selector `.ui2 `. Las fases siguientes ponen `Classes="ui2"` en la RAÍZ de cada vista migrada y NUNCA en un contenedor que hospede vistas no migradas (por ejemplo el `ContentControl` de contenido del shell).
- Tokens v2 con prefijo de clave `V2` (`V2AcentoBrush`, `V2AltoControl`) y variables CSS `--v2-*` mientras conviven con los viejos; el prefijo sale de `tokens.json` y se vacía en la Fase 9.
- Versiones de paquetes SOLO en `Directory.Packages.props` (Central Package Management). Única excepción heredada: los `VersionOverride` de `Microsoft.NET.Test.Sdk` y `xunit.runner.visualstudio` de los proyectos xUnit v3 (mismo patrón que `StockApp.Presentation.UiTests`).
- Los comentarios de `.axaml` NO pueden contener `--` (AVLN1001, cascadea a MSB4025). Tampoco los archivos generados.
- Compilar y testear con la solución: `dotnet build StockApp.sln` / `dotnet test StockApp.sln`. Nunca varios `.csproj` en un solo `dotnet build` (falso verde).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos, no regresión.
- Si un rojo no tiene sentido tras una mutación, correr `dotnet build-server shutdown` (Roslyn puede servir ensamblados stale) y repetir.
- Todo guardián nuevo se verifica por MUTACIÓN QUITANDO: se elimina la regla que custodia, se ve el rojo, se restaura re-agregando EXACTAMENTE lo quitado (con `git checkout -- <archivo>` solo si el archivo no tiene cambios sin commitear de la task en curso), se ve el verde. Nunca se agrega un efecto destructivo para mutar.
- Commits: conventional commits en español, SIN `Co-Authored-By` ni atribución de IA. No se pushea.

## Review Focus

1. **Fuga de estilos v2 a vistas no migradas.** Un selector sin `.ui2 `, o una clase compartida con el sistema viejo (`ghost`, `num`), cambia el aspecto de pantallas que nadie migró. Esperado: fuera de `.ui2` todo control conserva los valores viejos. Tests: `ControlesV2ConvivenciaTests` (Task 10) y `DataGridV2Tests.FueraDeUi2_ConservaLaZebraVieja` (Task 16).
2. **Errores crudos de .NET llegando a la UI dentro de una vista v2.** Si `CampoFormulario` v2 o el `ErrorTemplate` v2 interceptan la validación, un `decimal` con "abc" muestra "Input string was not in a correct format". Esperado: se ve "Ingresá un número válido." en `#DC2626`. Test: `CampoFormularioV2Tests.TextoInvalidoEnDecimal_MuestraElMensajeDelSeamEnRojo` (Task 13).
3. **Defecto chico enmascarado por el porcentaje global.** Un botón corrido 4px o una insignia con el color equivocado mueven el porcentaje global de píxeles distintos en centésimas, y los rellenos pálidos de la paleta difieren en menos que la tolerancia de antialiasing: el test daría verde con un defecto visible. Esperado: cualquier bloque denso de diferencias (de posición o de color) falla. Tests: `ComparadorImagenesTests.BloqueDesplazado8px_MarcaTeselasDensas` y `FondoPalidoCambiado_MarcaTeselasPorColorAunqueNingunPixelDifieraFuerte` (Task 5), `ComparacionVisualTests.VistaDesplazada8px_FallaYDejaLaPaginaDeComparacion` (Task 9) y las mutaciones de calibración (Task 18).
4. **El harness no mide lo que la app renderiza.** `VisualTestApp` desincronizado de `App.axaml`, texto renderizado con la fuente de fallback (DejaVu) en vez de Inter, o un mockup con la fuente sin cargar o que desborda 1280×760 y se recorta en silencio. Esperado: la herramienta falla con un mensaje accionable. Tests: `IncludesAppAxamlTests` (Tasks 3 y 8), `RenderizadorVistaTests.TextoSinFontFamilyExplicita_SeMideConInter` (Task 8), `CapturadorMockupTests` de fuentes y desborde (Task 6).
5. **Componentes v2 que heredan estilos globales viejos por su tipo base.** `TarjetaAcceso` hereda de `Button`, el link del breadcrumb es un `Button`, y `NumericUpDown`/`CalendarDatePicker` tienen un `TextBox` interno que recibe los estilos de `TextBox`: pueden quedar con fondo verde al hover, 36 de alto en el link o 38 de alto en el input. Tests: `EncabezadoPaginaTests.BreadcrumbOrigen_NoTomaElAltoDeBotonV2` (Task 12), `TarjetasV2Tests.TarjetaAcceso_Hover_NoUsaElVerdeViejo` (Task 15), `ControlesV2Tests.Inputs_MidenAlto36` (Task 10).

---

## Decisiones de la investigación (verificadas, con fuente)

| Tema | Decisión | Evidencia |
|---|---|---|
| Render headless a PNG | `AppBuilder.Configure<T>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })` + `window.CaptureRenderedFrame()` → `WriteableBitmap` 1280×760 `Rgba8888` a 96 dpi → `.Save(ruta)` | Spike en scratchpad (2026-09-28) y decompilado de `Avalonia.Headless.dll` 12.0.5: `GetLastRenderedFrame` lanza `NotSupportedException` si el render interface es el stub headless. |
| Proyecto de test propio para la comparación | `tests/StockApp.Presentation.VisualTests` y NO `UiTests` | `[assembly: AvaloniaTestApplication]` es uno por ensamblado. Con `UseHeadlessDrawing=true` (lo que usa hoy `UiTests/TestAppBuilder.cs`) el `IFontManagerImpl` es `HeadlessFontManagerStub`: métricas de texto falsas. Pasar `UiTests` a Skia cambiaría las métricas de 75 archivos de test. |
| Inter en Skia | `WithInterFont()` en Avalonia 12 solo agrega la colección; la familia default queda `DejaVu Sans` en WSL. Inter se aplica porque `Themes/Typography.axaml` setea `FontFamily` en `Window, UserControl`. | Decompilado de `Avalonia.Fonts.Inter.dll` (`AppBuilderExtension.WithInterFont` solo llama `AddFontCollection`) y spike (`FontManager.Current.DefaultFontFamily.Name = "DejaVu Sans"`). El comentario de `Typography.axaml` que dice lo contrario es incorrecto. |
| Cifras tabulares | `<Setter Property="TextElement.FontFeatures" Value="+tnum" />` en el contenedor; se hereda al `TextBlock` | Spike Skia: Inter 13px "1111" pasa de 25px a 34px (= "0000"). El veredicto "inconcluso" de agosto se midió con el stub de fuentes. |
| Geometría del DataGrid 12.0.1 | fila = `RowHeight` + 1px de `PART_BottomGridLine` → `RowHeight=39` para un pitch de 40. `ColumnHeaderHeight=36` incluye el separador de 1px. | Spike: `RowHeight=40` dio `DataGridRow` de 41px; header 36 con `PART_ColumnHeadersAndRowsSeparator` en y=35. |
| Captura del mockup | `Microsoft.Playwright` 1.63.0 (NuGet), dentro del mismo toolchain .NET; instalación de Chromium sin PowerShell con `Microsoft.Playwright.Program.Main(["install","chromium"])` | Spike: captura 1280×760 con `DeviceScaleFactor=1`, Chromium rev 1243 ya presente en `~/.cache/ms-playwright`. Descartado `npx playwright`: sumaría Node al pipeline de tests y dos versiones de navegador sin pin en `Directory.Packages.props`. Descartado el MCP de Playwright: no es un comando reproducible. |
| Flag de Chromium | `--font-render-hinting=none` | Spike con una card con dos líneas de texto: diff (delta de canal > 32) 0.165% sin flag, 0.052% con flag; sin flag el texto de 13px sale ~2% más ancho que en Skia. `--disable-font-subpixel-positioning` empeora (0.176%). |
| Inter local para el mockup | Los TTF embebidos en `Avalonia.Fonts.Inter` 12.0.5 (Inter 3.19, `fontversion 197853`) extraídos con `AssetLoader.Open` a `docs/ui-v2/mockups/_base/fuentes/`, `@font-face` con URL relativa | Spike: `file://` + URL relativa carga (`status: loaded`). `AssetLoader` fuera de una app necesita `AppBuilder.Configure<Application>().UseHeadless(new()).SetupWithoutStarting()` (probado en consola). Licencia SIL OFL 1.1 (`rsms/inter` v3.19): se versiona el `OFL.txt` junto a los TTF. |
| Diff de imágenes | SkiaSharp (MIT, ya transitivo vía `Avalonia.Skia` 3.119.4). NO ImageSharp. | Six Labors Split License: Apache 2.0 solo para open source, dependencia transitiva, empresas con fines de lucro < 1M USD o non-profit/charity; "all other scenarios require a commercial license". Un municipio no está listado (github.com/SixLabors/ImageSharp/blob/main/LICENSE). |
| Alto de línea en los mockups | CSS con `line-height` = `ceil(tamaño × 1.20996)` por rol (11→14, 12→15, 13→16, 14→17, 15→19, 20→25, 22→27), generado desde `tokens.json` (`factorAltoLinea`) | Medido con Skia: un `TextBlock` de Inter mide `ceil(tamaño × (ascender+descender)/unitsPerEm)` = `ceil(tamaño × 1.20996)` (layout rounding). Chromium con `line-height: normal` usa el valor sin redondear y los textos apilados derivan en subpíxeles. Un test de `VisualTests` custodia la igualdad. |
| Métrica de aceptación | Dos compuertas: % global de píxeles distintos (delta de canal > 32) Y cantidad de "teselas densas" (16×16 con ≥ 25% de diferencias fuertes, o ≥ 90% de diferencias leves > 4) | El % global solo no ve defectos chicos (un bloque de 200×100 corrido 8px = 0.16% de la pantalla). Los colores pálidos de la paleta difieren entre sí en menos de 32 por canal (`#FEE2E2` vs `#F5F5F4`: 19), así que un relleno equivocado solo se ve con la regla de color. El antialiasing reparte diferencias finas; un defecto de layout/color las concentra. |
| Generador de tokens | Proyecto consola `tools/StockApp.UiV2.Herramientas` (comando `tokens`) + test de sincronía en `tests/StockApp.UiV2.Herramientas.Tests` que falla si los archivos commiteados difieren de lo que genera el JSON | La misma herramienta necesita más comandos (fuentes, íconos, navegador, captura, diff) y el patrón `tools/` + `tests/*.Tests` ya existe (`StockApp.Licencias.Cli`). Un test que regenera archivos escribiría en el repo durante `dotnet test`. |
| Convivencia | Tokens con prefijo `V2`; estilos base bajo `.ui2`; componentes en `StockApp.Presentation.Controls.V2` (tipos nuevos, sus `ControlTheme` no chocan con los viejos `CampoFormulario`/`TarjetaMetrica`/`EstadoVacio`) | En Avalonia, ante estilos que matchean, gana el último declarado: los `.ui2` se incluyen al final de `App.axaml` y setean TODAS las propiedades que setean sus pares viejos globales. |
| Catálogo de componentes | Vive en `tests/StockApp.Presentation.VisualTests/Catalogo/`: no se compila en `GestionMunicipal`, no entra en la navegación ni en `GuardianDePatronTests`. Se "accede" corriendo la comparación: el render queda en `artifacts/ui-v2/comparacion/componentes-catalogo/`. | — |

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `docs/ui-v2/tokens.json` | Fuente única de tokens v2 (valores de la spec). |
| `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj` | Consola de herramientas de UI v2. |
| `tools/StockApp.UiV2.Herramientas/Program.cs` | Punto de entrada: delega en `Cli`. |
| `tools/StockApp.UiV2.Herramientas/Cli.cs` | Parseo de comandos y códigos de salida. |
| `tools/StockApp.UiV2.Herramientas/RutasRepo.cs` | Rutas del repo (raíz por `StockApp.sln`). |
| `tools/StockApp.UiV2.Herramientas/Lienzo.cs` | Tamaño canónico 1280×760. |
| `tools/StockApp.UiV2.Herramientas/Tokens/DefinicionTokens.cs` | Records del modelo de tokens + excepción. |
| `tools/StockApp.UiV2.Herramientas/Tokens/LectorTokens.cs` | Parseo y validación de `tokens.json`. |
| `tools/StockApp.UiV2.Herramientas/Tokens/GeneradorTokens.cs` | Genera `Tokens.axaml` y `tokens.css`. |
| `tools/StockApp.UiV2.Herramientas/Mockups/ExtractorFuentes.cs` | Extrae/verifica los TTF de Inter embebidos. |
| `tools/StockApp.UiV2.Herramientas/Mockups/ExportadorIconos.cs` | Ícono MDI → SVG inline. |
| `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs` | HTML → PNG 1280×760 con Playwright. |
| `tools/StockApp.UiV2.Herramientas/Imagenes/ResultadoComparacion.cs` | Resultado del diff. |
| `tools/StockApp.UiV2.Herramientas/Imagenes/ComparadorImagenes.cs` | Diff de PNG con SkiaSharp + teselas densas. |
| `tools/StockApp.UiV2.Herramientas/Imagenes/PaginaComparacion.cs` | HTML lado a lado + índice. |
| `tests/StockApp.UiV2.Herramientas.Tests/*` | Tests xUnit v2 de la herramienta. |
| `src/StockApp.Presentation/Themes/V2/Tokens.axaml` | GENERADO. Recursos v2. |
| `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` | Estilos v2 de controles base y tipografía, bajo `.ui2`. |
| `src/StockApp.Presentation/Themes/V2/DataGridV2.axaml` | Tema v2 de DataGrid, bajo `.ui2`. |
| `src/StockApp.Presentation/Controls/V2/Variantes.cs` | Enums `VarianteInsignia`, `VarianteAlerta`. |
| `src/StockApp.Presentation/Controls/V2/{Insignia,Alerta,TarjetaContenido,EncabezadoPagina,CampoFormulario,BarraFiltros,TarjetaMetrica,TarjetaAcceso,EstadoVacio}.cs` | Componentes v2. |
| `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` | `ControlTheme` de los componentes v2. |
| `docs/ui-v2/mockups/_base/tokens.css` | GENERADO. Variables CSS v2. |
| `docs/ui-v2/mockups/_base/base.css` | CSS de componentes para mockups (misma geometría que los `ControlTheme`). |
| `docs/ui-v2/mockups/_base/fuentes/Inter-{Regular,Medium,SemiBold,Bold}.ttf` + `OFL.txt` | Inter local, byte-idéntica a la embebida. |
| `docs/ui-v2/mockups/_fixtures/bloque.html` | Mockup mínimo para el autotest de la herramienta. |
| `docs/ui-v2/mockups/componentes/catalogo.html` | Mockup del catálogo de componentes. |
| `docs/ui-v2/herramientas.md` | Uso de la herramienta y del comando de comparación. |
| `scripts/ui-v2/comparar.sh` | Comando único para correr las comparaciones. |
| `tests/StockApp.Presentation.UiTests/V2/*` | Tests de UI de tokens, estilos y componentes v2. |
| `tests/StockApp.Presentation.VisualTests/*` | Render Skia, `CompararConMockup`, catálogo y su test. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `Directory.Packages.props` | + `Avalonia.Skia` 12.0.5, + `Microsoft.Playwright` 1.63.0 |
| `StockApp.sln` | + 3 proyectos |
| `src/StockApp.Presentation/App.axaml` | + 2 `ResourceInclude` y 2 `StyleInclude` v2 |
| `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` | includes en arrays públicos (para el guardián) + includes v2 |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | + §6.5 con la calibración del umbral |

**Comandos de verificación:**

- Suite completa: `dotnet test StockApp.sln`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Un test: agregar `--filter "FullyQualifiedName~NombreDelTest"`

---

## Bloque A — Tokens como fuente única

### Task 1: Herramienta de UI v2 y lector validado de `tokens.json`

**Files:**
- Create: `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`
- Create: `tools/StockApp.UiV2.Herramientas/Program.cs`
- Create: `tools/StockApp.UiV2.Herramientas/Cli.cs`
- Create: `tools/StockApp.UiV2.Herramientas/RutasRepo.cs`
- Create: `tools/StockApp.UiV2.Herramientas/Lienzo.cs`
- Create: `tools/StockApp.UiV2.Herramientas/Tokens/DefinicionTokens.cs`
- Create: `tools/StockApp.UiV2.Herramientas/Tokens/LectorTokens.cs`
- Create: `docs/ui-v2/tokens.json`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/JsonTokensDePrueba.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/LectorTokensTests.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/TokensSpecTests.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/CliTests.cs`
- Modify: `StockApp.sln`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `StockApp.UiV2.Herramientas.Cli.Ejecutar(string[] args, TextWriter salida, TextWriter error) : int` (0 ok, 1 fallo del comando, 2 uso incorrecto). Las Tasks 2, 4, 5, 6 agregan brazos al `switch`.
  - `StockApp.UiV2.Herramientas.RutasRepo` con `Raiz`, `TokensJson`, `TokensAxaml`, `CarpetaBaseMockups`, `TokensCss`, `CarpetaFuentes`, `CarpetaMockups` (todas `string`).
  - `StockApp.UiV2.Herramientas.Lienzo.Ancho = 1280`, `Lienzo.Alto = 760` (`const int`).
  - `StockApp.UiV2.Herramientas.Tokens.DefinicionTokens`, `EstiloTipografico`, `SombraToken`, `TokensInvalidosException`.
  - `StockApp.UiV2.Herramientas.Tokens.LectorTokens.Leer(string json) : DefinicionTokens` y `LeerArchivo(string ruta) : DefinicionTokens`.

- [ ] **Step 1: Crear el proyecto de la herramienta**

`tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>StockApp.UiV2.Herramientas</RootNamespace>
    <!-- Herramienta interna de desarrollo de la UI v2: NUNCA se empaqueta ni se distribuye. -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="StockApp.UiV2.Herramientas.Tests" />
  </ItemGroup>

</Project>
```

`tools/StockApp.UiV2.Herramientas/Program.cs`:

```csharp
return StockApp.UiV2.Herramientas.Cli.Ejecutar(args, Console.Out, Console.Error);
```

`tools/StockApp.UiV2.Herramientas/Cli.cs`:

```csharp
namespace StockApp.UiV2.Herramientas;

/// <summary>
/// Comandos de la herramienta de UI v2. Códigos de salida: 0 ok, 1 el comando falló,
/// 2 uso incorrecto. Cada comando se documenta en docs/ui-v2/herramientas.md.
/// </summary>
public static class Cli
{
    public const string Uso = """
        Uso: dotnet run --project tools/StockApp.UiV2.Herramientas -- <comando>

        Comandos:
          tokens [--verificar]              Genera Themes/V2/Tokens.axaml y mockups/_base/tokens.css desde docs/ui-v2/tokens.json
          fuentes [--verificar]             Copia los TTF de Inter embebidos en Avalonia.Fonts.Inter a mockups/_base/fuentes
          icono-svg <clave-mdi>             Imprime el SVG inline de un ícono MDI (p. ej. mdi-package-variant)
          instalar-navegador                Instala el Chromium de Playwright (una vez por máquina)
          capturar-mockup <html> <png>      Captura un mockup HTML a PNG de 1280x760
          comparar-png <ref> <cand> <diff>  Compara dos PNG y escribe la imagen de diferencias
        """;

    public static int Ejecutar(string[] args, TextWriter salida, TextWriter error)
    {
        switch (args)
        {
            default:
                error.WriteLine(Uso);
                return 2;
        }
    }
}
```

`tools/StockApp.UiV2.Herramientas/RutasRepo.cs`:

```csharp
namespace StockApp.UiV2.Herramientas;

/// <summary>Rutas del repositorio, resueltas subiendo desde el binario hasta encontrar StockApp.sln.</summary>
public static class RutasRepo
{
    public static string Raiz { get; } = BuscarRaiz(AppContext.BaseDirectory);

    public static string TokensJson => Path.Combine(Raiz, "docs", "ui-v2", "tokens.json");

    public static string TokensAxaml =>
        Path.Combine(Raiz, "src", "StockApp.Presentation", "Themes", "V2", "Tokens.axaml");

    public static string CarpetaMockups => Path.Combine(Raiz, "docs", "ui-v2", "mockups");

    public static string CarpetaBaseMockups => Path.Combine(CarpetaMockups, "_base");

    public static string TokensCss => Path.Combine(CarpetaBaseMockups, "tokens.css");

    public static string CarpetaFuentes => Path.Combine(CarpetaBaseMockups, "fuentes");

    internal static string BuscarRaiz(string desde)
    {
        for (var dir = new DirectoryInfo(desde); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StockApp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No se encontró StockApp.sln subiendo desde {desde}.");
    }
}
```

`tools/StockApp.UiV2.Herramientas/Lienzo.cs`:

```csharp
namespace StockApp.UiV2.Herramientas;

/// <summary>Tamaño canónico de mockups y renders de la UI v2 (spec §6.1).</summary>
public static class Lienzo
{
    public const int Ancho = 1280;
    public const int Alto = 760;
}
```

- [ ] **Step 2: Crear el proyecto de tests y agregar ambos a la solución**

`tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\tools\StockApp.UiV2.Herramientas\StockApp.UiV2.Herramientas.csproj" />
  </ItemGroup>

</Project>
```

Run:

```bash
dotnet sln StockApp.sln add tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj --solution-folder tools
dotnet sln StockApp.sln add tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --solution-folder tests
```

Expected: dos líneas `Project ... added to the solution.`

- [ ] **Step 3: Escribir los tests del lector (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/JsonTokensDePrueba.cs`:

```csharp
namespace StockApp.UiV2.Herramientas.Tests;

internal static class JsonTokensDePrueba
{
    public const string Valido = """
        {
          "prefijo": "V2",
          "factorAltoLinea": 1.20996,
          "colores": { "Acento": "#1D4ED8", "Fondo": "#F7F7F5" },
          "coloresDerivados": { "TextoSobreAcento": "#FFFFFF" },
          "radios": { "Control": 8 },
          "alturas": { "Control": 36 },
          "anchos": { "Sidebar": 240 },
          "espaciado": [4, 8],
          "sombraCard": { "x": 0, "y": 1, "desenfoque": 2, "expansion": 0, "color": "#000000", "opacidad": 0.05 },
          "tipografia": {
            "TituloPagina": { "tamano": 22, "peso": 600, "altoLinea": 26 },
            "HeaderColumna": { "tamano": 11, "peso": 600, "espaciadoLetras": 0.6, "mayusculas": true }
          }
        }
        """;
}
```

`tests/StockApp.UiV2.Herramientas.Tests/LectorTokensTests.cs`:

```csharp
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

public class LectorTokensTests
{
    [Fact]
    public void Leer_JsonValido_DevuelveLasSeccionesEnOrdenDeDocumento()
    {
        var def = LectorTokens.Leer(JsonTokensDePrueba.Valido);

        Assert.Equal("V2", def.Prefijo);
        Assert.Equal(1.20996, def.FactorAltoLinea);
        Assert.Equal(
            new[] { ("Acento", "#1D4ED8"), ("Fondo", "#F7F7F5"), ("TextoSobreAcento", "#FFFFFF") },
            def.Colores.ToArray());
        Assert.Equal(new[] { ("Control", 8.0) }, def.Radios.ToArray());
        Assert.Equal(new[] { ("Control", 36.0) }, def.Alturas.ToArray());
        Assert.Equal(new[] { ("Sidebar", 240.0) }, def.Anchos.ToArray());
        Assert.Equal(new[] { 4.0, 8.0 }, def.Espaciado.ToArray());
        Assert.Equal(new SombraToken(0, 1, 2, 0, "#000000", 0.05), def.SombraCard);
        Assert.Equal(new EstiloTipografico(22, 600, 26, null, false), def.Tipografia[0].Estilo);
        Assert.Equal(new EstiloTipografico(11, 600, null, 0.6, true), def.Tipografia[1].Estilo);
    }

    [Fact]
    public void Leer_ColorEnMinusculas_LanzaNombrandoLaClave()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"#1D4ED8\"", "\"#1d4ed8\"");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("colores.Acento", ex.Message);
    }

    [Fact]
    public void Leer_EspaciadoNoMultiploDe4_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("[4, 8]", "[4, 6]");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("múltiplo de 4", ex.Message);
    }

    [Fact]
    public void Leer_PesoFueraDe400a700_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"peso\": 600, \"altoLinea\"", "\"peso\": 550, \"altoLinea\"");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("tipografia.TituloPagina.peso", ex.Message);
    }

    [Fact]
    public void Leer_NombreConGuion_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"Fondo\":", "\"Fondo-App\":");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("PascalCase", ex.Message);
    }

    [Fact]
    public void Leer_ColorRepetidoEntreColoresYDerivados_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"TextoSobreAcento\":", "\"Acento\":");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("duplicado 'Acento'", ex.Message);
    }

    [Fact]
    public void Leer_OpacidadMayorA1_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"opacidad\": 0.05", "\"opacidad\": 5");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("sombraCard.opacidad", ex.Message);
    }

    [Fact]
    public void Leer_SinSeccionAnchos_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"anchos\": { \"Sidebar\": 240 },", "");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("'anchos'", ex.Message);
    }
}
```

`tests/StockApp.UiV2.Herramientas.Tests/CliTests.cs`:

```csharp
namespace StockApp.UiV2.Herramientas.Tests;

public class CliTests
{
    [Fact]
    public void Ejecutar_ComandoDesconocido_Devuelve2YMuestraElUso()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["no-existe"], salida, error);

        Assert.Equal(2, codigo);
        Assert.Contains("Uso: dotnet run --project tools/StockApp.UiV2.Herramientas", error.ToString());
    }
}
```

- [ ] **Step 4: Correr y ver el rojo**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
Expected: FAIL de compilación, `error CS0246: The type or namespace name 'TokensInvalidosException' could not be found` (y `LectorTokens`, `SombraToken`, `EstiloTipografico`).

- [ ] **Step 5: Implementar el modelo y el lector**

`tools/StockApp.UiV2.Herramientas/Tokens/DefinicionTokens.cs`:

```csharp
namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>tokens.json no cumple las reglas de la spec (formato de color, base 4, pesos, nombres).</summary>
public sealed class TokensInvalidosException(string mensaje) : Exception(mensaje);

/// <summary>
/// Rol tipográfico. AltoLinea null = alto natural de la fuente, que Avalonia redondea hacia arriba:
/// ceil(Tamano × FactorAltoLinea). El CSS de los mockups emite ese mismo valor.
/// </summary>
public sealed record EstiloTipografico(double Tamano, int Peso, double? AltoLinea, double? EspaciadoLetras, bool Mayusculas);

/// <summary>La única sombra del sistema (spec §2.2). Color #RRGGBB + opacidad 0..1.</summary>
public sealed record SombraToken(double X, double Y, double Desenfoque, double Expansion, string Color, double Opacidad);

/// <summary>
/// tokens.json ya validado. Las listas conservan el orden del documento: el orden de los
/// archivos generados es estable y un diff de tokens.json se lee igual en su salida.
/// FactorAltoLinea: (ascender + descender) / unitsPerEm de la Inter embebida (3.19).
/// </summary>
public sealed record DefinicionTokens(
    string Prefijo,
    double FactorAltoLinea,
    IReadOnlyList<(string Nombre, string Hex)> Colores,
    IReadOnlyList<(string Nombre, double Valor)> Radios,
    IReadOnlyList<(string Nombre, double Valor)> Alturas,
    IReadOnlyList<(string Nombre, double Valor)> Anchos,
    IReadOnlyList<double> Espaciado,
    SombraToken SombraCard,
    IReadOnlyList<(string Nombre, EstiloTipografico Estilo)> Tipografia);
```

`tools/StockApp.UiV2.Herramientas/Tokens/LectorTokens.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>
/// Lee y valida docs/ui-v2/tokens.json. Valida lo que la spec fija como regla (§2.2: base 4;
/// colores sólidos #RRGGBB; pesos 400 a 700) para que un token mal escrito falle acá y no
/// como un color raro en una pantalla.
/// </summary>
public static partial class LectorTokens
{
    private static readonly int[] PesosValidos = [400, 500, 600, 700];

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexValido();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex NombreValido();

    [GeneratedRegex("^([A-Z][A-Za-z0-9]*)?$")]
    private static partial Regex PrefijoValido();

    public static DefinicionTokens LeerArchivo(string ruta) => Leer(File.ReadAllText(ruta));

    public static DefinicionTokens Leer(string json)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;

        var prefijo = Texto(raiz, "prefijo");
        if (!PrefijoValido().IsMatch(prefijo))
        {
            throw new TokensInvalidosException($"prefijo '{prefijo}' inválido: vacío o PascalCase.");
        }

        var factorAltoLinea = Numero(raiz, "factorAltoLinea");
        if (factorAltoLinea is < 1 or > 2)
        {
            throw new TokensInvalidosException(
                $"factorAltoLinea: {factorAltoLinea.ToString(CultureInfo.InvariantCulture)} fuera de [1, 2].");
        }

        var colores = new List<(string, string)>();
        foreach (var seccion in new[] { "colores", "coloresDerivados" })
        {
            foreach (var p in Objeto(raiz, seccion).EnumerateObject())
            {
                var hex = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : "";
                if (!HexValido().IsMatch(hex))
                {
                    throw new TokensInvalidosException(
                        $"{seccion}.{p.Name}: '{hex}' no es un color #RRGGBB en mayúsculas.");
                }

                colores.Add((ValidarNombre(seccion, p.Name), hex));
            }
        }

        ValidarUnicos("colores", colores.Select(c => c.Item1));

        var espaciado = new List<double>();
        foreach (var e in Propiedad(raiz, "espaciado").EnumerateArray())
        {
            var v = e.GetDouble();
            if (v <= 0 || v % 4 != 0)
            {
                throw new TokensInvalidosException(
                    $"espaciado: {v.ToString(CultureInfo.InvariantCulture)} no es múltiplo de 4 (spec §2.2, base 4).");
            }

            espaciado.Add(v);
        }

        var s = Objeto(raiz, "sombraCard");
        var sombra = new SombraToken(
            Numero(s, "x"), Numero(s, "y"), Numero(s, "desenfoque"), Numero(s, "expansion"),
            Texto(s, "color"), Numero(s, "opacidad"));
        if (!HexValido().IsMatch(sombra.Color))
        {
            throw new TokensInvalidosException($"sombraCard.color: '{sombra.Color}' no es #RRGGBB.");
        }

        if (sombra.Opacidad is < 0 or > 1)
        {
            throw new TokensInvalidosException(
                $"sombraCard.opacidad: {sombra.Opacidad.ToString(CultureInfo.InvariantCulture)} fuera de [0, 1].");
        }

        var tipografia = new List<(string, EstiloTipografico)>();
        foreach (var p in Objeto(raiz, "tipografia").EnumerateObject())
        {
            var t = p.Value;
            var peso = (int)Numero(t, "peso");
            if (!PesosValidos.Contains(peso))
            {
                throw new TokensInvalidosException($"tipografia.{p.Name}.peso: {peso} no es 400, 500, 600 ni 700.");
            }

            tipografia.Add((ValidarNombre("tipografia", p.Name), new EstiloTipografico(
                Numero(t, "tamano"),
                peso,
                t.TryGetProperty("altoLinea", out var alto) ? alto.GetDouble() : null,
                t.TryGetProperty("espaciadoLetras", out var espaciadoLetras) ? espaciadoLetras.GetDouble() : null,
                t.TryGetProperty("mayusculas", out var mayusculas) && mayusculas.GetBoolean())));
        }

        ValidarUnicos("tipografia", tipografia.Select(t => t.Item1));

        return new DefinicionTokens(
            prefijo,
            factorAltoLinea,
            colores,
            Numeros(raiz, "radios"),
            Numeros(raiz, "alturas"),
            Numeros(raiz, "anchos"),
            espaciado,
            sombra,
            tipografia);
    }

    private static List<(string, double)> Numeros(JsonElement raiz, string seccion)
    {
        var lista = new List<(string, double)>();
        foreach (var p in Objeto(raiz, seccion).EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Number || p.Value.GetDouble() <= 0)
            {
                throw new TokensInvalidosException($"{seccion}.{p.Name}: debe ser un número positivo.");
            }

            lista.Add((ValidarNombre(seccion, p.Name), p.Value.GetDouble()));
        }

        ValidarUnicos(seccion, lista.Select(x => x.Item1));
        return lista;
    }

    private static JsonElement Propiedad(JsonElement elemento, string nombre) =>
        elemento.TryGetProperty(nombre, out var valor)
            ? valor
            : throw new TokensInvalidosException($"falta la sección '{nombre}'.");

    private static JsonElement Objeto(JsonElement elemento, string nombre)
    {
        var valor = Propiedad(elemento, nombre);
        return valor.ValueKind == JsonValueKind.Object
            ? valor
            : throw new TokensInvalidosException($"'{nombre}' debe ser un objeto.");
    }

    private static string Texto(JsonElement elemento, string nombre)
    {
        var valor = Propiedad(elemento, nombre);
        return valor.ValueKind == JsonValueKind.String
            ? valor.GetString()!
            : throw new TokensInvalidosException($"'{nombre}' debe ser texto.");
    }

    private static double Numero(JsonElement elemento, string nombre)
    {
        var valor = Propiedad(elemento, nombre);
        return valor.ValueKind == JsonValueKind.Number
            ? valor.GetDouble()
            : throw new TokensInvalidosException($"'{nombre}' debe ser un número.");
    }

    private static string ValidarNombre(string seccion, string nombre) =>
        NombreValido().IsMatch(nombre)
            ? nombre
            : throw new TokensInvalidosException($"{seccion}.{nombre}: el nombre debe ser PascalCase sin guiones.");

    private static void ValidarUnicos(string seccion, IEnumerable<string> nombres)
    {
        var duplicado = nombres.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1);
        if (duplicado is not null)
        {
            throw new TokensInvalidosException($"{seccion}: nombre duplicado '{duplicado.Key}'.");
        }
    }
}
```

- [ ] **Step 6: Correr y ver el verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~LectorTokensTests|FullyQualifiedName~CliTests"`
Expected: PASS, `Failed: 0, Passed: 9`.

- [ ] **Step 7: Escribir el guardián de los valores de la spec (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/TokensSpecTests.cs`:

```csharp
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Fija los valores de docs/ui-v2/tokens.json a los de la spec (§2.1 a §2.3). tokens.json es la
/// única fuente: si alguien "ajusta" un color para que una pantalla cierre con su mockup, este
/// test lo frena. Cambiar un valor acá exige cambiar primero la spec.
/// </summary>
public class TokensSpecTests
{
    private static readonly DefinicionTokens Tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);

    private static string Color(string nombre) => Tokens.Colores.Single(c => c.Nombre == nombre).Hex;

    [Fact]
    public void Prefijo_EsV2MientrasConvivenConLosTokensViejos() => Assert.Equal("V2", Tokens.Prefijo);

    [Fact]
    public void FactorAltoLinea_EsElDeLaInterEmbebida() => Assert.Equal(1.20996, Tokens.FactorAltoLinea);

    [Theory]
    [InlineData("Fondo", "#F7F7F5")]
    [InlineData("Superficie", "#FFFFFF")]
    [InlineData("Borde", "#E7E5E4")]
    [InlineData("BordeInput", "#D6D3D1")]
    [InlineData("SeparadorGrilla", "#F0EFED")]
    [InlineData("HeaderGrilla", "#FAFAF9")]
    [InlineData("Acento", "#1D4ED8")]
    [InlineData("AcentoHover", "#1E40AF")]
    [InlineData("AcentoSuave", "#DBEAFE")]
    [InlineData("AcentoOscuro", "#1E3A8A")]
    [InlineData("TextoPrimario", "#1C1917")]
    [InlineData("TextoSecundario", "#57534E")]
    [InlineData("TextoTerciario", "#78716C")]
    [InlineData("TextoEtiqueta", "#44403C")]
    [InlineData("TextoGrupo", "#A8A29E")]
    [InlineData("PeligroTexto", "#B91C1C")]
    [InlineData("BordeError", "#DC2626")]
    [InlineData("InsigniaNeutroFondo", "#F5F5F4")]
    [InlineData("InsigniaNeutroTexto", "#44403C")]
    [InlineData("InsigniaAdvertenciaFondo", "#FEF3C7")]
    [InlineData("InsigniaAdvertenciaTexto", "#92400E")]
    [InlineData("InsigniaPeligroFondo", "#FEE2E2")]
    [InlineData("InsigniaPeligroTexto", "#991B1B")]
    [InlineData("InsigniaExitoFondo", "#DCFCE7")]
    [InlineData("InsigniaExitoTexto", "#14532D")]
    [InlineData("AlertaAdvertenciaFondo", "#FEF3C7")]
    [InlineData("AlertaAdvertenciaBorde", "#FDE68A")]
    [InlineData("AlertaAdvertenciaTexto", "#92400E")]
    [InlineData("AlertaErrorFondo", "#FEE2E2")]
    [InlineData("AlertaErrorBorde", "#FCA5A5")]
    [InlineData("AlertaErrorTexto", "#991B1B")]
    [InlineData("AlertaInfoFondo", "#DBEAFE")]
    [InlineData("AlertaInfoBorde", "#DBEAFE")]
    [InlineData("AlertaInfoTexto", "#1E3A8A")]
    public void Color_TieneElValorDeLaSpec(string nombre, string hex) => Assert.Equal(hex, Color(nombre));

    /// <summary>
    /// Derivados de Fase 0: la spec no los nombra pero los necesitan los estados de los
    /// controles. Ninguno introduce un hex nuevo: todos repiten un valor ya aprobado.
    /// </summary>
    [Theory]
    [InlineData("TextoSobreAcento", "Superficie")]
    [InlineData("HoverNeutro", "InsigniaNeutroFondo")]
    [InlineData("DeshabilitadoFondo", "InsigniaNeutroFondo")]
    [InlineData("DeshabilitadoTexto", "TextoGrupo")]
    public void ColorDerivado_RepiteUnValorAprobado(string derivado, string origen) =>
        Assert.Equal(Color(origen), Color(derivado));

    [Theory]
    [InlineData("Control", 8)]
    [InlineData("Card", 10)]
    [InlineData("Insignia", 999)]
    [InlineData("Casilla", 4)]
    public void Radio_TieneElValorDeLaSpec(string nombre, double valor) =>
        Assert.Equal(valor, Tokens.Radios.Single(r => r.Nombre == nombre).Valor);

    [Theory]
    [InlineData("Control", 36)]
    [InlineData("FilaGrilla", 40)]
    [InlineData("HeaderGrilla", 36)]
    [InlineData("ItemSidebar", 32)]
    public void Altura_TieneElValorDeLaSpec(string nombre, double valor) =>
        Assert.Equal(valor, Tokens.Alturas.Single(a => a.Nombre == nombre).Valor);

    [Theory]
    [InlineData("Sidebar", 240)]
    [InlineData("FormularioMax", 760)]
    [InlineData("CardLogin", 400)]
    public void Ancho_TieneElValorDeLaSpec(string nombre, double valor) =>
        Assert.Equal(valor, Tokens.Anchos.Single(a => a.Nombre == nombre).Valor);

    [Fact]
    public void Sombra_EsLaUnicaDeLaSpec() =>
        Assert.Equal(new SombraToken(0, 1, 2, 0, "#000000", 0.05), Tokens.SombraCard);

    [Theory]
    [InlineData("TituloPagina", 22, 600)]
    [InlineData("TituloSeccion", 15, 600)]
    [InlineData("Marca", 15, 700)]
    [InlineData("ValorMetrica", 20, 600)]
    [InlineData("TituloLogin", 20, 600)]
    [InlineData("Cuerpo", 14, 400)]
    [InlineData("Control", 13, 400)]
    [InlineData("SubtituloFormulario", 13, 600)]
    [InlineData("EtiquetaCampo", 12, 500)]
    [InlineData("ItemSidebar", 12, 500)]
    [InlineData("Insignia", 12, 500)]
    [InlineData("ErrorCampo", 12, 400)]
    [InlineData("HeaderColumna", 11, 600)]
    [InlineData("HeaderSeccionSidebar", 11, 600)]
    public void Tipografia_TieneTamanoYPesoDeLaSpec(string rol, double tamano, int peso)
    {
        var estilo = Tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        Assert.Equal(tamano, estilo.Tamano);
        Assert.Equal(peso, estilo.Peso);
    }

    [Fact]
    public void TituloPagina_TieneAltoDeLinea26() =>
        Assert.Equal(26, Tokens.Tipografia.Single(t => t.Nombre == "TituloPagina").Estilo.AltoLinea);

    [Theory]
    [InlineData("HeaderColumna")]
    [InlineData("HeaderSeccionSidebar")]
    public void Headers_VanEnMayusculasConEspaciado06(string rol)
    {
        var estilo = Tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        Assert.True(estilo.Mayusculas);
        Assert.Equal(0.6, estilo.EspaciadoLetras);
    }
}
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~TokensSpecTests"`
Expected: FAIL, `System.IO.FileNotFoundException: Could not find file '.../docs/ui-v2/tokens.json'`.

- [ ] **Step 8: Escribir `docs/ui-v2/tokens.json`**

`factorAltoLinea` 1.20996 = (ascender 1984 + descender 494) / unitsPerEm 2048 de la Inter 3.19 embebida; verificado midiendo `TextBlock` con Skia: 11→14, 12→15, 13→16, 14→17, 15→19, 20→25, 22→27. Origen de los roles que la spec no nombra (tomados de los mockups de referencia, clase CSS entre paréntesis): `Boton` 13/600 (`.pt-btn`), `Enlace` 13/500 (`.pt-breadcrumb-link`), `TituloAcceso` 13/600 (`.pt-quickaccess-title`), `DescripcionAcceso` 12/400 (`.pt-quickaccess-desc`), `EtiquetaMetrica` 12/500 (`.pt-kpi-label`), `PieConteo` 12/400 (`.di-gridfoot`); radio `Casilla` 4 (`.pt-checkbox-box`). El espaciado 28 sale del padding horizontal de contenido de los mockups (`.pt-content`).

```json
{
  "prefijo": "V2",
  "factorAltoLinea": 1.20996,
  "colores": {
    "Fondo": "#F7F7F5",
    "Superficie": "#FFFFFF",
    "Borde": "#E7E5E4",
    "BordeInput": "#D6D3D1",
    "SeparadorGrilla": "#F0EFED",
    "HeaderGrilla": "#FAFAF9",
    "Acento": "#1D4ED8",
    "AcentoHover": "#1E40AF",
    "AcentoSuave": "#DBEAFE",
    "AcentoOscuro": "#1E3A8A",
    "TextoPrimario": "#1C1917",
    "TextoSecundario": "#57534E",
    "TextoTerciario": "#78716C",
    "TextoEtiqueta": "#44403C",
    "TextoGrupo": "#A8A29E",
    "PeligroTexto": "#B91C1C",
    "BordeError": "#DC2626",
    "InsigniaNeutroFondo": "#F5F5F4",
    "InsigniaNeutroTexto": "#44403C",
    "InsigniaAdvertenciaFondo": "#FEF3C7",
    "InsigniaAdvertenciaTexto": "#92400E",
    "InsigniaPeligroFondo": "#FEE2E2",
    "InsigniaPeligroTexto": "#991B1B",
    "InsigniaExitoFondo": "#DCFCE7",
    "InsigniaExitoTexto": "#14532D",
    "AlertaAdvertenciaFondo": "#FEF3C7",
    "AlertaAdvertenciaBorde": "#FDE68A",
    "AlertaAdvertenciaTexto": "#92400E",
    "AlertaErrorFondo": "#FEE2E2",
    "AlertaErrorBorde": "#FCA5A5",
    "AlertaErrorTexto": "#991B1B",
    "AlertaInfoFondo": "#DBEAFE",
    "AlertaInfoBorde": "#DBEAFE",
    "AlertaInfoTexto": "#1E3A8A"
  },
  "coloresDerivados": {
    "TextoSobreAcento": "#FFFFFF",
    "HoverNeutro": "#F5F5F4",
    "DeshabilitadoFondo": "#F5F5F4",
    "DeshabilitadoTexto": "#A8A29E"
  },
  "radios": {
    "Control": 8,
    "Card": 10,
    "Insignia": 999,
    "Casilla": 4
  },
  "alturas": {
    "Control": 36,
    "FilaGrilla": 40,
    "HeaderGrilla": 36,
    "ItemSidebar": 32
  },
  "anchos": {
    "Sidebar": 240,
    "FormularioMax": 760,
    "CardLogin": 400
  },
  "espaciado": [4, 8, 12, 16, 20, 24, 28, 32, 40, 48],
  "sombraCard": { "x": 0, "y": 1, "desenfoque": 2, "expansion": 0, "color": "#000000", "opacidad": 0.05 },
  "tipografia": {
    "TituloPagina": { "tamano": 22, "peso": 600, "altoLinea": 26 },
    "TituloSeccion": { "tamano": 15, "peso": 600 },
    "Marca": { "tamano": 15, "peso": 700 },
    "ValorMetrica": { "tamano": 20, "peso": 600 },
    "TituloLogin": { "tamano": 20, "peso": 600 },
    "Cuerpo": { "tamano": 14, "peso": 400 },
    "Control": { "tamano": 13, "peso": 400 },
    "Boton": { "tamano": 13, "peso": 600 },
    "Enlace": { "tamano": 13, "peso": 500 },
    "SubtituloFormulario": { "tamano": 13, "peso": 600 },
    "TituloAcceso": { "tamano": 13, "peso": 600 },
    "EtiquetaCampo": { "tamano": 12, "peso": 500 },
    "EtiquetaMetrica": { "tamano": 12, "peso": 500 },
    "DescripcionAcceso": { "tamano": 12, "peso": 400 },
    "PieConteo": { "tamano": 12, "peso": 400 },
    "ItemSidebar": { "tamano": 12, "peso": 500 },
    "Insignia": { "tamano": 12, "peso": 500 },
    "ErrorCampo": { "tamano": 12, "peso": 400 },
    "HeaderColumna": { "tamano": 11, "peso": 600, "espaciadoLetras": 0.6, "mayusculas": true },
    "HeaderSeccionSidebar": { "tamano": 11, "peso": 600, "espaciadoLetras": 0.6, "mayusculas": true }
  }
}
```

- [ ] **Step 9: Correr y ver el verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
Expected: PASS, `Failed: 0`.

- [ ] **Step 10: Verificación por mutación del guardián de spec**

Quitar la línea `"Acento": "#1D4ED8",` de `docs/ui-v2/tokens.json` y correr `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~TokensSpecTests"`.
Expected: FAIL en `Color_TieneElValorDeLaSpec(nombre: "Acento", ...)` con `System.InvalidOperationException: Sequence contains no matching element`.
Restaurar re-agregando la línea exacta (el archivo todavía no está commiteado: `git checkout` lo borraría); correr de nuevo: PASS.

- [ ] **Step 11: Commit**

```bash
git add tools/StockApp.UiV2.Herramientas tests/StockApp.UiV2.Herramientas.Tests docs/ui-v2/tokens.json StockApp.sln
git commit -m "feat(ui-v2): agrega tokens.json con los valores de la spec y su lector validado"
```

### Task 2: Generador de `Tokens.axaml` y `tokens.css` con test de sincronía

**Files:**
- Create: `tools/StockApp.UiV2.Herramientas/Tokens/GeneradorTokens.cs`
- Modify: `tools/StockApp.UiV2.Herramientas/Cli.cs`
- Create (generados): `src/StockApp.Presentation/Themes/V2/Tokens.axaml`, `docs/ui-v2/mockups/_base/tokens.css`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/GeneradorTokensTests.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/ArchivosGeneradosSincronizadosTests.cs`

**Interfaces:**
- Consumes: `LectorTokens.LeerArchivo`, `DefinicionTokens`, `RutasRepo.TokensJson/TokensAxaml/TokensCss`.
- Produces:
  - `GeneradorTokens.GenerarAxaml(DefinicionTokens) : string`, `GeneradorTokens.GenerarCss(DefinicionTokens) : string`, `internal GeneradorTokens.AKebab(string) : string`, `internal GeneradorTokens.AlfaDe(double) : byte`.
  - `internal Cli.Normalizar(string) : string` (CRLF → LF).
  - Claves de recurso (las usan TODAS las tasks siguientes): `V2Color{Nombre}` (`Color`), `V2{Nombre}Brush` (`SolidColorBrush`), `V2Radio{Nombre}` (`CornerRadius`), `V2Alto{Nombre}`, `V2Ancho{Nombre}`, `V2Espacio{n}` (`x:Double`), `V2SombraCard` (`BoxShadows`), `V2Fuente{Rol}Tamano` (`x:Double`), `V2Fuente{Rol}Peso` (`FontWeight`), `V2Fuente{Rol}AltoLinea`, `V2Fuente{Rol}EspaciadoLetras` (`x:Double`, solo si el rol lo define).
  - Variables CSS: `--v2-color-{kebab}`, `--v2-radio-{kebab}`, `--v2-alto-{kebab}`, `--v2-ancho-{kebab}`, `--v2-espacio-{n}`, `--v2-sombra-card`, `--v2-fuente-{kebab}-tamano|peso|alto-linea|espaciado-letras|transformacion`.

- [ ] **Step 1: Escribir los tests del generador (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/GeneradorTokensTests.cs`:

```csharp
using System.Xml.Linq;
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

public class GeneradorTokensTests
{
    private static readonly DefinicionTokens Prueba = LectorTokens.Leer(JsonTokensDePrueba.Valido);

    [Fact]
    public void GenerarAxaml_Color_EmiteColorYBrushConPrefijo()
    {
        var axaml = GeneradorTokens.GenerarAxaml(Prueba);

        Assert.Contains("<Color x:Key=\"V2ColorAcento\">#1D4ED8</Color>", axaml);
        Assert.Contains("<SolidColorBrush x:Key=\"V2AcentoBrush\" Color=\"#1D4ED8\" />", axaml);
        Assert.Contains("<SolidColorBrush x:Key=\"V2TextoSobreAcentoBrush\" Color=\"#FFFFFF\" />", axaml);
    }

    [Fact]
    public void GenerarAxaml_MedidasYTipografia_EmitenElTipoCorrecto()
    {
        var axaml = GeneradorTokens.GenerarAxaml(Prueba);

        Assert.Contains("<CornerRadius x:Key=\"V2RadioControl\">8</CornerRadius>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2AltoControl\">36</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2AnchoSidebar\">240</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2Espacio8\">8</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2FuenteTituloPaginaTamano\">22</x:Double>", axaml);
        Assert.Contains("<FontWeight x:Key=\"V2FuenteTituloPaginaPeso\">600</FontWeight>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2FuenteTituloPaginaAltoLinea\">26</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2FuenteHeaderColumnaEspaciadoLetras\">0.6</x:Double>", axaml);
        Assert.DoesNotContain("V2FuenteHeaderColumnaAltoLinea", axaml);
    }

    [Fact]
    public void Sombra_CuantizaElAlfaIgualEnAxamlYEnCss()
    {
        // 0.05 * 255 = 12.75 -> 13 = 0x0D. El CSS usa 13/255 = 0.051 para que Chromium y
        // Skia pinten EXACTAMENTE el mismo alfa, no dos redondeos distintos de 0.05.
        Assert.Contains("<BoxShadows x:Key=\"V2SombraCard\">0 1 2 0 #0D000000</BoxShadows>", GeneradorTokens.GenerarAxaml(Prueba));
        Assert.Contains("--v2-sombra-card: 0px 1px 2px 0px rgba(0, 0, 0, 0.051);", GeneradorTokens.GenerarCss(Prueba));
    }

    [Fact]
    public void GenerarCss_UsaKebabCaseYUnidades()
    {
        var css = GeneradorTokens.GenerarCss(Prueba);

        Assert.Contains("--v2-color-texto-sobre-acento: #FFFFFF;", css);
        Assert.Contains("--v2-radio-control: 8px;", css);
        Assert.Contains("--v2-espacio-4: 4px;", css);
        Assert.Contains("--v2-fuente-titulo-pagina-alto-linea: 26px;", css);
        Assert.Contains("--v2-fuente-header-columna-espaciado-letras: 0.6px;", css);
        Assert.Contains("--v2-fuente-header-columna-transformacion: uppercase;", css);
        Assert.Contains("--v2-fuente-titulo-pagina-peso: 600;", css);
        // Sin altoLinea en el token: ceil(11 × 1.20996) = ceil(13.31) = 14, lo que mide Avalonia.
        Assert.Contains("--v2-fuente-header-columna-alto-linea: 14px;", css);
    }

    [Theory]
    [InlineData("HeaderGrilla", "header-grilla")]
    [InlineData("V2", "v2")]
    [InlineData("InsigniaExitoFondo", "insignia-exito-fondo")]
    public void AKebab_SeparaPalabras(string pascal, string kebab) =>
        Assert.Equal(kebab, GeneradorTokens.AKebab(pascal));

    [Fact]
    public void SinPrefijo_LasClavesQuedanSinV2()
    {
        // Fase 9: vaciar "prefijo" en tokens.json convierte los tokens v2 en los únicos.
        var sinPrefijo = Prueba with { Prefijo = "" };

        Assert.Contains("x:Key=\"AcentoBrush\"", GeneradorTokens.GenerarAxaml(sinPrefijo));
        Assert.Contains("--color-acento: #1D4ED8;", GeneradorTokens.GenerarCss(sinPrefijo));
    }

    [Fact]
    public void TokensReales_GeneranXmlBienFormadoSinDobleGuion()
    {
        // "--" dentro de un comentario XAML rompe el build con AVLN1001.
        var axaml = GeneradorTokens.GenerarAxaml(LectorTokens.LeerArchivo(RutasRepo.TokensJson));

        Assert.DoesNotContain("--", axaml);
        var raiz = XDocument.Parse(axaml).Root!;
        Assert.Equal("ResourceDictionary", raiz.Name.LocalName);
    }
}
```

`tests/StockApp.UiV2.Herramientas.Tests/ArchivosGeneradosSincronizadosTests.cs`:

```csharp
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Los archivos generados se commitean (el build de Avalonia y los mockups los necesitan sin
/// correr nada). Este test es la red contra la "segunda copia a mano" que la spec §3.2 prohíbe:
/// si alguien edita Tokens.axaml o tokens.css directamente, o cambia tokens.json sin regenerar,
/// falla acá.
/// </summary>
public class ArchivosGeneradosSincronizadosTests
{
    private static readonly DefinicionTokens Tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);

    [Fact]
    public void TokensAxaml_CoincideConLoQueGeneraTokensJson() =>
        Assert.Equal(GeneradorTokens.GenerarAxaml(Tokens), Cli.Normalizar(File.ReadAllText(RutasRepo.TokensAxaml)));

    [Fact]
    public void TokensCss_CoincideConLoQueGeneraTokensJson() =>
        Assert.Equal(GeneradorTokens.GenerarCss(Tokens), Cli.Normalizar(File.ReadAllText(RutasRepo.TokensCss)));

    [Fact]
    public void ComandoVerificar_DevuelveCero()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, Cli.Ejecutar(["tokens", "--verificar"], salida, error));
        Assert.Contains("al día", salida.ToString());
    }

    [Fact]
    public void Normalizar_AceptaFinDeLineaDeWindows() =>
        Assert.Equal("a\nb\n", Cli.Normalizar("a\r\nb\r\n"));
}
```

- [ ] **Step 2: Correr y ver el rojo**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~GeneradorTokensTests|FullyQualifiedName~ArchivosGeneradosSincronizadosTests"`
Expected: FAIL de compilación, `The name 'GeneradorTokens' does not exist in the current context` y `'Cli' does not contain a definition for 'Normalizar'`.

- [ ] **Step 3: Implementar el generador**

`tools/StockApp.UiV2.Herramientas/Tokens/GeneradorTokens.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>
/// Genera las dos salidas de tokens.json (spec §3.2): recursos de Avalonia y variables CSS de
/// los mockups. Salida determinista: mismo JSON, mismos bytes (LF, sin BOM, orden del JSON).
/// Ningún texto emitido puede contener "--" dentro del .axaml (AVLN1001).
/// </summary>
public static partial class GeneradorTokens
{
    private const string EncabezadoAxaml =
        "<!-- ARCHIVO GENERADO. No editar a mano.\n" +
        "     Fuente: docs/ui-v2/tokens.json. Se regenera con el comando \"tokens\" de\n" +
        "     tools/StockApp.UiV2.Herramientas (ver docs/ui-v2/herramientas.md). -->\n";

    private const string EncabezadoCss =
        "/* ARCHIVO GENERADO. No editar a mano. Fuente: docs/ui-v2/tokens.json\n" +
        "   (comando \"tokens\" de tools/StockApp.UiV2.Herramientas). */\n";

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex LimiteDePalabra();

    internal static string AKebab(string pascal) => LimiteDePalabra().Replace(pascal, "-$1").ToLowerInvariant();

    internal static byte AlfaDe(double opacidad) =>
        (byte)Math.Round(opacidad * 255, MidpointRounding.AwayFromZero);

    private static string N(double valor) => valor.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Alto de línea que el mockup debe usar para un rol: el fijado por el token o, si no hay,
    /// el natural de Avalonia (alto de la fuente redondeado hacia arriba por el layout rounding).
    /// En el .axaml no se emite: Avalonia lo calcula solo.
    /// </summary>
    public static double AltoLineaCss(EstiloTipografico estilo, double factorAltoLinea) =>
        estilo.AltoLinea ?? Math.Ceiling(estilo.Tamano * factorAltoLinea);

    public static string GenerarAxaml(DefinicionTokens d)
    {
        var p = d.Prefijo;
        var sb = new StringBuilder();
        sb.Append(EncabezadoAxaml);
        sb.Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n");
        sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n");

        sb.Append("\n    <!-- Colores -->\n");
        foreach (var (nombre, hex) in d.Colores)
        {
            sb.Append($"    <Color x:Key=\"{p}Color{nombre}\">{hex}</Color>\n");
        }

        sb.Append("\n    <!-- Brushes: lo que referencian las vistas y los ControlTheme -->\n");
        foreach (var (nombre, hex) in d.Colores)
        {
            sb.Append($"    <SolidColorBrush x:Key=\"{p}{nombre}Brush\" Color=\"{hex}\" />\n");
        }

        sb.Append("\n    <!-- Radios -->\n");
        foreach (var (nombre, valor) in d.Radios)
        {
            sb.Append($"    <CornerRadius x:Key=\"{p}Radio{nombre}\">{N(valor)}</CornerRadius>\n");
        }

        sb.Append("\n    <!-- Alturas -->\n");
        foreach (var (nombre, valor) in d.Alturas)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Alto{nombre}\">{N(valor)}</x:Double>\n");
        }

        sb.Append("\n    <!-- Anchos -->\n");
        foreach (var (nombre, valor) in d.Anchos)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Ancho{nombre}\">{N(valor)}</x:Double>\n");
        }

        sb.Append("\n    <!-- Espaciado, base 4 -->\n");
        foreach (var valor in d.Espaciado)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Espacio{N(valor)}\">{N(valor)}</x:Double>\n");
        }

        var s = d.SombraCard;
        sb.Append("\n    <!-- Sombra: la única del sistema -->\n");
        sb.Append($"    <BoxShadows x:Key=\"{p}SombraCard\">{N(s.X)} {N(s.Y)} {N(s.Desenfoque)} {N(s.Expansion)} ");
        sb.Append($"#{AlfaDe(s.Opacidad):X2}{s.Color[1..]}</BoxShadows>\n");

        sb.Append("\n    <!-- Tipografía -->\n");
        foreach (var (nombre, t) in d.Tipografia)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Fuente{nombre}Tamano\">{N(t.Tamano)}</x:Double>\n");
            sb.Append($"    <FontWeight x:Key=\"{p}Fuente{nombre}Peso\">{t.Peso}</FontWeight>\n");
            if (t.AltoLinea is { } alto)
            {
                sb.Append($"    <x:Double x:Key=\"{p}Fuente{nombre}AltoLinea\">{N(alto)}</x:Double>\n");
            }

            if (t.EspaciadoLetras is { } espaciado)
            {
                sb.Append($"    <x:Double x:Key=\"{p}Fuente{nombre}EspaciadoLetras\">{N(espaciado)}</x:Double>\n");
            }
        }

        sb.Append("\n</ResourceDictionary>\n");
        return sb.ToString();
    }

    public static string GenerarCss(DefinicionTokens d)
    {
        var p = d.Prefijo.Length == 0 ? "--" : $"--{AKebab(d.Prefijo)}-";
        var sb = new StringBuilder();
        sb.Append(EncabezadoCss);
        sb.Append(":root {\n");

        foreach (var (nombre, hex) in d.Colores)
        {
            sb.Append($"  {p}color-{AKebab(nombre)}: {hex};\n");
        }

        foreach (var (nombre, valor) in d.Radios)
        {
            sb.Append($"  {p}radio-{AKebab(nombre)}: {N(valor)}px;\n");
        }

        foreach (var (nombre, valor) in d.Alturas)
        {
            sb.Append($"  {p}alto-{AKebab(nombre)}: {N(valor)}px;\n");
        }

        foreach (var (nombre, valor) in d.Anchos)
        {
            sb.Append($"  {p}ancho-{AKebab(nombre)}: {N(valor)}px;\n");
        }

        foreach (var valor in d.Espaciado)
        {
            sb.Append($"  {p}espacio-{N(valor)}: {N(valor)}px;\n");
        }

        var s = d.SombraCard;
        var r = Convert.ToInt32(s.Color[1..3], 16);
        var g = Convert.ToInt32(s.Color[3..5], 16);
        var b = Convert.ToInt32(s.Color[5..7], 16);
        var alfa = (AlfaDe(s.Opacidad) / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
        sb.Append($"  {p}sombra-card: {N(s.X)}px {N(s.Y)}px {N(s.Desenfoque)}px {N(s.Expansion)}px rgba({r}, {g}, {b}, {alfa});\n");

        foreach (var (nombre, t) in d.Tipografia)
        {
            var k = $"{p}fuente-{AKebab(nombre)}";
            sb.Append($"  {k}-tamano: {N(t.Tamano)}px;\n");
            sb.Append($"  {k}-peso: {t.Peso};\n");
            sb.Append($"  {k}-alto-linea: {N(AltoLineaCss(t, d.FactorAltoLinea))}px;\n");

            if (t.EspaciadoLetras is { } espaciado)
            {
                sb.Append($"  {k}-espaciado-letras: {N(espaciado)}px;\n");
            }

            if (t.Mayusculas)
            {
                sb.Append($"  {k}-transformacion: uppercase;\n");
            }
        }

        sb.Append("}\n");
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Agregar el comando `tokens` a la CLI**

En `tools/StockApp.UiV2.Herramientas/Cli.cs`, agregar `using StockApp.UiV2.Herramientas.Tokens;` arriba y reemplazar el método `Ejecutar` por:

```csharp
    public static int Ejecutar(string[] args, TextWriter salida, TextWriter error)
    {
        try
        {
            switch (args)
            {
                case ["tokens"]:
                    return ComandoTokens(verificar: false, salida, error);
                case ["tokens", "--verificar"]:
                    return ComandoTokens(verificar: true, salida, error);
                default:
                    error.WriteLine(Uso);
                    return 2;
            }
        }
        catch (TokensInvalidosException ex)
        {
            error.WriteLine($"tokens.json inválido: {ex.Message}");
            return 1;
        }
    }

    internal static string Normalizar(string texto) => texto.Replace("\r\n", "\n");

    private static int ComandoTokens(bool verificar, TextWriter salida, TextWriter error)
    {
        var definicion = LectorTokens.LeerArchivo(RutasRepo.TokensJson);
        var esperados = new[]
        {
            (Ruta: RutasRepo.TokensAxaml, Contenido: GeneradorTokens.GenerarAxaml(definicion)),
            (Ruta: RutasRepo.TokensCss, Contenido: GeneradorTokens.GenerarCss(definicion)),
        };

        if (verificar)
        {
            var desactualizados = esperados
                .Where(e => !File.Exists(e.Ruta) || Normalizar(File.ReadAllText(e.Ruta)) != e.Contenido)
                .Select(e => Path.GetRelativePath(RutasRepo.Raiz, e.Ruta))
                .ToList();
            foreach (var ruta in desactualizados)
            {
                error.WriteLine($"Desactualizado: {ruta}. Correr el comando tokens.");
            }

            if (desactualizados.Count == 0)
            {
                salida.WriteLine("Tokens generados al día.");
            }

            return desactualizados.Count == 0 ? 0 : 1;
        }

        foreach (var (ruta, contenido) in esperados)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, contenido);
            salida.WriteLine($"Escrito: {Path.GetRelativePath(RutasRepo.Raiz, ruta)}");
        }

        return 0;
    }
```

- [ ] **Step 5: Generar los archivos**

Run: `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens`
Expected:
```
Escrito: src/StockApp.Presentation/Themes/V2/Tokens.axaml
Escrito: docs/ui-v2/mockups/_base/tokens.css
```
Verificar a ojo que `Tokens.axaml` tiene `<SolidColorBrush x:Key="V2AcentoBrush" Color="#1D4ED8" />` y `<BoxShadows x:Key="V2SombraCard">0 1 2 0 #0D000000</BoxShadows>`.

- [ ] **Step 6: Correr y ver el verde (incluye que el `.axaml` generado compila)**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
Expected: PASS, `Failed: 0`.
Run: `dotnet build StockApp.sln`
Expected: `Build succeeded.` con `0 Error(s)` (el `Tokens.axaml` generado lo compila el XAML compiler de Avalonia aunque todavía nadie lo incluya).

- [ ] **Step 7: Verificación por mutación del test de sincronía**

Quitar de `src/StockApp.Presentation/Themes/V2/Tokens.axaml` la línea `<SolidColorBrush x:Key="V2AcentoBrush" Color="#1D4ED8" />`. Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~ArchivosGeneradosSincronizadosTests"`.
Expected: FAIL en `TokensAxaml_CoincideConLoQueGeneraTokensJson` y `ComandoVerificar_DevuelveCero`.
Restaurar con `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens`; correr de nuevo: PASS.

- [ ] **Step 8: Commit**

```bash
git add tools/StockApp.UiV2.Herramientas tests/StockApp.UiV2.Herramientas.Tests src/StockApp.Presentation/Themes/V2/Tokens.axaml docs/ui-v2/mockups/_base/tokens.css
git commit -m "feat(ui-v2): genera Tokens.axaml y tokens.css desde tokens.json con test de sincronía"
```

### Task 3: Cargar los tokens v2 en la app y en el banco de UI, con guardián de includes

**Files:**
- Modify: `src/StockApp.Presentation/App.axaml` (bloque `ResourceDictionary.MergedDictionaries`)
- Modify: `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` (constructor de `TestApp`)
- Create: `tests/StockApp.Presentation.UiTests/V2/RaizRepo.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/IncludesDeAppAxaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/IncludesAppAxamlTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/TokensV2Tests.cs`

**Interfaces:**
- Consumes: claves `V2*` de la Task 2.
- Produces:
  - `TestApp.ResourceIncludes : string[]` y `TestApp.StyleIncludes : string[]` (públicos, estáticos). Las Tasks 10, 11 y 16 agregan entradas.
  - `IncludesDeAppAxaml.Leer(string appAxaml) : (string[] Recursos, string[] Estilos)` (UiTests).
  - `RaizRepo.Ruta : string` (UiTests).

- [ ] **Step 1: Escribir el guardián de includes y los tests de tokens (rojo)**

`tests/StockApp.Presentation.UiTests/V2/RaizRepo.cs`:

```csharp
using System.IO;
using System.Runtime.CompilerServices;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Raíz del repo resuelta desde la ubicación de este archivo fuente (StockApp.sln).</summary>
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
```

`tests/StockApp.Presentation.UiTests/V2/IncludesDeAppAxaml.cs` (copia deliberada de la de `VisualTests`, Task 8: los dos proyectos de test no se referencian entre sí):

```csharp
using System.Linq;
using System.Xml.Linq;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Lee de App.axaml, en orden de documento, los Source de ResourceInclude y StyleInclude.</summary>
internal static class IncludesDeAppAxaml
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    public static (string[] Recursos, string[] Estilos) Leer(string appAxaml)
    {
        var documento = XDocument.Parse(appAxaml);
        var recursos = documento.Descendants(Avalonia + "ResourceInclude")
            .Select(e => (string)e.Attribute("Source")!).ToArray();
        var estilos = documento.Descendants(Avalonia + "StyleInclude")
            .Select(e => (string)e.Attribute("Source")!).ToArray();
        return (recursos, estilos);
    }
}
```

`tests/StockApp.Presentation.UiTests/V2/IncludesAppAxamlTests.cs`:

```csharp
using System.IO;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// TestApp replica a mano los includes de App.axaml. Si divergen, TODOS los tests de UI miden
/// una app que no existe: un estilo v2 incluido en producción y olvidado acá pasa en verde.
/// </summary>
public class IncludesAppAxamlTests
{
    [Fact]
    public void TestApp_CargaLosMismosIncludesQueAppAxaml_EnElMismoOrden()
    {
        var appAxaml = File.ReadAllText(Path.Combine(RaizRepo.Ruta, "src", "StockApp.Presentation", "App.axaml"));

        var (recursos, estilos) = IncludesDeAppAxaml.Leer(appAxaml);

        Assert.Equal(recursos, TestApp.ResourceIncludes);
        Assert.Equal(estilos, TestApp.StyleIncludes);
    }
}
```

`tests/StockApp.Presentation.UiTests/V2/TokensV2Tests.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Los tokens v2 se resuelven desde la app (DynamicResource falla en silencio si la clave no
/// existe) y conviven con los viejos sin pisarlos.
/// </summary>
public class TokensV2Tests
{
    private static object Recurso(string clave)
    {
        Assert.True(
            Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token '{clave}' no se resuelve: Themes/V2/Tokens.axaml no está incluido o la clave cambió.");
        return valor!;
    }

    [AvaloniaTheory]
    [InlineData("V2FondoBrush", "#F7F7F5")]
    [InlineData("V2AcentoBrush", "#1D4ED8")]
    [InlineData("V2BordeInputBrush", "#D6D3D1")]
    [InlineData("V2InsigniaExitoTextoBrush", "#14532D")]
    [InlineData("V2AlertaInfoTextoBrush", "#1E3A8A")]
    public void BrushV2_SeResuelveConElValorDeLaSpec(string clave, string hex) =>
        Assert.Equal(Color.Parse(hex), Assert.IsAssignableFrom<ISolidColorBrush>(Recurso(clave)).Color);

    [AvaloniaFact]
    public void MedidasV2_SeResuelvenConSuTipo()
    {
        Assert.Equal(36.0, Assert.IsType<double>(Recurso("V2AltoControl")));
        Assert.Equal(new CornerRadius(10), Assert.IsType<CornerRadius>(Recurso("V2RadioCard")));
        Assert.Equal(BoxShadows.Parse("0 1 2 0 #0D000000"), Assert.IsType<BoxShadows>(Recurso("V2SombraCard")));
        Assert.Equal(FontWeight.SemiBold, Assert.IsType<FontWeight>(Recurso("V2FuenteTituloPaginaPeso")));
        Assert.Equal(26.0, Assert.IsType<double>(Recurso("V2FuenteTituloPaginaAltoLinea")));
    }

    [AvaloniaTheory]
    [InlineData("BrandBrush", "#16A34A")]
    [InlineData("FondoBrush", "#F8FAFC")]
    [InlineData("BordeBrush", "#E2E8F0")]
    public void TokensViejos_SiguenIntactos(string clave, string hex) =>
        Assert.Equal(Color.Parse(hex), Assert.IsAssignableFrom<ISolidColorBrush>(Recurso(clave)).Color);
}
```

- [ ] **Step 2: Correr y ver el rojo**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~V2."`
Expected: FAIL de compilación, `'TestApp' does not contain a definition for 'ResourceIncludes'`.

- [ ] **Step 3: Incluir los tokens v2 en `App.axaml`**

En `src/StockApp.Presentation/App.axaml`, reemplazar:

```xml
                <ResourceInclude Source="avares://GestionMunicipal/Themes/Tokens.axaml"/>
                <ResourceInclude Source="avares://GestionMunicipal/Controls/Componentes.axaml"/>
```

por:

```xml
                <ResourceInclude Source="avares://GestionMunicipal/Themes/Tokens.axaml"/>
                <!-- UI v2 (spec 2026-09-28): tokens GENERADOS desde docs/ui-v2/tokens.json, con
                     prefijo V2 para convivir con los de arriba hasta la Fase 9. -->
                <ResourceInclude Source="avares://GestionMunicipal/Themes/V2/Tokens.axaml"/>
                <ResourceInclude Source="avares://GestionMunicipal/Controls/Componentes.axaml"/>
```

- [ ] **Step 4: Pasar los includes de `TestApp` a arrays públicos e incluir los tokens v2**

En `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs`, agregar dentro de la clase `TestApp`, antes del constructor:

```csharp
    /// <summary>
    /// Mismos ResourceInclude que App.axaml, en el mismo orden. Custodiado por
    /// V2/IncludesAppAxamlTests: agregar un include a App.axaml sin agregarlo acá es un rojo.
    /// </summary>
    public static readonly string[] ResourceIncludes =
    [
        "avares://GestionMunicipal/Themes/Tokens.axaml",
        "avares://GestionMunicipal/Themes/V2/Tokens.axaml",
        "avares://GestionMunicipal/Controls/Componentes.axaml",
    ];

    /// <summary>Mismos StyleInclude que App.axaml (después del FluentTheme), mismo orden.</summary>
    public static readonly string[] StyleIncludes =
    [
        "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml",
        "avares://GestionMunicipal/Themes/Typography.axaml",
        "avares://GestionMunicipal/Themes/Controls.axaml",
        "avares://GestionMunicipal/Themes/DataGrid.axaml",
    ];
```

Y en el constructor, reemplazar TODO el bloque desde `Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(` (el primero) hasta el cierre del último `Styles.Add(new StyleInclude(...DataGrid.axaml...))` inclusive, por:

```csharp
        // Mismo orden exacto que App.axaml: recursos primero (tokens viejos, tokens v2,
        // ControlTheme de Controls/), después FluentTheme y los StyleInclude (el Fluent.xaml del
        // DataGrid, el UI Kit viejo y, a partir de la Task 10, los estilos v2 bajo .ui2).
        foreach (var fuente in ResourceIncludes)
        {
            Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
            {
                Source = new Uri(fuente)
            });
        }

        Styles.Add(new FluentTheme());

        foreach (var fuente in StyleIncludes)
        {
            Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
            {
                Source = new Uri(fuente)
            });
        }
```

(`ResourceInclude` y `StyleInclude` ya se resuelven por el `using Avalonia.Markup.Xaml.Styling;` existente del archivo.)

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, `Failed: 0` (los tests existentes siguen verdes: el refactor de `TestApp` no cambia qué se carga, solo agrega los tokens v2).

- [ ] **Step 6: Verificación por mutación**

(a) Quitar `"avares://GestionMunicipal/Themes/V2/Tokens.axaml",` del array `ResourceIncludes` de `TestApp`. Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~V2."`. Expected: FAIL en `IncludesAppAxamlTests` y en `TokensV2Tests.BrushV2_SeResuelveConElValorDeLaSpec`. Restaurar re-agregando esa línea (no usar `git checkout`: borraría el Step 4).
(b) Quitar la línea `<ResourceInclude Source="avares://GestionMunicipal/Themes/V2/Tokens.axaml"/>` de `App.axaml`. Expected: FAIL solo en `IncludesAppAxamlTests`. Restaurar la línea. Correr: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/App.axaml tests/StockApp.Presentation.UiTests/TestAppBuilder.cs tests/StockApp.Presentation.UiTests/V2
git commit -m "feat(ui-v2): carga los tokens v2 en la app y custodia los includes del banco de UI"
```

### Task 4: Inter local, íconos MDI a SVG y CSS base de mockups

**Files:**
- Modify: `Directory.Packages.props` (sin cambios de versión: los paquetes ya están; ver Step 1)
- Modify: `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`
- Create: `tools/StockApp.UiV2.Herramientas/Mockups/ExtractorFuentes.cs`
- Create: `tools/StockApp.UiV2.Herramientas/Mockups/ExportadorIconos.cs`
- Modify: `tools/StockApp.UiV2.Herramientas/Cli.cs`
- Create: `docs/ui-v2/mockups/_base/fuentes/Inter-Regular.ttf`, `Inter-Medium.ttf`, `Inter-SemiBold.ttf`, `Inter-Bold.ttf`, `OFL.txt`
- Create: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/ExtractorFuentesTests.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/ExportadorIconosTests.cs`

**Interfaces:**
- Consumes: `RutasRepo.CarpetaFuentes`, `Cli`.
- Produces:
  - `ExtractorFuentes.Pesos : IReadOnlyList<string>` (`Regular`, `Medium`, `SemiBold`, `Bold`), `LeerEmbebida(string peso) : byte[]`, `Extraer(string carpeta) : IReadOnlyList<string>`, `Diferencias(string carpeta) : IReadOnlyList<string>`.
  - `ExportadorIconos.Svg(string clave, int tamano = 16) : string` → `<svg class="v2-icono" ...><path fill="currentColor" d="..."/></svg>`.
  - `base.css`: familia `"InterV2"` (400/500/600/700), clases `.v2-ventana` (1280×760), `.v2-icono`, `.v2-num`. Las Tasks 10 a 16 agregan la clase CSS de cada componente.

- [ ] **Step 1: Referenciar los paquetes que la herramienta necesita**

`Avalonia.Headless`, `Avalonia.Fonts.Inter` y `Optris.Icons.Avalonia.MaterialDesign` ya tienen versión en `Directory.Packages.props` (12.0.5, 12.0.5, 12.0.7): NO se toca ese archivo en esta task. En `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`, agregar antes de `</Project>`:

```xml
  <ItemGroup>
    <!-- AssetLoader de Avalonia necesita una plataforma inicializada para leer los TTF de Inter
         embebidos (avares://Avalonia.Fonts.Inter/Assets/...): se usa el headless sin render. -->
    <PackageReference Include="Avalonia.Headless" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <!-- Mismo proveedor de íconos que la app (prefijo mdi): el SVG del mockup sale del MISMO path. -->
    <PackageReference Include="Optris.Icons.Avalonia.MaterialDesign" />
  </ItemGroup>
```

- [ ] **Step 2: Escribir los tests (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/ExtractorFuentesTests.cs`:

```csharp
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// El mockup y la app tienen que dibujar con la MISMA Inter (3.19, la embebida en
/// Avalonia.Fonts.Inter 12.0.5). Si alguien reemplaza un TTF del repo por otra versión, los
/// anchos de texto cambian y la comparación visual pierde sentido.
/// </summary>
public class ExtractorFuentesTests
{
    [Fact]
    public void LeerEmbebida_DevuelveUnTrueType()
    {
        var bytes = ExtractorFuentes.LeerEmbebida("Regular");

        Assert.True(bytes.Length > 100_000);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, bytes[..4]);
    }

    [Fact]
    public void Extraer_EscribeLosCuatroPesosIdenticosALosEmbebidos()
    {
        var carpeta = Directory.CreateTempSubdirectory("fuentes-").FullName;

        var escritos = ExtractorFuentes.Extraer(carpeta);

        Assert.Equal(4, escritos.Count);
        Assert.Empty(ExtractorFuentes.Diferencias(carpeta));
    }

    [Fact]
    public void Diferencias_ArchivoFaltante_LoReporta()
    {
        var carpeta = Directory.CreateTempSubdirectory("fuentes-").FullName;
        ExtractorFuentes.Extraer(carpeta);
        File.Delete(Path.Combine(carpeta, "Inter-Bold.ttf"));

        var diferencias = ExtractorFuentes.Diferencias(carpeta);

        Assert.Equal("Inter-Bold.ttf", Path.GetFileName(Assert.Single(diferencias)));
    }

    [Fact]
    public void FuentesDelRepo_SonByteIdenticasALasEmbebidas() =>
        Assert.Empty(ExtractorFuentes.Diferencias(RutasRepo.CarpetaFuentes));
}
```

`tests/StockApp.UiV2.Herramientas.Tests/ExportadorIconosTests.cs`:

```csharp
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

public class ExportadorIconosTests
{
    [Fact]
    public void Svg_IconoExistente_UsaElPathYElViewBoxDelProveedorDeLaApp()
    {
        var svg = ExportadorIconos.Svg("mdi-package-variant");

        Assert.StartsWith("<svg class=\"v2-icono\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\"", svg);
        Assert.Contains("<path fill=\"currentColor\" d=\"M2,10.96C1.5,10.68", svg);
    }

    [Fact]
    public void Svg_IconoInexistente_Lanza() =>
        Assert.ThrowsAny<Exception>(() => ExportadorIconos.Svg("mdi-no-existe-este-icono"));

    [Fact]
    public void Cli_IconoSvg_ImprimeElSvg()
    {
        var salida = new StringWriter();

        Assert.Equal(0, Cli.Ejecutar(["icono-svg", "mdi-package-variant"], salida, new StringWriter()));
        Assert.StartsWith("<svg", salida.ToString());
    }
}
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~ExtractorFuentesTests|FullyQualifiedName~ExportadorIconosTests"`
Expected: FAIL de compilación, `The type or namespace name 'Mockups' does not exist in the namespace 'StockApp.UiV2.Herramientas'`.

- [ ] **Step 3: Implementar el extractor y el exportador**

`tools/StockApp.UiV2.Herramientas/Mockups/ExtractorFuentes.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Copia a disco los TTF de Inter que embebe Avalonia.Fonts.Inter (Inter 3.19), para que los
/// mockups carguen EXACTAMENTE la misma fuente que la app, offline y sin Google Fonts.
/// Solo los pesos que usa la spec: 400, 500, 600 y 700.
/// </summary>
public static class ExtractorFuentes
{
    public static readonly IReadOnlyList<string> Pesos = ["Regular", "Medium", "SemiBold", "Bold"];

    private static readonly object Candado = new();
    private static bool _avaloniaIniciado;

    public static byte[] LeerEmbebida(string peso)
    {
        AsegurarAvalonia();
        using var origen = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/Inter-{peso}.ttf"));
        using var memoria = new MemoryStream();
        origen.CopyTo(memoria);
        return memoria.ToArray();
    }

    public static IReadOnlyList<string> Extraer(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        var escritos = new List<string>();
        foreach (var peso in Pesos)
        {
            var ruta = Path.Combine(carpeta, $"Inter-{peso}.ttf");
            File.WriteAllBytes(ruta, LeerEmbebida(peso));
            escritos.Add(ruta);
        }

        return escritos;
    }

    /// <summary>Rutas de los TTF que faltan o difieren byte a byte de los embebidos.</summary>
    public static IReadOnlyList<string> Diferencias(string carpeta) =>
        Pesos
            .Select(peso => (Ruta: Path.Combine(carpeta, $"Inter-{peso}.ttf"), Peso: peso))
            .Where(x => !File.Exists(x.Ruta)
                        || !File.ReadAllBytes(x.Ruta).AsSpan().SequenceEqual(LeerEmbebida(x.Peso)))
            .Select(x => x.Ruta)
            .ToList();

    private static void AsegurarAvalonia()
    {
        lock (Candado)
        {
            if (_avaloniaIniciado || Application.Current is not null)
            {
                _avaloniaIniciado = true;
                return;
            }

            // AssetLoader.Open fuera de una app lanza "no IAssetLoader": se inicializa la
            // plataforma headless sin arrancar ningún lifetime (verificado en consola).
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _avaloniaIniciado = true;
        }
    }
}
```

`tools/StockApp.UiV2.Herramientas/Mockups/ExportadorIconos.cs`:

```csharp
using Optris.Icons.Avalonia.MaterialDesign;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// SVG inline de un ícono MDI a partir del MISMO proveedor que usa la app
/// (MaterialDesignIconProvider de Optris.Icons.Avalonia): mismo path, mismo viewBox.
/// </summary>
public static class ExportadorIconos
{
    public static string Svg(string clave, int tamano = 16)
    {
        var icono = new MaterialDesignIconProvider().GetIcon(clave);
        var caja = icono.ViewBox;
        return $"<svg class=\"v2-icono\" width=\"{tamano}\" height=\"{tamano}\" "
             + $"viewBox=\"{caja.X} {caja.Y} {caja.Width} {caja.Height}\" aria-hidden=\"true\">"
             + $"<path fill=\"currentColor\" d=\"{(string)icono.Path}\"/></svg>";
    }
}
```

- [ ] **Step 4: Agregar los comandos `fuentes` e `icono-svg`**

En `Cli.cs`, agregar `using StockApp.UiV2.Herramientas.Mockups;` y, dentro del `switch` de `Ejecutar`, antes de `default:`:

```csharp
                case ["fuentes"]:
                    foreach (var ruta in ExtractorFuentes.Extraer(RutasRepo.CarpetaFuentes))
                    {
                        salida.WriteLine($"Escrito: {Path.GetRelativePath(RutasRepo.Raiz, ruta)}");
                    }

                    return 0;
                case ["fuentes", "--verificar"]:
                {
                    var diferencias = ExtractorFuentes.Diferencias(RutasRepo.CarpetaFuentes);
                    foreach (var ruta in diferencias)
                    {
                        error.WriteLine($"Distinta de la embebida: {Path.GetRelativePath(RutasRepo.Raiz, ruta)}");
                    }

                    return diferencias.Count == 0 ? 0 : 1;
                }

                case ["icono-svg", var clave]:
                    salida.WriteLine(ExportadorIconos.Svg(clave));
                    return 0;
```

- [ ] **Step 5: Extraer las fuentes al repo y bajar la licencia OFL**

Run:

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes
curl -sSfL https://raw.githubusercontent.com/rsms/inter/v3.19/LICENSE.txt -o docs/ui-v2/mockups/_base/fuentes/OFL.txt
head -1 docs/ui-v2/mockups/_base/fuentes/OFL.txt
```

Expected: cuatro líneas `Escrito: docs/ui-v2/mockups/_base/fuentes/Inter-*.ttf` y luego `Copyright (c) 2016-2020 The Inter Project Authors.`

- [ ] **Step 6: Escribir `docs/ui-v2/mockups/_base/base.css`**

```css
/* Base de los mockups de la UI v2. Consume las variables de tokens.css (GENERADO).
   Cada componente de src/StockApp.Presentation/Controls/V2 tiene acá su clase v2-*
   con la MISMA geometría que su ControlTheme: si cambia uno, cambia el otro en el mismo commit.
   Solo técnicas que Avalonia replica 1:1 (spec §6.1): colores sólidos, bordes de 1px, radios,
   una única sombra, Inter local, px enteros. Prohibido: gradientes, blur, transiciones,
   transparencias apiladas, text-shadow, pseudo-elementos decorativos.
   Alto de línea: SIEMPRE var(--v2-fuente-<rol>-alto-linea), nunca "normal". tokens.css lo trae
   calculado como el alto que mide un TextBlock de Avalonia (ceil del alto natural de Inter) o el
   que fija el token (título de página 26). */

@font-face { font-family: "InterV2"; src: url("fuentes/Inter-Regular.ttf") format("truetype"); font-weight: 400; }
@font-face { font-family: "InterV2"; src: url("fuentes/Inter-Medium.ttf") format("truetype"); font-weight: 500; }
@font-face { font-family: "InterV2"; src: url("fuentes/Inter-SemiBold.ttf") format("truetype"); font-weight: 600; }
@font-face { font-family: "InterV2"; src: url("fuentes/Inter-Bold.ttf") format("truetype"); font-weight: 700; }

* { box-sizing: border-box; }

html, body {
  margin: 0;
  padding: 0;
  overflow: hidden;
  background: var(--v2-color-fondo);
}

body {
  font-family: "InterV2";
  font-size: var(--v2-fuente-cuerpo-tamano);
  line-height: var(--v2-fuente-cuerpo-alto-linea);
  font-weight: 400;
  color: var(--v2-color-texto-primario);
}

/* La ventana de la app: todo mockup va adentro de un .v2-ventana. */
.v2-ventana {
  position: relative;
  width: 1280px;
  height: 760px;
  overflow: hidden;
  background: var(--v2-color-fondo);
}

.v2-icono { display: block; flex: 0 0 auto; }

/* Columna numérica: derecha + cifras tabulares (spec §2.3). */
.v2-num { text-align: right; font-variant-numeric: tabular-nums; }
```

- [ ] **Step 7: Correr y ver el verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
Expected: PASS, `Failed: 0`.

- [ ] **Step 8: Verificación por mutación del guardián de fuentes**

Quitar `docs/ui-v2/mockups/_base/fuentes/Inter-SemiBold.ttf` (`rm`). Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~FuentesDelRepo"`. Expected: FAIL, la colección no está vacía (`Inter-SemiBold.ttf`). Restaurar con `dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes`; correr: PASS.

- [ ] **Step 9: Commit**

```bash
git add tools/StockApp.UiV2.Herramientas tests/StockApp.UiV2.Herramientas.Tests docs/ui-v2/mockups/_base
git commit -m "feat(ui-v2): agrega Inter local, exportador de íconos MDI y CSS base de mockups"
```

## Bloque B — Herramienta de comparación visual

### Task 5: Comparador de PNG con SkiaSharp (porcentaje global + teselas densas)

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`
- Create: `tools/StockApp.UiV2.Herramientas/Imagenes/ResultadoComparacion.cs`
- Create: `tools/StockApp.UiV2.Herramientas/Imagenes/ComparadorImagenes.cs`
- Modify: `tools/StockApp.UiV2.Herramientas/Cli.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/PngDePrueba.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/ComparadorImagenesTests.cs`

**Interfaces:**
- Consumes: `Cli`.
- Produces:
  - `record ResultadoComparacion(int Ancho, int Alto, int PixelesDistintos, int TeselasDensas)` con `double PorcentajeDistinto` (0 a 100).
  - `ComparadorImagenes.Comparar(string rutaReferencia, string rutaCandidata, string rutaDiff, int toleranciaCanal = ToleranciaCanalPorDefecto) : ResultadoComparacion`.
  - Constantes `ComparadorImagenes.ToleranciaCanalPorDefecto = 32`, `TamanoTesela = 16`, `DensidadTeselaMinima = 0.25`, `ToleranciaColor = 4`, `DensidadColorMinima = 0.9`.
  - Una tesela de 16×16 es "densa" si (a) al menos 25% de sus píxeles difieren fuerte (delta de canal > 32: algo se movió) o (b) al menos 90% difieren apenas (delta > 4: un relleno de color cambió; los colores pálidos de la paleta difieren entre sí en menos de 32, p. ej. `#FEE2E2` vs `#F5F5F4`). El antialiasing no llena teselas: solo toca bordes de glifos.
  - Excepciones: `ArgumentException` si los tamaños difieren; `InvalidOperationException` si una imagen es uniforme (captura vacía).

- [ ] **Step 1: Agregar Avalonia.Skia a la gestión central y a la herramienta**

En `Directory.Packages.props`, dentro del bloque `<!-- Avalonia -->`, después de `Avalonia.Markup.Xaml.Loader`, agregar:

```xml
    <!-- UI v2: render headless con métricas reales (VisualTests) y SkiaSharp para el diff de PNG
         de la herramienta. Trae SkiaSharp 3.119.4 + nativos de Linux alineados con Avalonia: no se
         referencia SkiaSharp directo para no desalinear su versión de la de Avalonia. -->
    <PackageVersion Include="Avalonia.Skia" Version="12.0.5" />
```

En `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`, dentro del `ItemGroup` de paquetes:

```xml
    <!-- SkiaSharp (MIT) para leer y comparar PNG. NO ImageSharp: la Six Labors Split License
         exige licencia comercial a organizaciones que no son open source, ni non-profit, ni
         empresas de menos de 1M USD; un municipio no entra en esas categorías. -->
    <PackageReference Include="Avalonia.Skia" />
```

- [ ] **Step 2: Escribir los tests (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/PngDePrueba.cs`:

```csharp
using SkiaSharp;

namespace StockApp.UiV2.Herramientas.Tests;

internal static class PngDePrueba
{
    /// <summary>
    /// PNG de ancho x alto con fondo liso, un píxel negro fijo en (0,0) para que nunca sea
    /// uniforme, y los rectángulos pedidos.
    /// </summary>
    public static string Crear(string carpeta, string nombre, int ancho, int alto, SKColor fondo,
        params (int X, int Y, int Ancho, int Alto, SKColor Color)[] rectangulos)
    {
        using var bitmap = new SKBitmap(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var lienzo = new SKCanvas(bitmap))
        {
            lienzo.Clear(fondo);
            foreach (var r in rectangulos)
            {
                using var pintura = new SKPaint { Color = r.Color, IsAntialias = false };
                lienzo.DrawRect(r.X, r.Y, r.Ancho, r.Alto, pintura);
            }
        }

        bitmap.SetPixel(0, 0, SKColors.Black);
        return Guardar(bitmap, carpeta, nombre);
    }

    public static string CrearUniforme(string carpeta, string nombre, int ancho, int alto, SKColor color)
    {
        using var bitmap = new SKBitmap(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(color);
        return Guardar(bitmap, carpeta, nombre);
    }

    private static string Guardar(SKBitmap bitmap, string carpeta, string nombre)
    {
        var ruta = Path.Combine(carpeta, nombre);
        using var imagen = SKImage.FromBitmap(bitmap);
        using var datos = imagen.Encode(SKEncodedImageFormat.Png, 100);
        using var archivo = File.Create(ruta);
        datos.SaveTo(archivo);
        return ruta;
    }
}
```

`tests/StockApp.UiV2.Herramientas.Tests/ComparadorImagenesTests.cs`:

```csharp
using SkiaSharp;
using StockApp.UiV2.Herramientas.Imagenes;

namespace StockApp.UiV2.Herramientas.Tests;

public class ComparadorImagenesTests
{
    private static readonly SKColor Fondo = SKColor.Parse("#F7F7F5");
    private static readonly SKColor Acento = SKColor.Parse("#1D4ED8");
    private readonly string _carpeta = Directory.CreateTempSubdirectory("comparador-").FullName;

    private string Diff => Path.Combine(_carpeta, "diff.png");

    [Fact]
    public void ImagenesIdenticas_CeroDistintosYCeroTeselas()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 100, 100, Fondo, (10, 10, 20, 20, Acento));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 100, 100, Fondo, (10, 10, 20, 20, Acento));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(new ResultadoComparacion(100, 100, 0, 0), r);
        Assert.Equal(0, r.PorcentajeDistinto);
    }

    [Fact]
    public void UnPixelDistinto_CuentaUnoYNoFormaTeselaDensa()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 100, 100, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 100, 100, Fondo, (50, 50, 1, 1, Acento));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(1, r.PixelesDistintos);
        Assert.Equal(0.01, r.PorcentajeDistinto, 6);
        Assert.Equal(0, r.TeselasDensas);
    }

    [Theory]
    [InlineData(32, 0)]
    [InlineData(33, 1)]
    public void DeltaDeCanal_SoloCuentaSiSuperaLaTolerancia(byte delta, int esperados)
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 20, 20, new SKColor(100, 100, 100));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 20, 20, new SKColor(100, 100, 100),
            (5, 5, 1, 1, new SKColor((byte)(100 + delta), 100, 100)));

        Assert.Equal(esperados, ComparadorImagenes.Comparar(a, b, Diff).PixelesDistintos);
    }

    [Fact]
    public void BloqueDesplazado8px_MarcaTeselasDensas()
    {
        // Review Focus 3: 200x100 corrido 8px en 1280x760 son 1600 píxeles, 0.16% de la pantalla.
        // El porcentaje global solo no lo ve; las teselas densas sí.
        var a = PngDePrueba.Crear(_carpeta, "a.png", 1280, 760, Fondo, (100, 100, 200, 100, Acento));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 1280, 760, Fondo, (108, 100, 200, 100, Acento));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.True(r.PorcentajeDistinto < 0.2, $"porcentaje {r.PorcentajeDistinto}");
        Assert.True(r.TeselasDensas >= 12, $"teselas densas {r.TeselasDensas}");
    }

    [Fact]
    public void FondoPalidoCambiado_MarcaTeselasPorColorAunqueNingunPixelDifieraFuerte()
    {
        // Insignia Peligro (#FEE2E2) pintada como Neutro (#F5F5F4): delta máximo 19, invisible
        // para la tolerancia de 32. Un bloque de 64x64 alineado a la grilla cubre 16 teselas.
        var a = PngDePrueba.Crear(_carpeta, "a.png", 256, 256, Fondo, (32, 32, 64, 64, SKColor.Parse("#FEE2E2")));
        var b = PngDePrueba.Crear(_carpeta, "b.png", 256, 256, Fondo, (32, 32, 64, 64, SKColor.Parse("#F5F5F4")));

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(0, r.PixelesDistintos);
        Assert.Equal(16, r.TeselasDensas);
    }

    [Fact]
    public void DiferenciasLevesDispersas_NoMarcanTeselas()
    {
        var leves = Enumerable.Range(0, 30)
            .Select(i => (X: 10 + i * 8, Y: 10 + (i % 5) * 40, Ancho: 1, Alto: 1, Color: new SKColor(0xF7, 0xF7, 0xE9)))
            .ToArray();
        var a = PngDePrueba.Crear(_carpeta, "a.png", 256, 256, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 256, 256, Fondo, leves);

        Assert.Equal(0, ComparadorImagenes.Comparar(a, b, Diff).TeselasDensas);
    }

    [Fact]
    public void PixelesSueltosDispersos_NoFormanTeselasDensas()
    {
        var sueltos = Enumerable.Range(0, 40)
            .Select(i => (X: 20 + i * 30, Y: 50 + (i % 7) * 90, Ancho: 1, Alto: 1, Color: Acento))
            .ToArray();
        var a = PngDePrueba.Crear(_carpeta, "a.png", 1280, 760, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 1280, 760, Fondo, sueltos);

        var r = ComparadorImagenes.Comparar(a, b, Diff);

        Assert.Equal(40, r.PixelesDistintos);
        Assert.Equal(0, r.TeselasDensas);
    }

    [Fact]
    public void TamanosDistintos_LanzaNombrandoAmbos()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 1280, 760, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 1600, 950, Fondo);

        var ex = Assert.Throws<ArgumentException>(() => ComparadorImagenes.Comparar(a, b, Diff));

        Assert.Contains("1280x760", ex.Message);
        Assert.Contains("1600x950", ex.Message);
    }

    [Fact]
    public void ImagenUniforme_LanzaPorCapturaVacia()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 50, 50, Fondo);
        var b = PngDePrueba.CrearUniforme(_carpeta, "b.png", 50, 50, Fondo);

        var ex = Assert.Throws<InvalidOperationException>(() => ComparadorImagenes.Comparar(a, b, Diff));

        Assert.Contains("uniforme", ex.Message);
    }

    [Fact]
    public void EscribeDiffConLosDistintosEnRojo()
    {
        var a = PngDePrueba.Crear(_carpeta, "a.png", 40, 40, Fondo);
        var b = PngDePrueba.Crear(_carpeta, "b.png", 40, 40, Fondo, (20, 20, 1, 1, Acento));

        ComparadorImagenes.Comparar(a, b, Diff);

        using var diff = SKBitmap.Decode(Diff);
        Assert.Equal(new SKColor(255, 0, 0), diff.GetPixel(20, 20));
        Assert.NotEqual(new SKColor(255, 0, 0), diff.GetPixel(30, 30));
    }
}
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~ComparadorImagenesTests"`
Expected: FAIL de compilación, `The type or namespace name 'Imagenes' does not exist`.

- [ ] **Step 3: Implementar**

`tools/StockApp.UiV2.Herramientas/Imagenes/ResultadoComparacion.cs`:

```csharp
namespace StockApp.UiV2.Herramientas.Imagenes;

/// <summary>
/// PixelesDistintos: delta de canal mayor a la tolerancia. TeselasDensas: bloques de 16x16 con
/// al menos 25% de píxeles distintos (defectos de layout o color; el antialiasing no los forma).
/// </summary>
public sealed record ResultadoComparacion(int Ancho, int Alto, int PixelesDistintos, int TeselasDensas)
{
    public double PorcentajeDistinto => 100.0 * PixelesDistintos / ((double)Ancho * Alto);
}
```

`tools/StockApp.UiV2.Herramientas/Imagenes/ComparadorImagenes.cs`:

```csharp
using SkiaSharp;

namespace StockApp.UiV2.Herramientas.Imagenes;

/// <summary>
/// Compara dos PNG del mismo tamaño. Un píxel es distinto si algún canal RGB difiere en más de
/// la tolerancia (absorbe el antialiasing de texto entre Chromium y Skia). Además de contar
/// píxeles, agrupa en teselas de 16x16: un defecto real concentra diferencias (algo corrido:
/// muchas fuertes; un relleno de otro color pálido: casi todas leves) y el antialiasing las
/// reparte finas por los bordes de los glifos.
/// La imagen de diferencias pinta la referencia aclarada en gris, las fuertes en rojo, las leves
/// en naranja y el contorno de cada tesela densa en magenta.
/// </summary>
public static class ComparadorImagenes
{
    public const int ToleranciaCanalPorDefecto = 32;
    public const int TamanoTesela = 16;
    public const double DensidadTeselaMinima = 0.25;
    public const int ToleranciaColor = 4;
    public const double DensidadColorMinima = 0.9;

    private static readonly SKColor Rojo = new(255, 0, 0);
    private static readonly SKColor Naranja = new(255, 153, 0);
    private static readonly SKColor Magenta = new(255, 0, 255);

    public static ResultadoComparacion Comparar(
        string rutaReferencia, string rutaCandidata, string rutaDiff, int toleranciaCanal = ToleranciaCanalPorDefecto)
    {
        using var referencia = Decodificar(rutaReferencia);
        using var candidata = Decodificar(rutaCandidata);
        if (referencia.Width != candidata.Width || referencia.Height != candidata.Height)
        {
            throw new ArgumentException(
                $"Tamaños distintos: {Path.GetFileName(rutaReferencia)} es {referencia.Width}x{referencia.Height} "
                + $"y {Path.GetFileName(rutaCandidata)} es {candidata.Width}x{candidata.Height}.");
        }

        ValidarNoUniforme(referencia, rutaReferencia);
        ValidarNoUniforme(candidata, rutaCandidata);

        var ancho = referencia.Width;
        var alto = referencia.Height;
        var distinto = new bool[ancho * alto];
        var distintoLeve = new bool[ancho * alto];
        var total = 0;
        using var diff = new SKBitmap(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Opaque);

        for (var y = 0; y < alto; y++)
        {
            for (var x = 0; x < ancho; x++)
            {
                var a = referencia.GetPixel(x, y);
                var b = candidata.GetPixel(x, y);
                var delta = Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
                if (delta > ToleranciaColor)
                {
                    distintoLeve[(y * ancho) + x] = true;
                }

                if (delta > toleranciaCanal)
                {
                    distinto[(y * ancho) + x] = true;
                    total++;
                    diff.SetPixel(x, y, Rojo);
                }
                else if (delta > ToleranciaColor)
                {
                    diff.SetPixel(x, y, Naranja);
                }
                else
                {
                    var gris = (byte)(191 + (Luminancia(a) / 4));
                    diff.SetPixel(x, y, new SKColor(gris, gris, gris));
                }
            }
        }

        var teselasDensas = 0;
        for (var ty = 0; ty < alto; ty += TamanoTesela)
        {
            for (var tx = 0; tx < ancho; tx += TamanoTesela)
            {
                var w = Math.Min(TamanoTesela, ancho - tx);
                var h = Math.Min(TamanoTesela, alto - ty);
                var fuertes = 0;
                var leves = 0;
                for (var y = ty; y < ty + h; y++)
                {
                    for (var x = tx; x < tx + w; x++)
                    {
                        if (distinto[(y * ancho) + x])
                        {
                            fuertes++;
                        }

                        if (distintoLeve[(y * ancho) + x])
                        {
                            leves++;
                        }
                    }
                }

                if (fuertes >= DensidadTeselaMinima * w * h || leves >= DensidadColorMinima * w * h)
                {
                    teselasDensas++;
                    MarcarContorno(diff, tx, ty, w, h);
                }
            }
        }

        Guardar(diff, rutaDiff);
        return new ResultadoComparacion(ancho, alto, total, teselasDensas);
    }

    private static SKBitmap Decodificar(string ruta) =>
        SKBitmap.Decode(ruta) ?? throw new InvalidOperationException($"No se pudo leer el PNG {ruta}.");

    private static void ValidarNoUniforme(SKBitmap imagen, string ruta)
    {
        var primero = imagen.GetPixel(0, 0);
        for (var y = 0; y < imagen.Height; y++)
        {
            for (var x = 0; x < imagen.Width; x++)
            {
                if (imagen.GetPixel(x, y) != primero)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException(
            $"{Path.GetFileName(ruta)} es uniforme (un solo color): la captura salió vacía.");
    }

    private static int Luminancia(SKColor c) => ((c.Red * 299) + (c.Green * 587) + (c.Blue * 114)) / 1000;

    private static void MarcarContorno(SKBitmap diff, int x0, int y0, int w, int h)
    {
        for (var x = x0; x < x0 + w; x++)
        {
            diff.SetPixel(x, y0, Magenta);
            diff.SetPixel(x, y0 + h - 1, Magenta);
        }

        for (var y = y0; y < y0 + h; y++)
        {
            diff.SetPixel(x0, y, Magenta);
            diff.SetPixel(x0 + w - 1, y, Magenta);
        }
    }

    private static void Guardar(SKBitmap bitmap, string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        using var imagen = SKImage.FromBitmap(bitmap);
        using var datos = imagen.Encode(SKEncodedImageFormat.Png, 100);
        using var archivo = File.Create(ruta);
        datos.SaveTo(archivo);
    }
}
```

Nota: `EscribeDiffConLosDistintosEnRojo` usa un píxel suelto en una imagen de 40×40: su tesela (16,16)-(31,31) tiene 1 de 256 distintos, no es densa, así que el píxel queda rojo y no magenta.

- [ ] **Step 4: Agregar el comando `comparar-png`**

En `Cli.cs`, agregar `using System.Globalization;` y `using StockApp.UiV2.Herramientas.Imagenes;`, y dentro del `switch`, antes de `default:`:

```csharp
                case ["comparar-png", var referencia, var candidata, var rutaDiff]:
                {
                    var r = ComparadorImagenes.Comparar(referencia, candidata, rutaDiff);
                    salida.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"Píxeles distintos: {r.PixelesDistintos} ({r.PorcentajeDistinto:0.000}%). Teselas densas: {r.TeselasDensas}. Diff: {rutaDiff}"));
                    return 0;
                }
```

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~ComparadorImagenesTests"`
Expected: PASS, `Failed: 0, Passed: 11`.

- [ ] **Step 6: Verificación por mutación de las teselas densas**

(a) En `ComparadorImagenes.Comparar`, quitar la línea `teselasDensas++;`. Run el filtro del Step 5. Expected: FAIL en `BloqueDesplazado8px_MarcaTeselasDensas` (`teselas densas 0`) y en `FondoPalidoCambiado_...`. Restaurar; PASS.
(b) Quitar `|| leves >= DensidadColorMinima * w * h` de la condición de tesela. Expected: FAIL solo en `FondoPalidoCambiado_...` (`Expected: 16, Actual: 0`). Restaurar re-agregando el tramo; correr: PASS.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props tools/StockApp.UiV2.Herramientas tests/StockApp.UiV2.Herramientas.Tests
git commit -m "feat(ui-v2): agrega comparador de PNG con SkiaSharp y detección de teselas densas"
```

### Task 6: Captura de mockups con Playwright

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`
- Create: `tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs`
- Modify: `tools/StockApp.UiV2.Herramientas/Cli.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs`

**Interfaces:**
- Consumes: `Lienzo.Ancho/Alto`, `RutasRepo.TokensCss`, `RutasRepo.CarpetaBaseMockups`.
- Produces:
  - `CapturadorMockup.ArgumentosChromium : IReadOnlyList<string>` (`--font-render-hinting=none`).
  - `CapturadorMockup.CapturarAsync(string rutaHtml, string rutaPng) : Task`. Lanza `FileNotFoundException` (HTML inexistente) o `InvalidOperationException` (Chromium no instalado, sin `@font-face`, fuente sin cargar, contenido que desborda 1280×760).
  - Comandos `instalar-navegador` y `capturar-mockup <html> <png>`.

- [ ] **Step 1: Agregar Playwright**

En `Directory.Packages.props`, en el bloque `<!-- Otros -->`, agregar:

```xml
    <!-- UI v2: captura de los mockups HTML a PNG (tools/StockApp.UiV2.Herramientas). Chromium se
         instala una vez por máquina con el comando instalar-navegador de esa herramienta; 1.63.0
         usa Chromium rev 1243. Solo herramientas de desarrollo, nunca la app. -->
    <PackageVersion Include="Microsoft.Playwright" Version="1.63.0" />
```

En `tools/StockApp.UiV2.Herramientas/StockApp.UiV2.Herramientas.csproj`, dentro del `ItemGroup` de paquetes:

```xml
    <PackageReference Include="Microsoft.Playwright" />
```

- [ ] **Step 2: Instalar Chromium (una vez por máquina)**

Primero el comando. En `Cli.cs`, dentro del `switch`, antes de `default:`:

```csharp
                case ["instalar-navegador"]:
                    // Equivale a "playwright install chromium" sin requerir PowerShell (playwright.ps1).
                    return Microsoft.Playwright.Program.Main(["install", "chromium"]);
```

Run: `dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador; echo "exit=$?"`
Expected: termina con `exit=0` (si la revisión 1243 ya está en `~/.cache/ms-playwright`, no descarga nada).

- [ ] **Step 3: Escribir los tests (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/CapturadorMockupTests.cs`:

```csharp
using SkiaSharp;
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Review Focus 4: la captura tiene que fallar fuerte si el mockup no mide lo que debe
/// (fuente de fallback, contenido recortado), no devolver un PNG que "casi" coincide.
/// </summary>
public class CapturadorMockupTests
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("captura-").FullName;

    private static string Uri(string ruta) => new Uri(ruta).AbsoluteUri;

    private string Html(string cuerpo, string estilo = "", bool conBase = true)
    {
        var enlaces = conBase
            ? $"<link rel=\"stylesheet\" href=\"{Uri(RutasRepo.TokensCss)}\">"
              + $"<link rel=\"stylesheet\" href=\"{Uri(Path.Combine(RutasRepo.CarpetaBaseMockups, "base.css"))}\">"
            : "";
        var ruta = Path.Combine(_carpeta, $"{Guid.NewGuid():N}.html");
        File.WriteAllText(ruta,
            $"<!doctype html><html><head><meta charset=\"utf-8\">{enlaces}<style>{estilo}</style></head>"
            + $"<body><div class=\"v2-ventana\">{cuerpo}</div></body></html>");
        return ruta;
    }

    [Fact]
    public async Task BloqueDeColor_PngDe1280x760ConElColorEnSuPosicion()
    {
        var html = Html(
            "<div class=\"b\"></div><p class=\"t\">Texto con Inter</p>",
            ".b{position:absolute;left:100px;top:100px;width:200px;height:100px;background:var(--v2-color-acento)}"
            + ".t{position:absolute;left:100px;top:240px;margin:0}");
        var png = Path.Combine(_carpeta, "bloque.png");

        await CapturadorMockup.CapturarAsync(html, png);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1280, imagen.Width);
        Assert.Equal(760, imagen.Height);
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(150, 150));
        Assert.Equal(SKColor.Parse("#F7F7F5"), imagen.GetPixel(10, 10));
    }

    [Fact]
    public async Task MockupSinFontFace_Lanza()
    {
        var html = Html("<p>Sin base.css</p>", conBase: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("@font-face", ex.Message);
    }

    [Fact]
    public async Task FuenteQueNoCarga_Lanza()
    {
        var html = Html(
            "<p class=\"t\">Texto</p>",
            "@font-face{font-family:\"Rota\";src:url(\"no-existe.ttf\")}.t{font-family:\"Rota\"}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("Rota", ex.Message);
    }

    [Fact]
    public async Task ContenidoQueDesborda_Lanza()
    {
        var html = Html("<div class=\"ancho\"></div>", "body{overflow:visible}.v2-ventana{width:1400px}.ancho{width:1400px;height:10px}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("desborda", ex.Message);
    }

    [Fact]
    public async Task HtmlInexistente_LanzaFileNotFound() =>
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => CapturadorMockup.CapturarAsync(Path.Combine(_carpeta, "no.html"), Path.Combine(_carpeta, "x.png")));
}
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~CapturadorMockupTests"`
Expected: FAIL de compilación, `The name 'CapturadorMockup' does not exist in the current context`.

- [ ] **Step 4: Implementar**

`tools/StockApp.UiV2.Herramientas/Mockups/CapturadorMockup.cs`:

```csharp
using Microsoft.Playwright;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Captura un mockup HTML a PNG de 1280x760 con Chromium de Playwright, escala 1.
/// --font-render-hinting=none: sin él, Chromium en Linux ensancha el texto por hinting
/// (medido: ~2% a 13px) y la comparación contra Skia se llena de ruido.
/// Antes de capturar fuerza la carga de TODAS las @font-face y falla si alguna no cargó o si el
/// contenido desborda el lienzo (overflow:hidden lo recortaría en silencio).
/// </summary>
public static class CapturadorMockup
{
    public static readonly IReadOnlyList<string> ArgumentosChromium = ["--font-render-hinting=none"];

    private const string ScriptFuentes = """
        async () => {
          const caras = Array.from(document.fonts);
          await Promise.all(caras.map(f => f.load().catch(() => null)));
          return caras.map(f => f.family + " " + f.weight + ": " + f.status);
        }
        """;

    private const string ScriptTamano = "() => [document.documentElement.scrollWidth, document.documentElement.scrollHeight]";

    public static async Task CapturarAsync(string rutaHtml, string rutaPng)
    {
        if (!File.Exists(rutaHtml))
        {
            throw new FileNotFoundException("No existe el mockup.", rutaHtml);
        }

        using var playwright = await Playwright.CreateAsync();
        IBrowser navegador;
        try
        {
            navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Args = ArgumentosChromium });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Chromium de Playwright no está instalado. Correr una vez: "
                + "dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador", ex);
        }

        await using (navegador)
        {
            var pagina = await navegador.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = Lienzo.Ancho, Height = Lienzo.Alto },
                DeviceScaleFactor = 1,
            });
            await pagina.GotoAsync(new Uri(Path.GetFullPath(rutaHtml)).AbsoluteUri);

            var estados = await pagina.EvaluateAsync<string[]>(ScriptFuentes);
            if (estados.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(rutaHtml)} no declara ninguna @font-face: tiene que enlazar _base/tokens.css y _base/base.css.");
            }

            var sinCargar = estados.Where(e => !e.EndsWith(": loaded", StringComparison.Ordinal)).ToList();
            if (sinCargar.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(rutaHtml)}: fuentes sin cargar ({string.Join(", ", sinCargar)}).");
            }

            var tamano = await pagina.EvaluateAsync<int[]>(ScriptTamano);
            if (tamano[0] > Lienzo.Ancho || tamano[1] > Lienzo.Alto)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(rutaHtml)} desborda el lienzo: {tamano[0]}x{tamano[1]} > {Lienzo.Ancho}x{Lienzo.Alto}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaPng))!);
            await pagina.ScreenshotAsync(new PageScreenshotOptions { Path = rutaPng });
        }
    }
}
```

Y en `Cli.cs`, dentro del `switch`, antes de `default:`:

```csharp
                case ["capturar-mockup", var html, var png]:
                    CapturadorMockup.CapturarAsync(html, png).GetAwaiter().GetResult();
                    salida.WriteLine($"Escrito: {png}");
                    return 0;
```

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~CapturadorMockupTests"`
Expected: PASS, `Failed: 0, Passed: 5`.

- [ ] **Step 6: Verificación por mutación de la guarda de fuentes**

En `CapturadorMockup.CapturarAsync`, quitar el bloque `if (sinCargar.Count > 0) { ... }`. Run el filtro del Step 5. Expected: FAIL en `FuenteQueNoCarga_Lanza` (`Assert.Throws() Failure: No exception was thrown`). Restaurar el bloque; correr: PASS.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props tools/StockApp.UiV2.Herramientas tests/StockApp.UiV2.Herramientas.Tests
git commit -m "feat(ui-v2): agrega captura de mockups con Playwright y validación de fuentes y desborde"
```

### Task 7: Página de comparación lado a lado

**Files:**
- Create: `tools/StockApp.UiV2.Herramientas/Imagenes/PaginaComparacion.cs`
- Create: `tests/StockApp.UiV2.Herramientas.Tests/PaginaComparacionTests.cs`

**Interfaces:**
- Consumes: `ResultadoComparacion`.
- Produces:
  - `PaginaComparacion.Escribir(string carpetaPantalla, string pantalla, ResultadoComparacion resultado, double umbralPorcentaje, int teselasDensasMax) : string` (ruta del `index.html` de la pantalla). Espera `mockup.png`, `app.png` y `diff.png` en `carpetaPantalla`; escribe `index.html` y `resultado.json` ahí, y regenera `../index.html` con todas las pantallas.
  - `PaginaComparacion.Aprobada(ResultadoComparacion, double umbralPorcentaje, int teselasDensasMax) : bool`.
  - `record PaginaComparacion.ResultadoPersistido(string Pantalla, int PixelesDistintos, double Porcentaje, int TeselasDensas, double UmbralPorcentaje, int TeselasDensasMax)`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.UiV2.Herramientas.Tests/PaginaComparacionTests.cs`:

```csharp
using System.Text.Json;
using StockApp.UiV2.Herramientas.Imagenes;

namespace StockApp.UiV2.Herramientas.Tests;

public class PaginaComparacionTests
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("comparacion-").FullName;

    private string Pantalla(string nombre) => Directory.CreateDirectory(Path.Combine(_raiz, nombre)).FullName;

    [Fact]
    public void Escribir_DentroDeAmbosUmbrales_MuestraLasTresImagenesYElVeredicto()
    {
        var carpeta = Pantalla("componentes-catalogo");

        var pagina = PaginaComparacion.Escribir(carpeta, "componentes-catalogo", new ResultadoComparacion(1280, 760, 97, 0), 0.5, 0);

        var html = File.ReadAllText(pagina);
        Assert.Contains("src=\"mockup.png\"", html);
        Assert.Contains("src=\"app.png\"", html);
        Assert.Contains("src=\"diff.png\"", html);
        Assert.Contains("0.010%", html);
        Assert.Contains("APROBADA", html);
    }

    [Fact]
    public void Escribir_ConTeselasDensasPorEncimaDelMaximo_EsRechazadaAunqueElPorcentajeSeaBajo()
    {
        var carpeta = Pantalla("x");

        var html = File.ReadAllText(PaginaComparacion.Escribir(carpeta, "x", new ResultadoComparacion(1280, 760, 1600, 14), 0.5, 0));

        Assert.Contains("RECHAZADA", html);
        Assert.Contains("Teselas densas: 14 (máximo 0)", html);
    }

    [Fact]
    public void Escribir_PersisteResultadoJson()
    {
        var carpeta = Pantalla("x");

        PaginaComparacion.Escribir(carpeta, "x", new ResultadoComparacion(100, 100, 5, 1), 1.0, 2);

        var persistido = JsonSerializer.Deserialize<PaginaComparacion.ResultadoPersistido>(
            File.ReadAllText(Path.Combine(carpeta, "resultado.json")))!;
        Assert.Equal(new PaginaComparacion.ResultadoPersistido("x", 5, 0.05, 1, 1.0, 2), persistido);
    }

    [Fact]
    public void Escribir_RegeneraElIndiceConTodasLasPantallasOrdenadas()
    {
        PaginaComparacion.Escribir(Pantalla("b-pantalla"), "b-pantalla", new ResultadoComparacion(10, 10, 0, 0), 1, 0);
        PaginaComparacion.Escribir(Pantalla("a-pantalla"), "a-pantalla", new ResultadoComparacion(10, 10, 50, 3), 1, 0);

        var indice = File.ReadAllText(Path.Combine(_raiz, "index.html"));

        Assert.True(indice.IndexOf("a-pantalla/index.html", StringComparison.Ordinal)
                    < indice.IndexOf("b-pantalla/index.html", StringComparison.Ordinal));
        Assert.Contains("RECHAZADA", indice);
        Assert.Contains("APROBADA", indice);
    }

    [Theory]
    [InlineData(0.5, 0, 0.5, 0, true)]
    [InlineData(0.51, 0, 0.5, 0, false)]
    [InlineData(0.1, 1, 0.5, 0, false)]
    public void Aprobada_ExigeAmbasCompuertas(double porcentaje, int teselas, double umbral, int maximo, bool esperado)
    {
        var distintos = (int)Math.Round(porcentaje * 10_000 / 100);
        Assert.Equal(esperado, PaginaComparacion.Aprobada(new ResultadoComparacion(100, 100, distintos, teselas), umbral, maximo));
    }
}
```

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~PaginaComparacionTests"`
Expected: FAIL de compilación, `The name 'PaginaComparacion' does not exist in the current context`.

- [ ] **Step 2: Implementar**

`tools/StockApp.UiV2.Herramientas/Imagenes/PaginaComparacion.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace StockApp.UiV2.Herramientas.Imagenes;

/// <summary>
/// Página de aprobación de una pantalla (spec §6.2.3): mockup, app y diferencias lado a lado,
/// más el índice de todas las pantallas comparadas. Vive en artifacts/ (ignorado por git).
/// </summary>
public static class PaginaComparacion
{
    public const string ArchivoResultado = "resultado.json";

    public sealed record ResultadoPersistido(
        string Pantalla, int PixelesDistintos, double Porcentaje, int TeselasDensas, double UmbralPorcentaje, int TeselasDensasMax);

    public static bool Aprobada(ResultadoComparacion r, double umbralPorcentaje, int teselasDensasMax) =>
        r.PorcentajeDistinto <= umbralPorcentaje && r.TeselasDensas <= teselasDensasMax;

    public static string Escribir(
        string carpetaPantalla, string pantalla, ResultadoComparacion r, double umbralPorcentaje, int teselasDensasMax)
    {
        var aprobada = Aprobada(r, umbralPorcentaje, teselasDensasMax);
        var titulo = WebUtility.HtmlEncode(pantalla);
        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <title>{{titulo}}: comparación</title>
            <style>
              body { margin: 16px; font-family: sans-serif; color: #1C1917; background: #FFFFFF; }
              .aprobada { color: #14532D; font-weight: 600; }
              .rechazada { color: #991B1B; font-weight: 600; }
              .fila { display: flex; gap: 16px; align-items: flex-start; }
              figure { margin: 0; flex: 1 1 0; min-width: 0; }
              figcaption { font-size: 13px; margin-bottom: 4px; }
              img { width: 100%; border: 1px solid #E7E5E4; }
            </style>
            </head>
            <body>
            <h1>{{titulo}}</h1>
            <p class="{{(aprobada ? "aprobada" : "rechazada")}}">{{(aprobada ? "APROBADA" : "RECHAZADA")}}</p>
            <p>Píxeles distintos: {{r.PixelesDistintos}} de {{r.Ancho * r.Alto}} ({{F(r.PorcentajeDistinto)}}%, umbral {{F(umbralPorcentaje)}}%).
               Teselas densas: {{r.TeselasDensas}} (máximo {{teselasDensasMax}}).</p>
            <p><a href="../index.html">Todas las pantallas</a>. Click en una imagen para verla a tamaño real.
               En el diff: rojo = diferencia fuerte, naranja = diferencia leve de color, contorno magenta = tesela densa.</p>
            <div class="fila">
              <figure><figcaption>Mockup</figcaption><a href="mockup.png"><img src="mockup.png" alt="mockup"></a></figure>
              <figure><figcaption>App (render headless)</figcaption><a href="app.png"><img src="app.png" alt="app"></a></figure>
              <figure><figcaption>Diferencias</figcaption><a href="diff.png"><img src="diff.png" alt="diferencias"></a></figure>
            </div>
            </body>
            </html>
            """;

        var rutaPagina = Path.Combine(carpetaPantalla, "index.html");
        File.WriteAllText(rutaPagina, html);
        File.WriteAllText(
            Path.Combine(carpetaPantalla, ArchivoResultado),
            JsonSerializer.Serialize(new ResultadoPersistido(
                pantalla, r.PixelesDistintos, r.PorcentajeDistinto, r.TeselasDensas, umbralPorcentaje, teselasDensasMax)));
        EscribirIndice(Path.GetDirectoryName(Path.GetFullPath(carpetaPantalla))!);
        return rutaPagina;
    }

    private static void EscribirIndice(string carpetaRaiz)
    {
        var filas = new StringBuilder();
        foreach (var carpeta in Directory.GetDirectories(carpetaRaiz).OrderBy(d => d, StringComparer.Ordinal))
        {
            var archivo = Path.Combine(carpeta, ArchivoResultado);
            if (!File.Exists(archivo))
            {
                continue;
            }

            var p = JsonSerializer.Deserialize<ResultadoPersistido>(File.ReadAllText(archivo))!;
            var aprobada = p.Porcentaje <= p.UmbralPorcentaje && p.TeselasDensas <= p.TeselasDensasMax;
            var nombre = Path.GetFileName(carpeta);
            filas.Append($"<tr><td><a href=\"{nombre}/index.html\">{WebUtility.HtmlEncode(p.Pantalla)}</a></td>")
                 .Append($"<td>{F(p.Porcentaje)}%</td><td>{p.TeselasDensas}</td>")
                 .Append($"<td class=\"{(aprobada ? "aprobada" : "rechazada")}\">{(aprobada ? "APROBADA" : "RECHAZADA")}</td></tr>\n");
        }

        File.WriteAllText(Path.Combine(carpetaRaiz, "index.html"), $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <title>Comparaciones UI v2</title>
            <style>
              body { margin: 16px; font-family: sans-serif; color: #1C1917; }
              td, th { padding: 4px 12px; text-align: left; border-bottom: 1px solid #F0EFED; }
              .aprobada { color: #14532D; font-weight: 600; }
              .rechazada { color: #991B1B; font-weight: 600; }
            </style>
            </head>
            <body>
            <h1>Comparaciones UI v2</h1>
            <table>
            <tr><th>Pantalla</th><th>Píxeles distintos</th><th>Teselas densas</th><th>Veredicto</th></tr>
            {{filas}}</table>
            </body>
            </html>
            """);
    }

    private static string F(double valor) => valor.ToString("0.000", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 3: Correr y ver el verde**

Run: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~PaginaComparacionTests"`
Expected: PASS, `Failed: 0, Passed: 7`.

- [ ] **Step 4: Verificación por mutación**

En `Aprobada`, quitar `&& r.TeselasDensas <= teselasDensasMax`. Expected: FAIL en `Escribir_ConTeselasDensasPorEncimaDelMaximo_...` y en `Aprobada_ExigeAmbasCompuertas(0.1, 1, ...)`. Restaurar; PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/StockApp.UiV2.Herramientas tests/StockApp.UiV2.Herramientas.Tests
git commit -m "feat(ui-v2): agrega página de comparación mockup, app y diferencias con índice"
```

### Task 8: Proyecto `VisualTests`: app headless sobre Skia y render de vistas a PNG

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Create: `tests/StockApp.Presentation.VisualTests/VisualTestApp.cs`
- Create: `tests/StockApp.Presentation.VisualTests/IncludesDeAppAxaml.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Comparacion/RenderizadorVista.cs`
- Create: `tests/StockApp.Presentation.VisualTests/IncludesAppAxamlTests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/RenderizadorVistaTests.cs`
- Modify: `StockApp.sln`

**Interfaces:**
- Consumes: `RutasRepo.Raiz`, `Lienzo.Ancho/Alto` (herramienta), `TestApp.ResourceIncludes/StyleIncludes` como referencia de contenido (NO se referencia UiTests).
- Produces:
  - `VisualTestApp.ResourceIncludes : string[]`, `VisualTestApp.StyleIncludes : string[]`. Las Tasks 10, 11 y 16 agregan entradas.
  - `RenderizadorVista.RenderizarAPng(Control vista, object? viewModel, string rutaPng) : void` — ventana de 1280×760 con fondo `V2FondoBrush`; el `viewModel` se asigna como `DataContext` de la vista ANTES de mostrarla (dispara el wiring de `DataContextChanged` igual que en producción).

- [ ] **Step 1: Crear el proyecto y agregarlo a la solución**

`tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <!-- Render headless con Skia (UseHeadlessDrawing=false): métricas de texto REALES de Inter.
         Proyecto separado de StockApp.Presentation.UiTests a propósito: AvaloniaTestApplication es
         uno por ensamblado y pasar UiTests a Skia cambiaría las métricas de todos sus tests. -->
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Skia" />
    <PackageReference Include="Avalonia.Headless" />
    <PackageReference Include="Avalonia.Headless.XUnit" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="Avalonia.Controls.DataGrid" />
    <PackageReference Include="Optris.Icons.Avalonia" />
    <PackageReference Include="Optris.Icons.Avalonia.MaterialDesign" />
    <!-- Mismo par xUnit v3 que StockApp.Presentation.UiTests (ver comentario en su csproj). -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" VersionOverride="17.12.0" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" VersionOverride="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StockApp.Presentation\StockApp.Presentation.csproj" />
    <!-- Captura de mockups, diff de PNG y página de comparación. -->
    <ProjectReference Include="..\..\tools\StockApp.UiV2.Herramientas\StockApp.UiV2.Herramientas.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln StockApp.sln add tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --solution-folder tests`
Expected: `Project ... added to the solution.`

- [ ] **Step 2: Escribir los tests (rojo)**

`tests/StockApp.Presentation.VisualTests/IncludesDeAppAxaml.cs` (copia deliberada de la de `UiTests/V2`, Task 3):

```csharp
using System.Xml.Linq;

namespace StockApp.Presentation.VisualTests;

/// <summary>Lee de App.axaml, en orden de documento, los Source de ResourceInclude y StyleInclude.</summary>
internal static class IncludesDeAppAxaml
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    public static (string[] Recursos, string[] Estilos) Leer(string appAxaml)
    {
        var documento = XDocument.Parse(appAxaml);
        var recursos = documento.Descendants(Avalonia + "ResourceInclude")
            .Select(e => (string)e.Attribute("Source")!).ToArray();
        var estilos = documento.Descendants(Avalonia + "StyleInclude")
            .Select(e => (string)e.Attribute("Source")!).ToArray();
        return (recursos, estilos);
    }
}
```

`tests/StockApp.Presentation.VisualTests/IncludesAppAxamlTests.cs`:

```csharp
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Review Focus 4: si VisualTestApp no carga lo mismo que App.axaml, la comparación visual
/// aprueba una app que no existe.
/// </summary>
public class IncludesAppAxamlTests
{
    [Fact]
    public void VisualTestApp_CargaLosMismosIncludesQueAppAxaml_EnElMismoOrden()
    {
        var appAxaml = File.ReadAllText(Path.Combine(RutasRepo.Raiz, "src", "StockApp.Presentation", "App.axaml"));

        var (recursos, estilos) = IncludesDeAppAxaml.Leer(appAxaml);

        Assert.Equal(recursos, VisualTestApp.ResourceIncludes);
        Assert.Equal(estilos, VisualTestApp.StyleIncludes);
    }
}
```

`tests/StockApp.Presentation.VisualTests/RenderizadorVistaTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.Presentation.VisualTests;

public class RenderizadorVistaTests
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("render-").FullName;

    [AvaloniaFact]
    public void BorderDeColor_PngDe1280x760ConElColorEnSuPosicionYElFondoV2()
    {
        var bloque = new Border { Width = 200, Height = 100, Background = new SolidColorBrush(Color.Parse("#1D4ED8")) };
        Canvas.SetLeft(bloque, 100);
        Canvas.SetTop(bloque, 100);
        var png = Path.Combine(_carpeta, "bloque.png");

        RenderizadorVista.RenderizarAPng(new Canvas { Children = { bloque } }, null, png);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1280, imagen.Width);
        Assert.Equal(760, imagen.Height);
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(150, 150));
        Assert.Equal(SKColor.Parse("#F7F7F5"), imagen.GetPixel(10, 10));
    }

    [AvaloniaFact]
    public void AsignaElViewModelComoDataContextDeLaVista()
    {
        var vista = new Canvas();
        var modelo = new object();

        RenderizadorVista.RenderizarAPng(vista, modelo, Path.Combine(_carpeta, "dc.png"));

        Assert.Same(modelo, vista.DataContext);
    }

    [AvaloniaFact]
    public void TextoSinFontFamilyExplicita_SeMideConInter()
    {
        // Review Focus 4: WithInterFont() NO fija la familia default en Avalonia 12 (queda
        // DejaVu Sans en WSL). Inter llega por el Setter de Typography.axaml sobre Window. Si ese
        // include se pierde, todo el texto del render se mide con otra fuente.
        const string texto = "Productos del inventario 1234";
        var sinFamilia = new TextBlock { Text = texto, FontSize = 22 };
        var conInter = new TextBlock { Text = texto, FontSize = 22, FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter") };
        var conDejaVu = new TextBlock { Text = texto, FontSize = 22, FontFamily = new FontFamily("DejaVu Sans") };
        var ventana = new Window { Content = new StackPanel { Children = { sinFamilia, conInter, conDejaVu } } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(conInter.DesiredSize.Width, sinFamilia.DesiredSize.Width);
        Assert.NotEqual(conDejaVu.DesiredSize.Width, sinFamilia.DesiredSize.Width);
    }

    public static TheoryData<string> RolesSinAltoDeLineaFijo => new(
        LectorTokens.LeerArchivo(RutasRepo.TokensJson).Tipografia
            .Where(t => t.Estilo.AltoLinea is null)
            .Select(t => t.Nombre));

    [AvaloniaTheory]
    [MemberData(nameof(RolesSinAltoDeLineaFijo))]
    public void AltoDeLinea_DeCadaRol_EsElQueUsaElCssDeLosMockups(string rol)
    {
        // tokens.css emite ceil(tamaño × factorAltoLinea) como line-height de cada rol. Si la
        // Inter embebida cambia de versión o Avalonia cambia el redondeo, los textos apilados del
        // mockup derivan respecto de la app: este test lo detecta antes que la comparación.
        var tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);
        var estilo = tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        var texto = new TextBlock { Text = "Ág 123", FontSize = estilo.Tamano, FontWeight = (FontWeight)estilo.Peso };
        var ventana = new Window { Content = new StackPanel { Children = { texto } } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(GeneradorTokens.AltoLineaCss(estilo, tokens.FactorAltoLinea), texto.Bounds.Height);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: FAIL de compilación, `The type or namespace name 'VisualTestApp' could not be found` y `'Comparacion' does not exist in the namespace`.

- [ ] **Step 3: Implementar `VisualTestApp`**

`tests/StockApp.Presentation.VisualTests/VisualTestApp.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;

[assembly: AvaloniaTestApplication(typeof(StockApp.Presentation.VisualTests.VisualTestApp))]

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Host headless con render REAL (Skia): lo que se captura es lo que dibujaría la app. Carga los
/// mismos includes que App.axaml, en el mismo orden (custodiado por IncludesAppAxamlTests).
/// </summary>
public class VisualTestApp : Avalonia.Application
{
    // Mismo guard que UiTests/TestAppBuilder.cs: IconProvider.Register no es idempotente y
    // Avalonia.Headless.XUnit construye una Application por test.
    private static bool _iconProviderRegistrado;

    public static readonly string[] ResourceIncludes =
    [
        "avares://GestionMunicipal/Themes/Tokens.axaml",
        "avares://GestionMunicipal/Themes/V2/Tokens.axaml",
        "avares://GestionMunicipal/Controls/Componentes.axaml",
    ];

    public static readonly string[] StyleIncludes =
    [
        "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml",
        "avares://GestionMunicipal/Themes/Typography.axaml",
        "avares://GestionMunicipal/Themes/Controls.axaml",
        "avares://GestionMunicipal/Themes/DataGrid.axaml",
    ];

    public VisualTestApp()
    {
        RequestedThemeVariant = ThemeVariant.Light;

        if (!_iconProviderRegistrado)
        {
            IconProvider.Current.Register<MaterialDesignIconProvider>();
            _iconProviderRegistrado = true;
        }

        // Mismo workaround que UiTests/TestAppBuilder.cs: sin App.axaml real, el tema del DataGrid
        // no resuelve a tiempo SystemControlTransparentBrush. En producción valen Transparent.
        Resources["DataGridRowBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridCellBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridCurrencyVisualPrimaryBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridFillerColumnGridLinesBrush"] = new SolidColorBrush(Colors.Transparent);

        foreach (var fuente in ResourceIncludes)
        {
            Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://StockApp.Presentation.VisualTests/"))
            {
                Source = new Uri(fuente)
            });
        }

        Styles.Add(new FluentTheme());

        foreach (var fuente in StyleIncludes)
        {
            Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.VisualTests/"))
            {
                Source = new Uri(fuente)
            });
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<VisualTestApp>()
            .UseSkia()
            .WithInterFont()
            .WithDataAnnotationsValidation()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
```

- [ ] **Step 4: Implementar `RenderizadorVista`**

`tests/StockApp.Presentation.VisualTests/Comparacion/RenderizadorVista.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Renderiza una vista real a PNG de 1280x760 con Skia. Requiere correr dentro de un
/// [AvaloniaFact] de este proyecto (VisualTestApp: UseSkia + UseHeadlessDrawing=false).
/// </summary>
public static class RenderizadorVista
{
    public static void RenderizarAPng(Control vista, object? viewModel, string rutaPng)
    {
        if (!Avalonia.Application.Current!.TryGetResource("V2FondoBrush", ThemeVariant.Light, out var fondo)
            || fondo is not IBrush brushFondo)
        {
            throw new InvalidOperationException("V2FondoBrush no se resuelve: Themes/V2/Tokens.axaml no está cargado.");
        }

        if (viewModel is not null)
        {
            vista.DataContext = viewModel;
        }

        var ventana = new Window
        {
            Width = Lienzo.Ancho,
            Height = Lienzo.Alto,
            SizeToContent = SizeToContent.Manual,
            Background = brushFondo,
            Content = vista,
        };

        ventana.Show();
        // Dos vueltas: la primera procesa Loaded (donde algunas vistas cargan o setean estado) y
        // la segunda el layout que eso invalida. Sin la segunda, el frame puede salir sin ese cambio.
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var frame = ventana.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("El render headless no produjo frame: revisar UseSkia y UseHeadlessDrawing=false.");
        if (frame.PixelSize.Width != Lienzo.Ancho || frame.PixelSize.Height != Lienzo.Alto)
        {
            throw new InvalidOperationException(
                $"Frame de {frame.PixelSize.Width}x{frame.PixelSize.Height}, se esperaba {Lienzo.Ancho}x{Lienzo.Alto}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaPng))!);
        frame.Save(rutaPng);
        ventana.Close();
    }
}
```

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: PASS, `Failed: 0, Passed: 23` (4 facts + 19 filas de `AltoDeLinea_...`, una por rol sin alto de línea fijo).

- [ ] **Step 6: Verificación por mutación**

(a) Quitar `"avares://GestionMunicipal/Themes/Typography.axaml",` de `VisualTestApp.StyleIncludes`. Expected: FAIL en `IncludesAppAxamlTests` Y en `TextoSinFontFamilyExplicita_SeMideConInter` (el texto se mide con la fuente default). Restaurar; PASS.
(b) En `GeneradorTokens.AltoLineaCss`, quitar el `Math.Ceiling(...)` (queda `estilo.AltoLinea ?? estilo.Tamano * factorAltoLinea`). Expected: FAIL en `AltoDeLinea_DeCadaRol_...` (p. ej. `Expected: 13.30956, Actual: 14`). Restaurar; PASS.
(c) En `BuildAvaloniaApp`, quitar `.UseSkia()`. Expected: FAIL en los tests de render (sin subsistema de render la app headless no arranca o `GetLastRenderedFrame` lanza `NotSupportedException`). Restaurar; PASS.

- [ ] **Step 7: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests StockApp.sln
git commit -m "test(ui-v2): agrega proyecto VisualTests con render headless sobre Skia"
```

### Task 9: `CompararConMockup`, umbral provisional, comando único y autotest de la herramienta

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Comparacion/UmbralComparacion.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs`
- Create: `tests/StockApp.Presentation.VisualTests/ComparacionVisualTests.cs`
- Create: `docs/ui-v2/mockups/_fixtures/bloque.html`
- Create: `scripts/ui-v2/comparar.sh`
- Create: `docs/ui-v2/herramientas.md`

**Interfaces:**
- Consumes: `RenderizadorVista.RenderizarAPng`, `CapturadorMockup.CapturarAsync`, `ComparadorImagenes.Comparar`, `PaginaComparacion.Escribir/Aprobada`, `RutasRepo`.
- Produces (lo que cada fase usa para su test por pantalla):
  - `UmbralComparacion.PorcentajePorDefecto : double`, `UmbralComparacion.TeselasDensasPorDefecto : int` (provisionales; la Task 18 fija los calibrados).
  - `ComparacionVisual.CompararConMockup(Control vista, object? viewModel, string rutaMockup, double umbral = UmbralComparacion.PorcentajePorDefecto, int teselasDensasMax = UmbralComparacion.TeselasDensasPorDefecto) : ResultadoComparacion` — `rutaMockup` relativa a `docs/ui-v2/mockups/` (p. ej. `"inventario/productos.html"`); escribe en `artifacts/ui-v2/comparacion/<modulo>-<pantalla>/`; falla con la ruta de la página si no aprueba.
  - `ComparacionVisual.NombrePantalla(string rutaMockup) : string`, `ComparacionVisual.CarpetaSalida : string`.
  - Trait de los tests de comparación: `[Trait("Categoria", "ComparacionVisual")]`.
  - Comando único: `scripts/ui-v2/comparar.sh [filtro]`.

- [ ] **Step 1: Escribir el mockup de autotest**

`docs/ui-v2/mockups/_fixtures/bloque.html`:

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Autotest de la herramienta de comparación</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  .bloque { position: absolute; left: 100px; top: 100px; width: 200px; height: 100px; background: var(--v2-color-acento); }
  .texto {
    position: absolute; left: 100px; top: 240px;
    font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea);
    color: var(--v2-color-texto-primario);
  }
</style>
</head>
<body>
<div class="v2-ventana">
  <div class="bloque"></div>
  <div class="texto">Bloque de prueba</div>
</div>
</body>
</html>
```

- [ ] **Step 2: Escribir los tests (rojo)**

`tests/StockApp.Presentation.VisualTests/ComparacionVisualTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Autotest de la herramienta contra docs/ui-v2/mockups/_fixtures/bloque.html: la misma
/// escena aprueba, y la escena con el bloque corrido 8px falla (Review Focus 3).
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class ComparacionVisualTests
{
    private static Control VistaBloque(double desplazamiento)
    {
        var bloque = new Border { Width = 200, Height = 100, Background = new SolidColorBrush(Color.Parse("#1D4ED8")) };
        Canvas.SetLeft(bloque, 100 + desplazamiento);
        Canvas.SetTop(bloque, 100);
        var texto = new TextBlock { Text = "Bloque de prueba", FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#1C1917")) };
        Canvas.SetLeft(texto, 100);
        Canvas.SetTop(texto, 240);
        return new Canvas { Children = { bloque, texto } };
    }

    [AvaloniaFact]
    public void VistaIgualAlMockup_ApruebaYDejaLaPaginaDeComparacion()
    {
        var r = ComparacionVisual.CompararConMockup(VistaBloque(0), null, "_fixtures/bloque.html", umbral: 0.5, teselasDensasMax: 2);

        Assert.True(r.PorcentajeDistinto <= 0.5);
        var carpeta = Path.Combine(ComparacionVisual.CarpetaSalida, "_fixtures-bloque");
        Assert.True(File.Exists(Path.Combine(carpeta, "index.html")));
        Assert.True(File.Exists(Path.Combine(carpeta, "mockup.png")));
        Assert.True(File.Exists(Path.Combine(carpeta, "app.png")));
        Assert.True(File.Exists(Path.Combine(carpeta, "diff.png")));
    }

    [AvaloniaFact]
    public void VistaDesplazada8px_FallaYDejaLaPaginaDeComparacion()
    {
        var ex = Assert.ThrowsAny<Exception>(
            () => ComparacionVisual.CompararConMockup(VistaBloque(8), null, "_fixtures/bloque.html", umbral: 0.5, teselasDensasMax: 2));

        Assert.Contains("teselas densas", ex.Message);
        Assert.Contains("index.html", ex.Message);
    }

    [Theory]
    [InlineData("componentes/catalogo.html", "componentes-catalogo")]
    [InlineData("inventario\\productos.html", "inventario-productos")]
    public void NombrePantalla_UneModuloYPantalla(string ruta, string esperado) =>
        Assert.Equal(esperado, ComparacionVisual.NombrePantalla(ruta));
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~ComparacionVisualTests"`
Expected: FAIL de compilación, `The name 'ComparacionVisual' does not exist in the current context`.

- [ ] **Step 3: Implementar**

`tests/StockApp.Presentation.VisualTests/Comparacion/UmbralComparacion.cs`:

```csharp
namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Compuertas de aceptación de una pantalla contra su mockup (spec §6.3). PROVISIONALES:
/// holgadas para poder construir el catálogo. La calibración con la pantalla
/// catálogo-de-componentes las reemplaza por los valores medidos y las documenta en la spec §6.5.
/// </summary>
public static class UmbralComparacion
{
    public const double PorcentajePorDefecto = 2.0;
    public const int TeselasDensasPorDefecto = 20;
}
```

`tests/StockApp.Presentation.VisualTests/Comparacion/ComparacionVisual.cs`:

```csharp
using System.Globalization;
using Avalonia.Controls;
using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Imagenes;
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Helper que cada fase usa para su test de regresión visual por pantalla:
/// <code>
/// [AvaloniaFact]
/// public void Productos_IgualAlMockup() =>
///     ComparacionVisual.CompararConMockup(new ProductoListView(), vmConDatosFijos, "inventario/productos.html");
/// </code>
/// </summary>
public static class ComparacionVisual
{
    public static string CarpetaSalida => Path.Combine(RutasRepo.Raiz, "artifacts", "ui-v2", "comparacion");

    public static ResultadoComparacion CompararConMockup(
        Control vista,
        object? viewModel,
        string rutaMockup,
        double umbral = UmbralComparacion.PorcentajePorDefecto,
        int teselasDensasMax = UmbralComparacion.TeselasDensasPorDefecto)
    {
        var rutaHtml = Path.GetFullPath(Path.Combine(RutasRepo.CarpetaMockups, rutaMockup));
        var pantalla = NombrePantalla(rutaMockup);
        var carpeta = Directory.CreateDirectory(Path.Combine(CarpetaSalida, pantalla)).FullName;
        var pngMockup = Path.Combine(carpeta, "mockup.png");
        var pngApp = Path.Combine(carpeta, "app.png");
        var pngDiff = Path.Combine(carpeta, "diff.png");

        // Playwright es async y este código corre en el dispatcher de Avalonia: se captura en el
        // thread pool (sin contexto de sincronización) y se espera. Verificado sin deadlock.
        Task.Run(() => CapturadorMockup.CapturarAsync(rutaHtml, pngMockup)).GetAwaiter().GetResult();
        RenderizadorVista.RenderizarAPng(vista, viewModel, pngApp);

        var resultado = ComparadorImagenes.Comparar(pngMockup, pngApp, pngDiff);
        var pagina = PaginaComparacion.Escribir(carpeta, pantalla, resultado, umbral, teselasDensasMax);

        Assert.True(
            PaginaComparacion.Aprobada(resultado, umbral, teselasDensasMax),
            string.Create(CultureInfo.InvariantCulture,
                $"{pantalla}: {resultado.PorcentajeDistinto:0.000}% de píxeles distintos (umbral {umbral:0.000}%) y "
                + $"{resultado.TeselasDensas} teselas densas (máximo {teselasDensasMax}). Comparación: {pagina}"));
        return resultado;
    }

    public static string NombrePantalla(string rutaMockup) =>
        Path.ChangeExtension(rutaMockup.Replace('\\', '/'), null)!.Replace('/', '-');
}
```

- [ ] **Step 4: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~ComparacionVisualTests"`
Expected: PASS, `Failed: 0, Passed: 4`.
Abrir `artifacts/ui-v2/comparacion/_fixtures-bloque/index.html` y confirmar que se ven las tres imágenes. Si `VistaIgualAlMockup_...` falla, abrir su página: el diff dice dónde difieren. Una diferencia en el bloque es un bug de la herramienta (tamaño, escala, fondo); una diferencia en el texto con teselas densas se anota para la calibración (Task 18) y se ajusta `teselasDensasMax` de ESTE test al valor observado + 1, nunca por encima de 4 (el negativo produce ≥ 12).

- [ ] **Step 5: Comando único**

`scripts/ui-v2/comparar.sh`:

```bash
#!/usr/bin/env bash
# Corre las comparaciones visuales mockup vs app de la UI v2 y deja las páginas en
# artifacts/ui-v2/comparacion/ (ignorado por git). Uso:
#   scripts/ui-v2/comparar.sh              todas las pantallas
#   scripts/ui-v2/comparar.sh Catalogo     solo los tests cuyo nombre contiene "Catalogo"
# Requisito una vez por máquina:
#   dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FILTRO="Categoria=ComparacionVisual"
if [[ $# -gt 0 ]]; then
  FILTRO="${FILTRO}&FullyQualifiedName~$1"
fi

set +e
dotnet test "$RAIZ/tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj" --filter "$FILTRO"
CODIGO=$?
set -e

echo "Comparaciones: $RAIZ/artifacts/ui-v2/comparacion/index.html"
exit "$CODIGO"
```

Run:

```bash
chmod +x scripts/ui-v2/comparar.sh
git check-ignore -v artifacts/ui-v2/comparacion/index.html
scripts/ui-v2/comparar.sh ComparacionVisualTests
```

Expected: `.gitignore:72:artifacts/	artifacts/ui-v2/comparacion/index.html`; luego `Passed!  - Failed:     0, Passed:     4` y la línea `Comparaciones: .../artifacts/ui-v2/comparacion/index.html`.

- [ ] **Step 6: Documentar la herramienta**

`docs/ui-v2/herramientas.md`:

````markdown
# Herramientas de la UI v2

## Una vez por máquina

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador
```

Instala el Chromium que usa Microsoft.Playwright 1.63.0 (rev 1243) en `~/.cache/ms-playwright`.

## Comando único de comparación

```bash
scripts/ui-v2/comparar.sh            # todas las pantallas
scripts/ui-v2/comparar.sh Productos  # solo los tests cuyo nombre contiene "Productos"
```

Abre `artifacts/ui-v2/comparacion/index.html`: una fila por pantalla con el porcentaje de píxeles
distintos, las teselas densas y el veredicto. Cada pantalla tiene su página con mockup, app y
diferencias lado a lado (rojo = diferencia fuerte; naranja = diferencia leve de color; contorno
magenta = tesela densa de 16x16: 25% o más de diferencias fuertes, o 90% o más de leves).

## Tokens

`docs/ui-v2/tokens.json` es la única fuente. Después de editarlo:

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens
```

Regenera `src/StockApp.Presentation/Themes/V2/Tokens.axaml` y `docs/ui-v2/mockups/_base/tokens.css`.
Nunca se editan a mano: `ArchivosGeneradosSincronizadosTests` falla si difieren del JSON.

## Mockup de una pantalla

1. `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, con `../_base/tokens.css` y `../_base/base.css`,
   todo adentro de `<div class="v2-ventana">` (1280x760), datos fijos.
2. Íconos: `dotnet run --project tools/StockApp.UiV2.Herramientas -- icono-svg mdi-<nombre>` y pegar el SVG.
3. Solo técnicas de la spec §6.1. Las clases `v2-*` de `base.css` replican la geometría de los
   componentes de `Controls/V2`.

## Test por pantalla

En `tests/StockApp.Presentation.VisualTests`, con `[Trait("Categoria", "ComparacionVisual")]`:

```csharp
[AvaloniaFact]
public void Productos_IgualAlMockup() =>
    ComparacionVisual.CompararConMockup(new ProductoListView(), vmConLosMismosDatosDelMockup, "inventario/productos.html");
```

El umbral por defecto es el calibrado en la Fase 0 (spec §6.5). No se sube por pantalla sin
aprobación del usuario.

## Otros comandos

- `tokens --verificar`, `fuentes`, `fuentes --verificar`
- `capturar-mockup <html> <png>`: captura suelta de un mockup.
- `comparar-png <referencia> <candidata> <diff>`: diff suelto de dos PNG del mismo tamaño (sirve
  para comparar capturas de la app real antes y después).

## Convenciones de la convivencia (hasta la Fase 9)

- Estilos base v2 solo bajo `.ui2`: la RAÍZ de una vista migrada lleva `Classes="ui2"`. Nunca en un
  contenedor que hospede vistas no migradas.
- Componentes v2: `xmlns:v2="using:StockApp.Presentation.Controls.V2"`.
- Tokens v2: claves `V2*` y variables `--v2-*`.
````

- [ ] **Step 7: Verificación por mutación del helper**

En `ComparacionVisual.CompararConMockup`, quitar la llamada `Assert.True(...)` completa. Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~VistaDesplazada8px"`. Expected: FAIL (`No exception was thrown`). Restaurar; PASS.

- [ ] **Step 8: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests docs/ui-v2/mockups/_fixtures scripts/ui-v2 docs/ui-v2/herramientas.md
git commit -m "feat(ui-v2): agrega CompararConMockup, comando comparar.sh y autotest de la herramienta"
```

## Bloque C — Estilos y componentes v2 (conviviendo con los viejos)

**Mecanismo de convivencia (vale para las Tasks 10 a 16).** Nada de lo que agregan estas tasks cambia una vista existente:

1. **Estilos de controles base** (`Button`, `TextBox`, `ComboBox`, `NumericUpDown`, `CalendarDatePicker`, `AutoCompleteBox`, `CheckBox`, `DataGrid`, `DataValidationErrors`, tipografía): todos sus selectores empiezan con `.ui2 `. Solo matchean controles que tienen un ancestro con `Classes="ui2"`. Hoy ninguna vista lo tiene.
2. **Orden y pisado:** `ControlesV2.axaml` y `DataGridV2.axaml` se incluyen al FINAL de `App.axaml.Styles`. Los estilos viejos globales (`Button`, `TextBox`, `Button.ghost`, `DataGridRow:nth-child(2n)`, `DataGridCell.num TextBlock`) también matchean dentro de `.ui2`; en Avalonia gana el último declarado para la misma propiedad, así que cada estilo v2 setea TODAS las propiedades que setea su par viejo (si no, se filtra un valor viejo).
3. **Componentes:** tipos nuevos en `StockApp.Presentation.Controls.V2` con su `ControlTheme` por tipo en `Controls/V2/ComponentesV2.axaml`. Los homónimos viejos (`Controls.CampoFormulario`, `Controls.TarjetaMetrica`, `Controls.EstadoVacio`) no se tocan hasta la Fase 9. Un componente v2 usado fuera de `.ui2` se ve bien igual: su `ControlTheme` no depende del scope (solo los controles base que contiene lo usan).
4. **Qué migra después:** cada fase pone `Classes="ui2"` en la RAÍZ de la vista que reescribe. Nunca en `MainWindow`, en el shell ni en un `ContentControl` que hospede vistas no migradas.

**Helper de montaje para los tests de esta sección:** se crea en la Task 10 y lo usan las Tasks 11 a 16.

### Task 10: Estilos v2 de tipografía, botones e inputs bajo `.ui2`

**Files:**
- Create: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml`
- Modify: `src/StockApp.Presentation/App.axaml` (`Application.Styles`)
- Modify: `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` (`StyleIncludes`)
- Modify: `tests/StockApp.Presentation.VisualTests/VisualTestApp.cs` (`StyleIncludes`)
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/MontajeV2.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/ControlesV2Tests.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/ControlesV2ConvivenciaTests.cs`

**Interfaces:**
- Consumes: claves `V2*` (Task 2).
- Produces:
  - Clases de `TextBlock` (dentro de `.ui2`): `titulo-pagina`, `titulo-seccion`, `cuerpo`, `texto-secundario`, `subtitulo-formulario`, `etiqueta-campo`, `pie-conteo`.
  - Clases de `Button` (dentro de `.ui2`): `primario`, `secundario`, `ghost`, `peligro`. Un `Button` sin clase dentro de `.ui2` NO recibe estilo v2 (la Fase 9 agrega el guardián que exige variante).
  - Inputs a 36 de alto dentro de `.ui2`: `TextBox`, `ComboBox`, `NumericUpDown`, `CalendarDatePicker`, `AutoCompleteBox`; `CheckBox` con casilla de 16.
  - `MontajeV2.Montar(string contenido, bool conScopeUi2 = true, double ancho = 800, double alto = 600) : Window`, `MontajeV2.Buscar<T>(Window, string nombre) : T`, `MontajeV2.Parte<T>(Control, string nombre) : T`, `MontajeV2.ColorDe(IBrush?) : Color`, `MontajeV2.Hex(string) : Color`, `MontajeV2.Clic(Window, Control) : void`.
  - Clases CSS: `v2-titulo-pagina`, `v2-titulo-seccion`, `v2-cuerpo`, `v2-texto-secundario`, `v2-subtitulo-formulario`, `v2-etiqueta-campo`, `v2-pie-conteo`, `v2-boton` + `v2-boton--{primario|secundario|ghost|peligro}`, `v2-input`, `v2-input--error`, `v2-input-placeholder`, `v2-glifo-combo`, `v2-glifo-fecha`, `v2-casilla-fila`, `v2-casilla-caja`, `v2-casilla`, `v2-casilla--marcada`, `v2-glifo-check`.
  - El `CalendarDatePicker` dentro de `.ui2` dibuja su botón con el ícono `mdi-calendar-blank-outline` (parte `PART_GlifoFecha`), no con el calendario de Fluent que muestra el día de hoy.

- [ ] **Step 1: Helper de montaje**

`tests/StockApp.Presentation.UiTests/V2/MontajeV2.cs`:

```csharp
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Monta XAML dentro de una vista con (o sin) el scope .ui2, con el TestApp real. Los prefijos
/// v2 e i se declaran solo si el contenido los usa (la Task 10 corre antes de que exista el
/// namespace Controls.V2).
/// </summary>
internal static class MontajeV2
{
    public static Window Montar(string contenido, bool conScopeUi2 = true, double ancho = 800, double alto = 600)
    {
        var xmlnsV2 = contenido.Contains("v2:") ? " xmlns:v2=\"using:StockApp.Presentation.Controls.V2\"" : "";
        var xmlnsI = contenido.Contains("i:") ? " xmlns:i=\"https://github.com/projektanker/icons.avalonia\"" : "";
        var clases = conScopeUi2 ? " Classes=\"ui2\"" : "";
        var xaml = $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"{xmlnsV2}{xmlnsI}
                    Width="{ancho}" Height="{alto}">
                <UserControl{clases}>
                    <StackPanel Margin="16" Spacing="8">
                        {contenido}
                    </StackPanel>
                </UserControl>
            </Window>
            """;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return ventana;
    }

    public static T Buscar<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    public static T Parte<T>(Control control, string nombre) where T : Control =>
        control.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    public static Color ColorDe(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    public static Color Hex(string hex) => Color.Parse(hex);

    /// <summary>Click real de puntero en el centro del control (dispara Command igual que un usuario).</summary>
    public static void Clic(Window ventana, Control control)
    {
        var centro = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseDown(centro, MouseButton.Left);
        ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
```

- [ ] **Step 2: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ControlesV2Tests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Optris.Icons.Avalonia;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class ControlesV2Tests
{
    [AvaloniaTheory]
    [InlineData("primario", "#1D4ED8", "#1D4ED8", "#FFFFFF")]
    [InlineData("secundario", "#FFFFFF", "#D6D3D1", "#44403C")]
    [InlineData("peligro", "#FFFFFF", "#D6D3D1", "#B91C1C")]
    public void Boton_Variante_ColoresAlto36Radio8Fuente13SemiBold(string clase, string fondo, string borde, string texto)
    {
        var boton = Buscar<Button>(Montar($"""<Button x:Name="B" Classes="{clase}" Content="Guardar" />"""), "B");

        Assert.Equal(Hex(fondo), ColorDe(boton.Background));
        Assert.Equal(Hex(borde), ColorDe(boton.BorderBrush));
        Assert.Equal(Hex(texto), ColorDe(boton.Foreground));
        Assert.Equal(36, boton.Bounds.Height);
        Assert.Equal(new CornerRadius(8), boton.CornerRadius);
        Assert.Equal(13, boton.FontSize);
        Assert.Equal(FontWeight.SemiBold, boton.FontWeight);
    }

    [AvaloniaFact]
    public void BotonGhost_NoArrastraNadaDelGhostViejo()
    {
        // Review Focus 1: Button.ghost viejo (Controls.axaml) también matchea dentro de .ui2.
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="ghost" Content="Limpiar filtros" />"""), "B");

        Assert.Equal(Colors.Transparent, ColorDe(boton.Background));
        Assert.Equal(Hex("#44403C"), ColorDe(boton.Foreground));
        Assert.Equal(HorizontalAlignment.Center, boton.HorizontalContentAlignment);
        Assert.Equal(new Thickness(16, 0), boton.Padding);
        Assert.Equal(new Thickness(1), boton.BorderThickness);
        Assert.Equal(36, boton.Bounds.Height);
    }

    [AvaloniaFact]
    public void BotonPrimario_Hover_PintaAcentoHoverEnElPresenter()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="primario" Content="Guardar" />"""), "B");

        ((IPseudoClasses)boton.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#1E40AF"), ColorDe(Parte<ContentPresenter>(boton, "PART_ContentPresenter").Background));
    }

    [AvaloniaFact]
    public void BotonSecundario_Hover_NoUsaElVerdeViejo()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="secundario" Content="Cancelar" />"""), "B");

        ((IPseudoClasses)boton.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<ContentPresenter>(boton, "PART_ContentPresenter").Background));
    }

    [AvaloniaTheory]
    [InlineData("""<TextBox x:Name="C" Text="Tornillo" />""")]
    [InlineData("""<ComboBox x:Name="C" />""")]
    [InlineData("""<NumericUpDown x:Name="C" Value="12" />""")]
    [InlineData("""<CalendarDatePicker x:Name="C" />""")]
    [InlineData("""<AutoCompleteBox x:Name="C" />""")]
    public void Inputs_MidenAlto36(string xaml)
    {
        // Review Focus 5: el TextBox interno de NumericUpDown/CalendarDatePicker recibe los estilos
        // de TextBox; sin el reseteo de su MinHeight el control mide 38.
        Assert.Equal(36, Buscar<Control>(Montar(xaml), "C").Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData("""<TextBox x:Name="C" Width="200" Text="Tornillo" />""")]
    [InlineData("""<ComboBox x:Name="C" Width="200" SelectedIndex="0"><ComboBoxItem>Tornillo</ComboBoxItem></ComboBox>""")]
    [InlineData("""<NumericUpDown x:Name="C" Width="200" Value="340" FormatString="0" />""")]
    [InlineData("""<CalendarDatePicker x:Name="C" Width="200" SelectedDate="2026-09-28" SelectedDateFormat="Custom" CustomDateFormatString="dd/MM/yyyy" />""")]
    public void Inputs_TextoEmpiezaA13DelBordeExterior(string xaml)
    {
        // 1 de borde + 12 de padding, igual que .v2-input del mockup. ComboBox y NumericUpDown
        // necesitan Padding 13 para llegar (su plantilla no descuenta el borde).
        var control = Buscar<Control>(Montar(xaml), "C");
        var texto = control.GetVisualDescendants().OfType<Control>()
            .First(c => c is TextPresenter || (c is TextBlock t && t.Text == "Tornillo"));

        Assert.Equal(13, texto.TranslatePoint(new Point(0, 0), control)!.Value.X);
    }

    [AvaloniaFact]
    public void CalendarDatePicker_GlifoEsIconoMdiSinElDiaDeHoy()
    {
        var picker = Buscar<CalendarDatePicker>(Montar("""<CalendarDatePicker x:Name="C" />"""), "C");
        var boton = Parte<Button>(picker, "PART_Button");

        Assert.DoesNotContain(boton.GetVisualDescendants().OfType<TextBlock>(), t => !string.IsNullOrEmpty(t.Text));
        Assert.Equal("mdi-calendar-blank-outline", Parte<Icon>(boton, "PART_GlifoFecha").Value);
    }

    [AvaloniaFact]
    public void TextBox_BordeInputRadio8Fuente13TextoEtiqueta()
    {
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" Text="Tornillo" />"""), "T");

        Assert.Equal(Hex("#D6D3D1"), ColorDe(entrada.BorderBrush));
        Assert.Equal(Hex("#FFFFFF"), ColorDe(entrada.Background));
        Assert.Equal(Hex("#44403C"), ColorDe(entrada.Foreground));
        Assert.Equal(new CornerRadius(8), entrada.CornerRadius);
        Assert.Equal(13, entrada.FontSize);
        Assert.Equal(new Thickness(12, 0), entrada.Padding);
    }

    [AvaloniaFact]
    public void TextBox_ConFoco_PintaElBordeDeAcento()
    {
        var ventana = Montar("""<TextBox x:Name="T" />""");
        var entrada = Buscar<TextBox>(ventana, "T");
        ventana.Activate();

        entrada.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#1D4ED8"), ColorDe(Parte<Border>(entrada, "PART_BorderElement").BorderBrush));
    }

    [AvaloniaFact]
    public void TextBox_ConErrores_PintaElBordeDeError()
    {
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />"""), "T");

        DataValidationErrors.SetErrors(entrada, new object[] { "Campo requerido" });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#DC2626"), ColorDe(Parte<Border>(entrada, "PART_BorderElement").BorderBrush));
    }

    [AvaloniaFact]
    public void TextBox_DentroDeUi2_ConservaElErrorConverterGlobal()
    {
        // Review Focus 2: el seam de Themes/Controls.axaml tiene que seguir llegando adentro de .ui2.
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />"""), "T");

        Assert.NotNull(DataValidationErrors.GetErrorConverter(entrada));
    }

    [AvaloniaFact]
    public void CheckBox_Casilla16Radio4BordeInput()
    {
        var casilla = Parte<Border>(Buscar<CheckBox>(Montar("""<CheckBox x:Name="C" Content="Activo" />"""), "C"), "NormalRectangle");

        Assert.Equal(16, casilla.Bounds.Width);
        Assert.Equal(16, casilla.Bounds.Height);
        Assert.Equal(new CornerRadius(4), casilla.CornerRadius);
        Assert.Equal(Hex("#D6D3D1"), ColorDe(casilla.BorderBrush));
    }

    [AvaloniaFact]
    public void CheckBox_Marcado_CasillaEnAcento()
    {
        var casilla = Parte<Border>(Buscar<CheckBox>(Montar("""<CheckBox x:Name="C" Content="Activo" IsChecked="True" />"""), "C"), "NormalRectangle");

        Assert.Equal(Hex("#1D4ED8"), ColorDe(casilla.Background));
        Assert.Equal(Hex("#1D4ED8"), ColorDe(casilla.BorderBrush));
    }

    [AvaloniaTheory]
    [InlineData("titulo-pagina", 22, 600, "#1C1917")]
    [InlineData("titulo-seccion", 15, 600, "#1C1917")]
    [InlineData("cuerpo", 14, 400, "#1C1917")]
    [InlineData("texto-secundario", 13, 400, "#57534E")]
    [InlineData("subtitulo-formulario", 13, 600, "#1C1917")]
    [InlineData("etiqueta-campo", 12, 500, "#44403C")]
    [InlineData("pie-conteo", 12, 400, "#78716C")]
    public void Tipografia_ClaseV2_TamanoPesoYColorDeLaSpec(string clase, double tamano, int peso, string color)
    {
        var texto = Buscar<TextBlock>(Montar($"""<TextBlock x:Name="T" Classes="{clase}" Text="Productos" />"""), "T");

        Assert.Equal(tamano, texto.FontSize);
        Assert.Equal((FontWeight)peso, texto.FontWeight);
        Assert.Equal(Hex(color), ColorDe(texto.Foreground));
    }

    [AvaloniaFact]
    public void TituloPagina_AltoDeLinea26()
    {
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="titulo-pagina" Text="Productos" />"""), "T");

        Assert.Equal(26, texto.LineHeight);
    }
}
```

`tests/StockApp.Presentation.UiTests/V2/ControlesV2ConvivenciaTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Review Focus 1: fuera de .ui2 (todas las vistas de hoy) los controles se ven EXACTAMENTE
/// como antes de la Fase 0. Valores tomados de Themes/Tokens.axaml y Themes/Controls.axaml viejos.
/// </summary>
public class ControlesV2ConvivenciaTests
{
    [AvaloniaFact]
    public void BotonPrimaryViejo_FueraDeUi2_SigueVerde()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="primary" Content="Guardar" />""", conScopeUi2: false), "B");

        Assert.Equal(Hex("#15803D"), ColorDe(boton.Background));
        Assert.Equal(new CornerRadius(6), boton.CornerRadius);
    }

    [AvaloniaFact]
    public void BotonGhostViejo_FueraDeUi2_SigueAlineadoALaIzquierda()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="ghost" Content="Inicio" />""", conScopeUi2: false), "B");

        Assert.Equal(HorizontalAlignment.Left, boton.HorizontalContentAlignment);
        Assert.Equal(new Thickness(12, 8), boton.Padding);
        Assert.Equal(new Thickness(0), boton.BorderThickness);
        Assert.Equal(Hex("#0F172A"), ColorDe(boton.Foreground));
    }

    [AvaloniaFact]
    public void TextBoxViejo_FueraDeUi2_SigueConRadio6YBordeViejo()
    {
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />""", conScopeUi2: false), "T");

        Assert.Equal(new CornerRadius(6), entrada.CornerRadius);
        Assert.Equal(Hex("#E2E8F0"), ColorDe(entrada.BorderBrush));
        Assert.Equal(new Thickness(10, 8), entrada.Padding);
    }

    [AvaloniaFact]
    public void ClaseV2FueraDeUi2_NoRecibeEstiloV2()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="primario" Content="Guardar" />""", conScopeUi2: false), "B");

        Assert.NotEqual(Hex("#1D4ED8"), ColorDe(boton.Background));
        Assert.True(double.IsNaN(boton.Height));
    }

    [AvaloniaFact]
    public void TituloPaginaFueraDeUi2_NoRecibeEstiloV2()
    {
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="titulo-pagina" Text="Productos" />""", conScopeUi2: false), "T");

        Assert.NotEqual(22, texto.FontSize);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ControlesV2"`
Expected: FAIL. Compila; los de convivencia pasan (todavía no hay estilos v2) y `ControlesV2Tests` falla, p. ej. `Boton_Variante_...` con `Assert.Equal() Failure: Expected #ff1d4ed8, Actual #ffffffff`.

- [ ] **Step 3: Escribir `Themes/V2/ControlesV2.axaml`**

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:i="https://github.com/projektanker/icons.avalonia">

    <!-- UI v2 (spec 2026-09-28 §3.3): estilos de controles base y tipografía.
         TODO selector empieza con ".ui2 ": solo aplica dentro de una vista migrada (raíz con
         Classes="ui2"). Las vistas viejas no se enteran.
         Se incluye en App.axaml al FINAL: los estilos viejos globales (Button, TextBox,
         Button.ghost de Themes/Controls.axaml) también matchean adentro de .ui2 y gana el último
         declarado, así que cada estilo de acá setea TODAS las propiedades que setea su par viejo.
         Hover, pressed y foco se pintan sobre la parte del template que Fluent pinta
         (PART_ContentPresenter, PART_BorderElement), no sobre el control: un Setter sobre el
         control no le gana al estilo de template de Fluent. -->

    <!-- Tipografía -->
    <Style Selector=".ui2 TextBlock.titulo-pagina">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteTituloPaginaTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteTituloPaginaPeso}" />
        <Setter Property="LineHeight" Value="{DynamicResource V2FuenteTituloPaginaAltoLinea}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.titulo-seccion">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteTituloSeccionTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteTituloSeccionPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.cuerpo">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteCuerpoTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteCuerpoPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.texto-secundario">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteControlPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoSecundarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.subtitulo-formulario">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteSubtituloFormularioTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteSubtituloFormularioPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.etiqueta-campo">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteEtiquetaCampoTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteEtiquetaCampoPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>
    <Style Selector=".ui2 TextBlock.pie-conteo">
        <Setter Property="FontSize" Value="{DynamicResource V2FuentePieConteoTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuentePieConteoPeso}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
    </Style>

    <!-- Botones: geometría común a las cuatro variantes -->
    <Style Selector=".ui2 Button.primario, .ui2 Button.secundario, .ui2 Button.ghost, .ui2 Button.peligro">
        <Setter Property="Height" Value="{DynamicResource V2AltoControl}" />
        <Setter Property="MinHeight" Value="0" />
        <Setter Property="Padding" Value="16,0" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteBotonTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteBotonPeso}" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Opacity" Value="1" />
    </Style>

    <Style Selector=".ui2 Button.primario">
        <Setter Property="Background" Value="{DynamicResource V2AcentoBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2AcentoBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoSobreAcentoBrush}" />
    </Style>
    <Style Selector=".ui2 Button.primario:pointerover /template/ ContentPresenter#PART_ContentPresenter, .ui2 Button.primario:pressed /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource V2AcentoHoverBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2AcentoHoverBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoSobreAcentoBrush}" />
    </Style>

    <Style Selector=".ui2 Button.secundario">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>
    <Style Selector=".ui2 Button.secundario:pointerover /template/ ContentPresenter#PART_ContentPresenter, .ui2 Button.secundario:pressed /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>

    <Style Selector=".ui2 Button.ghost">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>
    <Style Selector=".ui2 Button.ghost:pointerover /template/ ContentPresenter#PART_ContentPresenter, .ui2 Button.ghost:pressed /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>

    <Style Selector=".ui2 Button.peligro">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2PeligroTextoBrush}" />
    </Style>
    <Style Selector=".ui2 Button.peligro:pointerover /template/ ContentPresenter#PART_ContentPresenter, .ui2 Button.peligro:pressed /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2PeligroTextoBrush}" />
    </Style>

    <Style Selector=".ui2 Button.primario:disabled /template/ ContentPresenter#PART_ContentPresenter, .ui2 Button.secundario:disabled /template/ ContentPresenter#PART_ContentPresenter, .ui2 Button.peligro:disabled /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="{DynamicResource V2DeshabilitadoFondoBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2DeshabilitadoTextoBrush}" />
    </Style>
    <Style Selector=".ui2 Button.ghost:disabled /template/ ContentPresenter#PART_ContentPresenter">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="Foreground" Value="{DynamicResource V2DeshabilitadoTextoBrush}" />
    </Style>

    <!-- Inputs: 36 de alto, borde input, radio 8, texto 13 en color etiqueta -->
    <Style Selector=".ui2 TextBox">
        <Setter Property="MinHeight" Value="{DynamicResource V2AltoControl}" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
        <Setter Property="PlaceholderForeground" Value="{DynamicResource V2TextoTerciarioBrush}" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
    </Style>
    <Style Selector=".ui2 TextBox[AcceptsReturn=True]">
        <Setter Property="Padding" Value="12,8" />
        <Setter Property="VerticalContentAlignment" Value="Top" />
    </Style>
    <Style Selector=".ui2 TextBox:pointerover /template/ Border#PART_BorderElement">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
    </Style>
    <Style Selector=".ui2 TextBox:focus /template/ Border#PART_BorderElement">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2AcentoBrush}" />
        <Setter Property="BorderThickness" Value="1" />
    </Style>
    <Style Selector=".ui2 TextBox:error /template/ Border#PART_BorderElement">
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeErrorBrush}" />
    </Style>

    <!-- Un estilo por tipo, NO un selector múltiple: con ".ui2 ComboBox, .ui2 NumericUpDown" el
         compilador resuelve las propiedades contra el ancestro común (TemplatedControl) y
         VerticalContentAlignment no existe ahí.
         Padding 13 en ComboBox y NumericUpDown: sus plantillas Fluent no descuentan el borde de 1
         (medido: con Padding 12 el texto arranca en x=12, en TextBox y CalendarDatePicker en x=13).
         13 = los 12 de diseño + 1 de borde; custodiado por Inputs_TextoEmpiezaA13DelBordeExterior. -->
    <Style Selector=".ui2 ComboBox">
        <Setter Property="Height" Value="{DynamicResource V2AltoControl}" />
        <Setter Property="MinHeight" Value="0" />
        <Setter Property="Padding" Value="13,0" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
    </Style>
    <Style Selector=".ui2 NumericUpDown">
        <Setter Property="Height" Value="{DynamicResource V2AltoControl}" />
        <Setter Property="MinHeight" Value="0" />
        <Setter Property="Padding" Value="13,0" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
        <Setter Property="ShowButtonSpinner" Value="False" />
    </Style>
    <Style Selector=".ui2 CalendarDatePicker">
        <Setter Property="Height" Value="{DynamicResource V2AltoControl}" />
        <Setter Property="MinHeight" Value="0" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
    </Style>
    <!-- El botón de calendario de Fluent dibuja el NÚMERO DEL DÍA DE HOY (medido: un TextBlock
         con la fecha actual dentro de PART_Button): un render que cambia con la fecha no se puede
         comparar contra un mockup fijo. Se reemplaza su Template por un ícono MDI. Fluent le asigna
         a PART_Button un Theme, no un Template local, así que este Setter de estilo gana. El ancho
         20 es el mismo que el botón de Fluent. -->
    <Style Selector=".ui2 CalendarDatePicker /template/ Button#PART_Button">
        <Setter Property="Template">
            <ControlTemplate>
                <Border Background="Transparent" Width="20">
                    <i:Icon x:Name="PART_GlifoFecha"
                            Value="mdi-calendar-blank-outline"
                            FontSize="16"
                            Foreground="{DynamicResource V2TextoTerciarioBrush}"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center" />
                </Border>
            </ControlTemplate>
        </Setter>
    </Style>
    <Style Selector=".ui2 ComboBox /template/ PathIcon#DropDownGlyph">
        <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
    </Style>
    <!-- El TextBox interno ya está dentro del borde del control: sin esto recibe el MinHeight 36 y
         el borde de ".ui2 TextBox" y el control termina midiendo 38 con doble borde. -->
    <Style Selector=".ui2 NumericUpDown /template/ TextBox#PART_TextBox, .ui2 CalendarDatePicker /template/ TextBox#PART_TextBox">
        <Setter Property="MinHeight" Value="0" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Background" Value="Transparent" />
    </Style>
    <Style Selector=".ui2 ComboBox:pointerover /template/ Border#Background">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
    </Style>
    <Style Selector=".ui2 ComboBox:focus /template/ Border#Background">
        <Setter Property="BorderBrush" Value="{DynamicResource V2AcentoBrush}" />
    </Style>
    <Style Selector=".ui2 ComboBox:error /template/ Border#Background">
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeErrorBrush}" />
    </Style>

    <Style Selector=".ui2 AutoCompleteBox">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>

    <!-- CheckBox: casilla de 16 con radio 4 (mockup .pt-checkbox-box) -->
    <Style Selector=".ui2 CheckBox">
        <Setter Property="MinHeight" Value="{DynamicResource V2AltoControl}" />
        <Setter Property="MinWidth" Value="0" />
        <Setter Property="Padding" Value="8,0,0,0" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>
    <Style Selector=".ui2 CheckBox /template/ Border#NormalRectangle, .ui2 CheckBox:pointerover /template/ Border#NormalRectangle">
        <Setter Property="Width" Value="16" />
        <Setter Property="Height" Value="16" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioCasilla}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeInputBrush}" />
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
    </Style>
    <Style Selector=".ui2 CheckBox:checked /template/ Border#NormalRectangle, .ui2 CheckBox:checked:pointerover /template/ Border#NormalRectangle">
        <Setter Property="BorderBrush" Value="{DynamicResource V2AcentoBrush}" />
        <Setter Property="Background" Value="{DynamicResource V2AcentoBrush}" />
    </Style>
    <Style Selector=".ui2 CheckBox:checked /template/ Path#CheckGlyph">
        <Setter Property="Fill" Value="{DynamicResource V2TextoSobreAcentoBrush}" />
    </Style>

</Styles>
```

Nota para quien ejecute: los nombres de partes de template (`PART_ContentPresenter`, `PART_BorderElement`, `Border#Background` y `PathIcon#DropDownGlyph` del `ComboBox`, `NormalRectangle`, `Path#CheckGlyph`, `PART_TextBox`, `Button#PART_Button` del `CalendarDatePicker`) se verificaron decompilando `Avalonia.Themes.Fluent.dll` 12.0.5 con `ilspycmd -o <dir> ~/.nuget/packages/avalonia.themes.fluent/12.0.5/lib/net10.0/Avalonia.Themes.Fluent.dll -t "CompiledAvaloniaXaml.!AvaloniaResources"` y `grep -o 'Register("<nombre>"'`. Si un test de parte falla con `Sequence contains no matching element`, repetir ese grep para confirmar el tipo del elemento (por ejemplo `Path` vs `PathIcon`) y ajustar SOLO el tipo del selector.

- [ ] **Step 4: Incluirlo en la app y en los dos bancos de prueba**

En `src/StockApp.Presentation/App.axaml`, después de `<StyleInclude Source="avares://GestionMunicipal/Themes/DataGrid.axaml"/>`:

```xml
        <!-- UI v2: estilos de controles base bajo .ui2, AL FINAL para pisar a los viejos dentro de
             las vistas migradas. Fuera de .ui2 no matchean nada. -->
        <StyleInclude Source="avares://GestionMunicipal/Themes/V2/ControlesV2.axaml"/>
```

En `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` y en `tests/StockApp.Presentation.VisualTests/VisualTestApp.cs`, agregar al final del array `StyleIncludes`:

```csharp
        "avares://GestionMunicipal/Themes/V2/ControlesV2.axaml",
```

- [ ] **Step 5: CSS de mockups de tipografía, botones e inputs**

Agregar al final de `docs/ui-v2/mockups/_base/base.css`:

```css
/* Tipografía (ControlesV2.axaml, clases de TextBlock) */
.v2-titulo-pagina { font-size: var(--v2-fuente-titulo-pagina-tamano); font-weight: var(--v2-fuente-titulo-pagina-peso); line-height: var(--v2-fuente-titulo-pagina-alto-linea); color: var(--v2-color-texto-primario); }
.v2-titulo-seccion { font-size: var(--v2-fuente-titulo-seccion-tamano); line-height: var(--v2-fuente-titulo-seccion-alto-linea); font-weight: var(--v2-fuente-titulo-seccion-peso); color: var(--v2-color-texto-primario); }
.v2-cuerpo { font-size: var(--v2-fuente-cuerpo-tamano); line-height: var(--v2-fuente-cuerpo-alto-linea); font-weight: var(--v2-fuente-cuerpo-peso); color: var(--v2-color-texto-primario); }
.v2-texto-secundario { font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); font-weight: var(--v2-fuente-control-peso); color: var(--v2-color-texto-secundario); }
.v2-subtitulo-formulario { font-size: var(--v2-fuente-subtitulo-formulario-tamano); line-height: var(--v2-fuente-subtitulo-formulario-alto-linea); font-weight: var(--v2-fuente-subtitulo-formulario-peso); color: var(--v2-color-texto-primario); }
.v2-etiqueta-campo { font-size: var(--v2-fuente-etiqueta-campo-tamano); line-height: var(--v2-fuente-etiqueta-campo-alto-linea); font-weight: var(--v2-fuente-etiqueta-campo-peso); color: var(--v2-color-texto-etiqueta); }
.v2-pie-conteo { font-size: var(--v2-fuente-pie-conteo-tamano); line-height: var(--v2-fuente-pie-conteo-alto-linea); font-weight: var(--v2-fuente-pie-conteo-peso); color: var(--v2-color-texto-terciario); }

/* Botones (ControlesV2.axaml, Button.primario|secundario|ghost|peligro) */
.v2-boton {
  display: inline-flex; align-items: center; justify-content: center;
  height: var(--v2-alto-control); padding: 0 16px;
  border: 1px solid transparent; border-radius: var(--v2-radio-control);
  font-family: inherit; font-size: var(--v2-fuente-boton-tamano); line-height: var(--v2-fuente-boton-alto-linea); font-weight: var(--v2-fuente-boton-peso);
  white-space: nowrap;
}
.v2-boton--primario { background: var(--v2-color-acento); border-color: var(--v2-color-acento); color: var(--v2-color-texto-sobre-acento); }
.v2-boton--secundario { background: var(--v2-color-superficie); border-color: var(--v2-color-borde-input); color: var(--v2-color-texto-etiqueta); }
.v2-boton--ghost { background: transparent; border-color: transparent; color: var(--v2-color-texto-etiqueta); }
.v2-boton--peligro { background: var(--v2-color-superficie); border-color: var(--v2-color-borde-input); color: var(--v2-color-peligro-texto); }

/* Inputs (TextBox, ComboBox, NumericUpDown, CalendarDatePicker, AutoCompleteBox) */
.v2-input {
  display: flex; align-items: center; justify-content: space-between;
  height: var(--v2-alto-control); padding: 0 12px;
  border: 1px solid var(--v2-color-borde-input); border-radius: var(--v2-radio-control);
  background: var(--v2-color-superficie);
  font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-etiqueta);
  white-space: nowrap; overflow: hidden;
}
.v2-input--error { border-color: var(--v2-color-borde-error); }
.v2-input-placeholder { color: var(--v2-color-texto-terciario); }
/* Glifos a la derecha, con la geometría MEDIDA en el render de Avalonia:
   ComboBox: PathIcon 12x12 de Fluent a 10px del borde exterior derecho. Con borde 1 + padding 12
   el borde del contenido queda a 13, de ahí el margin-right de -3. Se dibuja con este SVG (mismo
   path que Fluent, mismo ajuste uniforme):
     <svg class="v2-glifo-combo" viewBox="19 486 2010 1095"><path fill="currentColor"
       d="M1939 486L2029 576L1024 1581L19 576L109 486L1024 1401L1939 486Z"/></svg>
   CalendarDatePicker: ícono mdi-calendar-blank-outline de 16 centrado en un botón de 20 que
   Fluent ubica a 4px del borde exterior: borde derecho del ícono a 6px, margin-right de -7.
     dotnet run --project tools/StockApp.UiV2.Herramientas -- icono-svg mdi-calendar-blank-outline */
.v2-glifo-combo { display: block; flex: 0 0 12px; width: 12px; height: 12px; margin-right: -3px; color: var(--v2-color-texto-terciario); }
.v2-glifo-fecha { display: block; flex: 0 0 16px; margin-right: -7px; color: var(--v2-color-texto-terciario); }

/* CheckBox. Geometría MEDIDA en el render de Avalonia: la plantilla Fluent reserva una caja de
   20x20 (la casilla de 16 va centrada adentro) y el tilde es un Path de 9 x 6.69 en (4.375, 5.82)
   de esa caja. El texto arranca a 20 + 8 de padding. */
.v2-casilla-fila { display: flex; align-items: center; gap: 8px; height: var(--v2-alto-control); font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-etiqueta); }
.v2-casilla-caja { position: relative; display: flex; align-items: center; justify-content: center; flex: 0 0 20px; width: 20px; height: 20px; }
.v2-casilla {
  width: 16px; height: 16px; border: 1px solid var(--v2-color-borde-input);
  border-radius: var(--v2-radio-casilla); background: var(--v2-color-superficie);
}
.v2-casilla--marcada { background: var(--v2-color-acento); border-color: var(--v2-color-acento); }
/* Tilde: mismo path que CheckMarkPathData de Fluent.
     <svg class="v2-glifo-check" viewBox="0.012 0.012 17.475 12.988"><path fill="currentColor"
       d="M5.5 10.586 1.707 6.793A1 1 0 0 0 .293 8.207l4.5 4.5a1 1 0 0 0 1.414 0l11-11A1 1 0 0 0 15.793.293L5.5 10.586Z"/></svg> */
.v2-glifo-check { position: absolute; left: 4.375px; top: 5.82px; width: 9px; height: 6.69px; color: var(--v2-color-texto-sobre-acento); }
```

- [ ] **Step 6: Correr y ver el verde (UiTests completo: la convivencia se prueba con toda la suite de UI)**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, `Failed: 0` (incluye los tests viejos: `ControlesConsumenTokensTests`, `ButtonGhostContrasteTests`, `SidebarContrasteTests`, etc., prueba de que nada viejo cambió).
Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: PASS (el guardián de includes de VisualTests ve el nuevo `StyleInclude`).

- [ ] **Step 7: Verificación por mutación del scope**

En `ControlesV2.axaml`, en el selector de geometría común (`.ui2 Button.primario, .ui2 Button.secundario, .ui2 Button.ghost, .ui2 Button.peligro`), quitar el prefijo `.ui2 ` SOLO del tramo del ghost (queda `Button.ghost`). Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ControlesV2ConvivenciaTests"`. Expected: FAIL en `BotonGhostViejo_FueraDeUi2_SigueAlineadoALaIzquierda` (`Expected: Left, Actual: Center`). Restaurar el prefijo; PASS. Repetir quitando el `.ui2 ` del estilo de color `.ui2 Button.ghost`: FAIL en el mismo test por el `Foreground` (`#0F172A` esperado). Restaurar; PASS.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Themes/V2/ControlesV2.axaml src/StockApp.Presentation/App.axaml tests/StockApp.Presentation.UiTests tests/StockApp.Presentation.VisualTests docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega estilos v2 de tipografía, botones e inputs bajo el scope ui2"
```

### Task 11: Componentes `Insignia` y `Alerta` (y el diccionario `ComponentesV2.axaml`)

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/Variantes.cs`
- Create: `src/StockApp.Presentation/Controls/V2/Insignia.cs`
- Create: `src/StockApp.Presentation/Controls/V2/Alerta.cs`
- Create: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml`
- Modify: `src/StockApp.Presentation/App.axaml` (`MergedDictionaries`)
- Modify: `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` (`ResourceIncludes`)
- Modify: `tests/StockApp.Presentation.VisualTests/VisualTestApp.cs` (`ResourceIncludes`)
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/InsigniaAlertaTests.cs`

**Interfaces:**
- Consumes: claves `V2*`, `MontajeV2`.
- Produces:
  - `enum VarianteInsignia { Neutro, Advertencia, Peligro, Exito }` (default `Neutro`).
  - `enum VarianteAlerta { Info, Advertencia, Error }` (default `Info`).
  - `Insignia : TemplatedControl` — `string? Texto`, `VarianteInsignia Variante`; partes `PART_Fondo` (`Border`), `PART_Texto` (`TextBlock`).
  - `Alerta : TemplatedControl` — `string? Texto`, `VarianteAlerta Variante`; partes `PART_Marco` (`Border`), `PART_Texto` (`TextBlock`).
  - `Controls/V2/ComponentesV2.axaml`: `ResourceDictionary` con `xmlns:v2` y `xmlns:i` al que las Tasks 12 a 15 agregan `ControlTheme` antes de `</ResourceDictionary>`.
  - CSS: `v2-insignia` + `v2-insignia--{advertencia|peligro|exito}`, `v2-alerta` + `v2-alerta--{info|advertencia|error}`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/InsigniaAlertaTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class InsigniaAlertaTests
{
    [AvaloniaTheory]
    [InlineData("Neutro", "#F5F5F4", "#44403C")]
    [InlineData("Advertencia", "#FEF3C7", "#92400E")]
    [InlineData("Peligro", "#FEE2E2", "#991B1B")]
    [InlineData("Exito", "#DCFCE7", "#14532D")]
    public void Insignia_Variante_ColoresDeLaSpec(string variante, string fondo, string texto)
    {
        var insignia = Buscar<Insignia>(Montar($"""<v2:Insignia x:Name="I" Texto="Activo" Variante="{variante}" />"""), "I");

        Assert.Equal(Hex(fondo), ColorDe(Parte<Border>(insignia, "PART_Fondo").Background));
        Assert.Equal(Hex(texto), ColorDe(Parte<TextBlock>(insignia, "PART_Texto").Foreground));
    }

    [AvaloniaFact]
    public void Insignia_PillConPadding2x8Fuente12MediumAlineadaALaIzquierda()
    {
        var insignia = Buscar<Insignia>(Montar("""<v2:Insignia x:Name="I" Texto="Activo" />"""), "I");
        var fondo = Parte<Border>(insignia, "PART_Fondo");
        var texto = Parte<TextBlock>(insignia, "PART_Texto");

        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);
        Assert.Equal(new CornerRadius(999), fondo.CornerRadius);
        Assert.Equal(new Thickness(8, 2), fondo.Padding);
        Assert.Equal("Activo", texto.Text);
        Assert.Equal(12, texto.FontSize);
        Assert.Equal(FontWeight.Medium, texto.FontWeight);
        Assert.Equal(HorizontalAlignment.Left, insignia.HorizontalAlignment);
    }

    [AvaloniaFact]
    public void Insignia_FueraDeUi2_SeVeIgual()
    {
        // Un componente v2 no depende del scope: se ve bien aunque su vista todavía no migró.
        var insignia = Buscar<Insignia>(Montar("""<v2:Insignia x:Name="I" Texto="Stock negativo" Variante="Peligro" />""", conScopeUi2: false), "I");

        Assert.Equal(Hex("#FEE2E2"), ColorDe(Parte<Border>(insignia, "PART_Fondo").Background));
    }

    [AvaloniaTheory]
    [InlineData("Info", "#DBEAFE", "#DBEAFE", "#1E3A8A")]
    [InlineData("Advertencia", "#FEF3C7", "#FDE68A", "#92400E")]
    [InlineData("Error", "#FEE2E2", "#FCA5A5", "#991B1B")]
    public void Alerta_Variante_ColoresDeLaSpec(string variante, string fondo, string borde, string texto)
    {
        var alerta = Buscar<Alerta>(Montar($"""<v2:Alerta x:Name="A" Texto="No se pudo guardar." Variante="{variante}" />"""), "A");
        var marco = Parte<Border>(alerta, "PART_Marco");

        Assert.Equal(Hex(fondo), ColorDe(marco.Background));
        Assert.Equal(Hex(borde), ColorDe(marco.BorderBrush));
        Assert.Equal(Hex(texto), ColorDe(Parte<TextBlock>(alerta, "PART_Texto").Foreground));
    }

    [AvaloniaFact]
    public void Alerta_Radio8Padding16x12Borde1Fuente13ConAjuste()
    {
        var alerta = Buscar<Alerta>(Montar("""<v2:Alerta x:Name="A" Texto="Texto largo de la alerta" />"""), "A");
        var marco = Parte<Border>(alerta, "PART_Marco");
        var texto = Parte<TextBlock>(alerta, "PART_Texto");

        Assert.Equal(VarianteAlerta.Info, alerta.Variante);
        Assert.Equal(new CornerRadius(8), marco.CornerRadius);
        Assert.Equal(new Thickness(16, 12), marco.Padding);
        Assert.Equal(new Thickness(1), marco.BorderThickness);
        Assert.Equal(13, texto.FontSize);
        Assert.Equal(TextWrapping.Wrap, texto.TextWrapping);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~InsigniaAlertaTests"`
Expected: FAIL de compilación, `The type or namespace name 'V2' does not exist in the namespace 'StockApp.Presentation.Controls'`.

- [ ] **Step 2: Implementar los tipos**

`src/StockApp.Presentation/Controls/V2/Variantes.cs`:

```csharp
namespace StockApp.Presentation.Controls.V2;

/// <summary>Variantes de Insignia (spec §2.1, tabla de insignias).</summary>
public enum VarianteInsignia
{
    Neutro,
    Advertencia,
    Peligro,
    Exito,
}

/// <summary>Variantes de Alerta (spec §2.1, tabla de alertas).</summary>
public enum VarianteAlerta
{
    Info,
    Advertencia,
    Error,
}
```

`src/StockApp.Presentation/Controls/V2/Insignia.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Pill de estado: palabra + color (spec §3.3). Reemplaza a Controls.BadgeEstado cuando la
/// Fase 9 borre el sistema viejo.
/// </summary>
public class Insignia : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<Insignia, string?>(nameof(Texto));

    public static readonly StyledProperty<VarianteInsignia> VarianteProperty =
        AvaloniaProperty.Register<Insignia, VarianteInsignia>(nameof(Variante), VarianteInsignia.Neutro);

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public VarianteInsignia Variante
    {
        get => GetValue(VarianteProperty);
        set => SetValue(VarianteProperty, value);
    }
}
```

`src/StockApp.Presentation/Controls/V2/Alerta.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>Mensaje de info, advertencia o error (spec §3.3).</summary>
public class Alerta : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<Alerta, string?>(nameof(Texto));

    public static readonly StyledProperty<VarianteAlerta> VarianteProperty =
        AvaloniaProperty.Register<Alerta, VarianteAlerta>(nameof(Variante), VarianteAlerta.Info);

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public VarianteAlerta Variante
    {
        get => GetValue(VarianteProperty);
        set => SetValue(VarianteProperty, value);
    }
}
```

- [ ] **Step 3: Escribir `Controls/V2/ComponentesV2.axaml`**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:v2="using:StockApp.Presentation.Controls.V2"
                    xmlns:i="https://github.com/projektanker/icons.avalonia">

    <!-- UI v2 (spec 2026-09-28 §3.3): ControlTheme de los componentes de Controls/V2.
         Tipos nuevos: no chocan con los ControlTheme de Controls/Componentes.axaml.
         Cada template tiene su espejo CSS en docs/ui-v2/mockups/_base/base.css (clases v2-*):
         si cambia la geometría acá, cambia allá en el mismo commit.
         Los colores que dependen de la variante van en estilos anidados sobre la parte, nunca
         como valor local en el template: un valor local del template no lo pisa ningún estilo. -->

    <ControlTheme x:Key="{x:Type v2:Insignia}" TargetType="v2:Insignia">
        <Setter Property="HorizontalAlignment" Value="Left" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <ControlTemplate>
                <!-- Padding 2x8: única excepción de la spec a la base 4 (§2.2). -->
                <Border x:Name="PART_Fondo"
                        CornerRadius="{DynamicResource V2RadioInsignia}"
                        Padding="8,2">
                    <TextBlock x:Name="PART_Texto"
                               Text="{TemplateBinding Texto}"
                               FontSize="{DynamicResource V2FuenteInsigniaTamano}"
                               FontWeight="{DynamicResource V2FuenteInsigniaPeso}" />
                </Border>
            </ControlTemplate>
        </Setter>

        <Style Selector="^ /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="{DynamicResource V2InsigniaNeutroFondoBrush}" />
        </Style>
        <Style Selector="^ /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2InsigniaNeutroTextoBrush}" />
        </Style>
        <Style Selector="^[Variante=Advertencia] /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="{DynamicResource V2InsigniaAdvertenciaFondoBrush}" />
        </Style>
        <Style Selector="^[Variante=Advertencia] /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2InsigniaAdvertenciaTextoBrush}" />
        </Style>
        <Style Selector="^[Variante=Peligro] /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="{DynamicResource V2InsigniaPeligroFondoBrush}" />
        </Style>
        <Style Selector="^[Variante=Peligro] /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2InsigniaPeligroTextoBrush}" />
        </Style>
        <Style Selector="^[Variante=Exito] /template/ Border#PART_Fondo">
            <Setter Property="Background" Value="{DynamicResource V2InsigniaExitoFondoBrush}" />
        </Style>
        <Style Selector="^[Variante=Exito] /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2InsigniaExitoTextoBrush}" />
        </Style>
    </ControlTheme>

    <ControlTheme x:Key="{x:Type v2:Alerta}" TargetType="v2:Alerta">
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="PART_Marco"
                        BorderThickness="1"
                        CornerRadius="{DynamicResource V2RadioControl}"
                        Padding="16,12">
                    <TextBlock x:Name="PART_Texto"
                               Text="{TemplateBinding Texto}"
                               TextWrapping="Wrap"
                               FontSize="{DynamicResource V2FuenteControlTamano}"
                               FontWeight="{DynamicResource V2FuenteControlPeso}" />
                </Border>
            </ControlTemplate>
        </Setter>

        <Style Selector="^ /template/ Border#PART_Marco">
            <Setter Property="Background" Value="{DynamicResource V2AlertaInfoFondoBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource V2AlertaInfoBordeBrush}" />
        </Style>
        <Style Selector="^ /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2AlertaInfoTextoBrush}" />
        </Style>
        <Style Selector="^[Variante=Advertencia] /template/ Border#PART_Marco">
            <Setter Property="Background" Value="{DynamicResource V2AlertaAdvertenciaFondoBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource V2AlertaAdvertenciaBordeBrush}" />
        </Style>
        <Style Selector="^[Variante=Advertencia] /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2AlertaAdvertenciaTextoBrush}" />
        </Style>
        <Style Selector="^[Variante=Error] /template/ Border#PART_Marco">
            <Setter Property="Background" Value="{DynamicResource V2AlertaErrorFondoBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource V2AlertaErrorBordeBrush}" />
        </Style>
        <Style Selector="^[Variante=Error] /template/ TextBlock#PART_Texto">
            <Setter Property="Foreground" Value="{DynamicResource V2AlertaErrorTextoBrush}" />
        </Style>
    </ControlTheme>

</ResourceDictionary>
```

- [ ] **Step 4: Incluir el diccionario en la app y en los dos bancos**

En `src/StockApp.Presentation/App.axaml`, después de `<ResourceInclude Source="avares://GestionMunicipal/Controls/Componentes.axaml"/>`:

```xml
                <!-- UI v2: ControlTheme de los componentes de Controls/V2 (tipos nuevos). -->
                <ResourceInclude Source="avares://GestionMunicipal/Controls/V2/ComponentesV2.axaml"/>
```

En `TestAppBuilder.cs` y en `VisualTestApp.cs`, agregar al final del array `ResourceIncludes`:

```csharp
        "avares://GestionMunicipal/Controls/V2/ComponentesV2.axaml",
```

- [ ] **Step 5: CSS de mockups**

Agregar al final de `docs/ui-v2/mockups/_base/base.css`:

```css
/* Insignia (Controls/V2/Insignia). Padding 2x8: excepción de la spec §2.2. */
.v2-insignia {
  display: inline-flex; align-items: center; flex: 0 0 auto;
  padding: 2px 8px; border-radius: var(--v2-radio-insignia);
  font-size: var(--v2-fuente-insignia-tamano); line-height: var(--v2-fuente-insignia-alto-linea); font-weight: var(--v2-fuente-insignia-peso);
  white-space: nowrap;
  background: var(--v2-color-insignia-neutro-fondo); color: var(--v2-color-insignia-neutro-texto);
}
.v2-insignia--advertencia { background: var(--v2-color-insignia-advertencia-fondo); color: var(--v2-color-insignia-advertencia-texto); }
.v2-insignia--peligro { background: var(--v2-color-insignia-peligro-fondo); color: var(--v2-color-insignia-peligro-texto); }
.v2-insignia--exito { background: var(--v2-color-insignia-exito-fondo); color: var(--v2-color-insignia-exito-texto); }

/* Alerta (Controls/V2/Alerta) */
.v2-alerta {
  padding: 12px 16px; border: 1px solid; border-radius: var(--v2-radio-control);
  font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); font-weight: var(--v2-fuente-control-peso);
}
.v2-alerta--info { background: var(--v2-color-alerta-info-fondo); border-color: var(--v2-color-alerta-info-borde); color: var(--v2-color-alerta-info-texto); }
.v2-alerta--advertencia { background: var(--v2-color-alerta-advertencia-fondo); border-color: var(--v2-color-alerta-advertencia-borde); color: var(--v2-color-alerta-advertencia-texto); }
.v2-alerta--error { background: var(--v2-color-alerta-error-fondo); border-color: var(--v2-color-alerta-error-borde); color: var(--v2-color-alerta-error-texto); }
```

- [ ] **Step 6: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, `Failed: 0` (incluye `GuardianDePatronTests.Guardian_CubreTodasLasVistasDelEnsamblado`: el namespace `Controls.V2` no empieza con `Views`, no lo afecta).
Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~IncludesAppAxamlTests"`
Expected: PASS.

- [ ] **Step 7: Verificación por mutación**

Quitar de `ComponentesV2.axaml` el par de estilos `^[Variante=Peligro]` (los dos `Style`). Expected: FAIL en `Insignia_Variante_ColoresDeLaSpec(variante: "Peligro", ...)` y `Insignia_FueraDeUi2_SeVeIgual`. Restaurar; PASS.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2 src/StockApp.Presentation/App.axaml tests/StockApp.Presentation.UiTests tests/StockApp.Presentation.VisualTests docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega componentes Insignia y Alerta con sus variantes"
```

### Task 12: `TarjetaContenido` y `EncabezadoPagina` (con breadcrumb)

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/TarjetaContenido.cs`
- Create: `src/StockApp.Presentation/Controls/V2/EncabezadoPagina.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml`
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/ComandoContador.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/TarjetaContenidoEncabezadoTests.cs`

**Interfaces:**
- Consumes: `MontajeV2` (incluido `Clic`), claves `V2*`.
- Produces:
  - `TarjetaContenido : ContentControl` — `Padding` default 16; parte `PART_Marco` (`Border`).
  - `EncabezadoPagina : TemplatedControl` — `string? Titulo`, `string? Subtitulo`, `string? BreadcrumbOrigen`, `ICommand? BreadcrumbComando`, `string? BreadcrumbActual`, `[Content] object? Acciones`; `Margin` default `0,0,0,16`; partes `PART_Breadcrumb` (`StackPanel`), `PART_BreadcrumbOrigen` (`Button`), `PART_BreadcrumbSeparador`, `PART_BreadcrumbActual`, `PART_Titulo`, `PART_Subtitulo` (`TextBlock`), `PART_Acciones` (`ContentPresenter`).
  - `ControlTheme` con clave `V2TemaEnlace` (`TargetType="Button"`): botón con apariencia de link (texto acento 13/500, sin fondo ni borde).
  - `ComandoContador : ICommand` (UiTests) con `int Ejecuciones`.
  - CSS: `v2-tarjeta`, `v2-encabezado`, `v2-breadcrumb`, `v2-breadcrumb-origen`, `v2-encabezado-fila`, `v2-encabezado-subtitulo`, `v2-acciones`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ComandoContador.cs`:

```csharp
using System;
using System.Windows.Input;

namespace StockApp.Presentation.UiTests.V2;

internal sealed class ComandoContador : ICommand
{
    public int Ejecuciones { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => Ejecuciones++;
}
```

`tests/StockApp.Presentation.UiTests/V2/TarjetaContenidoEncabezadoTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class TarjetaContenidoEncabezadoTests
{
    [AvaloniaFact]
    public void TarjetaContenido_SuperficieBordeRadio10SombraUnicaPadding16()
    {
        var tarjeta = Buscar<TarjetaContenido>(Montar("""<v2:TarjetaContenido x:Name="T"><TextBlock Text="Hola" /></v2:TarjetaContenido>"""), "T");
        var marco = Parte<Border>(tarjeta, "PART_Marco");

        Assert.Equal(Hex("#FFFFFF"), ColorDe(marco.Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(marco.BorderBrush));
        Assert.Equal(new Thickness(1), marco.BorderThickness);
        Assert.Equal(new CornerRadius(10), marco.CornerRadius);
        Assert.Equal(BoxShadows.Parse("0 1 2 0 #0D000000"), marco.BoxShadow);
        Assert.Equal(new Thickness(16), marco.Padding);
    }

    [AvaloniaFact]
    public void TarjetaContenido_PaddingPropio_SeRespeta()
    {
        var tarjeta = Buscar<TarjetaContenido>(Montar("""<v2:TarjetaContenido x:Name="T" Padding="24"><TextBlock Text="Hola" /></v2:TarjetaContenido>"""), "T");

        Assert.Equal(new Thickness(24), Parte<Border>(tarjeta, "PART_Marco").Padding);
    }

    [AvaloniaFact]
    public void TarjetaContenido_ControlNombradoAdentro_SeEncuentraDesdeLaVentana()
    {
        // ContentControl y no UserControl: el contenido queda en el NameScope de la vista, como
        // lo necesitan los code-behind que hacen FindControl.
        var ventana = Montar("""<v2:TarjetaContenido><TextBox x:Name="Adentro" /></v2:TarjetaContenido>""");

        Assert.NotNull(ventana.FindControl<TextBox>("Adentro"));
    }

    [AvaloniaFact]
    public void Encabezado_TituloYSubtituloConLaTipografiaDeLaSpec()
    {
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Productos" Subtitulo="128 productos activos" />"""), "E");
        var titulo = Parte<TextBlock>(encabezado, "PART_Titulo");
        var subtitulo = Parte<TextBlock>(encabezado, "PART_Subtitulo");

        Assert.Equal("Productos", titulo.Text);
        Assert.Equal(22, titulo.FontSize);
        Assert.Equal(FontWeight.SemiBold, titulo.FontWeight);
        Assert.Equal(26, titulo.LineHeight);
        Assert.Equal(Hex("#1C1917"), ColorDe(titulo.Foreground));
        Assert.Equal(13, subtitulo.FontSize);
        Assert.Equal(Hex("#57534E"), ColorDe(subtitulo.Foreground));
        Assert.Equal(new Thickness(0, 4, 0, 0), subtitulo.Margin);
        Assert.Equal(new Thickness(0, 0, 0, 16), encabezado.Margin);
    }

    [AvaloniaFact]
    public void Encabezado_SinBreadcrumbNiSubtitulo_NoLosMuestra()
    {
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Productos" />"""), "E");

        Assert.False(Parte<StackPanel>(encabezado, "PART_Breadcrumb").IsVisible);
        Assert.False(Parte<TextBlock>(encabezado, "PART_Subtitulo").IsVisible);
    }

    [AvaloniaFact]
    public void Encabezado_ClickEnElOrigenDelBreadcrumb_EjecutaElComando()
    {
        var ventana = Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Nuevo gasto" BreadcrumbOrigen="Gastos y facturas" BreadcrumbActual="Nuevo gasto" />""");
        var encabezado = Buscar<EncabezadoPagina>(ventana, "E");
        var comando = new ComandoContador();
        encabezado.BreadcrumbComando = comando;
        Dispatcher.UIThread.RunJobs();

        Clic(ventana, Parte<Button>(encabezado, "PART_BreadcrumbOrigen"));

        Assert.Equal(1, comando.Ejecuciones);
        Assert.Equal("Nuevo gasto", Parte<TextBlock>(encabezado, "PART_BreadcrumbActual").Text);
    }

    [AvaloniaFact]
    public void BreadcrumbOrigen_NoTomaElAltoDeBotonV2YPintaEnAcento()
    {
        // Review Focus 5: el link es un Button dentro de una vista .ui2; no puede recibir el alto
        // 36 ni el fondo de un botón.
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Nuevo gasto" BreadcrumbOrigen="Gastos y facturas" BreadcrumbActual="Nuevo gasto" />"""), "E");
        var origen = Parte<Button>(encabezado, "PART_BreadcrumbOrigen");

        Assert.True(origen.Bounds.Height < 24, $"alto {origen.Bounds.Height}");
        var presenter = Parte<ContentPresenter>(origen, "PART_ContentPresenter");
        Assert.Equal(Hex("#1D4ED8"), ColorDe(presenter.Foreground));
        Assert.Equal(FontWeight.Medium, presenter.FontWeight);
    }

    [AvaloniaFact]
    public void Encabezado_Acciones_QuedanAlineadasALaDerecha()
    {
        var ventana = Montar("""
            <v2:EncabezadoPagina x:Name="E" Titulo="Productos">
                <Button x:Name="Nuevo" Classes="primario" Content="Nuevo producto" />
            </v2:EncabezadoPagina>
            """);
        var encabezado = Buscar<EncabezadoPagina>(ventana, "E");
        var nuevo = Buscar<Button>(ventana, "Nuevo");

        var derecha = nuevo.TranslatePoint(new Point(nuevo.Bounds.Width, 0), encabezado)!.Value.X;

        Assert.Equal(encabezado.Bounds.Width, derecha, 1);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TarjetaContenidoEncabezadoTests"`
Expected: FAIL de compilación, `The type or namespace name 'TarjetaContenido' could not be found`.

- [ ] **Step 2: Implementar los tipos**

`src/StockApp.Presentation/Controls/V2/TarjetaContenido.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Card blanca con borde, radio 10 y la única sombra del sistema (spec §3.3). ContentControl y
/// no UserControl: el contenido queda en el NameScope de la vista que lo usa.
/// </summary>
public class TarjetaContenido : ContentControl
{
}
```

`src/StockApp.Presentation/Controls/V2/EncabezadoPagina.cs`:

```csharp
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Encabezado de página (spec §3.3 y §4.4): breadcrumb opcional, título, subtítulo y slot de
/// acciones a la derecha. El primer tramo del breadcrumb ejecuta BreadcrumbComando, que las
/// vistas bindean al comando Volver que su ViewModel ya expone: no hay back-stack.
/// </summary>
public class EncabezadoPagina : TemplatedControl
{
    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> SubtituloProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(Subtitulo));

    public static readonly StyledProperty<string?> BreadcrumbOrigenProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(BreadcrumbOrigen));

    public static readonly StyledProperty<ICommand?> BreadcrumbComandoProperty =
        AvaloniaProperty.Register<EncabezadoPagina, ICommand?>(nameof(BreadcrumbComando));

    public static readonly StyledProperty<string?> BreadcrumbActualProperty =
        AvaloniaProperty.Register<EncabezadoPagina, string?>(nameof(BreadcrumbActual));

    public static readonly StyledProperty<object?> AccionesProperty =
        AvaloniaProperty.Register<EncabezadoPagina, object?>(nameof(Acciones));

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    /// <summary>Si es null o vacío, no ocupa alto.</summary>
    public string? Subtitulo
    {
        get => GetValue(SubtituloProperty);
        set => SetValue(SubtituloProperty, value);
    }

    /// <summary>Primer tramo del breadcrumb (link). Si es null o vacío, no hay breadcrumb.</summary>
    public string? BreadcrumbOrigen
    {
        get => GetValue(BreadcrumbOrigenProperty);
        set => SetValue(BreadcrumbOrigenProperty, value);
    }

    public ICommand? BreadcrumbComando
    {
        get => GetValue(BreadcrumbComandoProperty);
        set => SetValue(BreadcrumbComandoProperty, value);
    }

    public string? BreadcrumbActual
    {
        get => GetValue(BreadcrumbActualProperty);
        set => SetValue(BreadcrumbActualProperty, value);
    }

    /// <summary>Slot de acciones a la derecha. Un solo botón primario por vista (spec §3.3).</summary>
    [Content]
    public object? Acciones
    {
        get => GetValue(AccionesProperty);
        set => SetValue(AccionesProperty, value);
    }
}
```

- [ ] **Step 3: Agregar los `ControlTheme` a `ComponentesV2.axaml`**

Antes de `</ResourceDictionary>`:

```xml
    <ControlTheme x:Key="{x:Type v2:TarjetaContenido}" TargetType="v2:TarjetaContenido">
        <Setter Property="Padding" Value="16" />
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="PART_Marco"
                        Background="{DynamicResource V2SuperficieBrush}"
                        BorderBrush="{DynamicResource V2BordeBrush}"
                        BorderThickness="1"
                        CornerRadius="{DynamicResource V2RadioCard}"
                        BoxShadow="{DynamicResource V2SombraCard}"
                        Padding="{TemplateBinding Padding}">
                    <ContentPresenter x:Name="PART_Contenido"
                                      Content="{TemplateBinding Content}"
                                      ContentTemplate="{TemplateBinding ContentTemplate}" />
                </Border>
            </ControlTemplate>
        </Setter>
    </ControlTheme>

    <!-- Botón con apariencia de link (primer tramo del breadcrumb). Template propio sin fondo ni
         borde: los estilos globales de Button (viejos) setean Background/Padding que este
         template no usa, y los v2 solo aplican con clase de variante. -->
    <ControlTheme x:Key="V2TemaEnlace" TargetType="Button">
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <ControlTemplate>
                <ContentPresenter x:Name="PART_ContentPresenter"
                                  Background="Transparent"
                                  Content="{TemplateBinding Content}"
                                  Foreground="{DynamicResource V2AcentoBrush}"
                                  FontSize="{DynamicResource V2FuenteEnlaceTamano}"
                                  FontWeight="{DynamicResource V2FuenteEnlacePeso}" />
            </ControlTemplate>
        </Setter>
    </ControlTheme>

    <ControlTheme x:Key="{x:Type v2:EncabezadoPagina}" TargetType="v2:EncabezadoPagina">
        <Setter Property="Margin" Value="0,0,0,16" />
        <Setter Property="Template">
            <ControlTemplate>
                <StackPanel>
                    <StackPanel x:Name="PART_Breadcrumb"
                                Orientation="Horizontal"
                                Margin="0,0,0,8"
                                IsVisible="{TemplateBinding BreadcrumbOrigen, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
                        <Button x:Name="PART_BreadcrumbOrigen"
                                Theme="{StaticResource V2TemaEnlace}"
                                Content="{TemplateBinding BreadcrumbOrigen}"
                                Command="{TemplateBinding BreadcrumbComando}" />
                        <TextBlock x:Name="PART_BreadcrumbSeparador"
                                   Text=" / "
                                   FontSize="{DynamicResource V2FuenteControlTamano}"
                                   Foreground="{DynamicResource V2TextoTerciarioBrush}" />
                        <TextBlock x:Name="PART_BreadcrumbActual"
                                   Text="{TemplateBinding BreadcrumbActual}"
                                   FontSize="{DynamicResource V2FuenteControlTamano}"
                                   Foreground="{DynamicResource V2TextoTerciarioBrush}" />
                    </StackPanel>
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel Grid.Column="0">
                            <TextBlock x:Name="PART_Titulo"
                                       Text="{TemplateBinding Titulo}"
                                       FontSize="{DynamicResource V2FuenteTituloPaginaTamano}"
                                       FontWeight="{DynamicResource V2FuenteTituloPaginaPeso}"
                                       LineHeight="{DynamicResource V2FuenteTituloPaginaAltoLinea}"
                                       Foreground="{DynamicResource V2TextoPrimarioBrush}" />
                            <TextBlock x:Name="PART_Subtitulo"
                                       Margin="0,4,0,0"
                                       Text="{TemplateBinding Subtitulo}"
                                       FontSize="{DynamicResource V2FuenteControlTamano}"
                                       Foreground="{DynamicResource V2TextoSecundarioBrush}"
                                       IsVisible="{TemplateBinding Subtitulo, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                        </StackPanel>
                        <ContentPresenter x:Name="PART_Acciones"
                                          Grid.Column="1"
                                          VerticalAlignment="Top"
                                          Content="{TemplateBinding Acciones}" />
                    </Grid>
                </StackPanel>
            </ControlTemplate>
        </Setter>
    </ControlTheme>
```

- [ ] **Step 4: CSS de mockups**

Agregar al final de `docs/ui-v2/mockups/_base/base.css`:

```css
/* TarjetaContenido (Controls/V2/TarjetaContenido). Padding default 16; los formularios usan 24. */
.v2-tarjeta {
  background: var(--v2-color-superficie); border: 1px solid var(--v2-color-borde);
  border-radius: var(--v2-radio-card); box-shadow: var(--v2-sombra-card); padding: 16px;
}

/* EncabezadoPagina (Controls/V2/EncabezadoPagina) */
.v2-encabezado { margin-bottom: 16px; }
.v2-breadcrumb {
  display: flex; margin-bottom: 8px; white-space: pre;
  font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-terciario);
}
.v2-breadcrumb-origen { color: var(--v2-color-acento); font-size: var(--v2-fuente-enlace-tamano); line-height: var(--v2-fuente-enlace-alto-linea); font-weight: var(--v2-fuente-enlace-peso); }
.v2-encabezado-fila { display: flex; justify-content: space-between; align-items: flex-start; }
.v2-encabezado-subtitulo { margin-top: 4px; font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-secundario); }
.v2-acciones { display: flex; gap: 8px; }
```

(El título usa `v2-titulo-pagina` de la Task 10.)

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TarjetaContenidoEncabezadoTests"`
Expected: PASS, `Failed: 0, Passed: 8`.

- [ ] **Step 6: Verificación por mutación**

(a) En el template de `EncabezadoPagina`, quitar `Theme="{StaticResource V2TemaEnlace}"` del `PART_BreadcrumbOrigen`. Expected: FAIL en `BreadcrumbOrigen_NoTomaElAltoDeBotonV2YPintaEnAcento`. Restaurar; PASS.
(b) Quitar `Command="{TemplateBinding BreadcrumbComando}"`. Expected: FAIL en `Encabezado_ClickEnElOrigenDelBreadcrumb_EjecutaElComando` (`Expected: 1, Actual: 0`). Restaurar; PASS.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2 tests/StockApp.Presentation.UiTests/V2 docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega TarjetaContenido y EncabezadoPagina con breadcrumb"
```

### Task 13: `CampoFormulario` v2 y plantilla de error v2 (sin interceptar `ErrorConverter`)

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/CampoFormulario.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml`
- Modify: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml`
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/ModelosDePrueba.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/CampoFormularioV2Tests.cs`

**Interfaces:**
- Consumes: `MontajeV2`, estilos `.ui2 TextBox` (Task 10), seam `ErrorValidacionConverter` (sin cambios).
- Produces:
  - `StockApp.Presentation.Controls.V2.CampoFormulario : ContentControl` — `string? Etiqueta`, `bool Requerido`; partes `PART_Etiqueta`, `PART_Requerido` (`TextBlock`), `PART_Control` (`ContentPresenter`).
  - Estilo `.ui2 DataValidationErrors` con `ErrorTemplate` v2: mensajes 12/400 en `#DC2626`, 4 de separación arriba (`ItemsControl` `PART_ErroresV2`).
  - `ModeloCampoDecimal` (`decimal Cantidad`) y `ModeloBusqueda` (`string? Busqueda`) en UiTests.
  - CSS: `v2-campo`, `v2-campo-etiqueta`, `v2-campo-requerido`, `v2-error-campo`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ModelosDePrueba.cs`:

```csharp
namespace StockApp.Presentation.UiTests.V2;

/// <summary>Modelos mínimos para bindings reales (ReflectionBinding) en tests de componentes v2.</summary>
public sealed class ModeloCampoDecimal
{
    public decimal Cantidad { get; set; }
}

public sealed class ModeloBusqueda
{
    public string? Busqueda { get; set; }
}
```

`tests/StockApp.Presentation.UiTests/V2/CampoFormularioV2Tests.cs`:

```csharp
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;
using CampoFormularioV2 = StockApp.Presentation.Controls.V2.CampoFormulario;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Review Focus 2: CampoFormulario v2 agrega etiqueta y asterisco y NADA más. El mensaje de error
/// lo dibuja el DataValidationErrors del propio control con lo que ya produjo el seam
/// ErrorValidacionConverter (Themes/Controls.axaml). Si el componente interceptara la validación,
/// volverían a verse excepciones crudas de .NET.
/// </summary>
public class CampoFormularioV2Tests
{
    private const string CampoCantidad = """
        <v2:CampoFormulario x:Name="C" Etiqueta="Cantidad" Requerido="True">
            <TextBox x:Name="T" Text="{ReflectionBinding Cantidad, Mode=TwoWay}" />
        </v2:CampoFormulario>
        """;

    [AvaloniaFact]
    public void Etiqueta_12_500_ColorEtiquetaYAsteriscoRojoSiEsRequerido()
    {
        var campo = Buscar<CampoFormularioV2>(Montar(CampoCantidad), "C");
        var etiqueta = Parte<TextBlock>(campo, "PART_Etiqueta");
        var requerido = Parte<TextBlock>(campo, "PART_Requerido");

        Assert.Equal("Cantidad", etiqueta.Text);
        Assert.Equal(12, etiqueta.FontSize);
        Assert.Equal(FontWeight.Medium, etiqueta.FontWeight);
        Assert.Equal(Hex("#44403C"), ColorDe(etiqueta.Foreground));
        Assert.True(requerido.IsVisible);
        Assert.Equal("*", requerido.Text);
        Assert.Equal(Hex("#DC2626"), ColorDe(requerido.Foreground));
    }

    [AvaloniaFact]
    public void NoRequerido_OcultaElAsterisco()
    {
        var campo = Buscar<CampoFormularioV2>(Montar("""<v2:CampoFormulario x:Name="C" Etiqueta="Notas"><TextBox /></v2:CampoFormulario>"""), "C");

        Assert.False(Parte<TextBlock>(campo, "PART_Requerido").IsVisible);
    }

    [AvaloniaFact]
    public void ControlDeAdentro_ConservaElErrorConverterGlobal()
    {
        var entrada = Buscar<TextBox>(Montar(CampoCantidad), "T");

        Assert.NotNull(DataValidationErrors.GetErrorConverter(entrada));
    }

    [AvaloniaFact]
    public void TextoInvalidoEnDecimal_MuestraElMensajeDelSeamEnRojo()
    {
        var ventana = Montar(CampoCantidad);
        var modelo = new ModeloCampoDecimal { Cantidad = 5m };
        ventana.DataContext = modelo;
        Dispatcher.UIThread.RunJobs();
        var entrada = Buscar<TextBox>(ventana, "T");

        entrada.Text = "abc";
        Dispatcher.UIThread.RunJobs();

        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().ToList();
        var mensaje = Assert.Single(textos, t => t.Text == "Ingresá un número válido.");
        Assert.Equal(Hex("#DC2626"), ColorDe(mensaje.Foreground));
        Assert.Equal(12, mensaje.FontSize);
        Assert.Contains(mensaje.GetVisualAncestors(), a => a is DataValidationErrors);
        Assert.DoesNotContain(textos, t => t.Text?.Contains("format", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(5m, modelo.Cantidad);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CampoFormularioV2Tests"`
Expected: FAIL de compilación, `The type or namespace name 'CampoFormulario' does not exist in the namespace 'StockApp.Presentation.Controls.V2'`.

- [ ] **Step 2: Implementar el tipo**

`src/StockApp.Presentation/Controls/V2/CampoFormulario.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Etiqueta arriba, asterisco si es requerido, control en el Content (spec §3.3 y §5.2).
/// NO intercepta la validación: el control de entrada queda como hijo directo de un
/// ContentPresenter y sigue recibiendo por selector de tipo el ErrorConverter de
/// Themes/Controls.axaml. El mensaje de error lo dibuja el DataValidationErrors del propio
/// control (estilo .ui2 DataValidationErrors de Themes/V2/ControlesV2.axaml).
/// </summary>
public class CampoFormulario : ContentControl
{
    public static readonly StyledProperty<string?> EtiquetaProperty =
        AvaloniaProperty.Register<CampoFormulario, string?>(nameof(Etiqueta));

    public static readonly StyledProperty<bool> RequeridoProperty =
        AvaloniaProperty.Register<CampoFormulario, bool>(nameof(Requerido));

    public string? Etiqueta
    {
        get => GetValue(EtiquetaProperty);
        set => SetValue(EtiquetaProperty, value);
    }

    /// <summary>Marca visual de obligatorio. No valida nada por sí solo.</summary>
    public bool Requerido
    {
        get => GetValue(RequeridoProperty);
        set => SetValue(RequeridoProperty, value);
    }
}
```

- [ ] **Step 3: `ControlTheme` y plantilla de error**

En `ComponentesV2.axaml`, antes de `</ResourceDictionary>`:

```xml
    <ControlTheme x:Key="{x:Type v2:CampoFormulario}" TargetType="v2:CampoFormulario">
        <Setter Property="Template">
            <ControlTemplate>
                <StackPanel Spacing="4">
                    <StackPanel Orientation="Horizontal" Spacing="4">
                        <TextBlock x:Name="PART_Etiqueta"
                                   Text="{TemplateBinding Etiqueta}"
                                   FontSize="{DynamicResource V2FuenteEtiquetaCampoTamano}"
                                   FontWeight="{DynamicResource V2FuenteEtiquetaCampoPeso}"
                                   Foreground="{DynamicResource V2TextoEtiquetaBrush}" />
                        <TextBlock x:Name="PART_Requerido"
                                   Text="*"
                                   FontSize="{DynamicResource V2FuenteEtiquetaCampoTamano}"
                                   FontWeight="{DynamicResource V2FuenteEtiquetaCampoPeso}"
                                   Foreground="{DynamicResource V2BordeErrorBrush}"
                                   IsVisible="{TemplateBinding Requerido}" />
                    </StackPanel>
                    <!-- ContentPresenter pelado: NO agregar ErrorTemplate ni envolver el control. -->
                    <ContentPresenter x:Name="PART_Control" Content="{TemplateBinding Content}" />
                </StackPanel>
            </ControlTemplate>
        </Setter>
    </ControlTheme>
```

En `Themes/V2/ControlesV2.axaml`, agregar al elemento raíz `<Styles ...>` los atributos:

```xml
        xmlns:col="using:System.Collections"
        xmlns:sys="using:System"
```

y antes de `</Styles>`:

```xml
    <!-- Mensaje de error de campo (spec §2.3: 12, color borde error). Solo cambia CÓMO se ve lo
         que el seam ya produjo: el contenido son los errores de DataValidationErrors.Errors, ya
         pasados por ErrorValidacionConverter. -->
    <Style Selector=".ui2 DataValidationErrors">
        <Setter Property="ErrorTemplate">
            <DataTemplate x:DataType="col:IEnumerable">
                <ItemsControl x:Name="PART_ErroresV2" ItemsSource="{Binding}" Margin="0,4,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="sys:Object">
                            <TextBlock Text="{Binding}"
                                       TextWrapping="Wrap"
                                       FontSize="{DynamicResource V2FuenteErrorCampoTamano}"
                                       FontWeight="{DynamicResource V2FuenteErrorCampoPeso}"
                                       Foreground="{DynamicResource V2BordeErrorBrush}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </DataTemplate>
        </Setter>
    </Style>
```

- [ ] **Step 4: CSS de mockups**

Agregar al final de `base.css`:

```css
/* CampoFormulario (Controls/V2/CampoFormulario). El mensaje de error va pegado al input
   (margin-top 4) y NO separado por el gap del campo: en Avalonia es parte del
   DataValidationErrors del control, no del campo. Estructura:
   <div class="v2-campo"><div class="v2-campo-etiqueta"><span>Código</span><span class="v2-campo-requerido">*</span></div>
     <div><div class="v2-input v2-input--error">A-001</div><div class="v2-error-campo">El código ya existe</div></div></div> */
.v2-campo { display: flex; flex-direction: column; gap: 4px; }
.v2-campo-etiqueta {
  display: flex; gap: 4px;
  font-size: var(--v2-fuente-etiqueta-campo-tamano); line-height: var(--v2-fuente-etiqueta-campo-alto-linea); font-weight: var(--v2-fuente-etiqueta-campo-peso);
  color: var(--v2-color-texto-etiqueta);
}
.v2-campo-requerido { color: var(--v2-color-borde-error); }
.v2-error-campo {
  margin-top: 4px;
  font-size: var(--v2-fuente-error-campo-tamano); line-height: var(--v2-fuente-error-campo-alto-linea); font-weight: var(--v2-fuente-error-campo-peso);
  color: var(--v2-color-borde-error);
}
```

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, `Failed: 0` (incluye `CampoFormularioTests` y `MovimientoFormControlValidacionTests` viejos intactos).

- [ ] **Step 6: Verificación por mutación (quitando)**

(a) En `Themes/Controls.axaml` (viejo), quitar la línea `<Setter Property="(DataValidationErrors.ErrorConverter)" ...>` del estilo `TextBox`. Expected: FAIL en `TextoInvalidoEnDecimal_MuestraElMensajeDelSeamEnRojo` (no aparece "Ingresá un número válido.", aparece el texto crudo de la excepción) y en `ControlDeAdentro_ConservaElErrorConverterGlobal`. Restaurar con `git checkout -- src/StockApp.Presentation/Themes/Controls.axaml`; PASS.
(b) Quitar el estilo `.ui2 DataValidationErrors` de `ControlesV2.axaml`. Expected: FAIL en el mismo test por el color (Fluent pinta el error con su propio brush, no `#DC2626`) o por el tamaño. Restaurar; PASS.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2 src/StockApp.Presentation/Themes/V2/ControlesV2.axaml tests/StockApp.Presentation.UiTests/V2 docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega CampoFormulario v2 y plantilla de error sin interceptar el ErrorConverter"
```

### Task 14: `BarraFiltros`

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/BarraFiltros.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml`
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/BarraFiltrosTests.cs`

**Interfaces:**
- Consumes: `MontajeV2`, `ModeloBusqueda` (Task 13), estilos `.ui2 TextBox`.
- Produces:
  - `BarraFiltros : TemplatedControl` — `string? TextoBusqueda` (binding **TwoWay por defecto**), `string? MarcaAguaBusqueda`, `[Content] object? Filtros`; `Margin` default `0,0,0,16`; partes `PART_Busqueda` (`TextBox`, ancho 280), `PART_Filtros` (`ContentPresenter`).
  - CSS: `v2-barra-filtros`, `v2-busqueda`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/BarraFiltrosTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class BarraFiltrosTests
{
    [AvaloniaFact]
    public void EscribirEnLaBusqueda_ActualizaElViewModelSinDeclararTwoWay()
    {
        var ventana = Montar("""<v2:BarraFiltros x:Name="F" TextoBusqueda="{ReflectionBinding Busqueda}" MarcaAguaBusqueda="Buscar por código o nombre" />""");
        var modelo = new ModeloBusqueda();
        ventana.DataContext = modelo;
        Dispatcher.UIThread.RunJobs();

        Parte<TextBox>(Buscar<BarraFiltros>(ventana, "F"), "PART_Busqueda").Text = "tornillo";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("tornillo", modelo.Busqueda);
    }

    [AvaloniaFact]
    public void CambioDeTextoBusqueda_LlegaALaCaja()
    {
        var barra = Buscar<BarraFiltros>(Montar("""<v2:BarraFiltros x:Name="F" />"""), "F");

        barra.TextoBusqueda = "pintura";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("pintura", Parte<TextBox>(barra, "PART_Busqueda").Text);
    }

    [AvaloniaFact]
    public void Busqueda_Ancho280Alto36ConMarcaDeAgua()
    {
        var barra = Buscar<BarraFiltros>(Montar("""<v2:BarraFiltros x:Name="F" MarcaAguaBusqueda="Buscar por código o nombre" />"""), "F");
        var busqueda = Parte<TextBox>(barra, "PART_Busqueda");

        Assert.Equal(280, busqueda.Bounds.Width);
        Assert.Equal(36, busqueda.Bounds.Height);
        Assert.Equal("Buscar por código o nombre", busqueda.PlaceholderText);
        Assert.Equal(new Thickness(0, 0, 0, 16), barra.Margin);
    }

    [AvaloniaFact]
    public void Filtros_VanDespuesDeLaBusquedaConSeparacion8()
    {
        var ventana = Montar("""
            <v2:BarraFiltros x:Name="F">
                <ComboBox x:Name="Categoria" Width="200" />
            </v2:BarraFiltros>
            """);
        var barra = Buscar<BarraFiltros>(ventana, "F");
        var categoria = Buscar<ComboBox>(ventana, "Categoria");

        Assert.Equal(288, categoria.TranslatePoint(new Point(0, 0), barra)!.Value.X);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~BarraFiltrosTests"`
Expected: FAIL de compilación, `The type or namespace name 'BarraFiltros' could not be found`.

- [ ] **Step 2: Implementar**

`src/StockApp.Presentation/Controls/V2/BarraFiltros.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Buscador (280 de ancho) más filtros de un listado (spec §3.3 y §5.1). TextoBusqueda es
/// TwoWay por defecto: {Binding Busqueda} en la vista alcanza, como en un TextBox.Text.
/// </summary>
public class BarraFiltros : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoBusquedaProperty =
        AvaloniaProperty.Register<BarraFiltros, string?>(nameof(TextoBusqueda), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> MarcaAguaBusquedaProperty =
        AvaloniaProperty.Register<BarraFiltros, string?>(nameof(MarcaAguaBusqueda));

    public static readonly StyledProperty<object?> FiltrosProperty =
        AvaloniaProperty.Register<BarraFiltros, object?>(nameof(Filtros));

    public string? TextoBusqueda
    {
        get => GetValue(TextoBusquedaProperty);
        set => SetValue(TextoBusquedaProperty, value);
    }

    public string? MarcaAguaBusqueda
    {
        get => GetValue(MarcaAguaBusquedaProperty);
        set => SetValue(MarcaAguaBusquedaProperty, value);
    }

    /// <summary>Filtros a la derecha del buscador (ComboBox, chips), separados 8.</summary>
    [Content]
    public object? Filtros
    {
        get => GetValue(FiltrosProperty);
        set => SetValue(FiltrosProperty, value);
    }
}
```

En `ComponentesV2.axaml`, antes de `</ResourceDictionary>`:

```xml
    <ControlTheme x:Key="{x:Type v2:BarraFiltros}" TargetType="v2:BarraFiltros">
        <Setter Property="Margin" Value="0,0,0,16" />
        <Setter Property="Template">
            <ControlTemplate>
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBox x:Name="PART_Busqueda"
                             Width="280"
                             Text="{Binding TextoBusqueda, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                             PlaceholderText="{TemplateBinding MarcaAguaBusqueda}" />
                    <ContentPresenter x:Name="PART_Filtros" Content="{TemplateBinding Filtros}" />
                </StackPanel>
            </ControlTemplate>
        </Setter>
    </ControlTheme>
```

- [ ] **Step 3: CSS de mockups**

Agregar al final de `base.css`:

```css
/* BarraFiltros (Controls/V2/BarraFiltros): buscador de 280 + filtros separados 8. */
.v2-barra-filtros { display: flex; gap: 8px; margin-bottom: 16px; }
.v2-busqueda { flex: 0 0 280px; width: 280px; }
```

- [ ] **Step 4: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~BarraFiltrosTests"`
Expected: PASS, `Failed: 0, Passed: 4`.

- [ ] **Step 5: Verificación por mutación**

Quitar `, defaultBindingMode: BindingMode.TwoWay` de `TextoBusquedaProperty`. Expected: FAIL en `EscribirEnLaBusqueda_ActualizaElViewModelSinDeclararTwoWay` (`Expected: tornillo, Actual: (null)`). Restaurar; PASS.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2 tests/StockApp.Presentation.UiTests/V2 docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega BarraFiltros con búsqueda TwoWay por defecto"
```

### Task 15: `TarjetaMetrica` v2, `TarjetaAcceso` y `EstadoVacio` v2

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/TarjetaMetrica.cs`
- Create: `src/StockApp.Presentation/Controls/V2/TarjetaAcceso.cs`
- Create: `src/StockApp.Presentation/Controls/V2/EstadoVacio.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml`
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/TarjetasV2Tests.cs`

**Interfaces:**
- Consumes: `MontajeV2` (incluido `Clic`), `ComandoContador`, proveedor de íconos `mdi` (registrado en `TestApp`).
- Produces:
  - `StockApp.Presentation.Controls.V2.TarjetaMetrica : TemplatedControl` — `string? Valor`, `string? Etiqueta`, `bool SinDato`; partes `PART_Marco`, `PART_Valor`, `PART_SinDato` (texto `—`), `PART_Etiqueta`.
  - `TarjetaAcceso : Button` — `string? Icono` (clave `mdi-*`), `string? Titulo`, `string? Descripcion`; se usa con `Command`; partes `PART_Marco` (`Border`), `PART_Icono` (`Icon`), `PART_Titulo`, `PART_Descripcion`.
  - `StockApp.Presentation.Controls.V2.EstadoVacio : TemplatedControl` — `string? Titulo`, `string? Mensaje`, `bool EsError`, `[Content] object? Acciones`; partes `PART_Titulo`, `PART_Mensaje`, `PART_Acciones`.
  - CSS: `v2-metrica`, `v2-metrica-valor`, `v2-metrica-valor--sin-dato`, `v2-metrica-etiqueta`, `v2-acceso`, `v2-acceso-titulo`, `v2-acceso-descripcion`, `v2-vacio`, `v2-vacio-titulo`, `v2-vacio-titulo--error`, `v2-vacio-mensaje`, `v2-vacio-acciones`.

Decisión de Fase 0 sobre "sin dato": la spec §6.1 prohíbe técnicas que Avalonia no replica 1:1 y el mockup de referencia usa borde punteado (`.pt-kpi-missing`), que `Border` de Avalonia no tiene. El estado sin dato se comunica con el valor `—` en color texto grupo (`#A8A29E`, spec §2.1 "KPI sin dato") y borde sólido. Se valida en el catálogo.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/TarjetasV2Tests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Optris.Icons.Avalonia;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;
using EstadoVacioV2 = StockApp.Presentation.Controls.V2.EstadoVacio;
using TarjetaMetricaV2 = StockApp.Presentation.Controls.V2.TarjetaMetrica;

namespace StockApp.Presentation.UiTests.V2;

public class TarjetasV2Tests
{
    [AvaloniaFact]
    public void TarjetaMetrica_ConValor_20_600PrimarioYEtiqueta12Terciario()
    {
        var tarjeta = Buscar<TarjetaMetricaV2>(Montar("""<v2:TarjetaMetrica x:Name="M" Valor="3" Etiqueta="Facturas a vencer (7 días)" />"""), "M");
        var valor = Parte<TextBlock>(tarjeta, "PART_Valor");
        var etiqueta = Parte<TextBlock>(tarjeta, "PART_Etiqueta");

        Assert.True(valor.IsVisible);
        Assert.Equal("3", valor.Text);
        Assert.Equal(20, valor.FontSize);
        Assert.Equal(FontWeight.SemiBold, valor.FontWeight);
        Assert.Equal(Hex("#1C1917"), ColorDe(valor.Foreground));
        Assert.Equal(12, etiqueta.FontSize);
        Assert.Equal(Hex("#78716C"), ColorDe(etiqueta.Foreground));
        Assert.False(Parte<TextBlock>(tarjeta, "PART_SinDato").IsVisible);
        Assert.Equal(new CornerRadius(10), Parte<Border>(tarjeta, "PART_Marco").CornerRadius);
    }

    [AvaloniaFact]
    public void TarjetaMetrica_SinDato_MuestraGuionEnTextoGrupoYOcultaElValor()
    {
        var tarjeta = Buscar<TarjetaMetricaV2>(Montar("""<v2:TarjetaMetrica x:Name="M" Valor="99" SinDato="True" Etiqueta="Último backup" />"""), "M");
        var sinDato = Parte<TextBlock>(tarjeta, "PART_SinDato");

        Assert.False(Parte<TextBlock>(tarjeta, "PART_Valor").IsVisible);
        Assert.True(sinDato.IsVisible);
        Assert.Equal("—", sinDato.Text);
        Assert.Equal(Hex("#A8A29E"), ColorDe(sinDato.Foreground));
    }

    [AvaloniaFact]
    public void TarjetaAcceso_Click_EjecutaElComando()
    {
        var ventana = Montar("""<v2:TarjetaAcceso x:Name="A" Icono="mdi-package-variant" Titulo="Productos" Descripcion="Ver y administrar el catálogo" />""");
        var tarjeta = Buscar<TarjetaAcceso>(ventana, "A");
        var comando = new ComandoContador();
        tarjeta.Command = comando;
        Dispatcher.UIThread.RunJobs();

        Clic(ventana, tarjeta);

        Assert.Equal(1, comando.Ejecuciones);
    }

    [AvaloniaFact]
    public void TarjetaAcceso_MarcoIconoYTextos()
    {
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Icono="mdi-package-variant" Titulo="Productos" Descripcion="Ver y administrar el catálogo" />"""), "A");
        var marco = Parte<Border>(tarjeta, "PART_Marco");

        Assert.Equal(Hex("#FFFFFF"), ColorDe(marco.Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(marco.BorderBrush));
        Assert.Equal(new CornerRadius(8), marco.CornerRadius);
        Assert.Equal(new Thickness(16), marco.Padding);
        Assert.Equal("mdi-package-variant", Parte<Icon>(tarjeta, "PART_Icono").Value);
        Assert.Equal(Hex("#1D4ED8"), ColorDe(Parte<Icon>(tarjeta, "PART_Icono").Foreground));
        Assert.Equal(13, Parte<TextBlock>(tarjeta, "PART_Titulo").FontSize);
        Assert.Equal(FontWeight.SemiBold, Parte<TextBlock>(tarjeta, "PART_Titulo").FontWeight);
        Assert.Equal(Hex("#78716C"), ColorDe(Parte<TextBlock>(tarjeta, "PART_Descripcion").Foreground));
    }

    [AvaloniaFact]
    public void TarjetaAcceso_Hover_NoUsaElVerdeViejo()
    {
        // Review Focus 5: TarjetaAcceso hereda de Button. El estilo viejo global
        // "Button:pointerover" (BrandSuave verde) no tiene que alcanzarla.
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Titulo="Productos" />"""), "A");

        ((IPseudoClasses)tarjeta.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(tarjeta, "PART_Marco").Background));
    }

    [AvaloniaFact]
    public void TarjetaAcceso_SinIcono_NoLoMuestra()
    {
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Titulo="Reportes" />"""), "A");

        Assert.False(Parte<Icon>(tarjeta, "PART_Icono").IsVisible);
    }

    [AvaloniaFact]
    public void EstadoVacio_Normal_Titulo15_600PrimarioYMensaje13Secundario()
    {
        var vacio = Buscar<EstadoVacioV2>(Montar("""<v2:EstadoVacio x:Name="V" Titulo="Sin resultados" Mensaje="No hay productos que coincidan." />"""), "V");
        var titulo = Parte<TextBlock>(vacio, "PART_Titulo");
        var mensaje = Parte<TextBlock>(vacio, "PART_Mensaje");

        Assert.Equal(15, titulo.FontSize);
        Assert.Equal(FontWeight.SemiBold, titulo.FontWeight);
        Assert.Equal(Hex("#1C1917"), ColorDe(titulo.Foreground));
        Assert.Equal(13, mensaje.FontSize);
        Assert.Equal(Hex("#57534E"), ColorDe(mensaje.Foreground));
        Assert.False(Parte<ContentPresenter>(vacio, "PART_Acciones").IsVisible);
    }

    [AvaloniaFact]
    public void EstadoVacio_EsError_TituloEnPeligroTexto()
    {
        // Conserva la semántica del EstadoVacio viejo: "no hay datos" vs "falló la carga".
        var vacio = Buscar<EstadoVacioV2>(Montar("""<v2:EstadoVacio x:Name="V" Titulo="No se pudo cargar" EsError="True" />"""), "V");

        Assert.Equal(Hex("#B91C1C"), ColorDe(Parte<TextBlock>(vacio, "PART_Titulo").Foreground));
    }

    [AvaloniaFact]
    public void EstadoVacio_ConAcciones_LasMuestra()
    {
        var ventana = Montar("""
            <v2:EstadoVacio x:Name="V" Titulo="No se pudo cargar" EsError="True">
                <Button x:Name="Reintentar" Classes="secundario" Content="Reintentar" />
            </v2:EstadoVacio>
            """);

        Assert.True(Parte<ContentPresenter>(Buscar<EstadoVacioV2>(ventana, "V"), "PART_Acciones").IsVisible);
        Assert.NotNull(ventana.FindControl<Button>("Reintentar"));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TarjetasV2Tests"`
Expected: FAIL de compilación, `The type or namespace name 'TarjetaAcceso' could not be found`.

- [ ] **Step 2: Implementar los tipos**

`src/StockApp.Presentation/Controls/V2/TarjetaMetrica.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>KPI de Inicio (spec §3.3 y §5.3): valor, etiqueta y estado sin dato.</summary>
public class TarjetaMetrica : TemplatedControl
{
    public static readonly StyledProperty<string?> ValorProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Valor));

    public static readonly StyledProperty<string?> EtiquetaProperty =
        AvaloniaProperty.Register<TarjetaMetrica, string?>(nameof(Etiqueta));

    public static readonly StyledProperty<bool> SinDatoProperty =
        AvaloniaProperty.Register<TarjetaMetrica, bool>(nameof(SinDato));

    public string? Valor
    {
        get => GetValue(ValorProperty);
        set => SetValue(ValorProperty, value);
    }

    public string? Etiqueta
    {
        get => GetValue(EtiquetaProperty);
        set => SetValue(EtiquetaProperty, value);
    }

    /// <summary>True: muestra "—" en color texto grupo en lugar del valor.</summary>
    public bool SinDato
    {
        get => GetValue(SinDatoProperty);
        set => SetValue(SinDatoProperty, value);
    }
}
```

`src/StockApp.Presentation/Controls/V2/TarjetaAcceso.cs`:

```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Optris.Icons.Avalonia;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Acceso rápido de Inicio y tarjeta del hub de Reportes (spec §3.3, §4.2 y §5.3). Hereda de
/// Button para tener Command, foco y teclado; su StyleKey es TarjetaAcceso, así que los estilos
/// "Button" (viejos y v2) no la alcanzan.
/// </summary>
public class TarjetaAcceso : Button
{
    private Icon? _icono;

    public static readonly StyledProperty<string?> IconoProperty =
        AvaloniaProperty.Register<TarjetaAcceso, string?>(nameof(Icono));

    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<TarjetaAcceso, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> DescripcionProperty =
        AvaloniaProperty.Register<TarjetaAcceso, string?>(nameof(Descripcion));

    protected override Type StyleKeyOverride => typeof(TarjetaAcceso);

    /// <summary>Clave de ícono Material Design, p. ej. "mdi-package-variant". Null: sin ícono.</summary>
    public string? Icono
    {
        get => GetValue(IconoProperty);
        set => SetValue(IconoProperty, value);
    }

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    public string? Descripcion
    {
        get => GetValue(DescripcionProperty);
        set => SetValue(DescripcionProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _icono = e.NameScope.Find<Icon>("PART_Icono");
        AplicarIcono();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconoProperty)
        {
            AplicarIcono();
        }
    }

    /// <summary>
    /// El Icon de Optris resuelve el ícono en el proveedor apenas cambia Value y lanza con una
    /// clave nula o vacía (IconImage.HandleValueChanged): por eso Value se asigna desde acá y
    /// solo si hay clave, en vez de un TemplateBinding directo.
    /// </summary>
    private void AplicarIcono()
    {
        if (_icono is null)
        {
            return;
        }

        var hayIcono = !string.IsNullOrEmpty(Icono);
        _icono.IsVisible = hayIcono;
        if (hayIcono)
        {
            _icono.Value = Icono!;
        }
    }
}
```

`src/StockApp.Presentation/Controls/V2/EstadoVacio.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Estado vacío rediseñado (spec §3.3). Conserva la distinción del viejo: EsError = "falló la
/// carga" (título en peligro), false = "no hay datos todavía". Acciones opcionales debajo.
/// </summary>
public class EstadoVacio : TemplatedControl
{
    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<EstadoVacio, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> MensajeProperty =
        AvaloniaProperty.Register<EstadoVacio, string?>(nameof(Mensaje));

    public static readonly StyledProperty<bool> EsErrorProperty =
        AvaloniaProperty.Register<EstadoVacio, bool>(nameof(EsError));

    public static readonly StyledProperty<object?> AccionesProperty =
        AvaloniaProperty.Register<EstadoVacio, object?>(nameof(Acciones));

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    public string? Mensaje
    {
        get => GetValue(MensajeProperty);
        set => SetValue(MensajeProperty, value);
    }

    public bool EsError
    {
        get => GetValue(EsErrorProperty);
        set => SetValue(EsErrorProperty, value);
    }

    [Content]
    public object? Acciones
    {
        get => GetValue(AccionesProperty);
        set => SetValue(AccionesProperty, value);
    }
}
```

- [ ] **Step 3: `ControlTheme` de los tres**

En `ComponentesV2.axaml`, antes de `</ResourceDictionary>`:

```xml
    <ControlTheme x:Key="{x:Type v2:TarjetaMetrica}" TargetType="v2:TarjetaMetrica">
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="PART_Marco"
                        Background="{DynamicResource V2SuperficieBrush}"
                        BorderBrush="{DynamicResource V2BordeBrush}"
                        BorderThickness="1"
                        CornerRadius="{DynamicResource V2RadioCard}"
                        BoxShadow="{DynamicResource V2SombraCard}"
                        Padding="16">
                    <StackPanel>
                        <TextBlock x:Name="PART_Valor"
                                   Text="{TemplateBinding Valor}"
                                   FontSize="{DynamicResource V2FuenteValorMetricaTamano}"
                                   FontWeight="{DynamicResource V2FuenteValorMetricaPeso}"
                                   Foreground="{DynamicResource V2TextoPrimarioBrush}"
                                   IsVisible="{TemplateBinding SinDato, Converter={x:Static BoolConverters.Not}}" />
                        <TextBlock x:Name="PART_SinDato"
                                   Text="—"
                                   FontSize="{DynamicResource V2FuenteValorMetricaTamano}"
                                   FontWeight="{DynamicResource V2FuenteValorMetricaPeso}"
                                   Foreground="{DynamicResource V2TextoGrupoBrush}"
                                   IsVisible="{TemplateBinding SinDato}" />
                        <TextBlock x:Name="PART_Etiqueta"
                                   Margin="0,4,0,0"
                                   Text="{TemplateBinding Etiqueta}"
                                   TextWrapping="Wrap"
                                   FontSize="{DynamicResource V2FuenteEtiquetaMetricaTamano}"
                                   FontWeight="{DynamicResource V2FuenteEtiquetaMetricaPeso}"
                                   Foreground="{DynamicResource V2TextoTerciarioBrush}" />
                    </StackPanel>
                </Border>
            </ControlTemplate>
        </Setter>
    </ControlTheme>

    <!-- Fondo y borde van como Setter del control (no como valor local del template) para que
         el estado :pointerover los pueda pisar. -->
    <ControlTheme x:Key="{x:Type v2:TarjetaAcceso}" TargetType="v2:TarjetaAcceso">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="Padding" Value="16" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="PART_Marco"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="{TemplateBinding CornerRadius}"
                        Padding="{TemplateBinding Padding}">
                    <Grid ColumnDefinitions="Auto,8,*">
                        <!-- Value e IsVisible los asigna TarjetaAcceso.AplicarIcono (ver su comentario). -->
                        <i:Icon x:Name="PART_Icono"
                                Grid.Column="0"
                                FontSize="16"
                                VerticalAlignment="Top"
                                Foreground="{DynamicResource V2AcentoBrush}" />
                        <StackPanel Grid.Column="2">
                            <TextBlock x:Name="PART_Titulo"
                                       Text="{TemplateBinding Titulo}"
                                       FontSize="{DynamicResource V2FuenteTituloAccesoTamano}"
                                       FontWeight="{DynamicResource V2FuenteTituloAccesoPeso}"
                                       Foreground="{DynamicResource V2TextoPrimarioBrush}" />
                            <TextBlock x:Name="PART_Descripcion"
                                       Margin="0,4,0,0"
                                       Text="{TemplateBinding Descripcion}"
                                       TextWrapping="Wrap"
                                       FontSize="{DynamicResource V2FuenteDescripcionAccesoTamano}"
                                       FontWeight="{DynamicResource V2FuenteDescripcionAccesoPeso}"
                                       Foreground="{DynamicResource V2TextoTerciarioBrush}"
                                       IsVisible="{TemplateBinding Descripcion, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                        </StackPanel>
                    </Grid>
                </Border>
            </ControlTemplate>
        </Setter>

        <Style Selector="^:pointerover">
            <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        </Style>
        <Style Selector="^:pressed">
            <Setter Property="Background" Value="{DynamicResource V2HoverNeutroBrush}" />
        </Style>
    </ControlTheme>

    <ControlTheme x:Key="{x:Type v2:EstadoVacio}" TargetType="v2:EstadoVacio">
        <Setter Property="Template">
            <ControlTemplate>
                <StackPanel HorizontalAlignment="Center" Margin="0,32" Spacing="4">
                    <TextBlock x:Name="PART_Titulo"
                               Text="{TemplateBinding Titulo}"
                               HorizontalAlignment="Center"
                               FontSize="{DynamicResource V2FuenteTituloSeccionTamano}"
                               FontWeight="{DynamicResource V2FuenteTituloSeccionPeso}" />
                    <TextBlock x:Name="PART_Mensaje"
                               Text="{TemplateBinding Mensaje}"
                               HorizontalAlignment="Center"
                               TextAlignment="Center"
                               TextWrapping="Wrap"
                               MaxWidth="360"
                               FontSize="{DynamicResource V2FuenteControlTamano}"
                               Foreground="{DynamicResource V2TextoSecundarioBrush}"
                               IsVisible="{TemplateBinding Mensaje, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                    <ContentPresenter x:Name="PART_Acciones"
                                      Margin="0,8,0,0"
                                      HorizontalAlignment="Center"
                                      Content="{TemplateBinding Acciones}"
                                      IsVisible="{TemplateBinding Acciones, Converter={x:Static ObjectConverters.IsNotNull}}" />
                </StackPanel>
            </ControlTemplate>
        </Setter>

        <Style Selector="^ /template/ TextBlock#PART_Titulo">
            <Setter Property="Foreground" Value="{DynamicResource V2TextoPrimarioBrush}" />
        </Style>
        <Style Selector="^[EsError=True] /template/ TextBlock#PART_Titulo">
            <Setter Property="Foreground" Value="{DynamicResource V2PeligroTextoBrush}" />
        </Style>
    </ControlTheme>
```

- [ ] **Step 4: CSS de mockups**

Agregar al final de `base.css`:

```css
/* TarjetaMetrica (Controls/V2/TarjetaMetrica). Sin dato: "—" en texto grupo, borde sólido
   (el borde punteado del mockup de referencia no se replica 1:1 en Avalonia, spec §6.1). */
.v2-metrica {
  background: var(--v2-color-superficie); border: 1px solid var(--v2-color-borde);
  border-radius: var(--v2-radio-card); box-shadow: var(--v2-sombra-card); padding: 16px;
}
.v2-metrica-valor { font-size: var(--v2-fuente-valor-metrica-tamano); line-height: var(--v2-fuente-valor-metrica-alto-linea); font-weight: var(--v2-fuente-valor-metrica-peso); color: var(--v2-color-texto-primario); }
.v2-metrica-valor--sin-dato { color: var(--v2-color-texto-grupo); }
.v2-metrica-etiqueta { margin-top: 4px; font-size: var(--v2-fuente-etiqueta-metrica-tamano); line-height: var(--v2-fuente-etiqueta-metrica-alto-linea); font-weight: var(--v2-fuente-etiqueta-metrica-peso); color: var(--v2-color-texto-terciario); }

/* TarjetaAcceso (Controls/V2/TarjetaAcceso): ícono de 16 en acento, 8 de separación. */
.v2-acceso {
  display: flex; gap: 8px; align-items: flex-start; padding: 16px;
  background: var(--v2-color-superficie); border: 1px solid var(--v2-color-borde);
  border-radius: var(--v2-radio-control);
}
.v2-acceso .v2-icono { color: var(--v2-color-acento); }
.v2-acceso-titulo { font-size: var(--v2-fuente-titulo-acceso-tamano); line-height: var(--v2-fuente-titulo-acceso-alto-linea); font-weight: var(--v2-fuente-titulo-acceso-peso); color: var(--v2-color-texto-primario); }
.v2-acceso-descripcion { margin-top: 4px; font-size: var(--v2-fuente-descripcion-acceso-tamano); line-height: var(--v2-fuente-descripcion-acceso-alto-linea); font-weight: var(--v2-fuente-descripcion-acceso-peso); color: var(--v2-color-texto-terciario); }

/* EstadoVacio (Controls/V2/EstadoVacio) */
.v2-vacio { display: flex; flex-direction: column; align-items: center; gap: 4px; margin: 32px 0; text-align: center; }
.v2-vacio-titulo { font-size: var(--v2-fuente-titulo-seccion-tamano); line-height: var(--v2-fuente-titulo-seccion-alto-linea); font-weight: var(--v2-fuente-titulo-seccion-peso); color: var(--v2-color-texto-primario); }
.v2-vacio-titulo--error { color: var(--v2-color-peligro-texto); }
.v2-vacio-mensaje { max-width: 360px; font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-secundario); }
.v2-vacio-acciones { margin-top: 8px; display: flex; gap: 8px; }
```

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, `Failed: 0`.

- [ ] **Step 6: Verificación por mutación**

(a) Quitar el estilo `^:pointerover` del `ControlTheme` de `TarjetaAcceso`. Expected: FAIL en `TarjetaAcceso_Hover_NoUsaElVerdeViejo`. Restaurar; PASS.
(b) En `TarjetaAcceso.AplicarIcono`, quitar la guarda `if (hayIcono)` (asignar `_icono.Value = Icono!;` siempre). Expected: FAIL en `TarjetaAcceso_SinIcono_NoLoMuestra` (excepción del proveedor de íconos). Restaurar; PASS.
(c) Quitar el estilo `^[EsError=True] /template/ TextBlock#PART_Titulo`. Expected: FAIL en `EstadoVacio_EsError_TituloEnPeligroTexto`. Restaurar; PASS.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2 tests/StockApp.Presentation.UiTests/V2 docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega TarjetaMetrica, TarjetaAcceso y EstadoVacio v2"
```

### Task 16: Tema v2 de `DataGrid` bajo `.ui2`

**Files:**
- Create: `src/StockApp.Presentation/Themes/V2/DataGridV2.axaml`
- Modify: `src/StockApp.Presentation/App.axaml` (`Application.Styles`)
- Modify: `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` (`StyleIncludes`)
- Modify: `tests/StockApp.Presentation.VisualTests/VisualTestApp.cs` (`StyleIncludes`)
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `tests/StockApp.Presentation.UiTests/V2/DataGridV2Tests.cs`

**Interfaces:**
- Consumes: `MontajeV2`, `ItemPrueba` (UiTests), `ArbolVisual.EsVisibleEnArbol` (UiTests), claves `V2*`.
- Produces:
  - Dentro de `.ui2`: filas con pitch 40 (`RowHeight` 39 + 1 de separador), header 36, sin zebra, separadores `#F0EFED`, header `#FAFAF9` 11/600 letter-spacing 0.6 terciario, celdas 13 en color etiqueta con padding `12,0`, `DataGridCell.num` a la derecha con `+tnum`, borde de grilla `#F0EFED` radio 8. Conserva de `Themes/DataGrid.axaml`: `CanUserResizeColumns=True`, `MinColumnWidth=40`, apagado del foco de celda.
  - CSS: `v2-grilla`, `v2-grilla-header`, `v2-grilla-fila`, `v2-grilla-celda`.

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.UiTests/V2/DataGridV2Tests.cs`:

```csharp
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class DataGridV2Tests
{
    private const string Grilla = """
        <DataGrid x:Name="G" AutoGenerateColumns="False" Height="200">
            <DataGrid.Columns>
                <DataGridTextColumn Header="NOMBRE" Binding="{ReflectionBinding Nombre}" Width="*" />
                <DataGridTextColumn Header="VALOR" Binding="{ReflectionBinding Valor}" Width="100" CellStyleClasses="num" />
            </DataGrid.Columns>
        </DataGrid>
        """;

    private static DataGrid MontarGrilla(bool conScopeUi2 = true)
    {
        var grilla = Buscar<DataGrid>(Montar(Grilla, conScopeUi2), "G");
        grilla.ItemsSource = new[] { new ItemPrueba("uno", 1), new ItemPrueba("dos", 1111), new ItemPrueba("tres", 3) };
        Dispatcher.UIThread.RunJobs();
        return grilla;
    }

    [AvaloniaFact]
    public void Filas_TienenElPitchDelTokenAltoFilaGrilla()
    {
        // DataGrid 12.0.1 suma 1px de separador DEBAJO del RowHeight (medido). El tema usa
        // RowHeight 39 para que el pitch sea el token (40) igual que el mockup.
        Assert.True(Avalonia.Application.Current!.TryGetResource("V2AltoFilaGrilla", ThemeVariant.Light, out var token));
        var filas = MontarGrilla().GetVisualDescendants().OfType<DataGridRow>().ToList();

        Assert.Equal(3, filas.Count);
        Assert.All(filas, f => Assert.Equal((double)token!, f.Bounds.Height));
    }

    [AvaloniaFact]
    public void Header_Alto36FondoHeaderGrillaTipografia11_600Terciario()
    {
        var header = MontarGrilla().GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => h.Content is "NOMBRE");

        Assert.Equal(36, header.Bounds.Height);
        Assert.Equal(Hex("#FAFAF9"), ColorDe(header.Background));
        Assert.Equal(Hex("#78716C"), ColorDe(header.Foreground));
        Assert.Equal(11, header.FontSize);
        Assert.Equal(FontWeight.SemiBold, header.FontWeight);
        Assert.Equal(0.6, header.LetterSpacing);
    }

    [AvaloniaFact]
    public void SinZebra_TodasLasFilasBlancas()
    {
        var filas = MontarGrilla().GetVisualDescendants().OfType<DataGridRow>().ToList();

        Assert.All(filas, f => Assert.Equal(Hex("#FFFFFF"), ColorDe(f.Background)));
    }

    [AvaloniaFact]
    public void FueraDeUi2_ConservaLaZebraVieja()
    {
        // Review Focus 1: Themes/DataGrid.axaml sigue mandando en las grillas no migradas.
        var fondos = MontarGrilla(conScopeUi2: false).GetVisualDescendants().OfType<DataGridRow>()
            .Select(f => ColorDe(f.Background)).Distinct().ToList();

        Assert.Equal(2, fondos.Count);
    }

    [AvaloniaFact]
    public void CeldaNum_ALaDerechaConCifrasTabulares()
    {
        var celdas = MontarGrilla().GetVisualDescendants().OfType<DataGridCell>().Where(c => c.Classes.Contains("num")).ToList();

        Assert.NotEmpty(celdas);
        Assert.All(celdas, c =>
        {
            Assert.Equal(HorizontalAlignment.Right, c.HorizontalContentAlignment);
            var rasgos = c.GetValue(TextElement.FontFeaturesProperty);
            Assert.NotNull(rasgos);
            Assert.Contains(rasgos!, f => f.ToString().Contains("tnum"));
        });
    }

    [AvaloniaFact]
    public void Celda_Fuente13ColorEtiquetaPadding12()
    {
        var celda = MontarGrilla().GetVisualDescendants().OfType<DataGridCell>().First();

        Assert.Equal(13, celda.FontSize);
        Assert.Equal(Hex("#44403C"), ColorDe(celda.Foreground));
        Assert.Equal(new Thickness(12, 0), celda.Padding);
    }

    [AvaloniaFact]
    public void Celda_TextoEmpiezaA12DelBordeDeLaCeldaComoEnElMockup()
    {
        // Fluent le da 12 de margen propio al TextBlock de DataGridTextColumn: sin anularlo el
        // texto queda a 24 (12 + 12), el doble que en .v2-grilla-celda.
        var celda = MontarGrilla().GetVisualDescendants().OfType<DataGridCell>().First(c => !c.Classes.Contains("num"));
        var texto = celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock");

        Assert.Equal(12, texto.TranslatePoint(new Point(0, 0), celda)!.Value.X);
    }

    [AvaloniaFact]
    public void Grilla_BordeSeparadorRadio8YConservaResizeYAnchoMinimo()
    {
        var grilla = MontarGrilla();

        Assert.Equal(Hex("#F0EFED"), ColorDe(grilla.BorderBrush));
        Assert.Equal(Hex("#F0EFED"), ColorDe(grilla.HorizontalGridLinesBrush));
        Assert.Equal(new CornerRadius(8), grilla.CornerRadius);
        Assert.True(grilla.CanUserResizeColumns);
        Assert.Equal(40, grilla.MinColumnWidth);
    }

    [AvaloniaFact]
    public void Header_SinSeparadoresVerticales()
    {
        var separadores = MontarGrilla().GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Name == "VerticalSeparator").ToList();

        Assert.All(separadores, s => Assert.False(ArbolVisual.EsVisibleEnArbol(s)));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~DataGridV2Tests"`
Expected: FAIL (compila; fallan todos salvo `FueraDeUi2_ConservaLaZebraVieja`, p. ej. `Filas_...` con `Expected: 40, Actual: 37`).

- [ ] **Step 2: Escribir `Themes/V2/DataGridV2.axaml`**

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- UI v2 (spec 2026-09-28 §3.3): tema de DataGrid, SOLO dentro de .ui2. Se incluye al final
         de App.axaml. Themes/DataGrid.axaml (viejo) sigue aplicando y NO se repite acá lo que se
         conserva: CanUserResizeColumns, MinColumnWidth 40 (fix 3548397 de MinWidth por columna y
         scroll horizontal) y el apagado del recuadro de foco de celda.
         Geometría MEDIDA en Avalonia.Controls.DataGrid 12.0.1: la fila mide RowHeight + 1 de
         PART_BottomGridLine; el header mide ColumnHeaderHeight con el separador de 1 adentro. Por eso
         RowHeight es 39 (pitch 40 = V2AltoFilaGrilla) y ColumnHeaderHeight es el token (36).
         Custodiado por UiTests/V2/DataGridV2Tests. -->

    <Style Selector=".ui2 DataGrid">
        <Setter Property="RowHeight" Value="39" />
        <Setter Property="ColumnHeaderHeight" Value="{DynamicResource V2AltoHeaderGrilla}" />
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2SeparadorGrillaBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{DynamicResource V2RadioControl}" />
        <Setter Property="GridLinesVisibility" Value="Horizontal" />
        <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource V2SeparadorGrillaBrush}" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>

    <!-- Sin zebra: pisa DataGridRow:nth-child(2n) del tema viejo. -->
    <Style Selector=".ui2 DataGridRow, .ui2 DataGridRow:nth-child(2n)">
        <Setter Property="Background" Value="{DynamicResource V2SuperficieBrush}" />
    </Style>
    <Style Selector=".ui2 DataGridRow:pointerover /template/ Rectangle#BackgroundRectangle">
        <Setter Property="Fill" Value="{DynamicResource V2HeaderGrillaBrush}" />
        <Setter Property="Opacity" Value="1" />
    </Style>
    <Style Selector=".ui2 DataGridRow:selected /template/ Rectangle#BackgroundRectangle">
        <Setter Property="Fill" Value="{DynamicResource V2AcentoSuaveBrush}" />
        <Setter Property="Opacity" Value="1" />
    </Style>

    <Style Selector=".ui2 DataGridColumnHeader">
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteHeaderColumnaTamano}" />
        <Setter Property="FontWeight" Value="{DynamicResource V2FuenteHeaderColumnaPeso}" />
        <Setter Property="LetterSpacing" Value="{DynamicResource V2FuenteHeaderColumnaEspaciadoLetras}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
        <Setter Property="Background" Value="{DynamicResource V2HeaderGrillaBrush}" />
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="MinHeight" Value="0" />
    </Style>
    <Style Selector=".ui2 DataGridColumnHeader /template/ Rectangle#VerticalSeparator">
        <Setter Property="IsVisible" Value="False" />
    </Style>
    <Style Selector=".ui2 DataGrid /template/ Rectangle#PART_ColumnHeadersAndRowsSeparator">
        <Setter Property="Fill" Value="{DynamicResource V2SeparadorGrillaBrush}" />
    </Style>

    <Style Selector=".ui2 DataGridCell">
        <Setter Property="Padding" Value="12,0" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="FontSize" Value="{DynamicResource V2FuenteControlTamano}" />
        <Setter Property="Foreground" Value="{DynamicResource V2TextoEtiquetaBrush}" />
    </Style>
    <!-- El TextBlock que genera DataGridTextColumn (CellTextBlock) trae Margin 12,0,12,0 del tema
         Fluent (medido: con Padding 12 de celda el texto quedaba a 24 del borde). El padding es uno
         solo, el de la celda, así las columnas de texto y las de template (Insignia) alinean igual. -->
    <Style Selector=".ui2 DataGridCell TextBlock#CellTextBlock">
        <Setter Property="Margin" Value="0" />
    </Style>
    <!-- Columna numérica: derecha + cifras tabulares (spec §2.3). TextElement.FontFeatures se
         hereda al TextBlock de la celda (verificado con render Skia: "1111" pasa de 25 a 34 px). -->
    <Style Selector=".ui2 DataGridCell.num">
        <Setter Property="HorizontalContentAlignment" Value="Right" />
        <Setter Property="TextElement.FontFeatures" Value="+tnum" />
    </Style>
    <Style Selector=".ui2 DataGridCell.num TextBlock">
        <Setter Property="HorizontalAlignment" Value="Right" />
    </Style>

</Styles>
```

- [ ] **Step 3: Incluirlo en la app y en los dos bancos**

En `App.axaml`, después del `StyleInclude` de `ControlesV2.axaml`:

```xml
        <StyleInclude Source="avares://GestionMunicipal/Themes/V2/DataGridV2.axaml"/>
```

En `TestAppBuilder.cs` y `VisualTestApp.cs`, al final de `StyleIncludes`:

```csharp
        "avares://GestionMunicipal/Themes/V2/DataGridV2.axaml",
```

- [ ] **Step 4: CSS de mockups**

Agregar al final de `base.css`:

```css
/* DataGrid v2 (Themes/V2/DataGridV2.axaml). Header 36 con su separador adentro; filas de pitch
   40 con el separador abajo (border-box); celdas con padding 0 12. Los anchos de columna del
   mockup se copian de la vista: fijas con flex 0 0 Npx, "*" con flex 1 1 0. */
.v2-grilla { border: 1px solid var(--v2-color-separador-grilla); border-radius: var(--v2-radio-control); overflow: hidden; background: var(--v2-color-superficie); }
.v2-grilla-header {
  display: flex; align-items: center; height: var(--v2-alto-header-grilla);
  background: var(--v2-color-header-grilla); border-bottom: 1px solid var(--v2-color-separador-grilla);
  font-size: var(--v2-fuente-header-columna-tamano); line-height: var(--v2-fuente-header-columna-alto-linea); font-weight: var(--v2-fuente-header-columna-peso);
  letter-spacing: var(--v2-fuente-header-columna-espaciado-letras); text-transform: var(--v2-fuente-header-columna-transformacion);
  color: var(--v2-color-texto-terciario);
}
.v2-grilla-fila {
  display: flex; align-items: center; height: var(--v2-alto-fila-grilla);
  border-bottom: 1px solid var(--v2-color-separador-grilla); background: var(--v2-color-superficie);
  font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-etiqueta);
}
/* Celda: flex para centrar vertical igual que VerticalContentAlignment=Center; la numérica
   alinea a la derecha con justify-content (text-align no aplica a un contenedor flex). */
.v2-grilla-celda { display: flex; align-items: center; align-self: stretch; padding: 0 12px; white-space: nowrap; overflow: hidden; }
.v2-grilla-celda.v2-num { justify-content: flex-end; }
```

- [ ] **Step 5: Correr y ver el verde**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, `Failed: 0` (incluye `DataGridEstiloRealTests`, `GastosViewColumnasAnchoTests`, `DataGridColumnasRedimensionablesTests` viejos).
Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
Expected: PASS.

- [ ] **Step 6: Verificación por mutación**

(a) Quitar el `Setter` de `RowHeight`. Expected: FAIL en `Filas_TienenElPitchDelTokenAltoFilaGrilla`. Restaurar; PASS.
(b) Quitar el estilo de "Sin zebra". Expected: FAIL en `SinZebra_TodasLasFilasBlancas`. Restaurar; PASS.
(c) Quitar el `Setter` de `TextElement.FontFeatures`. Expected: FAIL en `CeldaNum_ALaDerechaConCifrasTabulares`. Restaurar; PASS.
(d) Quitar el estilo `.ui2 DataGridCell TextBlock#CellTextBlock`. Expected: FAIL en `Celda_TextoEmpiezaA12DelBordeDeLaCeldaComoEnElMockup` (`Expected: 12, Actual: 24`). Restaurar; PASS.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Themes/V2/DataGridV2.axaml src/StockApp.Presentation/App.axaml tests/StockApp.Presentation.UiTests tests/StockApp.Presentation.VisualTests docs/ui-v2/mockups/_base/base.css
git commit -m "feat(ui-v2): agrega tema v2 de DataGrid sin zebra con pitch de fila 40 y cifras tabulares"
```

## Bloque D — Validación end-to-end y calibración

### Task 17: Pantalla catálogo-de-componentes, su mockup y su test de comparación

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesViewModel.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesView.axaml`
- Create: `tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesView.axaml.cs`
- Create: `tests/StockApp.Presentation.VisualTests/CatalogoComponentesTests.cs`
- Create: `docs/ui-v2/mockups/componentes/catalogo.html`

**Interfaces:**
- Consumes: todos los componentes v2 (Tasks 11 a 15), estilos `.ui2` (Tasks 10, 13, 16), `ComparacionVisual.CompararConMockup` (Task 9), `RenderizadorVista` (Task 8).
- Produces:
  - `CatalogoComponentesView` (`UserControl`, `Classes="ui2"`) y `CatalogoComponentesViewModel` con datos fijos, en el proyecto de tests visuales: fuera de `GestionMunicipal` y de la navegación de producción (no lo ven `ViewLocator`, `GuardianDePatronTests` ni `ReflexionVistaViewModelTests`).
  - `record ProductoMuestra(string Codigo, string Nombre, string Costo, string Stock, string Estado, VarianteInsignia VarianteEstado)`.
  - Mockup `docs/ui-v2/mockups/componentes/catalogo.html` (pantalla `componentes-catalogo`).
  - Cómo se accede: `scripts/ui-v2/comparar.sh Catalogo` y abrir `artifacts/ui-v2/comparacion/componentes-catalogo/index.html` (el `app.png` ES la pantalla renderizada).

- [ ] **Step 1: Escribir los tests (rojo)**

`tests/StockApp.Presentation.VisualTests/CatalogoComponentesTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.VisualTests.Catalogo;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Validación end-to-end de la Fase 0 (spec §7): el catálogo con TODOS los componentes v2 tiene
/// que quedar igual a su mockup. Es también la pantalla con la que se calibra el umbral.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class CatalogoComponentesTests
{
    [AvaloniaFact]
    public void Catalogo_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new CatalogoComponentesView(), new CatalogoComponentesViewModel(), "componentes/catalogo.html");

    [AvaloniaFact]
    public void Catalogo_MuestraCadaComponenteYControlV2()
    {
        // Si un componente falta en el catálogo, la calibración no lo cubre.
        var vista = new CatalogoComponentesView();
        RenderizadorVista.RenderizarAPng(vista, new CatalogoComponentesViewModel(),
            Path.Combine(Directory.CreateTempSubdirectory("catalogo-").FullName, "catalogo.png"));
        var tipos = vista.GetVisualDescendants().Select(v => v.GetType()).ToHashSet();

        Assert.Contains(typeof(EncabezadoPagina), tipos);
        Assert.Contains(typeof(TarjetaContenido), tipos);
        Assert.Contains(typeof(BarraFiltros), tipos);
        Assert.Contains(typeof(DataGrid), tipos);
        Assert.Contains(typeof(CampoFormulario), tipos);
        Assert.Contains(typeof(Insignia), tipos);
        Assert.Contains(typeof(Alerta), tipos);
        Assert.Contains(typeof(TarjetaMetrica), tipos);
        Assert.Contains(typeof(TarjetaAcceso), tipos);
        Assert.Contains(typeof(EstadoVacio), tipos);
        Assert.Contains(typeof(ComboBox), tipos);
        Assert.Contains(typeof(CalendarDatePicker), tipos);
        Assert.Contains(typeof(NumericUpDown), tipos);
        Assert.Contains(typeof(CheckBox), tipos);
        var clasesDeBoton = vista.GetVisualDescendants().OfType<Button>().SelectMany(b => b.Classes).ToHashSet();
        Assert.Superset(new HashSet<string> { "primario", "secundario", "ghost", "peligro" }, clasesDeBoton);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~CatalogoComponentesTests"`
Expected: FAIL de compilación, `The type or namespace name 'Catalogo' does not exist in the namespace 'StockApp.Presentation.VisualTests'`.

- [ ] **Step 2: ViewModel con datos fijos**

`tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesViewModel.cs`:

```csharp
using System.Windows.Input;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.VisualTests.Catalogo;

public sealed record ProductoMuestra(string Codigo, string Nombre, string Costo, string Stock, string Estado, VarianteInsignia VarianteEstado);

/// <summary>
/// Datos fijos del catálogo: los MISMOS textos que docs/ui-v2/mockups/componentes/catalogo.html.
/// Montos ya formateados: el catálogo valida componentes, no converters.
/// </summary>
public sealed class CatalogoComponentesViewModel
{
    public string Busqueda { get; set; } = "";

    public IReadOnlyList<string> Categorias { get; } = ["Todas las categorías", "Ferretería", "Pinturería"];

    public string CategoriaFiltro { get; set; } = "Todas las categorías";

    public IReadOnlyList<ProductoMuestra> Productos { get; } =
    [
        new("A-001", "Tornillo 8x40", "12.50", "340", "Activo", VarianteInsignia.Exito),
        new("A-002", "Pintura látex blanca 4 L", "1850.00", "-3", "Stock negativo", VarianteInsignia.Peligro),
        new("A-003", "Guantes de nitrilo", "95.00", "120", "Inactivo", VarianteInsignia.Neutro),
    ];

    public string Nombre { get; set; } = "Tornillo 8x40";

    public string Codigo { get; set; } = "A-001";

    public IReadOnlyList<string> CategoriasProducto { get; } = ["Ferretería", "Pinturería"];

    public string CategoriaSeleccionada { get; set; } = "Ferretería";

    public DateTime? FechaAlta { get; set; } = new DateTime(2026, 9, 28);

    public decimal StockInicial { get; set; } = 340;

    public bool Activo { get; set; } = true;

    public ICommand VolverCommand { get; } = new ComandoInerte();

    private sealed class ComandoInerte : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
```

- [ ] **Step 3: La vista**

`tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:cat="using:StockApp.Presentation.VisualTests.Catalogo"
             x:Class="StockApp.Presentation.VisualTests.Catalogo.CatalogoComponentesView"
             x:DataType="cat:CatalogoComponentesViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- Pantalla de desarrollo de la Fase 0: todos los componentes v2 con datos fijos. Vive en el
         proyecto de tests visuales, fuera de GestionMunicipal y de la navegación. Su mockup es
         docs/ui-v2/mockups/componentes/catalogo.html: un cambio de layout acá va con el mismo
         cambio allá, en el mismo commit. -->

    <Grid Margin="24" RowDefinitions="Auto,*">
        <v2:EncabezadoPagina Grid.Row="0"
                             BreadcrumbOrigen="Inicio"
                             BreadcrumbComando="{Binding VolverCommand}"
                             BreadcrumbActual="Catálogo de componentes"
                             Titulo="Catálogo de componentes"
                             Subtitulo="Tokens y componentes de la UI v2 con datos fijos">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Classes="secundario" Content="Cancelar" />
                <Button Classes="primario" Content="Guardar" />
            </StackPanel>
        </v2:EncabezadoPagina>

        <Grid Grid.Row="1" ColumnDefinitions="736,16,*">
            <StackPanel Grid.Column="0" Spacing="16">
                <v2:TarjetaContenido>
                    <StackPanel>
                        <v2:BarraFiltros TextoBusqueda="{Binding Busqueda}"
                                         MarcaAguaBusqueda="Buscar por código o nombre">
                            <ComboBox Width="200"
                                      ItemsSource="{Binding Categorias}"
                                      SelectedItem="{Binding CategoriaFiltro}" />
                        </v2:BarraFiltros>
                        <DataGrid Height="158"
                                  AutoGenerateColumns="False"
                                  IsReadOnly="True"
                                  HeadersVisibility="Column"
                                  ItemsSource="{Binding Productos}">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="CÓDIGO" Width="100" x:DataType="cat:ProductoMuestra" Binding="{Binding Codigo}" />
                                <DataGridTextColumn Header="NOMBRE" Width="*" x:DataType="cat:ProductoMuestra" Binding="{Binding Nombre}" />
                                <DataGridTextColumn Header="COSTO" Width="110" CellStyleClasses="num" x:DataType="cat:ProductoMuestra" Binding="{Binding Costo}" />
                                <DataGridTextColumn Header="STOCK" Width="90" CellStyleClasses="num" x:DataType="cat:ProductoMuestra" Binding="{Binding Stock}" />
                                <DataGridTemplateColumn Header="ESTADO" Width="140">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="cat:ProductoMuestra">
                                            <v2:Insignia Texto="{Binding Estado}" Variante="{Binding VarianteEstado}" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>
                        <TextBlock Classes="pie-conteo" Margin="0,12,0,0" Text="3 productos" />
                    </StackPanel>
                </v2:TarjetaContenido>

                <v2:TarjetaContenido Padding="24">
                    <StackPanel>
                        <TextBlock Classes="subtitulo-formulario" Margin="0,0,0,12" Text="Datos del producto" />
                        <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,16,Auto,16,Auto">
                            <v2:CampoFormulario Grid.Row="0" Grid.Column="0" Etiqueta="Nombre" Requerido="True">
                                <TextBox Text="{Binding Nombre}" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario Grid.Row="0" Grid.Column="2" Etiqueta="Código" Requerido="True">
                                <TextBox x:Name="CampoCodigo" Text="{Binding Codigo}" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario Grid.Row="2" Grid.Column="0" Etiqueta="Categoría">
                                <ComboBox HorizontalAlignment="Stretch"
                                          ItemsSource="{Binding CategoriasProducto}"
                                          SelectedItem="{Binding CategoriaSeleccionada}" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario Grid.Row="2" Grid.Column="2" Etiqueta="Fecha de alta">
                                <CalendarDatePicker HorizontalAlignment="Stretch"
                                                    SelectedDate="{Binding FechaAlta}"
                                                    SelectedDateFormat="Custom"
                                                    CustomDateFormatString="dd/MM/yyyy" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario Grid.Row="4" Grid.Column="0" Etiqueta="Stock inicial">
                                <NumericUpDown HorizontalAlignment="Stretch"
                                               Value="{Binding StockInicial}"
                                               FormatString="0" />
                            </v2:CampoFormulario>
                            <CheckBox Grid.Row="4" Grid.Column="2"
                                      VerticalAlignment="Bottom"
                                      Content="Producto activo"
                                      IsChecked="{Binding Activo}" />
                        </Grid>
                    </StackPanel>
                </v2:TarjetaContenido>
            </StackPanel>

            <StackPanel Grid.Column="2" Spacing="16">
                <Grid ColumnDefinitions="*,12,*">
                    <v2:TarjetaMetrica Grid.Column="0" Valor="3" Etiqueta="Facturas a vencer (7 días)" />
                    <v2:TarjetaMetrica Grid.Column="2" SinDato="True" Etiqueta="Último backup" />
                </Grid>

                <v2:TarjetaContenido>
                    <StackPanel Spacing="12">
                        <TextBlock Classes="titulo-seccion" Text="Insignias y botones" />
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <v2:Insignia Texto="Inactivo" Variante="Neutro" />
                            <v2:Insignia Texto="Próxima" Variante="Advertencia" />
                            <v2:Insignia Texto="Stock negativo" Variante="Peligro" />
                            <v2:Insignia Texto="Activo" Variante="Exito" />
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <Button Classes="secundario" Content="Exportar" />
                            <Button Classes="ghost" Content="Limpiar filtros" />
                            <Button Classes="peligro" Content="Eliminar" />
                        </StackPanel>
                    </StackPanel>
                </v2:TarjetaContenido>

                <StackPanel Spacing="8">
                    <v2:Alerta Variante="Info" Texto="Los cambios se guardan al confirmar." />
                    <v2:Alerta Variante="Advertencia" Texto="El último backup tiene más de 7 días." />
                    <v2:Alerta Variante="Error" Texto="No se pudo conectar con el servidor." />
                </StackPanel>

                <Grid ColumnDefinitions="*,12,*">
                    <v2:TarjetaAcceso Grid.Column="0"
                                      Icono="mdi-package-variant"
                                      Titulo="Productos"
                                      Descripcion="Ver y administrar el catálogo" />
                    <v2:TarjetaAcceso Grid.Column="2"
                                      Icono="mdi-chart-box-outline"
                                      Titulo="Reportes"
                                      Descripcion="Valorización y estadísticas" />
                </Grid>

                <v2:TarjetaContenido>
                    <v2:EstadoVacio Titulo="Sin resultados"
                                    Mensaje="No hay productos que coincidan con la búsqueda." />
                </v2:TarjetaContenido>
            </StackPanel>
        </Grid>
    </Grid>
</UserControl>
```

`tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.VisualTests.Catalogo;

public partial class CatalogoComponentesView : UserControl
{
    public CatalogoComponentesView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Estado de error fijo para mostrar un campo inválido. Se setea en Loaded (después de que
        // los bindings resolvieron: una actualización exitosa del binding limpiaría el error) y
        // directo, no por validación del VM: el catálogo valida CÓMO se ve el seam, no el seam.
        DataValidationErrors.SetErrors(CampoCodigo, new object[] { "El código ya existe" });
    }
}
```

- [ ] **Step 4: El mockup**

`docs/ui-v2/mockups/componentes/catalogo.html` (los SVG salen de `icono-svg` y de las notas de `base.css`; los paths van completos):

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Catálogo de componentes (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* Layout propio de esta pantalla: el mismo Grid que CatalogoComponentesView.axaml. */
  .contenido { position: absolute; left: 24px; top: 24px; right: 24px; bottom: 24px; display: flex; flex-direction: column; }
  .columnas { display: flex; gap: 16px; align-items: flex-start; }
  .izquierda { flex: 0 0 736px; display: flex; flex-direction: column; gap: 16px; }
  .derecha { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; gap: 16px; }
  .par { display: flex; gap: 12px; }
  .par > * { flex: 1 1 0; min-width: 0; }
  .fila-8 { display: flex; gap: 8px; align-items: center; }
  .pila-12 { display: flex; flex-direction: column; gap: 12px; }
  .pila-8 { display: flex; flex-direction: column; gap: 8px; }
  .combo-filtro { flex: 0 0 200px; width: 200px; }
  .formulario { padding: 24px; }
  .subtitulo { margin-bottom: 12px; }
  .campos { display: grid; grid-template-columns: 1fr 1fr; column-gap: 16px; row-gap: 16px; align-items: start; }
  .abajo { align-self: end; }
  .pie { margin-top: 12px; }
  .col-codigo { flex: 0 0 100px; }
  .col-nombre { flex: 1 1 0; min-width: 0; }
  .col-costo { flex: 0 0 110px; }
  .col-stock { flex: 0 0 90px; }
  .col-estado { flex: 0 0 140px; }
</style>
</head>
<body>
<div class="v2-ventana">
  <div class="contenido">

    <div class="v2-encabezado">
      <div class="v2-breadcrumb"><span class="v2-breadcrumb-origen">Inicio</span><span> / </span><span>Catálogo de componentes</span></div>
      <div class="v2-encabezado-fila">
        <div>
          <div class="v2-titulo-pagina">Catálogo de componentes</div>
          <div class="v2-encabezado-subtitulo">Tokens y componentes de la UI v2 con datos fijos</div>
        </div>
        <div class="v2-acciones">
          <span class="v2-boton v2-boton--secundario">Cancelar</span>
          <span class="v2-boton v2-boton--primario">Guardar</span>
        </div>
      </div>
    </div>

    <div class="columnas">
      <div class="izquierda">
        <div class="v2-tarjeta">
          <div class="v2-barra-filtros">
            <div class="v2-input v2-busqueda"><span class="v2-input-placeholder">Buscar por código o nombre</span></div>
            <div class="v2-input combo-filtro"><span>Todas las categorías</span><svg class="v2-glifo-combo" viewBox="19 486 2010 1095" aria-hidden="true"><path fill="currentColor" d="M1939 486L2029 576L1024 1581L19 576L109 486L1024 1401L1939 486Z"/></svg></div>
          </div>
          <div class="v2-grilla">
            <div class="v2-grilla-header">
              <div class="v2-grilla-celda col-codigo">CÓDIGO</div>
              <div class="v2-grilla-celda col-nombre">NOMBRE</div>
              <div class="v2-grilla-celda col-costo">COSTO</div>
              <div class="v2-grilla-celda col-stock">STOCK</div>
              <div class="v2-grilla-celda col-estado">ESTADO</div>
            </div>
            <div class="v2-grilla-fila">
              <div class="v2-grilla-celda col-codigo">A-001</div>
              <div class="v2-grilla-celda col-nombre">Tornillo 8x40</div>
              <div class="v2-grilla-celda col-costo v2-num">12.50</div>
              <div class="v2-grilla-celda col-stock v2-num">340</div>
              <div class="v2-grilla-celda col-estado"><span class="v2-insignia v2-insignia--exito">Activo</span></div>
            </div>
            <div class="v2-grilla-fila">
              <div class="v2-grilla-celda col-codigo">A-002</div>
              <div class="v2-grilla-celda col-nombre">Pintura látex blanca 4 L</div>
              <div class="v2-grilla-celda col-costo v2-num">1850.00</div>
              <div class="v2-grilla-celda col-stock v2-num">-3</div>
              <div class="v2-grilla-celda col-estado"><span class="v2-insignia v2-insignia--peligro">Stock negativo</span></div>
            </div>
            <div class="v2-grilla-fila">
              <div class="v2-grilla-celda col-codigo">A-003</div>
              <div class="v2-grilla-celda col-nombre">Guantes de nitrilo</div>
              <div class="v2-grilla-celda col-costo v2-num">95.00</div>
              <div class="v2-grilla-celda col-stock v2-num">120</div>
              <div class="v2-grilla-celda col-estado"><span class="v2-insignia">Inactivo</span></div>
            </div>
          </div>
          <div class="v2-pie-conteo pie">3 productos</div>
        </div>

        <div class="v2-tarjeta formulario">
          <div class="v2-subtitulo-formulario subtitulo">Datos del producto</div>
          <div class="campos">
            <div class="v2-campo">
              <div class="v2-campo-etiqueta"><span>Nombre</span><span class="v2-campo-requerido">*</span></div>
              <div><div class="v2-input">Tornillo 8x40</div></div>
            </div>
            <div class="v2-campo">
              <div class="v2-campo-etiqueta"><span>Código</span><span class="v2-campo-requerido">*</span></div>
              <div><div class="v2-input v2-input--error">A-001</div><div class="v2-error-campo">El código ya existe</div></div>
            </div>
            <div class="v2-campo">
              <div class="v2-campo-etiqueta"><span>Categoría</span></div>
              <div><div class="v2-input"><span>Ferretería</span><svg class="v2-glifo-combo" viewBox="19 486 2010 1095" aria-hidden="true"><path fill="currentColor" d="M1939 486L2029 576L1024 1581L19 576L109 486L1024 1401L1939 486Z"/></svg></div></div>
            </div>
            <div class="v2-campo">
              <div class="v2-campo-etiqueta"><span>Fecha de alta</span></div>
              <div><div class="v2-input"><span>28/09/2026</span><svg class="v2-icono v2-glifo-fecha" width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M19 3H18V1H16V3H8V1H6V3H5C3.89 3 3 3.9 3 5V19C3 20.11 3.9 21 5 21H19C20.11 21 21 20.11 21 19V5C21 3.9 20.11 3 19 3M19 19H5V9H19V19M19 7H5V5H19V7Z"/></svg></div></div>
            </div>
            <div class="v2-campo">
              <div class="v2-campo-etiqueta"><span>Stock inicial</span></div>
              <div><div class="v2-input">340</div></div>
            </div>
            <div class="v2-casilla-fila abajo">
              <div class="v2-casilla-caja">
                <div class="v2-casilla v2-casilla--marcada"></div>
                <svg class="v2-glifo-check" viewBox="0.012 0.012 17.475 12.988" aria-hidden="true"><path fill="currentColor" d="M5.5 10.586 1.707 6.793A1 1 0 0 0 .293 8.207l4.5 4.5a1 1 0 0 0 1.414 0l11-11A1 1 0 0 0 15.793.293L5.5 10.586Z"/></svg>
              </div>
              <span>Producto activo</span>
            </div>
          </div>
        </div>
      </div>

      <div class="derecha">
        <div class="par">
          <div class="v2-metrica">
            <div class="v2-metrica-valor">3</div>
            <div class="v2-metrica-etiqueta">Facturas a vencer (7 días)</div>
          </div>
          <div class="v2-metrica">
            <div class="v2-metrica-valor v2-metrica-valor--sin-dato">—</div>
            <div class="v2-metrica-etiqueta">Último backup</div>
          </div>
        </div>

        <div class="v2-tarjeta pila-12">
          <div class="v2-titulo-seccion">Insignias y botones</div>
          <div class="fila-8">
            <span class="v2-insignia">Inactivo</span>
            <span class="v2-insignia v2-insignia--advertencia">Próxima</span>
            <span class="v2-insignia v2-insignia--peligro">Stock negativo</span>
            <span class="v2-insignia v2-insignia--exito">Activo</span>
          </div>
          <div class="fila-8">
            <span class="v2-boton v2-boton--secundario">Exportar</span>
            <span class="v2-boton v2-boton--ghost">Limpiar filtros</span>
            <span class="v2-boton v2-boton--peligro">Eliminar</span>
          </div>
        </div>

        <div class="pila-8">
          <div class="v2-alerta v2-alerta--info">Los cambios se guardan al confirmar.</div>
          <div class="v2-alerta v2-alerta--advertencia">El último backup tiene más de 7 días.</div>
          <div class="v2-alerta v2-alerta--error">No se pudo conectar con el servidor.</div>
        </div>

        <div class="par">
          <div class="v2-acceso">
            <svg class="v2-icono" width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M2,10.96C1.5,10.68 1.35,10.07 1.63,9.59L3.13,7C3.24,6.8 3.41,6.66 3.6,6.58L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.66,6.72 20.82,6.88 20.91,7.08L22.36,9.6C22.64,10.08 22.47,10.69 22,10.96L21,11.54V16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V10.96C2.7,11.13 2.32,11.14 2,10.96M12,4.15V4.15L12,10.85V10.85L17.96,7.5L12,4.15M5,15.91L11,19.29V12.58L5,9.21V15.91M19,15.91V12.69L14,15.59C13.67,15.77 13.3,15.76 13,15.6V19.29L19,15.91M13.85,13.36L20.13,9.73L19.55,8.72L13.27,12.35L13.85,13.36Z"/></svg>
            <div>
              <div class="v2-acceso-titulo">Productos</div>
              <div class="v2-acceso-descripcion">Ver y administrar el catálogo</div>
            </div>
          </div>
          <div class="v2-acceso">
            <svg class="v2-icono" width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M9 17H7V10H9V17M13 17H11V7H13V17M17 17H15V13H17V17M19 19H5V5H19V19.1M19 3H5C3.9 3 3 3.9 3 5V19C3 20.1 3.9 21 5 21H19C20.1 21 21 20.1 21 19V5C21 3.9 20.1 3 19 3Z"/></svg>
            <div>
              <div class="v2-acceso-titulo">Reportes</div>
              <div class="v2-acceso-descripcion">Valorización y estadísticas</div>
            </div>
          </div>
        </div>

        <div class="v2-tarjeta">
          <div class="v2-vacio">
            <div class="v2-vacio-titulo">Sin resultados</div>
            <div class="v2-vacio-mensaje">No hay productos que coincidan con la búsqueda.</div>
          </div>
        </div>
      </div>
    </div>

  </div>
</div>
</body>
</html>
```

Nota: el `ComboBox` de "Categoría" ocupa todo el ancho del campo: el glifo queda a la derecha por el `justify-content: space-between` de `.v2-input`.

- [ ] **Step 5: Correr y ver el verde de la estructura**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~Catalogo_MuestraCadaComponenteYControlV2"`
Expected: PASS.

- [ ] **Step 6: Primera comparación y ciclo de ajuste**

Run: `scripts/ui-v2/comparar.sh Catalogo_IgualAlMockup`
Abrir `artifacts/ui-v2/comparacion/componentes-catalogo/index.html`.

Si falla, ajustar SIN tocar tokens ni la API de los componentes, con estas reglas y en este orden:
1. Diferencia de geometría en un componente (alto, padding, separación, posición de un glifo): se mide en la app (`TranslatePoint`/`Bounds` en un test de UiTests de ese componente) y se corrige el CSS de `base.css` para que replique la geometría medida. Si la medida contradice un token o la spec, se corrige el `ControlTheme`/estilo v2 y se agrega el assert a su test de componente.
2. Diferencia de layout de la pantalla (columnas, gaps): se corrige el HTML o el XAML del catálogo, el que se aparte de lo escrito en esta task.
3. Diferencias solo de antialiasing (rojo fino en bordes de glifos, sin teselas densas): no se tocan; son el ruido que la Task 18 mide.

Iterar hasta `Passed` con los umbrales provisionales (2.0% y 20 teselas densas). Cada ajuste de `base.css` o de un `ControlTheme` corre la suite de UI (`dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`) antes de seguir.

- [ ] **Step 7: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests docs/ui-v2/mockups/componentes docs/ui-v2/mockups/_base/base.css src/StockApp.Presentation/Themes/V2 src/StockApp.Presentation/Controls/V2 tests/StockApp.Presentation.UiTests/V2
git commit -m "feat(ui-v2): agrega pantalla catálogo de componentes con su mockup y test de comparación"
```

### Task 18: Calibración del umbral con el catálogo

**Files:**
- Modify: `tests/StockApp.Presentation.VisualTests/Comparacion/UmbralComparacion.cs`
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (nueva §6.5)

**Interfaces:**
- Consumes: `CatalogoComponentesTests.Catalogo_IgualAlMockup`, `artifacts/ui-v2/comparacion/componentes-catalogo/resultado.json`.
- Produces: `UmbralComparacion.PorcentajePorDefecto` y `UmbralComparacion.TeselasDensasPorDefecto` calibrados (los usan TODAS las fases) y la §6.5 de la spec con las mediciones.

El umbral tiene que dejar pasar el ruido de antialiasing y rechazar el defecto chico más difícil. Se mide las dos cosas.

- [ ] **Step 1: Medir la línea de base (3 corridas)**

Run tres veces:

```bash
scripts/ui-v2/comparar.sh Catalogo_IgualAlMockup
cat artifacts/ui-v2/comparacion/componentes-catalogo/resultado.json
```

Anotar `Porcentaje` y `TeselasDensas` de cada corrida. Expected: los tres iguales (el render y la captura son deterministas). Si difieren, NO calibrar: buscar la fuente de no determinismo (fecha, animación, fuente sin cargar) y eliminarla primero.

- [ ] **Step 2: Medir cuatro defectos chicos, mutando el catálogo QUITANDO**

Para cada mutación: quitar lo indicado en `CatalogoComponentesView.axaml`, correr `scripts/ui-v2/comparar.sh Catalogo_IgualAlMockup`, anotar `Porcentaje` y `TeselasDensas` de `resultado.json`, y restaurar con `git checkout -- tests/StockApp.Presentation.VisualTests/Catalogo/CatalogoComponentesView.axaml`.

| # | Mutación (quitar) | Defecto que representa |
|---|---|---|
| M1 | `Classes="primario"` del botón "Guardar" del encabezado | color equivocado en un control chico |
| M2 | `Variante="Peligro"` de la insignia "Stock negativo" de la card de insignias | relleno pálido equivocado (delta < 32) |
| M3 | `Margin="0,0,0,12"` del subtítulo "Datos del producto" | layout corrido 12px |
| M4 | `Spacing="8"` del `StackPanel` de las alertas | layout corrido 8px en bloques chicos |

- [ ] **Step 3: Fijar los umbrales con esta regla**

- `TeselasDensasPorDefecto` = teselas de la línea de base (el máximo de las tres corridas). Tiene que ser MENOR que las teselas de cada mutación; si alguna mutación no lo supera, la métrica no la detecta: parar y reportar (no subir el umbral).
- `PorcentajePorDefecto` = el mayor entre (porcentaje de base × 2) y (porcentaje de base + 0.05), redondeado hacia arriba a 0.01. Es una red secundaria contra diferencias difusas grandes (fondo equivocado en toda la pantalla); el detector principal de defectos chicos son las teselas.

Reemplazar el contenido de `tests/StockApp.Presentation.VisualTests/Comparacion/UmbralComparacion.cs` por (con los dos valores calculados en lugar de `<B>` y `<P>`, números literales):

```csharp
namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Compuertas de aceptación de una pantalla contra su mockup (spec §6.3), calibradas en la
/// Fase 0 con la pantalla catálogo-de-componentes (spec §6.5, mediciones y regla). No se suben
/// por pantalla sin aprobación del usuario.
/// </summary>
public static class UmbralComparacion
{
    /// <summary>Máximo % de píxeles con diferencia fuerte (delta de canal &gt; 32).</summary>
    public const double PorcentajePorDefecto = <P>;

    /// <summary>Máximo de teselas 16x16 densas (≥ 25% fuertes o ≥ 90% leves).</summary>
    public const int TeselasDensasPorDefecto = <B>;
}
```

Ejemplo de cómo queda con una base de 0.412% y 2 teselas: `PorcentajePorDefecto = 0.83;` y `TeselasDensasPorDefecto = 2;`.

- [ ] **Step 4: Verificar que el catálogo pasa y que cada mutación falla con los valores nuevos**

Run: `scripts/ui-v2/comparar.sh Catalogo_IgualAlMockup` → Expected: `Passed: 1`.
Repetir M1 a M4 (quitar, correr, restaurar). Expected en cada una: `Failed: 1` con el mensaje `... teselas densas (máximo <B>). Comparación: .../componentes-catalogo/index.html`.

- [ ] **Step 5: Documentar en la spec**

Agregar al final de la §6 de `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (antes de `## 7. Fases`) una sección `### 6.5 Calibración del umbral (Fase 0)` con: la métrica (tolerancia de canal 32, teselas 16×16 con 25% fuertes o 90% leves > 4), la tabla de la línea de base (3 corridas) y de M1 a M4 con porcentaje y teselas medidos, la regla del Step 3, los dos valores elegidos, el flag `--font-render-hinting=none` de Chromium, y la salvedad de §6.4 (el render headless y Windows pueden diferir en antialiasing: la aprobación final es del usuario mirando la app).

- [ ] **Step 6: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Comparacion/UmbralComparacion.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "feat(ui-v2): calibra el umbral de comparación visual con el catálogo de componentes"
```

### Task 19: Cierre de la Fase 0: suite completa, verificación orgánica y comparación para aprobar

**Files:**
- Ninguno nuevo. Si algo falla, el arreglo va en la task dueña del archivo, con su test.

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: evidencia de cierre para el usuario (salida de la suite, capturas antes/después, página de comparación del catálogo).

- [ ] **Step 1: Verificar los generados**

Run:

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar; echo "fuentes exit=$?"
```

Expected: `Tokens generados al día.` y `fuentes exit=0`.

- [ ] **Step 2: Suite completa con la solución**

Run: `mkdir -p artifacts/ui-v2 && dotnet build-server shutdown && dotnet test StockApp.sln 2>&1 | tee artifacts/ui-v2/fase0-suite.log | grep -E "(Passed|Failed)!"`
Expected: una línea `Passed!` por proyecto de test, incluidos `StockApp.UiV2.Herramientas.Tests` y `StockApp.Presentation.VisualTests`. Únicas excepciones admitidas: rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes en el worktree (verificar en el log que TODOS los fallidos de esos proyectos mencionan un `.ods`; cualquier otro rojo es regresión).

- [ ] **Step 3: Verificación orgánica: la app real se ve IGUAL que antes**

Fase 0 no migró ninguna vista: las capturas de la app antes y después tienen que coincidir. Procedimiento de `scripts/gui-verificacion/README.md` (WSLg, captura por PowerShell).

1. `docker ps --filter name=stockapp-pg` → el contenedor está `Up` (si no: `docker start stockapp-pg`).
2. ANTES, desde el checkout principal en `main` (`/home/capua25/workspace/stockapp`, solo lectura, sin cambiar de rama): levantar API y app como indica el README, loguear con `admin` / `Admin1234`, y capturar con `scripts/gui-verificacion/capturar.sh` tres pantallas a `artifacts/ui-v2/organica/antes-{login,inicio,productos}.png` del worktree: Login, Inicio y el listado de Productos. Cerrar la app.
3. DESPUÉS, desde este worktree (`feat/ui-v2`): mismo procedimiento, misma ventana (la geometría persistida hace que el tamaño coincida), capturas a `artifacts/ui-v2/organica/despues-{login,inicio,productos}.png`.
4. Comparar cada par:

```bash
for p in login inicio productos; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- comparar-png \
    artifacts/ui-v2/organica/antes-$p.png artifacts/ui-v2/organica/despues-$p.png artifacts/ui-v2/organica/diff-$p.png
done
```

Expected: `Teselas densas: 0` en las tres (diferencias posibles solo en el cursor de texto o en la hora si una pantalla la muestra: mirar `diff-*.png` y justificar cada zona roja). Si las capturas difieren de tamaño, el comando falla con `Tamaños distintos`: repetir con la ventana maximizada en ambos casos.
5. En la app DESPUÉS, crear y editar un producto de prueba (flujo completo con guardar) para confirmar que la app funciona igual.

- [ ] **Step 4: Generar la comparación del catálogo para aprobación**

Run: `scripts/ui-v2/comparar.sh`
Expected: `Passed!` y la línea `Comparaciones: .../artifacts/ui-v2/comparacion/index.html`. Entregar al usuario la ruta de `artifacts/ui-v2/comparacion/componentes-catalogo/index.html` y la tabla de calibración de la spec §6.5. La aprobación visual del catálogo (incluida la variante Info de `Alerta`, que la spec deja para validar acá, y las decisiones de Fase 0: hover neutro, deshabilitado, sin dato con borde sólido, botón peligro con texto rojo sobre blanco) es del usuario; la Fase 1 no empieza sin ella.

- [ ] **Step 5: Commit de cierre (solo si hubo arreglos en esta task)**

Si los Steps 1 a 4 no cambiaron archivos versionados, no hay commit. Si hubo arreglos, cada uno se commitea con su task dueña (`fix(ui-v2): ...`), nunca como un commit genérico de cierre.
