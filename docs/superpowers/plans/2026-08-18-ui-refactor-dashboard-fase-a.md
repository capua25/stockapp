# Refactor visual "Dashboard de datos" — Fase A (tandas 0-5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir la fundación del refactor visual — andamiaje de tests blindado, tokens de diseño completos, controles base, componentes reutilizables, red de seguridad de permisos y shell rediseñado — dejando el terreno listo para el barrido mecánico de las 58 vistas (Fase B, tandas 6-13).

**Architecture:** Se trabaja de adentro hacia afuera. Primero se blinda el banco de pruebas para que mida lo que la app realmente renderiza (tanda 0). Después se completa `Themes/Tokens.axaml`, que hoy define solo colores, con espaciado, radios, sombras y tipografía (tanda 1), y se estilan los controles base y las grillas contra esos tokens (tanda 2). Recién entonces se crean los cinco componentes reutilizables en una carpeta `Controls/` que hoy no existe (tanda 3). Antes de tocar el shell se escribe la red de tests de permisos que hoy falta (tanda 4), y por último se rediseña el sidebar con grupos colapsables persistidos (tanda 5).

**Tech Stack:** .NET, Avalonia 12.0.5, Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), Avalonia.Headless.XUnit para tests de UI, Optris.Icons.Avalonia (prefijo `mdi`), fuente Inter vía `Avalonia.Fonts.Inter`.

**Spec:** `docs/superpowers/specs/2026-08-18-ui-refactor-dashboard-design.md`

## Global Constraints

Estas reglas aplican a TODAS las tareas de este plan. No se repiten en cada tanda.

- **Tema fijo claro.** No se construye variante oscura ni `ThemeDictionaries`. Fuera de alcance.
- **El verde `#16A34A` NO se usa como texto sobre el sidebar `#1E293B`.** El contraste es 4.44:1, por debajo del umbral de texto (4.5:1). Sirve solo para barra de acento e íconos (umbral gráfico 3:1).
- **Una vista tiene un solo botón primario (verde).** Si hay dos acciones principales, no hay ninguna.
- **No se cambia ni una palabra de copy de la UI.** Hay tests que dependen de literales de texto. Corregir textos es otro trabajo.
- **No se renombran ni se mueven Views.** `ViewLocatorTests.cs` custodia la convención de nombres.
- **Un test se reescribe para que verifique MEJOR, NUNCA para que pase.** Todo test reescrito o nuevo que custodie un gate se valida **por mutación**: se reintroduce el bug, se comprueba el rojo, se saca, se comprueba el verde. Un test que no se vio fallar no es un guardián.
- **Los comentarios XAML no pueden contener `--`.** Rompe el build con AVLN1001 y cascadea a MSB4025. Usá `—` o reformulá.
- **Las versiones de paquetes van en `Directory.Packages.props`** (Central Package Management), nunca en los `.csproj`.
- **Cada tanda cierra con `dotnet test StockApp.sln` en verde y UN commit.**
- **Modo de falla crítico de Avalonia:** un `{Binding PuedeXxx}` con typo evalúa a `null` y `IsVisible` **queda en su default `true`** — el control gateado se le muestra a un usuario sin permisos, en silencio. `ReflexionVistaViewModelTests.cs` es la única red contra esto.

**Comandos de verificación:**

- Suite completa: `dotnet test StockApp.sln`
- Solo tests de UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Un test puntual: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~NombreDelTest"`

---

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `tests/StockApp.Presentation.UiTests/ArbolVisualHelpers.cs` | Helper compartido `EsVisibleEnArbol`, hoy duplicado 4 veces con 2 nombres distintos |
| `tests/StockApp.Presentation.UiTests/SesionFake.cs` | `CurrentSessionFake` unificado con `(rol, permisos)`, hoy duplicado en 4 archivos |
| `tests/StockApp.Presentation.UiTests/TokensDisenioTests.cs` | Guardián de existencia y valor de los tokens de diseño |
| `tests/StockApp.Presentation.UiTests/SidebarContrasteTests.cs` | Contraste WCAG de la paleta de sidebar nueva |
| `tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs` | Los 31 gates del sidebar, hoy sin un solo test |
| `tests/StockApp.Presentation.UiTests/DocumentoFormViewGatesTests.cs` | Los 5 gates de `DocumentoFormView`, hoy sin cobertura |
| `src/StockApp.Presentation/Controls/HeaderVista.axaml` (+ `.axaml.cs`) | Eyebrow, título, resumen, slot de acciones |
| `src/StockApp.Presentation/Controls/TarjetaMetrica.axaml` (+ `.axaml.cs`) | KPI sobre las grillas |
| `src/StockApp.Presentation/Controls/BadgeEstado.axaml` (+ `.axaml.cs`) | Estado como palabra + color, no solo color |
| `src/StockApp.Presentation/Controls/CampoFormulario.axaml` (+ `.axaml.cs`) | Etiqueta, control, error |
| `src/StockApp.Presentation/Controls/EstadoVacio.axaml` (+ `.axaml.cs`) | Distingue "sin datos" de "falló la carga" |
| `src/StockApp.Presentation/Services/IServicioPreferenciasSidebar.cs` | Contrato de persistencia local |
| `src/StockApp.Presentation/Services/PreferenciasSidebar.cs` | `record` persistido |
| `src/StockApp.Presentation/Services/ServicioPreferenciasSidebar.cs` | Implementación sobre `%APPDATA%/StockApp/sidebar.json` |
| `src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs` | Modelo de grupo colapsable del sidebar |
| `tests/StockApp.Presentation.Tests/Services/ServicioPreferenciasSidebarTests.cs` | Round-trip contra path temporal |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/App.axaml:5` | `RequestedThemeVariant` de `"Default"` a `"Light"` |
| `src/StockApp.Presentation/App.axaml.cs:~314` | Registro DI del servicio de preferencias |
| `src/StockApp.Presentation/Themes/Tokens.axaml` | +espaciado, radios, sombras, `TextoTerciarioBrush`, paleta sidebar nueva |
| `src/StockApp.Presentation/Themes/Typography.axaml` | +clase `.micro` |
| `src/StockApp.Presentation/Themes/Controls.axaml` | Consume tokens en vez de literales |
| `src/StockApp.Presentation/Themes/DataGrid.axaml` | De 39 líneas que apagan foco a estilo real de grilla |
| `src/StockApp.Presentation/Views/ShellMainView.axaml` | 450 líneas a `ItemsControl` con grupos colapsables |
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | +estado de expansión por grupo |
| `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` | +`Themes/DataGrid.axaml` |
| `tests/StockApp.Presentation.UiTests/TareaFakes.cs:96-108` | `TareaSessionFake` gana permisos configurables |
| `tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs` | Se borran 2 tests; los de gates pasan a Operador con permisos mixtos |

---

## Tanda 0: Blindaje del andamiaje

**Objetivo:** que el banco de pruebas mida lo que la app realmente renderiza, y sacar del camino lo que estorba. Nada de esto es visible para el usuario; todo lo demás depende de que esté bien.

### Task 0.1: Fijar el tema claro

**Files:**
- Modify: `src/StockApp.Presentation/App.axaml:5`

**Interfaces:**
- Consumes: nada.
- Produces: la app deja de negociar el tema con el sistema operativo. Todas las tandas siguientes asumen fondo claro.

**Por qué este paso NO lleva test:** un test que lea el archivo y verifique que dice `"Light"` sería exactamente el tipo de test cosmético que esta misma tanda borra en la Task 0.4 — asserta un literal sin custodiar comportamiento, y se rompe ante cualquier reformateo. La verificación real de este cambio es la inspección visual con el SO en modo oscuro, listada al cierre de la Fase A. `TestApp` ya fija `ThemeVariant.Light` (`TestAppBuilder.cs:32`), así que los tests headless ya corrían en claro: este cambio alinea producción con lo que el banco de pruebas siempre supuso.

- [ ] **Step 1: Cambiar el atributo**

En `src/StockApp.Presentation/App.axaml`, línea 5, cambiar:

```xml
             RequestedThemeVariant="Default"&gt;
             &lt;!-- "Default" ThemeVariant follows system theme variant. "Dark" or "Light" are other available options. --&gt;
```

por:

```xml
             RequestedThemeVariant="Light"&gt;
             &lt;!-- Tema fijo claro (spec 2026-08-18). Todos los tokens de Themes/Tokens.axaml son
                  de tema claro y no hay ThemeDictionaries: con "Default", en un SO en modo oscuro
                  FluentTheme renderizaba en oscuro lo que él controla (fondo de ventana, TextBox,
                  ComboBox, DataGrid, scrollbars) mientras los tokens propios seguían claros,
                  resultado ilegible. La app tiene aspecto fijo, no negociado con el SO. --&gt;
```

- [ ] **Step 2: Verificar que compila y la suite sigue verde**

Run: `dotnet test StockApp.sln`
Expected: PASS, mismo conteo de tests que antes del cambio.

### Task 0.2: Cargar el estilo real del DataGrid en el banco de pruebas

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs:84-87`
- Test: `tests/StockApp.Presentation.UiTests/DataGridEstiloRealTests.cs` (crear)

**Interfaces:**
- Consumes: nada.
- Produces: a partir de acá, todo test que monte un `DataGrid` ve el mismo estilo que la app real. La tanda 2 depende de esto: sin este include, los tests de estilo de grilla darían falsos verdes contra Fluent crudo.

**El problema:** `TestAppBuilder.cs` incluye `Tokens.axaml` (como `ResourceInclude`), `Typography.axaml` y `Controls.axaml` (como `StyleInclude`), pero **no** `Themes/DataGrid.axaml`. La app sí lo incluye (`App.axaml`, después del `Fluent.xaml` del DataGrid). Las 21 grillas de la app se testean hoy contra Fluent crudo.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/DataGridEstiloRealTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// Guardián del andamiaje, no de una vista. TestAppBuilder.cs cargaba Tokens/Typography/Controls
/// pero NO Themes/DataGrid.axaml, así que las 21 grillas de la app se testeaban contra el Fluent
/// crudo del DataGrid en vez de contra el estilo real. Este test falla si alguien saca ese
/// StyleInclude del banco de pruebas.
/// &lt;/summary&gt;
public class DataGridEstiloRealTests
{
    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="600" Height="400"&gt;
            &lt;DataGrid x:Name="Grilla" AutoGenerateColumns="True" /&gt;
        &lt;/Window&gt;
        """;

    [AvaloniaFact]
    public void Montar_UnaGrilla_ElRecuadroDeFocoDeCeldaEstaApagadoPorElEstiloDeLaApp()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        var grilla = window.GetVisualDescendants().OfType&lt;DataGrid&gt;().First();
        grilla.ItemsSource = new[] { new ItemPrueba { Nombre = "uno", Numero = 1 } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var celda = window.GetVisualDescendants().OfType&lt;DataGridCell&gt;().First();
        celda.Focus();
        Dispatcher.UIThread.RunJobs();

        var focusVisual = celda.GetVisualDescendants().OfType&lt;Grid&gt;()
            .FirstOrDefault(g =&gt; g.Name == "FocusVisual");

        Assert.NotNull(focusVisual);
        Assert.False(
            focusVisual!.IsVisible,
            "El recuadro de foco de celda debe estar apagado por Themes/DataGrid.axaml. "
            + "Si está visible, TestAppBuilder.cs no está cargando ese StyleInclude y todos los "
            + "tests de grilla están midiendo Fluent crudo en vez del estilo real de la app.");
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~DataGridEstiloRealTests"`
Expected: FAIL — `focusVisual.IsVisible` es `true`, porque el estilo que lo apaga no está cargado en el banco de pruebas.

Si en cambio falla con `Assert.NotNull(focusVisual)` (el `Grid#FocusVisual` no aparece en el árbol), significa que el template de `DataGridCell` no se materializó: agregá una segunda `Dispatcher.UIThread.RunJobs()` después del `Focus()` antes de dar por bueno el diagnóstico.

- [ ] **Step 3: Agregar el StyleInclude que falta**

En `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs`, después del bloque que agrega `Controls.axaml` (líneas 84-87), agregar:

```csharp
        // DESPUES del StyleInclude del Fluent.xaml del DataGrid (arriba) y de Controls.axaml,
        // mismo orden exacto que App.axaml: DataGrid.axaml overridea el tema Fluent del DataGrid.
        // Sin esto, las 21 grillas de la app se testean contra Fluent crudo y cualquier test de
        // estilo de grilla da un falso verde.
        Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://StockApp.Presentation/Themes/DataGrid.axaml")
        });
```

- [ ] **Step 4: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~DataGridEstiloRealTests"`
Expected: PASS

- [ ] **Step 5: Correr la suite de UI completa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS.

**Atención:** este cambio hace que el banco de pruebas empiece a aplicar un estilo que antes no aplicaba. Si algún test existente se pone rojo acá, NO lo silencies ni lo ajustes para que pase: significa que ese test estaba verde midiendo algo distinto de lo que la app hace. Anotá cuál es, arreglalo entendiendo por qué cambió, y dejá constancia en el mensaje de commit.

### Task 0.3: Verificar el soporte de `FontFeatures` en Inter

**Files:**
- Test: `tests/StockApp.Presentation.UiTests/SondaFontFeaturesTests.cs` (crear, temporal)

**Interfaces:**
- Consumes: nada.
- Produces: una DECISIÓN que la tanda 1 (clase `.micro`) y la tanda 2 (alineación numérica de grillas) necesitan tomada. No produce código permanente.

**Qué se verifica y por qué:** la spec da por hecho dos features OpenType distintas y no verificadas.

1. **`tnum`** (cifras tabulares): todos los dígitos con el mismo ancho, para que las columnas de números de las 21 grillas queden alineadas. Sin esto, `1.111,00` y `9.999,00` ocupan anchos distintos y la columna se ve torcida.
2. **`smcp`** (versalitas): lo que la spec pide para los headers de tabla y los eyebrows. **La spec no lo lista como riesgo, pero lo es tanto como `tnum`** — Avalonia no tiene `text-transform`, así que "versalitas" o sale de la fuente o no sale.

La `Avalonia.Fonts.Inter` embebida puede no traer ninguna de las dos.

- [ ] **Step 1: Escribir la sonda**

Crear `tests/StockApp.Presentation.UiTests/SondaFontFeaturesTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using Xunit.Abstractions;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// SONDA TEMPORAL — se borra al cerrar la tanda 0. No es un guardián: su trabajo es imprimir
/// mediciones para decidir si Inter (la que embebe Avalonia.Fonts.Inter) soporta las features
/// OpenType que la spec asume. Ver el paso siguiente del plan para qué hacer con cada resultado.
/// &lt;/summary&gt;
public class SondaFontFeaturesTests
{
    private readonly ITestOutputHelper _salida;

    public SondaFontFeaturesTests(ITestOutputHelper salida) =&gt; _salida = salida;

    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="600" Height="400"&gt;
            &lt;StackPanel&gt;
                &lt;TextBlock x:Name="AnchoUno"     Text="1111111111" FontSize="14" /&gt;
                &lt;TextBlock x:Name="AnchoNueve"   Text="9999999999" FontSize="14" /&gt;
                &lt;TextBlock x:Name="TnumUno"      Text="1111111111" FontSize="14" FontFeatures="+tnum" /&gt;
                &lt;TextBlock x:Name="TnumNueve"    Text="9999999999" FontSize="14" FontFeatures="+tnum" /&gt;
                &lt;TextBlock x:Name="SmcpNormal"   Text="reportes" FontSize="11" /&gt;
                &lt;TextBlock x:Name="SmcpVersal"   Text="reportes" FontSize="11" FontFeatures="+smcp" /&gt;
            &lt;/StackPanel&gt;
        &lt;/Window&gt;
        """;

    [AvaloniaFact]
    public void Sonda_MideElAnchoConYSinFeaturesOpenType()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        double Ancho(string nombre) =&gt; window.GetVisualDescendants().OfType&lt;TextBlock&gt;()
            .First(t =&gt; t.Name == nombre).Bounds.Width;

        var sinTnumUno = Ancho("AnchoUno");
        var sinTnumNueve = Ancho("AnchoNueve");
        var conTnumUno = Ancho("TnumUno");
        var conTnumNueve = Ancho("TnumNueve");
        var smcpNormal = Ancho("SmcpNormal");
        var smcpVersal = Ancho("SmcpVersal");

        _salida.WriteLine($"SIN tnum: '1111111111'={sinTnumUno} '9999999999'={sinTnumNueve} diff={sinTnumUno - sinTnumNueve}");
        _salida.WriteLine($"CON tnum: '1111111111'={conTnumUno} '9999999999'={conTnumNueve} diff={conTnumUno - conTnumNueve}");
        _salida.WriteLine($"smcp: normal={smcpNormal} versalitas={smcpVersal} diff={smcpVersal - smcpNormal}");
        _salida.WriteLine($"VEREDICTO tnum: {(conTnumUno == conTnumNueve ? "FUNCIONA" : "NO FUNCIONA")}");
        _salida.WriteLine($"VEREDICTO smcp: {(smcpVersal != smcpNormal ? "FUNCIONA (el ancho cambió)" : "NO FUNCIONA (ancho idéntico)")}");

        Assert.True(true, "Sonda: mirá la salida, no el resultado.");
    }
}
```

- [ ] **Step 2: Correr la sonda y leer la salida**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~SondaFontFeatures" --logger "console;verbosity=detailed"`
Expected: PASS, y en la salida las 5 líneas de medición.

Nota sobre Inter: aun **sin** `tnum`, Inter tiene dígitos de ancho uniforme por diseño en muchas de sus versiones. Si `sinTnumUno == sinTnumNueve`, la sonda no puede distinguir "la feature funciona" de "no hacía falta". En ese caso el veredicto práctico es el mismo — las columnas quedan alineadas — y se declara `tnum` como NO NECESARIO.

- [ ] **Step 3: Anotar el veredicto y elegir la rama**

Escribí el resultado en el mensaje de commit de la tanda. Según lo que haya dado:

**Si `tnum` FUNCIONA o NO ES NECESARIO** → la tanda 2 usa `FontFeatures="+tnum"` en el estilo de celda numérica (o nada, si no hace falta) y alineación derecha.

**Si `tnum` NO FUNCIONA** → fallback de la spec: en la tanda 2, las columnas numéricas llevan **ancho fijo** (`Width` explícito en la `DataGridTextColumn`) más `HorizontalAlignment="Right"`. Se ve bien igual. NO se intenta embeber otra fuente: eso es un cambio de dependencia fuera del alcance de este refactor.

**Si `smcp` FUNCIONA** → la clase `.micro` de la tanda 1 lleva `FontFeatures="+smcp"` y el texto se escribe en minúsculas.

**Si `smcp` NO FUNCIONA** → la clase `.micro` NO usa versalitas. En su lugar: `FontSize="11"`, `FontWeight="SemiBold"`, `LetterSpacing="0.6"` y `Foreground` terciario, con el texto escrito **en mayúsculas directamente en cada XAML**. Ojo con el Global Constraint de no cambiar copy: los headers de columna de grilla y los eyebrows de sección **no** son copy de negocio, son etiquetas estructurales; escribirlos en mayúsculas está permitido. Los textos de botones, mensajes, títulos de vista y labels de formulario **no se tocan**.

- [ ] **Step 4: Borrar la sonda**

```bash
git rm tests/StockApp.Presentation.UiTests/SondaFontFeaturesTests.cs
```

La sonda cumplió su función. Dejarla sería dejar un test que no asserta nada.

### Task 0.4: Borrar los 2 tests que no custodian nada

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs` — borrar los tests de las líneas 149-162 y 181-198

**Interfaces:**
- Consumes: nada.
- Produces: nada. Es limpieza.

**Corrección respecto de la spec:** la spec dice "los 5 de categoría C pura se borran". Una re-clasificación con criterio explícito sobre los 122 tests encontró **2**, no 5. Los otros tres candidatos cosméticos que se revisaron — geometría con `TranslatePoint`, colores literales, contraste — resultan custodiar bugs reales ya documentados en el propio código. **Se borran 2. No fuerces el número a 5.**

**Decisión firme sobre `MantenimientoViewTests.cs:378`** (que la spec dejaba abierta para la tanda 11): **NO se borra.** Su autor lo validó por mutación y dejó escrito que la aserción obvia — comparar la Y de dos botones — NO detecta el bug, porque con la tarjeta de Diagnóstico dockeada a la izquierda la Y sigue dando el orden correcto por casualidad. Por eso el test compara el origen y el borde izquierdo de las dos tarjetas. Custodia una regresión real de `DockPanel` con `LastChildFill` que ya ocurrió en este repo. Cuando la tanda 11 rediseñe Mantenimiento, ese test se **adapta** conservando su criterio geométrico, nunca se borra.

- [ ] **Step 1: Borrar el primer test**

En `tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs`, borrar completo el método `Montar_FilaActivaQuePuedeAnular_MuestraBotonAnularConPuntosSuspensivos` con su `[AvaloniaFact]` y su comentario:

```csharp
    [AvaloniaFact]
    public void Montar_FilaActivaQuePuedeAnular_MuestraBotonAnularConPuntosSuspensivos()
    {
        // I3: el botón mentía sobre lo que hacía ("Anular" sin indicar que abre otra pantalla).
        var (window, _, _) = Montar(activos: new List&lt;DocumentoAdministrativo&gt;
        {
            DocumentoDe(1, "0087", EstadoDocumento.Pendiente),
        }, rol: RolUsuario.Admin);

        var botones = window.GetVisualDescendants().OfType&lt;Button&gt;()
            .Select(b =&gt; b.Content as string).ToList();
        Assert.Contains("Anular…", botones);
        Assert.DoesNotContain("Anular", botones);
    }
```

Razón: la única diferencia bajo prueba es el carácter Unicode `…`. Ningún comportamiento, gate ni binding está en juego.

- [ ] **Step 2: Borrar el segundo test**

En el mismo archivo, borrar completo `Montar_FilaCerradaQuePuedeReabrir_MuestraBotonReabrirConPuntosSuspensivos`, mismo patrón, mismos asserts sobre `"Reabrir…"` vs `"Reabrir"`.

**NO borres** `ClickReal_EnAnular_NavegaAlDetalleEnVezDeEjecutarLaAccionAca`, que está entre los dos: ese sí custodia comportamiento — que "Anular…" navegue al detalle en vez de ejecutar la acción, porque anular exige motivo obligatorio.

- [ ] **Step 3: Correr la suite de UI**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS, con 2 tests MENOS que antes. Verificá el conteo — si bajó más de 2, borraste de más.

### Task 0.5: Unificar el helper `EsVisibleEnArbol`

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/ArbolVisualHelpers.cs`
- Modify: `tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs:82-97` (borrar copia local)
- Modify: `tests/StockApp.Presentation.UiTests/IngresoPorFacturaViewTests.cs:93-103` (borrar copia local)
- Modify: `tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs:172-178` (borrar copia local)
- Modify: `tests/StockApp.Presentation.UiTests/TareaListViewTests.cs:104-110` (borrar copia local)
- Test: `tests/StockApp.Presentation.UiTests/ArbolVisualHelpersTests.cs` (crear)

**Interfaces:**
- Consumes: nada.
- Produces: `public static class ArbolVisual` con `public static bool EsVisibleEnArbol(Visual visual)`. Las tandas 4 y 5 lo usan intensivamente para verificar gates de permisos: un botón gateado dentro de un grupo colapsado debe reportar **no visible**.

**Por qué nadie lo unificó antes:** está duplicado 4 veces con **dos nombres distintos** — `EsVisibleEnArbol` en `TareaFormViewTests.cs` e `IngresoPorFacturaViewTests.cs`, e `IsVisibleEnArbol` en `InicioPanelTareasTests.cs` y `TareaListViewTests.cs`. Los cuerpos son idénticos byte a byte. Un `grep` de un solo nombre encuentra la mitad.

- [ ] **Step 1: Escribir el test del helper**

Crear `tests/StockApp.Presentation.UiTests/ArbolVisualHelpersTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// El helper existe porque IsVisible NO cae en cascada en Avalonia: un TextBox dentro de un
/// StackPanel con IsVisible=False sigue reportando su propio IsVisible=True. Todo test de gate
/// de permisos que use GetVisualDescendants necesita caminar la cadena de ancestros o va a dar
/// un falso verde. Estos tests fijan ese contrato.
/// &lt;/summary&gt;
public class ArbolVisualHelpersTests
{
    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300"&gt;
            &lt;StackPanel&gt;
                &lt;StackPanel x:Name="PanelVisible" IsVisible="True"&gt;
                    &lt;TextBlock x:Name="HijoDePanelVisible" Text="uno" /&gt;
                &lt;/StackPanel&gt;
                &lt;StackPanel x:Name="PanelOculto" IsVisible="False"&gt;
                    &lt;TextBlock x:Name="HijoDePanelOculto" Text="dos" /&gt;
                &lt;/StackPanel&gt;
                &lt;TextBlock x:Name="OcultoElMismo" Text="tres" IsVisible="False" /&gt;
            &lt;/StackPanel&gt;
        &lt;/Window&gt;
        """;

    private static TextBlock Buscar(Window w, string nombre)
        =&gt; w.GetVisualDescendants().OfType&lt;TextBlock&gt;().First(t =&gt; t.Name == nombre);

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void EsVisibleEnArbol_HijoDeUnPanelVisible_EsVisible()
    {
        var window = Montar();
        Assert.True(ArbolVisual.EsVisibleEnArbol(Buscar(window, "HijoDePanelVisible")));
    }

    [AvaloniaFact]
    public void EsVisibleEnArbol_HijoDeUnPanelOculto_NoEsVisibleAunqueElHijoDigaQueSi()
    {
        var window = Montar();
        var hijo = Buscar(window, "HijoDePanelOculto");

        // El corazón del asunto: el hijo reporta IsVisible=True aunque su padre esté oculto.
        Assert.True(hijo.IsVisible);
        Assert.False(ArbolVisual.EsVisibleEnArbol(hijo));
    }

    [AvaloniaFact]
    public void EsVisibleEnArbol_ControlOcultoElMismo_NoEsVisible()
    {
        var window = Montar();
        Assert.False(ArbolVisual.EsVisibleEnArbol(Buscar(window, "OcultoElMismo")));
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ArbolVisualHelpersTests"`
Expected: FAIL de compilación — `ArbolVisual` no existe.

- [ ] **Step 3: Crear el helper compartido**

Crear `tests/StockApp.Presentation.UiTests/ArbolVisualHelpers.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// Helpers de árbol visual compartidos por todo el banco de pruebas de UI.
/// &lt;/summary&gt;
public static class ArbolVisual
{
    /// &lt;summary&gt;
    /// IsVisible propio de un control NO cae en cascada al valor del ancestro en Avalonia: un
    /// TextBox dentro de un StackPanel con IsVisible=False sigue reportando su propio
    /// IsVisible=True, y GetVisualDescendants lo encuentra igual. Camina la cadena de ancestros
    /// para saber si de verdad está en pantalla. Imprescindible para tests de gates de permisos:
    /// sin esto, un botón gateado dentro de un contenedor oculto da falso verde.
    /// &lt;/summary&gt;
    public static bool EsVisibleEnArbol(Visual visual)
    {
        for (Visual? actual = visual; actual is not null; actual = actual.GetVisualParent())
        {
            if (actual is Control c &amp;&amp; !c.IsVisible) return false;
        }
        return true;
    }
}
```

- [ ] **Step 4: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ArbolVisualHelpersTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Borrar las 4 copias locales y redirigir a las llamadas**

En cada uno de los 4 archivos, borrar el método privado local y reemplazar cada invocación:

- `TareaFormViewTests.cs`: borrar `EsVisibleEnArbol` (líneas 82-97, con su doc comment). Reemplazar cada `EsVisibleEnArbol(x)` por `ArbolVisual.EsVisibleEnArbol(x)`.
- `IngresoPorFacturaViewTests.cs`: borrar `EsVisibleEnArbol` (líneas 93-103). Mismo reemplazo.
- `InicioPanelTareasTests.cs`: borrar `IsVisibleEnArbol` (líneas 172-178). Reemplazar cada `IsVisibleEnArbol(x)` por `ArbolVisual.EsVisibleEnArbol(x)`.
- `TareaListViewTests.cs`: borrar `IsVisibleEnArbol` (líneas 104-110). Mismo reemplazo.

Ojo con el nombre: dos archivos usan `Es...` y dos usan `Is...`. Buscá los dos.

- [ ] **Step 6: Correr la suite de UI**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS. El conteo sube 3 (los del helper) respecto del final de la Task 0.4.

- [ ] **Step 7: Commit de la tanda 0**

```bash
git add -A
git commit -m "refactor(ui): blinda el andamiaje de tests antes del refactor visual

- App.axaml pasa a RequestedThemeVariant=Light (tema fijo claro)
- TestAppBuilder carga Themes/DataGrid.axaml: los tests de grilla medían Fluent crudo
- Borra 2 tests que solo verificaban un caracter de puntuacion en un boton
- Unifica EsVisibleEnArbol/IsVisibleEnArbol, duplicado 4 veces con 2 nombres

Veredicto de la sonda de FontFeatures sobre Inter: tnum=<COMPLETAR>, smcp=<COMPLETAR>"
```

**Antes de commitear, reemplazá los dos `<COMPLETAR>` por el veredicto real de la Task 0.3.** Ese dato lo consumen las tandas 1 y 2.

---

## Tanda 1: Fundación de tokens

**Objetivo:** que `Themes/Tokens.axaml` deje de definir solo colores. Hoy tiene 94 líneas, todas de paleta; de ahí salen los 9 valores distintos de `Spacing` y los 32 de `Margin` regados por las 58 vistas. Sin tokens de espaciado no hay coherencia posible, y ninguna tanda posterior puede consumir lo que no existe.

### Task 1.1: Tokens de espaciado, radios y sombras

**Files:**
- Modify: `src/StockApp.Presentation/Themes/Tokens.axaml`
- Test: `tests/StockApp.Presentation.UiTests/TokensDisenioTests.cs` (crear)

**Interfaces:**
- Consumes: nada.
- Produces: recursos resolubles por clave desde cualquier XAML de la app. Nombres exactos que las tandas 2 a 13 van a consumir:
  - `x:Double`: `Espacio1`=4, `Espacio2`=8, `Espacio3`=12, `Espacio4`=16, `Espacio5`=24, `Espacio6`=32, `Espacio7`=48
  - `Thickness`: `MargenVista`=24, `PaddingCard`=16, `PaddingCelda`="12,8"
  - `CornerRadius`: `RadioChico`=4, `RadioBase`=6, `RadioCard`=10
  - `BoxShadows`: `SombraCard`, `SombraElevada`, `SombraModal`
  - `SolidColorBrush`: `TextoTerciarioBrush`

**Por qué hacen falta DOS familias de espaciado:** `StackPanel.Spacing` es un `double`, pero `Margin` y `Padding` son `Thickness`. Un mismo recurso no sirve para los dos. Se definen los 7 `x:Double` como la escala canónica, y encima un puñado corto de `Thickness` derivados para los tres usos que se repiten en toda la app. Los `Thickness` sueltos que una vista necesite (por ejemplo `"0,8,0,0"`) se siguen escribiendo inline: tokenizar cada combinación sería peor que el problema.

**Este test SÍ es un guardián, no una tautología:** si alguien renombra o borra un token, los `{DynamicResource Espacio5}` de las 58 vistas **no explotan** — quedan sin resolver y el control se cae a su valor default, en silencio. Es el mismo modo de falla que el `{Binding PuedeXxx}` con typo. El test fija los nombres como contrato público.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/TokensDisenioTests.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// Guardián del contrato de nombres de Themes/Tokens.axaml. Las 58 vistas consumen estos
/// recursos por clave con DynamicResource: si una clave se renombra o se borra, el binding NO
/// explota — queda sin resolver y el control se cae a su valor default, en silencio, igual que
/// un {Binding PuedeXxx} con typo. Este test convierte ese fallo silencioso en un rojo.
/// &lt;/summary&gt;
public class TokensDisenioTests
{
    private static object Recurso(string clave)
    {
        Assert.True(
            Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token '{clave}' no existe en Themes/Tokens.axaml. Las vistas que lo consumen con "
            + "DynamicResource se van a caer a su valor default sin avisar.");
        return valor!;
    }

    [AvaloniaTheory]
    [InlineData("Espacio1", 4.0)]
    [InlineData("Espacio2", 8.0)]
    [InlineData("Espacio3", 12.0)]
    [InlineData("Espacio4", 16.0)]
    [InlineData("Espacio5", 24.0)]
    [InlineData("Espacio6", 32.0)]
    [InlineData("Espacio7", 48.0)]
    public void EscalaDeEspaciado_ExisteConElValorDeLaSpec(string clave, double esperado)
    {
        Assert.Equal(esperado, Assert.IsType&lt;double&gt;(Recurso(clave)));
    }

    [AvaloniaFact]
    public void MargenVista_Es24EnLosCuatroLados()
    {
        // Espacio5. Es el margen exterior estandar de TODA vista: hoy van de 16 a 40 segun el
        // archivo, y NuevaImportacionView.axaml (509 lineas) directamente no tiene ninguno.
        Assert.Equal(new Thickness(24), Assert.IsType&lt;Thickness&gt;(Recurso("MargenVista")));
    }

    [AvaloniaFact]
    public void PaddingCard_Es16EnLosCuatroLados()
    {
        Assert.Equal(new Thickness(16), Assert.IsType&lt;Thickness&gt;(Recurso("PaddingCard")));
    }

    [AvaloniaFact]
    public void PaddingCelda_Es12Horizontal8Vertical()
    {
        Assert.Equal(new Thickness(12, 8, 12, 8), Assert.IsType&lt;Thickness&gt;(Recurso("PaddingCelda")));
    }

    [AvaloniaTheory]
    [InlineData("RadioChico", 4.0)]
    [InlineData("RadioBase", 6.0)]
    [InlineData("RadioCard", 10.0)]
    public void EscalaDeRadios_ExisteConElValorDeLaSpec(string clave, double esperado)
    {
        var radio = Assert.IsType&lt;CornerRadius&gt;(Recurso(clave));
        Assert.Equal(esperado, radio.TopLeft);
        Assert.Equal(esperado, radio.BottomRight);
    }

    [AvaloniaTheory]
    [InlineData("SombraCard")]
    [InlineData("SombraElevada")]
    [InlineData("SombraModal")]
    public void EscalaDeSombras_ExisteYNoEstaVacia(string clave)
    {
        var sombras = Assert.IsType&lt;BoxShadows&gt;(Recurso(clave));
        Assert.True(sombras.Count &gt; 0, $"'{clave}' existe pero no define ninguna sombra.");
    }

    [AvaloniaFact]
    public void TextoTerciarioBrush_EsElGrisDeLaSpec()
    {
        // Reemplaza los 60 usos de Opacity="0.5|0.6|0.7". El color se declara, no se atenua:
        // asi el contraste es medible y testeable en vez de depender de sobre que fondo cayo.
        var brush = Assert.IsType&lt;SolidColorBrush&gt;(Recurso("TextoTerciarioBrush"));
        Assert.Equal(Color.Parse("#94A3B8"), brush.Color);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TokensDisenioTests"`
Expected: FAIL — todos los casos fallan con el mensaje "El token 'X' no existe en Themes/Tokens.axaml".

- [ ] **Step 3: Agregar los tokens**

En `src/StockApp.Presentation/Themes/Tokens.axaml`, ANTES del bloque `<!-- ── Brushes ... -->`, insertar:

```xml
    &lt;!-- ── Escala de espaciado, base 4 ────────────────────────────────────────
         Los 9 valores sueltos de Spacing y los 32 de Margin del codigo colapsan
         a estos 7. Los valores 2, 6 y 20 quedan eliminados: no habia criterio
         que los distinguiera de sus vecinos.
         Spacing de StackPanel es double; Margin y Padding son Thickness. Por eso
         hay dos familias: los 7 doubles son la escala canonica, y abajo van los
         tres Thickness derivados que se repiten en toda la app. Un Thickness
         asimetrico puntual (p. ej. "0,8,0,0") se sigue escribiendo inline. --&gt;
    &lt;x:Double x:Key="Espacio1"&gt;4&lt;/x:Double&gt;
    &lt;x:Double x:Key="Espacio2"&gt;8&lt;/x:Double&gt;
    &lt;x:Double x:Key="Espacio3"&gt;12&lt;/x:Double&gt;
    &lt;x:Double x:Key="Espacio4"&gt;16&lt;/x:Double&gt;
    &lt;x:Double x:Key="Espacio5"&gt;24&lt;/x:Double&gt;
    &lt;x:Double x:Key="Espacio6"&gt;32&lt;/x:Double&gt;
    &lt;x:Double x:Key="Espacio7"&gt;48&lt;/x:Double&gt;

    &lt;!-- Margen exterior estandar de TODA vista (Espacio5). Hoy van de 16 a 40
         segun el archivo y NuevaImportacionView.axaml no tiene ninguno. --&gt;
    &lt;Thickness x:Key="MargenVista"&gt;24&lt;/Thickness&gt;
    &lt;Thickness x:Key="PaddingCard"&gt;16&lt;/Thickness&gt;
    &lt;Thickness x:Key="PaddingCelda"&gt;12,8&lt;/Thickness&gt;

    &lt;!-- ── Radios ──────────────────────────────────────────────────────────
         De 4/6/8/10 sin criterio a tres con significado: chico para badges,
         base para botones e inputs, card para contenedores. --&gt;
    &lt;CornerRadius x:Key="RadioChico"&gt;4&lt;/CornerRadius&gt;
    &lt;CornerRadius x:Key="RadioBase"&gt;6&lt;/CornerRadius&gt;
    &lt;CornerRadius x:Key="RadioCard"&gt;10&lt;/CornerRadius&gt;

    &lt;!-- ── Sombras ─────────────────────────────────────────────────────────
         De una sola hardcodeada en Controls.axaml a tres niveles de elevacion. --&gt;
    &lt;BoxShadows x:Key="SombraCard"&gt;0 1 3 0 #1A0F172A&lt;/BoxShadows&gt;
    &lt;BoxShadows x:Key="SombraElevada"&gt;0 4 12 0 #260F172A&lt;/BoxShadows&gt;
    &lt;BoxShadows x:Key="SombraModal"&gt;0 12 32 0 #330F172A&lt;/BoxShadows&gt;

    &lt;!-- Texto terciario: reemplaza los 60 usos de Opacity="0.5|0.6|0.7".
         El color se DECLARA, no se atenua — asi el contraste es medible y
         testeable, en vez de depender de sobre que fondo cayo la opacidad. --&gt;
    &lt;Color x:Key="ColorTextoTerciario"&gt;#94A3B8&lt;/Color&gt;

```

Y en el bloque de brushes, junto a `TextoSecundarioBrush`, agregar:

```xml
    &lt;SolidColorBrush x:Key="TextoTerciarioBrush" Color="{StaticResource ColorTextoTerciario}" /&gt;
```

- [ ] **Step 4: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TokensDisenioTests"`
Expected: PASS, 16 casos.

Si falla con `XamlParseException` sobre `x:Double`, verificá que el `ResourceDictionary` raíz declare `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"` — ya lo hace en la línea 2 del archivo.

### Task 1.2: Paleta de sidebar "pizarra media"

**Files:**
- Modify: `src/StockApp.Presentation/Themes/Tokens.axaml` — los 4 colores del bloque sidebar
- Test: `tests/StockApp.Presentation.UiTests/SidebarContrasteTests.cs` (crear)

**Interfaces:**
- Consumes: nada.
- Produces: `ColorSidebar`=`#1E293B`, `ColorSidebarActivo`=`#334155`, `ColorSidebarAccent`=`#16A34A`, `ColorSidebarTexto`=`#CBD5E1`. La tanda 5 los consume.

**El cambio:** el sidebar deja de estar teñido de verde bosque y pasa a pizarra media. El verde `#16A34A` no cambia de valor: cambia de rol. Deja de ser fondo y pasa a ser acento de acción. Razón: con el sidebar teñido de verde, el verde no señala nada; con el sidebar neutro, el verde guía.

**La restricción que este test custodia:** el verde `#16A34A` sobre `#1E293B` da **4.44:1**, por debajo del umbral de texto (4.5:1) y por encima del umbral gráfico (3:1). Sirve para barra de acento e íconos, **no** para texto. Es exactamente el tipo de regla que se olvida seis tandas más tarde.

- [ ] **Step 1: Preparar el helper de contraste**

Abrí `tests/StockApp.Presentation.UiTests/ButtonGhostContrasteTests.cs` (118 líneas). Ya calcula contraste WCAG para los tests de contraste existentes.

**Si ya tiene un método de cálculo de ratio** (busca luminancia relativa / `0.2126` / `0.7152` / `0.0722`): extraelo a un archivo nuevo `tests/StockApp.Presentation.UiTests/ContrasteHelpers.cs` como `public static class Contraste` con `public static double Ratio(Color a, Color b)`, y dejá `ButtonGhostContrasteTests.cs` llamándolo. Ese archivo tiene 3 tests de contraste que deben seguir en verde después de la extracción — si se ponen rojos, la extracción cambió el cálculo.

**Si no lo tiene**, creá `tests/StockApp.Presentation.UiTests/ContrasteHelpers.cs`:

```csharp
using System;
using Avalonia.Media;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// Calculo de contraste WCAG 2.1 (relative luminance + contrast ratio). Umbrales:
/// texto normal AA 4.5:1, AAA 7:1; elementos graficos (iconos, barras, bordes) 3:1.
/// &lt;/summary&gt;
public static class Contraste
{
    private static double Canal(byte valor)
    {
        var c = valor / 255.0;
        return c &lt;= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminancia(Color color)
        =&gt; 0.2126 * Canal(color.R) + 0.7152 * Canal(color.G) + 0.0722 * Canal(color.B);

    public static double Ratio(Color a, Color b)
    {
        var la = Luminancia(a);
        var lb = Luminancia(b);
        var claro = Math.Max(la, lb);
        var oscuro = Math.Min(la, lb);
        return (claro + 0.05) / (oscuro + 0.05);
    }
}
```

- [ ] **Step 2: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/SidebarContrasteTests.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// Guardián de accesibilidad de la paleta del sidebar "pizarra media". Fija los tres hechos que
/// justifican la eleccion de la paleta, incluido el limite duro: el verde de marca NO sirve como
/// texto sobre el sidebar. Esa restriccion es la que se olvida seis tandas mas tarde.
/// &lt;/summary&gt;
public class SidebarContrasteTests
{
    private static Color ColorDe(string clave)
    {
        Assert.True(
            Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token de color '{clave}' no existe en Themes/Tokens.axaml.");
        return Assert.IsType&lt;Color&gt;(valor!);
    }

    [AvaloniaFact]
    public void TextoDelSidebar_SobreElFondo_SuperaAAA()
    {
        var ratio = Contraste.Ratio(ColorDe("ColorSidebarTexto"), ColorDe("ColorSidebar"));
        Assert.True(ratio &gt;= 7.0, $"Texto de sidebar sobre fondo: {ratio:F2}:1, se esperaba AAA (&gt;=7:1).");
    }

    [AvaloniaFact]
    public void TextoDelSidebar_SobreElItemActivo_SuperaAAA()
    {
        var ratio = Contraste.Ratio(Colors.White, ColorDe("ColorSidebarActivo"));
        Assert.True(ratio &gt;= 7.0, $"Blanco sobre item activo: {ratio:F2}:1, se esperaba AAA (&gt;=7:1).");
    }

    [AvaloniaFact]
    public void AcentoVerde_SirveComoGraficoPeroNOComoTexto()
    {
        // ESTA es la restriccion que hay que recordar: 4.44:1 pasa el umbral grafico (3:1) y NO
        // pasa el de texto (4.5:1). El verde va en la barra de acento y en los iconos del item
        // activo. Si algun dia se usa como color de TEXTO sobre el sidebar, este test se pone
        // rojo — y ese rojo es correcto, no hay que ajustarlo.
        var ratio = Contraste.Ratio(ColorDe("ColorSidebarAccent"), ColorDe("ColorSidebar"));

        Assert.True(ratio &gt;= 3.0, $"El acento debe pasar el umbral grafico: {ratio:F2}:1 &lt; 3:1.");
        Assert.True(ratio &lt; 4.5,
            $"El acento da {ratio:F2}:1. Si ahora supera 4.5:1 alguien cambio la paleta: reevalua "
            + "si el verde puede usarse como texto y actualiza la restriccion de la spec.");
    }
}
```

- [ ] **Step 3: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~SidebarContrasteTests"`
Expected: FAIL. Con la paleta vieja (`#14532D` de fondo, `#FFFFFF` de texto, `#4ADE80` de acento), el tercer test falla: blanco sobre verde bosque y `#4ADE80` sobre `#14532D` no cumplen la banda esperada.

- [ ] **Step 4: Cambiar los 4 colores**

En `src/StockApp.Presentation/Themes/Tokens.axaml`, reemplazar el bloque del sidebar:

```xml
    &lt;!-- Sidebar (dashboard shell): pizarra media. El verde de marca #16A34A NO
         cambia de valor, cambia de ROL: deja de ser fondo y pasa a ser acento de
         accion (item activo, barra de acento, iconos). Con el sidebar tenido de
         verde el verde no senalaba nada; con el sidebar neutro, guia.
         Contrastes: texto #CBD5E1 sobre #1E293B = 9.86:1 (AAA); blanco sobre
         activo #334155 = 10.36:1 (AAA).
         RESTRICCION: el acento #16A34A sobre #1E293B da 4.44:1 — pasa el umbral
         grafico (3:1) y NO el de texto (4.5:1). Prohibido usarlo como color de
         texto sobre el sidebar. Custodiado por SidebarContrasteTests.cs. --&gt;
    &lt;Color x:Key="ColorSidebar"&gt;#1E293B&lt;/Color&gt;
    &lt;Color x:Key="ColorSidebarActivo"&gt;#334155&lt;/Color&gt;
    &lt;Color x:Key="ColorSidebarAccent"&gt;#16A34A&lt;/Color&gt;
    &lt;Color x:Key="ColorSidebarTexto"&gt;#CBD5E1&lt;/Color&gt;
```

Los overlays `ColorSidebarHoverOverlay` (`#22FFFFFF`) y `ColorSidebarPressedOverlay` (`#33FFFFFF`) **no se tocan**: siguen siendo blancos translúcidos y funcionan igual sobre pizarra que sobre verde bosque.

- [ ] **Step 5: Correr los tests de contraste, los nuevos y los viejos**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Contraste"`
Expected: PASS — los 3 de `SidebarContrasteTests`, los 3 de `ButtonGhostContrasteTests` y `PermisoAdvertenciaContrasteTests`.

`ButtonGhostContrasteTests.cs` mide texto blanco sobre el sidebar y debe seguir verde: blanco sobre `#1E293B` da 15.3:1, mejor que sobre el verde bosque anterior. **Si se pone rojo, no lo ajustes** — leé qué mide exactamente antes de tocar nada.

### Task 1.3: Clase tipográfica `.micro`

**Files:**
- Modify: `src/StockApp.Presentation/Themes/Typography.axaml` (49 líneas)
- Test: `tests/StockApp.Presentation.UiTests/TipografiaMicroTests.cs` (crear)

**Interfaces:**
- Consumes: `TextoTerciarioBrush` (Task 1.1), y el veredicto de `smcp` de la Task 0.3.
- Produces: clase `TextBlock.micro`. La consumen los headers de columna de las 21 grillas (tanda 2) y los eyebrows del header de vista (tanda 3).

**El nivel que falta:** la escala hoy es título-vista 20 / sección 16 / body 14 / caption 12. Falta el nivel de etiqueta estructural: headers de tabla, eyebrows de sección. Sin él, los 29 `FontSize` literales del código no tienen a dónde mapear.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/TipografiaMicroTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// La clase .micro es el nivel de etiqueta estructural que faltaba en la escala (titulo-vista 20 /
/// seccion 16 / body 14 / caption 12). La consumen los headers de las 21 grillas y los eyebrows
/// del header de vista: si se rompe, los 29 FontSize literales del codigo no tienen a donde mapear.
/// &lt;/summary&gt;
public class TipografiaMicroTests
{
    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="200"&gt;
            &lt;TextBlock x:Name="Etiqueta" Classes="micro" Text="reportes" /&gt;
        &lt;/Window&gt;
        """;

    private static TextBlock Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType&lt;TextBlock&gt;().First(t =&gt; t.Name == "Etiqueta");
    }

    [AvaloniaFact]
    public void ClaseMicro_AplicaTamanio11()
    {
        Assert.Equal(11.0, Montar().FontSize);
    }

    [AvaloniaFact]
    public void ClaseMicro_UsaTextoTerciarioNoOpacidad()
    {
        // El punto de todo el ejercicio: el gris se DECLARA. Si aparece un Opacity aca, se
        // vuelve al problema original (contraste no medible, dependiente del fondo).
        var etiqueta = Montar();
        Assert.Equal(Color.Parse("#94A3B8"), Assert.IsType&lt;SolidColorBrush&gt;(etiqueta.Foreground).Color);
        Assert.Equal(1.0, etiqueta.Opacity);
    }

    [AvaloniaFact]
    public void ClaseMicro_TieneLetterSpacingParaQueRespireEnMayusculas()
    {
        Assert.True(Montar().LetterSpacing &gt; 0, "Sin letter-spacing, una etiqueta de 11px en mayusculas se lee apretada.");
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TipografiaMicroTests"`
Expected: FAIL — la clase `micro` no existe, así que el `TextBlock` conserva el `FontSize` heredado (no 11).

- [ ] **Step 3: Agregar la clase**

En `src/StockApp.Presentation/Themes/Typography.axaml`, después del bloque `TextBlock.caption`, agregar:

```xml
    &lt;!-- Etiqueta estructural: headers de columna de grilla y eyebrows de seccion.
         Es el nivel que faltaba en la escala. Color declarado (terciario), NUNCA
         Opacity: asi el contraste es medible. El texto va escrito en MAYUSCULAS
         en cada XAML — Avalonia no tiene text-transform. Eso NO viola la regla
         de "no se cambia el copy": headers de columna y eyebrows son etiquetas
         estructurales, no copy de negocio. Botones, mensajes, titulos de vista y
         labels de formulario no se tocan. --&gt;
    &lt;Style Selector="TextBlock.micro"&gt;
        &lt;Setter Property="FontSize" Value="11" /&gt;
        &lt;Setter Property="FontWeight" Value="SemiBold" /&gt;
        &lt;Setter Property="LetterSpacing" Value="0.6" /&gt;
        &lt;Setter Property="Foreground" Value="{DynamicResource TextoTerciarioBrush}" /&gt;
    &lt;/Style&gt;
```

**Si la sonda de la Task 0.3 dio `smcp` = FUNCIONA**, agregá además `<Setter Property="FontFeatures" Value="+smcp" />` al bloque, y entonces el texto se escribe en minúsculas en los XAML (la fuente hace las versalitas). Si dio NO FUNCIONA, dejá el bloque tal cual está arriba y el texto va en mayúsculas.

- [ ] **Step 4: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TipografiaMicroTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Correr la suite completa y commitear**

Run: `dotnet test StockApp.sln`
Expected: PASS.

```bash
git add -A
git commit -m "feat(ui): completa la fundacion de tokens de diseno

Tokens.axaml definia solo colores: de ahi salian los 9 valores distintos de
Spacing y los 32 de Margin regados por las 58 vistas.

- Escala de espaciado base 4 (7 doubles + 3 Thickness derivados)
- Radios chico/base/card, sombras card/elevada/modal
- TextoTerciarioBrush: reemplaza los 60 usos de Opacity
- Paleta de sidebar a pizarra media; el verde pasa de fondo a acento
- Clase tipografica .micro para headers de grilla y eyebrows

Guardianes nuevos: TokensDisenioTests (los DynamicResource sin resolver son
silenciosos), SidebarContrasteTests (el verde no sirve como texto: 4.44:1)"
```

---

## Tanda 2: Controles base y grillas

**Objetivo:** que los controles base consuman los tokens en vez de literales, y que `Themes/DataGrid.axaml` deje de ser 39 líneas que solo apagan dos indicadores de foco. **La app son sus 21 grillas**: esta tanda es la que más cambia lo que el usuario ve.

### Task 2.1: Controls.axaml consume tokens

**Files:**
- Modify: `src/StockApp.Presentation/Themes/Controls.axaml` (248 líneas)
- Test: `tests/StockApp.Presentation.UiTests/ControlesConsumenTokensTests.cs` (crear)

**Interfaces:**
- Consumes: `RadioBase`, `RadioCard`, `RadioChico`, `PaddingCard`, `SombraCard` (Task 1.1).
- Produces: nada nuevo. Es sustitución de literales por tokens.

**Qué se sustituye, con línea exacta:**

| Línea | Hoy | Pasa a |
|---|---|---|
| `Controls.axaml:177` | `CornerRadius="6"` en `TextBox` | `{DynamicResource RadioBase}` |
| `Controls.axaml:200` | `CornerRadius="6"` en `ComboBox` | `{DynamicResource RadioBase}` |
| `Controls.axaml:~222` | `CornerRadius="8"` en `Border.card` | `{DynamicResource RadioCard}` |
| `Controls.axaml:~223` | `Padding="16"` en `Border.card` | `{DynamicResource PaddingCard}` |
| `Controls.axaml:224` | `BoxShadow="0 1 3 0 #1A0F172A"` | `{DynamicResource SombraCard}` |
| `Controls.axaml:~236` | radio de `Border.badge-inactiva` | `{DynamicResource RadioChico}` |

Los `CornerRadius` de los bloques de `Button` (líneas 19-170) van a `RadioBase` también. Recorré el archivo entero: **cualquier `CornerRadius`, `Padding` o `BoxShadow` literal que quede es una fuga.**

**NO TOQUES** los dos `Setter` de `(DataValidationErrors.ErrorConverter)` — `TextBox` en la línea 185 y `ComboBox` en la línea 203. Son el seam del que dependen los 2 tests de `MovimientoFormControlValidacionTests.cs`, y la spec marcaba solo el de `TextBox`: **son dos**.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/ControlesConsumenTokensTests.cs`:

```csharp
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// Verifica que los controles base resuelvan su geometria DESDE los tokens, no desde literales.
/// El valor del test no es el numero en si: es que si manana la escala de radios cambia, estos
/// controles la siguen; si alguien vuelve a hardcodear un 8, el test se pone rojo.
/// &lt;/summary&gt;
public class ControlesConsumenTokensTests
{
    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="500" Height="400"&gt;
            &lt;StackPanel&gt;
                &lt;TextBox x:Name="Caja" /&gt;
                &lt;ComboBox x:Name="Combo" /&gt;
                &lt;Button x:Name="BotonPrimario" Classes="primary" Content="Guardar" /&gt;
                &lt;Border x:Name="Tarjeta" Classes="card" /&gt;
            &lt;/StackPanel&gt;
        &lt;/Window&gt;
        """;

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Buscar&lt;T&gt;(Window w, string nombre) where T : Control
        =&gt; w.GetVisualDescendants().OfType&lt;T&gt;().First(c =&gt; c.Name == nombre);

    private static CornerRadius Token(string clave)
    {
        Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor);
        return (CornerRadius)valor!;
    }

    [AvaloniaFact]
    public void TextBox_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar&lt;TextBox&gt;(Montar(), "Caja").CornerRadius);
    }

    [AvaloniaFact]
    public void ComboBox_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar&lt;ComboBox&gt;(Montar(), "Combo").CornerRadius);
    }

    [AvaloniaFact]
    public void BotonPrimario_UsaElRadioBaseDelSistema()
    {
        Assert.Equal(Token("RadioBase"), Buscar&lt;Button&gt;(Montar(), "BotonPrimario").CornerRadius);
    }

    [AvaloniaFact]
    public void Card_UsaElRadioDeCardYSuPaddingYSombra()
    {
        var tarjeta = Buscar&lt;Border&gt;(Montar(), "Tarjeta");

        Assert.Equal(Token("RadioCard"), tarjeta.CornerRadius);
        Assert.Equal(new Thickness(16), tarjeta.Padding);
        Assert.True(tarjeta.BoxShadow.Count &gt; 0, "La card perdio su sombra: sin ella no se despega del fondo.");
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ControlesConsumenTokensTests"`
Expected: FAIL en `Card_UsaElRadioDeCardYSuPaddingYSombra` — la card tiene radio 8 literal y el token dice 10.

Los tests de `TextBox`/`ComboBox`/`Button` pueden pasar por casualidad: hoy ya tienen `6` literal, que coincide con `RadioBase`. **Eso no es un falso positivo del test, es un valor que ya estaba bien.** El test sigue siendo válido: si mañana la escala cambia, estos controles la tienen que seguir. Verificalo por mutación en el Step 4.

- [ ] **Step 3: Sustituir los literales por tokens**

Recorré `Controls.axaml` entero y reemplazá según la tabla de arriba. Ejemplo del bloque de card:

```xml
    &lt;Style Selector="Border.card"&gt;
        &lt;Setter Property="Background" Value="{DynamicResource SuperficieBrush}" /&gt;
        &lt;Setter Property="BorderBrush" Value="{DynamicResource BordeBrush}" /&gt;
        &lt;Setter Property="BorderThickness" Value="1" /&gt;
        &lt;Setter Property="CornerRadius" Value="{DynamicResource RadioCard}" /&gt;
        &lt;Setter Property="Padding" Value="{DynamicResource PaddingCard}" /&gt;
        &lt;Setter Property="BoxShadow" Value="{DynamicResource SombraCard}" /&gt;
    &lt;/Style&gt;
```

- [ ] **Step 4: Validar por mutación**

Cambiá temporalmente `RadioCard` de `10` a `20` en `Tokens.axaml` y corré `Card_UsaElRadioDeCardYSuPaddingYSombra`.

Expected: **PASS** — porque el test compara contra el token, no contra un literal. Eso confirma que la card sigue al sistema.

Ahora, con `RadioCard` todavía en 20, volvé a poner `CornerRadius="10"` literal en `Border.card` y corré de nuevo.

Expected: **FAIL** — el test detecta que la card se desenganchó del sistema.

Revertí las dos mutaciones. Sin este paso, el test no está validado.

- [ ] **Step 5: Correr la suite de UI**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS. Prestá atención a `MovimientoFormControlValidacionTests` (2 tests): si se ponen rojos, tocaste uno de los dos `Setter` de `ErrorConverter`.

### Task 2.2: DataGrid.axaml de verdad

**Files:**
- Modify: `src/StockApp.Presentation/Themes/DataGrid.axaml` (39 líneas hoy)
- Test: `tests/StockApp.Presentation.UiTests/DataGridEstiloRealTests.cs` (ampliar el de la Task 0.2)

**Interfaces:**
- Consumes: `Espacio*`, `PaddingCelda`, `TextoTerciarioBrush`, `BordeBrush`, `FondoBrush`, `SuperficieBrush` (tanda 1). Depende de que la Task 0.2 haya cargado este archivo en el banco de pruebas — **sin eso, todo test de esta tarea da falso verde contra Fluent crudo**.
- Produces: estilo de grilla para las 21 grillas de la app. La tanda 10 elimina los 7 estilos `DataGridCell.num` duplicados apoyándose en esto.

**Qué se agrega** (lo existente, los dos `Style` que apagan el foco, **se conserva tal cual**):

- `DataGridColumnHeader`: `.micro` (11px, SemiBold, letter-spacing), fondo `FondoBrush`, borde inferior de 1px, padding de celda.
- `DataGrid`: `RowHeight` 36, `AlternatingRowBackground` con `FondoBrush`, `GridLinesVisibility` horizontal, `BorderBrush`/`CornerRadius` del sistema.
- `DataGridCell`: padding `PaddingCelda`, alineación vertical centrada.
- `DataGridCell.num`: alineación derecha + cifras tabulares. **Este es el que la tanda 10 va a usar para borrar 7 copias locales.**

- [ ] **Step 1: Ampliar el test**

Agregar a `tests/StockApp.Presentation.UiTests/DataGridEstiloRealTests.cs`, dentro de la misma clase:

```csharp
    [AvaloniaFact]
    public void Montar_UnaGrilla_LosHeadersUsanLaEscalaMicro()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        var grilla = window.GetVisualDescendants().OfType&lt;DataGrid&gt;().First();
        grilla.ItemsSource = new[] { new ItemPrueba { Nombre = "uno", Numero = 1 } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = window.GetVisualDescendants().OfType&lt;DataGridColumnHeader&gt;().First();

        Assert.Equal(11.0, header.FontSize);
        Assert.Equal(FontWeight.SemiBold, header.FontWeight);
    }

    [AvaloniaFact]
    public void Montar_UnaGrilla_LasFilasTienenAltoYAlternanFondo()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        var grilla = window.GetVisualDescendants().OfType&lt;DataGrid&gt;().First();
        grilla.ItemsSource = new[]
        {
            new ItemPrueba { Nombre = "uno", Numero = 1 },
            new ItemPrueba { Nombre = "dos", Numero = 2 },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(36.0, grilla.RowHeight);
        Assert.NotNull(grilla.AlternatingRowBackground);
    }
```

Agregá los `using` que falten: `Avalonia.Media` para `FontWeight`.

- [ ] **Step 2: Correr para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~DataGridEstiloRealTests"`
Expected: FAIL en los dos nuevos — headers con el tamaño de Fluent, `RowHeight` en `NaN` (auto) y `AlternatingRowBackground` en `null`.

- [ ] **Step 3: Escribir el estilo**

En `src/StockApp.Presentation/Themes/DataGrid.axaml`, DESPUÉS de los dos `Style` existentes que apagan el foco (que se conservan intactos), agregar:

```xml
    &lt;!-- ════════════════════════════════════════════════════════════════════
         Estilo real de grilla. Hasta la tanda 2 este archivo solo apagaba dos
         indicadores de foco y las 21 grillas de la app corrian con Fluent crudo.
         La app SON sus grillas: esto es lo que mas cambia lo que el usuario ve.
         ════════════════════════════════════════════════════════════════════ --&gt;

    &lt;Style Selector="DataGrid"&gt;
        &lt;Setter Property="RowHeight" Value="36" /&gt;
        &lt;Setter Property="AlternatingRowBackground" Value="{DynamicResource FondoBrush}" /&gt;
        &lt;Setter Property="Background" Value="{DynamicResource SuperficieBrush}" /&gt;
        &lt;Setter Property="BorderBrush" Value="{DynamicResource BordeBrush}" /&gt;
        &lt;Setter Property="BorderThickness" Value="1" /&gt;
        &lt;Setter Property="CornerRadius" Value="{DynamicResource RadioCard}" /&gt;
        &lt;Setter Property="GridLinesVisibility" Value="Horizontal" /&gt;
        &lt;Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource BordeBrush}" /&gt;
    &lt;/Style&gt;

    &lt;!-- Header: misma escala .micro que los eyebrows (11px SemiBold con
         letter-spacing). Es una etiqueta estructural, no copy. --&gt;
    &lt;Style Selector="DataGridColumnHeader"&gt;
        &lt;Setter Property="FontSize" Value="11" /&gt;
        &lt;Setter Property="FontWeight" Value="SemiBold" /&gt;
        &lt;Setter Property="LetterSpacing" Value="0.6" /&gt;
        &lt;Setter Property="Foreground" Value="{DynamicResource TextoTerciarioBrush}" /&gt;
        &lt;Setter Property="Background" Value="{DynamicResource FondoBrush}" /&gt;
        &lt;Setter Property="Padding" Value="{DynamicResource PaddingCelda}" /&gt;
        &lt;Setter Property="BorderBrush" Value="{DynamicResource BordeBrush}" /&gt;
        &lt;Setter Property="BorderThickness" Value="0,0,0,1" /&gt;
    &lt;/Style&gt;

    &lt;Style Selector="DataGridCell"&gt;
        &lt;Setter Property="Padding" Value="{DynamicResource PaddingCelda}" /&gt;
        &lt;Setter Property="VerticalContentAlignment" Value="Center" /&gt;
    &lt;/Style&gt;

    &lt;!-- Celda numerica. Hoy este estilo esta COPIADO 7 veces en vistas sueltas;
         la tanda 10 borra esas copias y se apoya en este. --&gt;
    &lt;Style Selector="DataGridCell.num"&gt;
        &lt;Setter Property="HorizontalContentAlignment" Value="Right" /&gt;
    &lt;/Style&gt;
    &lt;Style Selector="DataGridCell.num TextBlock"&gt;
        &lt;Setter Property="HorizontalAlignment" Value="Right" /&gt;
    &lt;/Style&gt;
```

**Según el veredicto de `tnum` de la Task 0.3:**

- **Si `tnum` FUNCIONA:** agregá `<Setter Property="FontFeatures" Value="+tnum" />` al bloque `DataGridCell.num TextBlock`.
- **Si NO FUNCIONA o NO ES NECESARIO:** dejalo como está. La alineación derecha ya resuelve la lectura; el fallback de ancho fijo por columna se aplica caso por caso en las tandas 6-13, solo donde una columna numérica se vea torcida.

- [ ] **Step 4: Correr para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~DataGridEstiloRealTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Correr la suite completa — acá es donde puede doler**

Run: `dotnet test StockApp.sln`
Expected: PASS.

Esta tanda cambia el alto de fila y el fondo alternado de las 21 grillas. Tests candidatos a ponerse rojos: `DataGridSortClickTests.cs`, `NuevaImportacionGastosGridTests.cs`, `NuevaImportacionLineasPoaGridTests.cs`, y cualquiera que compare posiciones. **Ninguno se ajusta para que pase sin entender primero qué cambió.** Si un test rojo verificaba una posición que el rediseño movió legítimamente, se actualiza el valor esperado y se anota el porqué en el commit. Si verificaba comportamiento y se rompió, el estilo tiene un bug.

- [ ] **Step 6: Commit de la tanda 2**

```bash
git add -A
git commit -m "feat(ui): estila las grillas y engancha los controles base a los tokens

Themes/DataGrid.axaml tenia 39 lineas que solo apagaban dos indicadores de
foco: las 21 grillas de la app corrian con Fluent crudo.

- DataGrid: alto de fila 36, fondo alternado, lineas horizontales, borde
- DataGridColumnHeader: escala .micro (11px SemiBold, letter-spacing)
- DataGridCell: padding del sistema; .num alinea a la derecha
- Controls.axaml: radios, padding y sombra pasan a DynamicResource

Los dos Setter de DataValidationErrors.ErrorConverter (TextBox y ComboBox)
quedan intactos: son el seam de MovimientoFormControlValidacionTests"
```

---

## Tanda 3: Componentes reutilizables

**Objetivo:** crear `src/StockApp.Presentation/Controls/`, que hoy no existe. Esa ausencia es la causa mecánica de la mitad de la deuda: sin componentes, se copia. Los 26 bloques de navegación duplicados del shell y los 7 estilos de celda numérica repetidos son síntomas del mismo hueco.

**Decisión de implementación:** los componentes heredan de `TemplatedControl` (o `ContentControl` cuando llevan un slot de contenido), con su template en un `ControlTheme` dentro de `Controls/Componentes.axaml`. **No se usa `UserControl`.** Razón: un `UserControl` trae su propio `NameScope`, lo que hace que `Window.FindControl` no lo atraviese — el comentario de `InicioViewTests.cs:143-147` ya documenta ese dolor. Con `TemplatedControl` los tests localizan por tipo y por propiedad, sin pelear con scopes.

### Task 3.1: HeaderVista

**Files:**
- Create: `src/StockApp.Presentation/Controls/HeaderVista.cs`
- Create: `src/StockApp.Presentation/Controls/Componentes.axaml`
- Modify: `src/StockApp.Presentation/App.axaml` — incluir `Componentes.axaml`
- Modify: `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs` — incluir `Componentes.axaml`
- Test: `tests/StockApp.Presentation.UiTests/HeaderVistaTests.cs` (crear)

**Interfaces:**
- Consumes: `Espacio1`, `Espacio5`, `TextoTerciarioBrush`, clase `.micro` (tanda 1).
- Produces: `StockApp.Presentation.Controls.HeaderVista` con las propiedades:
  - `Eyebrow` (`string?`) — etiqueta de sección, en `.micro`
  - `Titulo` (`string?`) — título de la vista
  - `Resumen` (`string?`) — línea de contexto bajo el título
  - `Acciones` (`object?`) — slot para los botones de la derecha

  Las tandas 6 a 13 lo consumen en las 58 vistas. **15 vistas no tienen ni título hoy.**

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/HeaderVistaTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// El header de vista existe porque hoy cada vista improvisa el suyo y 15 no tienen ni titulo.
/// Estos tests fijan el contrato que las 58 vistas van a consumir en las tandas 6 a 13.
/// &lt;/summary&gt;
public class HeaderVistaTests
{
    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:c="using:StockApp.Presentation.Controls"
                Width="800" Height="200"&gt;
            &lt;c:HeaderVista x:Name="Header"
                           Eyebrow="INVENTARIO"
                           Titulo="Productos"
                           Resumen="128 productos activos"&gt;
                &lt;c:HeaderVista.Acciones&gt;
                    &lt;Button x:Name="BotonAccion" Classes="primary" Content="Nuevo" /&gt;
                &lt;/c:HeaderVista.Acciones&gt;
            &lt;/c:HeaderVista&gt;
        &lt;/Window&gt;
        """;

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Montar_ConLosTresTextos_LosTresSeRenderizan()
    {
        var textos = Montar().GetVisualDescendants().OfType&lt;TextBlock&gt;()
            .Select(t =&gt; t.Text).ToList();

        Assert.Contains("INVENTARIO", textos);
        Assert.Contains("Productos", textos);
        Assert.Contains("128 productos activos", textos);
    }

    [AvaloniaFact]
    public void Montar_ConAcciones_ElBotonDelSlotLlegaAlArbolVisual()
    {
        var boton = Montar().GetVisualDescendants().OfType&lt;Button&gt;()
            .FirstOrDefault(b =&gt; b.Name == "BotonAccion");

        Assert.NotNull(boton);
        Assert.True(ArbolVisual.EsVisibleEnArbol(boton!));
    }

    [AvaloniaFact]
    public void Montar_SinEyebrow_ElEyebrowNoOcupaLugar()
    {
        // Muchas vistas no tienen seccion padre. Un eyebrow vacio no debe dejar un hueco.
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;("""
            &lt;Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="800" Height="200"&gt;
                &lt;c:HeaderVista Titulo="Productos" /&gt;
            &lt;/Window&gt;
            """, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = window.GetVisualDescendants().OfType&lt;HeaderVista&gt;().First();
        var eyebrow = header.GetVisualDescendants().OfType&lt;TextBlock&gt;()
            .FirstOrDefault(t =&gt; t.Classes.Contains("micro"));

        Assert.True(eyebrow is null || !eyebrow.IsVisible,
            "Con Eyebrow en null el TextBlock del eyebrow debe estar oculto, no vacio ocupando alto.");
    }

    [AvaloniaFact]
    public void Montar_SinResumen_ElResumenNoOcupaLugar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;("""
            &lt;Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="800" Height="200"&gt;
                &lt;c:HeaderVista Titulo="Productos" /&gt;
            &lt;/Window&gt;
            """, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = window.GetVisualDescendants().OfType&lt;HeaderVista&gt;().First();
        var visiblesConTexto = header.GetVisualDescendants().OfType&lt;TextBlock&gt;()
            .Count(t =&gt; t.IsVisible &amp;&amp; !string.IsNullOrEmpty(t.Text));

        Assert.Equal(1, visiblesConTexto);
    }
}
```

- [ ] **Step 2: Correr para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~HeaderVistaTests"`
Expected: FAIL de compilación — el namespace `StockApp.Presentation.Controls` no existe.

- [ ] **Step 3: Crear el control**

Crear `src/StockApp.Presentation/Controls/HeaderVista.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace StockApp.Presentation.Controls;

/// &lt;summary&gt;
/// Encabezado estandar de vista: eyebrow de seccion, titulo, linea de resumen y slot de acciones
/// a la derecha. Existe porque cada una de las 58 vistas improvisaba el suyo y 15 no tenian ni
/// titulo. TemplatedControl y no UserControl: un UserControl trae su propio NameScope y
/// Window.FindControl no lo atraviesa (ver el comentario de InicioViewTests.cs).
/// &lt;/summary&gt;
public class HeaderVista : TemplatedControl
{
    public static readonly StyledProperty&lt;string?&gt; EyebrowProperty =
        AvaloniaProperty.Register&lt;HeaderVista, string?&gt;(nameof(Eyebrow));

    public static readonly StyledProperty&lt;string?&gt; TituloProperty =
        AvaloniaProperty.Register&lt;HeaderVista, string?&gt;(nameof(Titulo));

    public static readonly StyledProperty&lt;string?&gt; ResumenProperty =
        AvaloniaProperty.Register&lt;HeaderVista, string?&gt;(nameof(Resumen));

    public static readonly StyledProperty&lt;object?&gt; AccionesProperty =
        AvaloniaProperty.Register&lt;HeaderVista, object?&gt;(nameof(Acciones));

    /// &lt;summary&gt;Etiqueta de seccion sobre el titulo, en escala .micro. Si es null, no ocupa alto.&lt;/summary&gt;
    public string? Eyebrow
    {
        get =&gt; GetValue(EyebrowProperty);
        set =&gt; SetValue(EyebrowProperty, value);
    }

    public string? Titulo
    {
        get =&gt; GetValue(TituloProperty);
        set =&gt; SetValue(TituloProperty, value);
    }

    /// &lt;summary&gt;Linea de contexto bajo el titulo. Si es null, no ocupa alto.&lt;/summary&gt;
    public string? Resumen
    {
        get =&gt; GetValue(ResumenProperty);
        set =&gt; SetValue(ResumenProperty, value);
    }

    /// &lt;summary&gt;
    /// Slot de acciones, alineado a la derecha. Regla de jerarquia: UNA sola accion primaria
    /// (Classes="primary") por vista. Si hay dos acciones principales, no hay ninguna.
    /// &lt;/summary&gt;
    [Content]
    public object? Acciones
    {
        get =&gt; GetValue(AccionesProperty);
        set =&gt; SetValue(AccionesProperty, value);
    }
}
```

- [ ] **Step 4: Crear el template**

Crear `src/StockApp.Presentation/Controls/Componentes.axaml`:

```xml
&lt;ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"&gt;

    &lt;!-- ════════════════════════════════════════════════════════════════════
         StockApp UI Kit — ControlThemes de los componentes de Controls/.
         Se incluye en App.axaml como ResourceInclude y en TestAppBuilder.cs
         del banco de pruebas. Los componentes son TemplatedControl, no
         UserControl: un UserControl trae su propio NameScope y complica la
         localizacion desde los tests.
         ════════════════════════════════════════════════════════════════════ --&gt;

    &lt;ControlTheme x:Key="{x:Type c:HeaderVista}" TargetType="c:HeaderVista"&gt;
        &lt;Setter Property="Margin" Value="0,0,0,24" /&gt;
        &lt;Setter Property="Template"&gt;
            &lt;ControlTemplate&gt;
                &lt;Grid ColumnDefinitions="*,Auto" VerticalAlignment="Top"&gt;
                    &lt;StackPanel Grid.Column="0" Spacing="{DynamicResource Espacio1}"&gt;
                        &lt;TextBlock Classes="micro"
                                   Text="{TemplateBinding Eyebrow}"
                                   IsVisible="{TemplateBinding Eyebrow, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" /&gt;
                        &lt;TextBlock Classes="titulo-vista"
                                   Text="{TemplateBinding Titulo}" /&gt;
                        &lt;TextBlock Classes="body"
                                   Foreground="{DynamicResource TextoSecundarioBrush}"
                                   Text="{TemplateBinding Resumen}"
                                   IsVisible="{TemplateBinding Resumen, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" /&gt;
                    &lt;/StackPanel&gt;

                    &lt;ContentPresenter Grid.Column="1"
                                      Content="{TemplateBinding Acciones}"
                                      VerticalAlignment="Center" /&gt;
                &lt;/Grid&gt;
            &lt;/ControlTemplate&gt;
        &lt;/Setter&gt;
    &lt;/ControlTheme&gt;

&lt;/ResourceDictionary&gt;
```

- [ ] **Step 5: Registrar el diccionario en la app y en el banco de pruebas**

En `src/StockApp.Presentation/App.axaml`, dentro de `Application.Resources` → `ResourceDictionary.MergedDictionaries`, después del `ResourceInclude` de `Tokens.axaml`:

```xml
                &lt;ResourceInclude Source="avares://StockApp.Presentation/Controls/Componentes.axaml"/&gt;
```

En `tests/StockApp.Presentation.UiTests/TestAppBuilder.cs`, después del `ResourceInclude` de `Tokens.axaml`:

```csharp
        // Componentes de Controls/: sus ControlTheme viven aca. Sin este include, un
        // c:HeaderVista monta sin Template y el arbol visual queda vacio.
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(
            new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://StockApp.Presentation/Controls/Componentes.axaml")
        });
```

- [ ] **Step 6: Correr para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~HeaderVistaTests"`
Expected: PASS, 5 tests.

### Task 3.2: TarjetaMetrica, BadgeEstado y EstadoVacio

**Files:**
- Create: `src/StockApp.Presentation/Controls/TarjetaMetrica.cs`
- Create: `src/StockApp.Presentation/Controls/BadgeEstado.cs`
- Create: `src/StockApp.Presentation/Controls/EstadoVacio.cs`
- Modify: `src/StockApp.Presentation/Controls/Componentes.axaml` — tres `ControlTheme` más
- Test: `tests/StockApp.Presentation.UiTests/ComponentesBasicosTests.cs` (crear)

**Interfaces:**
- Consumes: tokens de la tanda 1, `Border.card` de `Controls.axaml`.
- Produces:
  - `TarjetaMetrica` con `Etiqueta` (`string?`), `Valor` (`string?`), `Detalle` (`string?`)
  - `BadgeEstado` con `Texto` (`string?`) y `Tono` (`TonoBadge`: `Neutro`, `Exito`, `Advertencia`, `Peligro`, `Info`)
  - `EstadoVacio` con `Titulo` (`string?`), `Mensaje` (`string?`), `EsError` (`bool`)

**Por qué el badge lleva palabra y no solo color:** hoy el stock negativo se comunica pintando el número de rojo. Un usuario daltónico no lo distingue. El badge dice la palabra y además la pinta.

**Por qué `EstadoVacio` lleva `EsError`:** hoy una grilla sin datos y una grilla que falló al cargar se ven idénticas — las dos, vacías. El usuario no puede saber si tiene que cargar datos o reintentar.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/ComponentesBasicosTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

public class ComponentesBasicosTests
{
    private static Window Montar(string contenido)
    {
        var xaml = $$"""
            &lt;Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="600" Height="300"&gt;
                {{contenido}}
            &lt;/Window&gt;
            """;
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TarjetaMetrica_RenderizaEtiquetaValorYDetalle()
    {
        var textos = Montar("""&lt;c:TarjetaMetrica Etiqueta="STOCK TOTAL" Valor="1.284" Detalle="+12 esta semana" /&gt;""")
            .GetVisualDescendants().OfType&lt;TextBlock&gt;().Select(t =&gt; t.Text).ToList();

        Assert.Contains("STOCK TOTAL", textos);
        Assert.Contains("1.284", textos);
        Assert.Contains("+12 esta semana", textos);
    }

    [AvaloniaFact]
    public void TarjetaMetrica_SinDetalle_NoDejaHueco()
    {
        var tarjeta = Montar("""&lt;c:TarjetaMetrica Etiqueta="STOCK" Valor="1.284" /&gt;""")
            .GetVisualDescendants().OfType&lt;TarjetaMetrica&gt;().First();

        var conTexto = tarjeta.GetVisualDescendants().OfType&lt;TextBlock&gt;()
            .Count(t =&gt; t.IsVisible &amp;&amp; !string.IsNullOrEmpty(t.Text));

        Assert.Equal(2, conTexto);
    }

    [AvaloniaFact]
    public void BadgeEstado_DiceLaPalabraNoSoloElColor()
    {
        // El punto del componente: hoy el stock negativo se comunica SOLO pintando el numero de
        // rojo, y un usuario daltonico no lo distingue. El badge dice la palabra Y la pinta.
        var textos = Montar("""&lt;c:BadgeEstado Texto="Bajo minimo" Tono="Advertencia" /&gt;""")
            .GetVisualDescendants().OfType&lt;TextBlock&gt;().Select(t =&gt; t.Text).ToList();

        Assert.Contains("Bajo minimo", textos);
    }

    [AvaloniaTheory]
    [InlineData("Exito", "#16A34A")]
    [InlineData("Advertencia", "#D97706")]
    [InlineData("Peligro", "#DC2626")]
    [InlineData("Info", "#0EA5E9")]
    public void BadgeEstado_CadaTonoUsaSuColorSemantico(string tono, string colorEsperado)
    {
        var badge = Montar($"""&lt;c:BadgeEstado Texto="Estado" Tono="{tono}" /&gt;""")
            .GetVisualDescendants().OfType&lt;BadgeEstado&gt;().First();

        var texto = badge.GetVisualDescendants().OfType&lt;TextBlock&gt;().First();

        Assert.Equal(
            Color.Parse(colorEsperado),
            Assert.IsType&lt;SolidColorBrush&gt;(texto.Foreground).Color);
    }

    [AvaloniaFact]
    public void EstadoVacio_SinDatos_YFalloDeCarga_SonDistinguibles()
    {
        // Hoy los dos casos se ven identicos: una grilla vacia. El usuario no sabe si tiene que
        // cargar datos o reintentar.
        var vacio = Montar("""&lt;c:EstadoVacio Titulo="Sin movimientos" Mensaje="Todavia no registraste ninguno." /&gt;""")
            .GetVisualDescendants().OfType&lt;EstadoVacio&gt;().First();

        var error = Montar("""&lt;c:EstadoVacio Titulo="No se pudo cargar" Mensaje="Revisa la conexion." EsError="True" /&gt;""")
            .GetVisualDescendants().OfType&lt;EstadoVacio&gt;().First();

        Assert.False(vacio.EsError);
        Assert.True(error.EsError);

        var colorVacio = Assert.IsType&lt;SolidColorBrush&gt;(
            vacio.GetVisualDescendants().OfType&lt;TextBlock&gt;().First().Foreground).Color;
        var colorError = Assert.IsType&lt;SolidColorBrush&gt;(
            error.GetVisualDescendants().OfType&lt;TextBlock&gt;().First().Foreground).Color;

        Assert.NotEqual(colorVacio, colorError);
    }
}
```

- [ ] **Step 2: Correr para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ComponentesBasicosTests"`
Expected: FAIL de compilación — los tres tipos no existen.

- [ ] **Step 3: Crear los tres controles**

Crear `src/StockApp.Presentation/Controls/TarjetaMetrica.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls;

/// &lt;summary&gt;
/// KPI de la fila de metricas que va sobre las grillas. Etiqueta en .micro, valor grande,
/// detalle opcional debajo.
/// &lt;/summary&gt;
public class TarjetaMetrica : TemplatedControl
{
    public static readonly StyledProperty&lt;string?&gt; EtiquetaProperty =
        AvaloniaProperty.Register&lt;TarjetaMetrica, string?&gt;(nameof(Etiqueta));

    public static readonly StyledProperty&lt;string?&gt; ValorProperty =
        AvaloniaProperty.Register&lt;TarjetaMetrica, string?&gt;(nameof(Valor));

    public static readonly StyledProperty&lt;string?&gt; DetalleProperty =
        AvaloniaProperty.Register&lt;TarjetaMetrica, string?&gt;(nameof(Detalle));

    public string? Etiqueta
    {
        get =&gt; GetValue(EtiquetaProperty);
        set =&gt; SetValue(EtiquetaProperty, value);
    }

    public string? Valor
    {
        get =&gt; GetValue(ValorProperty);
        set =&gt; SetValue(ValorProperty, value);
    }

    /// &lt;summary&gt;Linea de contexto bajo el valor. Si es null, no ocupa alto.&lt;/summary&gt;
    public string? Detalle
    {
        get =&gt; GetValue(DetalleProperty);
        set =&gt; SetValue(DetalleProperty, value);
    }
}
```

Crear `src/StockApp.Presentation/Controls/BadgeEstado.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls;

/// &lt;summary&gt;Tono semantico del badge. Mapea a los brushes de Tokens.axaml.&lt;/summary&gt;
public enum TonoBadge
{
    Neutro,
    Exito,
    Advertencia,
    Peligro,
    Info,
}

/// &lt;summary&gt;
/// Estado comunicado con PALABRA mas color, no solo color. Hoy el stock negativo se pinta de
/// rojo y nada mas: un usuario daltonico no lo distingue de un stock normal.
/// &lt;/summary&gt;
public class BadgeEstado : TemplatedControl
{
    public static readonly StyledProperty&lt;string?&gt; TextoProperty =
        AvaloniaProperty.Register&lt;BadgeEstado, string?&gt;(nameof(Texto));

    public static readonly StyledProperty&lt;TonoBadge&gt; TonoProperty =
        AvaloniaProperty.Register&lt;BadgeEstado, TonoBadge&gt;(nameof(Tono), TonoBadge.Neutro);

    public string? Texto
    {
        get =&gt; GetValue(TextoProperty);
        set =&gt; SetValue(TextoProperty, value);
    }

    public TonoBadge Tono
    {
        get =&gt; GetValue(TonoProperty);
        set =&gt; SetValue(TonoProperty, value);
    }
}
```

Crear `src/StockApp.Presentation/Controls/EstadoVacio.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls;

/// &lt;summary&gt;
/// Estado vacio de una grilla o listado. EsError distingue "todavia no hay datos" de "fallo la
/// carga": hoy los dos casos se ven identicos y el usuario no sabe si cargar datos o reintentar.
/// &lt;/summary&gt;
public class EstadoVacio : TemplatedControl
{
    public static readonly StyledProperty&lt;string?&gt; TituloProperty =
        AvaloniaProperty.Register&lt;EstadoVacio, string?&gt;(nameof(Titulo));

    public static readonly StyledProperty&lt;string?&gt; MensajeProperty =
        AvaloniaProperty.Register&lt;EstadoVacio, string?&gt;(nameof(Mensaje));

    public static readonly StyledProperty&lt;bool&gt; EsErrorProperty =
        AvaloniaProperty.Register&lt;EstadoVacio, bool&gt;(nameof(EsError));

    public string? Titulo
    {
        get =&gt; GetValue(TituloProperty);
        set =&gt; SetValue(TituloProperty, value);
    }

    public string? Mensaje
    {
        get =&gt; GetValue(MensajeProperty);
        set =&gt; SetValue(MensajeProperty, value);
    }

    /// &lt;summary&gt;True = la carga fallo. False = no hay datos todavia.&lt;/summary&gt;
    public bool EsError
    {
        get =&gt; GetValue(EsErrorProperty);
        set =&gt; SetValue(EsErrorProperty, value);
    }
}
```

- [ ] **Step 4: Agregar los tres ControlTheme**

En `src/StockApp.Presentation/Controls/Componentes.axaml`, antes del `</ResourceDictionary>` de cierre:

```xml
    &lt;ControlTheme x:Key="{x:Type c:TarjetaMetrica}" TargetType="c:TarjetaMetrica"&gt;
        &lt;Setter Property="Template"&gt;
            &lt;ControlTemplate&gt;
                &lt;Border Classes="card"&gt;
                    &lt;StackPanel Spacing="{DynamicResource Espacio1}"&gt;
                        &lt;TextBlock Classes="micro" Text="{TemplateBinding Etiqueta}" /&gt;
                        &lt;TextBlock Text="{TemplateBinding Valor}"
                                   FontSize="24"
                                   FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextoPrimarioBrush}" /&gt;
                        &lt;TextBlock Classes="caption"
                                   Text="{TemplateBinding Detalle}"
                                   IsVisible="{TemplateBinding Detalle, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" /&gt;
                    &lt;/StackPanel&gt;
                &lt;/Border&gt;
            &lt;/ControlTemplate&gt;
        &lt;/Setter&gt;
    &lt;/ControlTheme&gt;

    &lt;ControlTheme x:Key="{x:Type c:BadgeEstado}" TargetType="c:BadgeEstado"&gt;
        &lt;Setter Property="Template"&gt;
            &lt;ControlTemplate&gt;
                &lt;Border x:Name="Fondo"
                        CornerRadius="{DynamicResource RadioChico}"
                        Padding="8,2"
                        HorizontalAlignment="Left"&gt;
                    &lt;TextBlock x:Name="Texto"
                               Text="{TemplateBinding Texto}"
                               FontSize="12"
                               FontWeight="Medium" /&gt;
                &lt;/Border&gt;
            &lt;/ControlTemplate&gt;
        &lt;/Setter&gt;

        &lt;!-- Tono neutro por defecto --&gt;
        &lt;Style Selector="^ /template/ TextBlock#Texto"&gt;
            &lt;Setter Property="Foreground" Value="{DynamicResource TextoSecundarioBrush}" /&gt;
        &lt;/Style&gt;
        &lt;Style Selector="^ /template/ Border#Fondo"&gt;
            &lt;Setter Property="Background" Value="{DynamicResource DeshabilitadoFondoBrush}" /&gt;
        &lt;/Style&gt;

        &lt;Style Selector="^[Tono=Exito] /template/ TextBlock#Texto"&gt;
            &lt;Setter Property="Foreground" Value="{DynamicResource SuccessBrush}" /&gt;
        &lt;/Style&gt;
        &lt;Style Selector="^[Tono=Exito] /template/ Border#Fondo"&gt;
            &lt;Setter Property="Background" Value="{DynamicResource BrandSuaveBrush}" /&gt;
        &lt;/Style&gt;

        &lt;Style Selector="^[Tono=Advertencia] /template/ TextBlock#Texto"&gt;
            &lt;Setter Property="Foreground" Value="{DynamicResource WarningBrush}" /&gt;
        &lt;/Style&gt;

        &lt;Style Selector="^[Tono=Peligro] /template/ TextBlock#Texto"&gt;
            &lt;Setter Property="Foreground" Value="{DynamicResource DangerBrush}" /&gt;
        &lt;/Style&gt;

        &lt;Style Selector="^[Tono=Info] /template/ TextBlock#Texto"&gt;
            &lt;Setter Property="Foreground" Value="{DynamicResource InfoBrush}" /&gt;
        &lt;/Style&gt;
    &lt;/ControlTheme&gt;

    &lt;ControlTheme x:Key="{x:Type c:EstadoVacio}" TargetType="c:EstadoVacio"&gt;
        &lt;Setter Property="Template"&gt;
            &lt;ControlTemplate&gt;
                &lt;StackPanel Spacing="{DynamicResource Espacio2}"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center"
                            Margin="{DynamicResource MargenVista}"&gt;
                    &lt;TextBlock x:Name="Titulo"
                               Text="{TemplateBinding Titulo}"
                               Classes="seccion"
                               HorizontalAlignment="Center" /&gt;
                    &lt;TextBlock Text="{TemplateBinding Mensaje}"
                               Classes="caption"
                               HorizontalAlignment="Center"
                               TextWrapping="Wrap"
                               MaxWidth="360"
                               TextAlignment="Center" /&gt;
                &lt;/StackPanel&gt;
            &lt;/ControlTemplate&gt;
        &lt;/Setter&gt;

        &lt;!-- Fallo de carga: el titulo va en Danger. Sin esto, "no hay datos" y
             "no se pudo cargar" se ven identicos y el usuario no sabe si cargar
             datos o reintentar. --&gt;
        &lt;Style Selector="^[EsError=True] /template/ TextBlock#Titulo"&gt;
            &lt;Setter Property="Foreground" Value="{DynamicResource DangerBrush}" /&gt;
        &lt;/Style&gt;
    &lt;/ControlTheme&gt;
```

- [ ] **Step 5: Correr para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ComponentesBasicosTests"`
Expected: PASS, 8 tests.

Si `BadgeEstado_CadaTonoUsaSuColorSemantico` falla para `Exito`: el selector `^[Tono=Exito]` requiere que el valor del enum se resuelva por nombre. Verificá que el XAML del test escriba `Tono="Exito"` exactamente como el miembro del enum.

### Task 3.3: CampoFormulario, conservando el seam de validación

**Files:**
- Create: `src/StockApp.Presentation/Controls/CampoFormulario.cs`
- Modify: `src/StockApp.Presentation/Controls/Componentes.axaml`
- Test: `tests/StockApp.Presentation.UiTests/CampoFormularioTests.cs` (crear)

**Interfaces:**
- Consumes: tokens de la tanda 1.
- Produces: `CampoFormulario : ContentControl` con `Etiqueta` (`string?`) y `Requerido` (`bool`). El control de entrada va en el `Content`.

**EL RIESGO DE ESTA TAREA — leelo antes de escribir una línea:**

`Controls.axaml` define `(DataValidationErrors.ErrorConverter)` en **dos** lugares: `TextBox` (línea 185) y `ComboBox` (línea 203). Esos `Setter` blindan la app entera contra excepciones crudas de .NET llegando a la UI, y son de lo que dependen los 2 tests de `MovimientoFormControlValidacionTests.cs`.

**`CampoFormulario` NO reemplaza ese seam, NO lo envuelve y NO define un `ErrorTemplate` propio.** Solo agrega la etiqueta arriba del control. El `TextBox` o `ComboBox` que va adentro sigue siendo un control normal que recibe los `Setter` globales por selector de tipo. Si el componente interceptara la validación, los 2 tests se caerían — y si alguien los "arregla" para que pasen, se pierde el blindaje entero.

- [ ] **Step 1: Correr los 2 tests del seam ANTES de tocar nada, y anotar el resultado**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~MovimientoFormControlValidacionTests"`
Expected: PASS, 2 tests. Anotá el conteo. Es la línea base de esta tarea.

- [ ] **Step 2: Escribir el test que falla**

Crear `tests/StockApp.Presentation.UiTests/CampoFormularioTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// &lt;summary&gt;
/// CampoFormulario agrega la etiqueta y nada mas. El punto critico esta en el ultimo test: el
/// control de adentro tiene que seguir recibiendo los Setter globales de Controls.axaml,
/// incluido (DataValidationErrors.ErrorConverter). Si el componente interceptara la validacion,
/// se romperia el blindaje de toda la app contra excepciones crudas llegando a la UI.
/// &lt;/summary&gt;
public class CampoFormularioTests
{
    private const string Xaml = """
        &lt;Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:c="using:StockApp.Presentation.Controls"
                Width="500" Height="200"&gt;
            &lt;c:CampoFormulario x:Name="Campo" Etiqueta="Precio unitario"&gt;
                &lt;TextBox x:Name="Entrada" /&gt;
            &lt;/c:CampoFormulario&gt;
        &lt;/Window&gt;
        """;

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse&lt;Window&gt;(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Montar_RenderizaLaEtiqueta()
    {
        var textos = Montar().GetVisualDescendants().OfType&lt;TextBlock&gt;().Select(t =&gt; t.Text).ToList();
        Assert.Contains("Precio unitario", textos);
    }

    [AvaloniaFact]
    public void Montar_ElControlDelContenidoLlegaAlArbolYEsVisible()
    {
        var entrada = Montar().GetVisualDescendants().OfType&lt;TextBox&gt;()
            .FirstOrDefault(t =&gt; t.Name == "Entrada");

        Assert.NotNull(entrada);
        Assert.True(ArbolVisual.EsVisibleEnArbol(entrada!));
    }

    [AvaloniaFact]
    public void Montar_ElTextBoxDeAdentroCONSERVAElErrorConverterGlobal()
    {
        // ESTE es el test que importa. Controls.axaml define el ErrorConverter por selector de
        // tipo "TextBox"; si CampoFormulario cambiara el tipo del control, lo envolviera en algo
        // que rompa el selector, o definiera su propio ErrorTemplate, este Setter dejaria de
        // aplicar y la app perderia el blindaje contra excepciones crudas de .NET en la UI.
        var entrada = Montar().GetVisualDescendants().OfType&lt;TextBox&gt;().First(t =&gt; t.Name == "Entrada");

        var converter = DataValidationErrors.GetErrorConverter(entrada);

        Assert.NotNull(converter);
    }
}
```

- [ ] **Step 3: Correr para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CampoFormularioTests"`
Expected: FAIL de compilación — `CampoFormulario` no existe.

- [ ] **Step 4: Crear el control**

Crear `src/StockApp.Presentation/Controls/CampoFormulario.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Controls;

/// &lt;summary&gt;
/// Etiqueta mas control de entrada. Hereda de ContentControl: el control de entrada va en el
/// Content y se renderiza en un ContentPresenter, asi sigue recibiendo los Setter globales de
/// Themes/Controls.axaml por selector de tipo.
///
/// IMPORTANTE: este componente NO intercepta la validacion. Controls.axaml define
/// (DataValidationErrors.ErrorConverter) para TextBox (linea 185) y ComboBox (linea 203), que es
/// el blindaje de toda la app contra excepciones crudas de .NET llegando a la UI. No definir un
/// ErrorTemplate propio aca ni envolver el control en nada que rompa el selector de tipo.
/// &lt;/summary&gt;
public class CampoFormulario : ContentControl
{
    public static readonly StyledProperty&lt;string?&gt; EtiquetaProperty =
        AvaloniaProperty.Register&lt;CampoFormulario, string?&gt;(nameof(Etiqueta));

    public static readonly StyledProperty&lt;bool&gt; RequeridoProperty =
        AvaloniaProperty.Register&lt;CampoFormulario, bool&gt;(nameof(Requerido));

    public string? Etiqueta
    {
        get =&gt; GetValue(EtiquetaProperty);
        set =&gt; SetValue(EtiquetaProperty, value);
    }

    /// &lt;summary&gt;Marca visualmente el campo como obligatorio. No valida nada por si solo.&lt;/summary&gt;
    public bool Requerido
    {
        get =&gt; GetValue(RequeridoProperty);
        set =&gt; SetValue(RequeridoProperty, value);
    }
}
```

- [ ] **Step 5: Agregar el ControlTheme**

En `src/StockApp.Presentation/Controls/Componentes.axaml`, antes del cierre:

```xml
    &lt;ControlTheme x:Key="{x:Type c:CampoFormulario}" TargetType="c:CampoFormulario"&gt;
        &lt;Setter Property="Template"&gt;
            &lt;ControlTemplate&gt;
                &lt;StackPanel Spacing="{DynamicResource Espacio1}"&gt;
                    &lt;StackPanel Orientation="Horizontal" Spacing="2"&gt;
                        &lt;TextBlock Text="{TemplateBinding Etiqueta}"
                                   Classes="caption"
                                   Foreground="{DynamicResource TextoPrimarioBrush}" /&gt;
                        &lt;TextBlock Text="*"
                                   Classes="caption"
                                   Foreground="{DynamicResource DangerBrush}"
                                   IsVisible="{TemplateBinding Requerido}" /&gt;
                    &lt;/StackPanel&gt;

                    &lt;!-- ContentPresenter pelado: el control de entrada queda como hijo directo y
                         sigue recibiendo los Setter por selector de tipo de Controls.axaml,
                         incluido el ErrorConverter. NO agregar ErrorTemplate aca. --&gt;
                    &lt;ContentPresenter Content="{TemplateBinding Content}" /&gt;
                &lt;/StackPanel&gt;
            &lt;/ControlTemplate&gt;
        &lt;/Setter&gt;
    &lt;/ControlTheme&gt;
```

- [ ] **Step 6: Correr los tests nuevos Y los 2 del seam**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CampoFormularioTests|FullyQualifiedName~MovimientoFormControlValidacionTests"`
Expected: PASS, 5 tests (3 nuevos + los 2 del seam, que deben seguir igual que en el Step 1).

**Si alguno de los 2 del seam se pone rojo, PARÁ.** No los ajustes. Significa que el componente rompió el blindaje de validación de la app entera. Revisá el template: probablemente el `ContentPresenter` está envuelto en algo, o hay un `ErrorTemplate` de más.

- [ ] **Step 7: Validar por mutación el test del seam**

Agregá temporalmente al `ControlTheme` de `CampoFormulario`, dentro del `StackPanel` del template, un `Setter` que pise el converter:

```xml
                    &lt;ContentPresenter Content="{TemplateBinding Content}"
                                      DataValidationErrors.ErrorConverter="{x:Null}" /&gt;
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Montar_ElTextBoxDeAdentroCONSERVAElErrorConverterGlobal"`
Expected: **FAIL**. Si pasa igual, el test no custodia nada y hay que rehacerlo.

Revertí la mutación y volvé a correr: Expected PASS.

- [ ] **Step 8: Correr la suite completa y commitear**

Run: `dotnet test StockApp.sln`
Expected: PASS.

```bash
git add -A
git commit -m "feat(ui): crea los cinco componentes reutilizables en Controls/

La carpeta Controls/ no existia, y esa ausencia es la causa mecanica de la
mitad de la deuda visual: sin componentes, se copia.

- HeaderVista: eyebrow, titulo, resumen y slot de acciones (15 vistas no
  tienen ni titulo hoy)
- TarjetaMetrica: KPI sobre las grillas
- BadgeEstado: estado con PALABRA mas color, no solo color (hoy el stock
  negativo se pinta de rojo y un daltonico no lo distingue)
- EstadoVacio: distingue 'no hay datos' de 'fallo la carga', hoy identicos
- CampoFormulario: etiqueta mas control, CONSERVANDO el seam de
  DataValidationErrors.ErrorConverter de Controls.axaml

TemplatedControl y no UserControl: un UserControl trae su propio NameScope"
```

---

## Tanda 4: Red de seguridad de permisos

**Objetivo:** escribir los tests de gate que hoy NO existen, **antes** de tocar el shell. No es una tanda de refactor visual: es la que hace que el rediseño del sidebar sea seguro.

**Los tres huecos, ya presentes en `main`:**

1. `ShellMainView.axaml` tiene **31 `IsVisible` gateados** (24 botones + 7 headers de sección) y **cero tests de UI**. Es exactamente la vista que la tanda 5 reescribe entera.
2. `DocumentoListView.axaml` tiene 5 gates por fila, y `DocumentoListViewTests.cs` monta **siempre** con `RolUsuario.Admin`. Admin cortocircuita el chequeo en `AuthorizationService.cs:65-66` antes de mirar permisos: los tests están verdes sin probar el gate.
3. `DocumentoFormView.axaml` tiene 5 gates (líneas 53 y 63-67) y cero cobertura de UI.

**Decisión sobre cómo localizan estos tests — importa más de lo que parece:** los tests localizan los botones del sidebar por **identidad del `Command`** (`ReferenceEquals(boton.Command, vm.NavProductosCommand)`), no por `x:Name` ni por texto. Dos razones:

- `ShellMainView.axaml` hoy **no tiene un solo `x:Name`** en sus 450 líneas.
- La tanda 5 colapsa los 26 bloques a un `ItemsControl` con template. Cualquier `x:Name` que se agregue ahora desaparece en esa reescritura, y habría que rehacer la red justo cuando hay que usarla. La identidad del `Command` sobrevive: el `RelayCommand` generado por CommunityToolkit es el mismo objeto antes y después del rediseño.

**Por qué no alcanzan los 52 tests de `ShellMainViewModelTests.cs`:** son tests de ViewModel. Verifican que `PuedeGestionarProductos` devuelva lo correcto. Si mañana alguien borra el `IsVisible="{Binding PuedeGestionarProductos}"` del XAML, esos 52 siguen en verde y el botón se le muestra a quien no debe. Un gate de UI se custodia montando la View real.

### Task 4.1: Fakes de sesión unificados y con permisos

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/SesionFakes.cs`
- Modify: `tests/StockApp.Presentation.UiTests/TareaFakes.cs:96-108` — `TareaSessionFake` gana permisos
- Modify: `tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs:64-72` — borrar `AuthServiceFake` privado
- Modify: `tests/StockApp.Presentation.UiTests/InicioViewTests.cs:67-75` — borrar `AuthServiceFake` privado

**Interfaces:**
- Consumes: nada.
- Produces:
  - `internal sealed class SesionFake : ICurrentSession` — ctor `(RolUsuario rol, params string[] permisos)`
  - `internal sealed class InfoAppFake : IInfoApp` — ctor `(string version = "0.0.0")`
  - `internal sealed class AuthServiceFake : IAuthService` — promovido desde las dos copias privadas

  Las tareas 4.2 a 4.4 y toda la tanda 5 los consumen.

**El problema concreto:** `TareaSessionFake` (`TareaFakes.cs:96-108`) acepta el rol pero su `PermisosActuales` devuelve **siempre un `HashSet` vacío** y `UsuarioActual` siempre `null`. Por eso `DocumentoListViewTests` monta con Admin: es lo único que produce gates en `true`. Si cambiás esos tests a `Operador` sin tocar el fake, los 5 gates dan `false` y el test verifica el caso trivial — nace muerto. El molde correcto ya existe en `GastosViewTests.cs:35-53`, pero está duplicado en 4 archivos.

**Ojo con Moq:** `StockApp.Presentation.UiTests` **no referencia Moq** (a diferencia de `StockApp.Presentation.Tests`). El helper `Crear` de `ShellMainViewModelTests.cs:20-35` usa `Mock<T>` y **no es portable**. Los fakes van escritos a mano.

- [ ] **Step 1: Escribir el test del fake**

Crear `tests/StockApp.Presentation.UiTests/SesionFakesTests.cs`:

```csharp
using System.Collections.Generic;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El fake de sesion es la base de toda la red de seguridad de permisos: si miente, los tests de
/// gate dan falsos verdes en masa. TareaSessionFake devolvia SIEMPRE un set vacio de permisos, y
/// por eso los tests de Documentos montaban con Admin — el unico rol que producia gates en true.
/// </summary>
public class SesionFakesTests
{
    [Fact]
    public void SesionFake_ConPermisosExplicitos_LosDevuelve()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos);

        Assert.Equal(RolUsuario.Operador, sesion.RolActual);
        Assert.Contains(Permisos.VerFinanzas, sesion.PermisosActuales);
        Assert.Contains(Permisos.RegistrarGastos, sesion.PermisosActuales);
        Assert.DoesNotContain(Permisos.GestionarProductos, sesion.PermisosActuales);
    }

    [Fact]
    public void SesionFake_SinPermisos_DevuelveSetVacioPeroUsuarioValido()
    {
        var sesion = new SesionFake(RolUsuario.Operador);

        Assert.True(sesion.EstaAutenticado);
        Assert.NotNull(sesion.UsuarioActual);
        Assert.Empty(sesion.PermisosActuales);
    }

    [Fact]
    public void SesionFake_ComoAdmin_NoNecesitaPermisosExplicitos()
    {
        // Admin cortocircuita el chequeo en AuthorizationService.cs:65-66. El fake refleja eso:
        // el rol es lo que importa, la lista queda vacia a proposito.
        var sesion = new SesionFake(RolUsuario.Admin);

        Assert.Equal(RolUsuario.Admin, sesion.RolActual);
        Assert.Empty(sesion.PermisosActuales);
    }
}
```

- [ ] **Step 2: Correr para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~SesionFakesTests"`
Expected: FAIL de compilación — `SesionFake` no existe.

- [ ] **Step 3: Crear los fakes compartidos**

Crear `tests/StockApp.Presentation.UiTests/SesionFakes.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Sesion de prueba con rol Y permisos explicitos. Reemplaza los CurrentSessionFake duplicados
/// en GastosViewTests, PagosGastoViewTests, IngresosViewTests e InicioViewTests.
///
/// Usar SIEMPRE RolUsuario.Operador con permisos explicitos para testear un gate: Admin
/// cortocircuita el chequeo en AuthorizationService.cs:65-66 y el test pasa sin probar nada.
/// </summary>
internal sealed class SesionFake : ICurrentSession
{
    private readonly IReadOnlySet<string> _permisos;

    public SesionFake(RolUsuario rol, params string[] permisos)
    {
        RolActual = rol;
        _permisos = new HashSet<string>(permisos);
    }

    public bool EstaAutenticado => true;
    public UsuarioSesion? UsuarioActual => new(1, "prueba", RolActual!.Value, "Usuario de prueba");
    public RolUsuario? RolActual { get; }
    public IReadOnlySet<string> PermisosActuales => _permisos;

    public void EstablecerPermisos(IReadOnlySet<string> permisos) { }

    public void IniciarSesion(Usuario usuario)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public void CerrarSesion()
        => throw new NotSupportedException("No usado en este banco de pruebas.");
}

/// <summary>IInfoApp solo expone Version; ShellMainViewModel la usa para VersionTexto.</summary>
internal sealed class InfoAppFake : IInfoApp
{
    public InfoAppFake(string version = "0.0.0") => Version = version;

    public string Version { get; }
}

/// <summary>
/// Promovido desde las dos copias privadas identicas de InicioPanelTareasTests.cs:64-72 e
/// InicioViewTests.cs:67-75.
/// </summary>
internal sealed class AuthServiceFake : IAuthService
{
    public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task LogoutAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync()
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
}
```

Ajustá los `using` si algún tipo (`UsuarioSesion`, `Usuario`, `LoginResult`) vive en otro namespace: copiá los que ya usa `GastosViewTests.cs`.

- [ ] **Step 4: Correr para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~SesionFakesTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Dar permisos configurables a `TareaSessionFake`**

En `tests/StockApp.Presentation.UiTests/TareaFakes.cs`, reemplazar `TareaSessionFake` (líneas 96-108) por:

```csharp
internal sealed class TareaSessionFake : ICurrentSession
{
    private readonly IReadOnlySet<string> _permisos;

    public TareaSessionFake(RolUsuario rol) : this(rol, Array.Empty<string>()) { }

    /// <summary>
    /// Overload con permisos explicitos. Sin esto, PermisosActuales devolvia SIEMPRE un set
    /// vacio y la unica forma de que un gate diera true era montar con Admin — que cortocircuita
    /// el chequeo y deja el test verde sin probar el gate.
    /// </summary>
    public TareaSessionFake(RolUsuario rol, params string[] permisos)
    {
        RolActual = rol;
        _permisos = new HashSet<string>(permisos);
    }

    public bool EstaAutenticado => true;
    public StockApp.Application.Auth.UsuarioSesion? UsuarioActual
        => new(1, "prueba", RolActual!.Value, "Usuario de prueba");
    public RolUsuario? RolActual { get; }
    public IReadOnlySet<string> PermisosActuales => _permisos;
    public void EstablecerPermisos(IReadOnlySet<string> permisos) { }

    public void IniciarSesion(Usuario usuario) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public void CerrarSesion() => throw new NotSupportedException("No usado en este banco de pruebas.");
}
```

**Atención:** `UsuarioActual` pasa de `null` a un usuario real. Los 4 archivos que usan este fake (`DocumentoListViewTests`, `TareaListViewTests`, `TareaFormViewTests`, `InicioPanelTareasTests`) podrían depender del `null`. Corré esos 4 archivos ahora:

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TareaListViewTests|FullyQualifiedName~TareaFormViewTests|FullyQualifiedName~InicioPanelTareasTests|FullyQualifiedName~DocumentoListViewTests"`
Expected: PASS. Si alguno se rompe por el `UsuarioActual` no nulo, dejá el `null` como default del ctor de un solo argumento y devolvé el usuario real solo en el overload con permisos.

- [ ] **Step 6: Borrar las dos copias privadas de `AuthServiceFake`**

Borrar el `private sealed class AuthServiceFake` de `InicioPanelTareasTests.cs:64-72` y de `InicioViewTests.cs:67-75`. Ambos archivos pasan a resolver el `internal sealed class AuthServiceFake` de `SesionFakes.cs` sin cambiar una sola línea de uso.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
Expected: PASS.

### Task 4.2: Los 24 gates de botón del sidebar

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs`

**Interfaces:**
- Consumes: `SesionFake`, `InfoAppFake`, `AuthServiceFake` (Task 4.1); `NavigationServiceFake` y `ConfirmacionServiceFake` (ya existen como `internal` en `MovimientoRegistroFakes.cs:57-109`); `ArbolVisual.EsVisibleEnArbol` (Task 0.5).
- Produces: la red que hace segura la tanda 5.

**Alcance honesto de esta tarea:** cubre los **24 botones** gateados, que son los que tienen un `Command` con el cual localizarlos. Los **7 headers de sección** (`TextBlock` con `IsVisible` propio, sin `Command`) **no** se cubren acá: quedan cubiertos en la Task 5.3, donde el header pasa a ser la cabecera del `GrupoNavegacion` y gana su propio gate testeable. No finjas que esta tarea los cubre.

**Los 20 botones y su gate:**

| Comando | Gate |
|---|---|
| `NavInicioCommand` | ninguno (siempre visible) |
| `NavProductosCommand` | `PuedeGestionarProductos` |
| `NavRegistrarEntradaCommand` | `PuedeRegistrarEntradaSalida` |
| `NavIngresoPorFacturaCommand` | `PuedeIngresarPorFactura` |
| `NavRegistrarSalidaCommand` | `PuedeRegistrarEntradaSalida` |
| `NavHistorialCommand` | `PuedeRegistrarMovimientos` |
| `NavTareasCommand` | `PuedeGestionarTareas` |
| `NavDocumentosCommand` | `PuedeGestionarDocumentos` |
| `NavGastosCommand` | `PuedeVerFinanzas` |
| `NavIngresosCommand` | `PuedeVerFinanzas` |
| `NavMaestrosFinanzasCommand` | `PuedeGestionarMaestrosFinanzas` |
| `NavLibroCajaCommand` | `PuedeVerFinanzas` |
| `NavControlPoaCommand` | `PuedeVerFinanzas` |
| `NavCalendarioPagosCommand` | `PuedeVerFinanzas` |
| `NavImportarPlanillasCommand` | `EsAdmin` |
| `NavCategoriasCommand` | `PuedeGestionarTablasMaestras` |
| `NavProveedoresCommand` | `PuedeGestionarTablasMaestras` |
| `NavUnidadesMedidaCommand` | `PuedeGestionarTablasMaestras` |
| `NavValorizacionCommand` | `PuedeVerReportes` |
| `NavStockPorCategoriaCommand` | `PuedeVerReportes` |
| `NavHistorialPorProductoCommand` | `PuedeVerHistorialPorProducto` |
| `NavProductosMasMovidosCommand` | `PuedeVerReportes` |
| `NavLogAuditoriaCommand` | `PuedeVerReportes` |
| `NavMantenimientoCommand` | `EsAdmin` |
| `NavUsuariosCommand` | `EsAdmin` |

**Los nombres exactos de los comandos hay que confirmarlos** contra `ShellMainViewModel.cs` antes de escribir: la tabla sale de los `Command="{Binding NavXxxCommand}"` de `ShellMainView.axaml`, pero el generador de CommunityToolkit deriva el nombre del método (`NavInicio()` → `NavInicioCommand`). Abrí el archivo y verificá cada uno. Si alguno no coincide, corregí la tabla en este plan antes de seguir.

**Los tres gates compuestos, que son donde vive el riesgo real:**

- `PuedeIngresarPorFactura` exige **cuatro** permisos simultáneos: `RegistrarMovimientos` + `RegistrarGastos` + `VerFinanzas` + `GestionarProductos`.
- `PuedeRegistrarEntradaSalida` exige `RegistrarMovimientos` + `GestionarProductos`.
- `PuedeVerHistorialPorProducto` exige `VerReportes` + `RegistrarMovimientos`.

Un test que solo pruebe "con el permiso obvio se ve" deja pasar el bug de coherencia que ya ocurrió dos veces en este repo (fixes de 2026-08-15 y 2026-08-16). Hay que probar el caso **parcial**: con uno solo de los permisos requeridos, el botón sigue oculto.

- [ ] **Step 1: Escribir los tests**

Crear `tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad de los gates de permiso del sidebar. Hasta esta tanda, ShellMainView.axaml
/// tenia 31 IsVisible gateados y CERO tests de UI — y es justo la vista que el refactor reescribe
/// entera.
///
/// Los 52 tests de ShellMainViewModelTests NO cubren esto: son de ViewModel. Si alguien borra un
/// IsVisible del XAML, siguen todos en verde y el boton se le muestra a quien no debe.
///
/// Localizan por identidad del Command a proposito: ShellMainView.axaml no tiene un solo x:Name,
/// y la tanda 5 colapsa los 26 bloques a un ItemsControl — cualquier nombre desapareceria ahi.
/// El RelayCommand generado es el mismo objeto antes y despues del rediseno.
/// </summary>
public class ShellMainViewGatesTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=StockApp.Presentation"
                Width="1000" Height="800">
            <vistas:ShellMainView />
        </Window>
        """;

    private static (Window Window, ShellMainViewModel Vm) Montar(RolUsuario rol, params string[] permisos)
    {
        var vm = new ShellMainViewModel(
            new SesionFake(rol, permisos),
            new NavigationServiceFake(),
            new InfoAppFake(),
            new ConfirmacionServiceFake(),
            new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    /// <summary>
    /// Identidad estable: el RelayCommand generado por [RelayCommand] es el mismo objeto en el VM
    /// y en el Button del arbol. Sobrevive al rediseno del sidebar.
    /// </summary>
    private static bool EsVisible(Window window, object comando)
    {
        var boton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ReferenceEquals(b.Command, comando));

        return boton is not null && ArbolVisual.EsVisibleEnArbol(boton);
    }

    // ── Caso base: sin permisos, no se ve nada salvo Inicio ─────────────────────

    [AvaloniaFact]
    public void OperadorSinPermisos_SoloVeInicio()
    {
        var (window, vm) = Montar(RolUsuario.Operador);

        Assert.True(EsVisible(window, vm.NavInicioCommand), "Inicio no tiene gate: siempre visible.");

        Assert.False(EsVisible(window, vm.NavProductosCommand));
        Assert.False(EsVisible(window, vm.NavHistorialCommand));
        Assert.False(EsVisible(window, vm.NavGastosCommand));
        Assert.False(EsVisible(window, vm.NavCategoriasCommand));
        Assert.False(EsVisible(window, vm.NavValorizacionCommand));
        Assert.False(EsVisible(window, vm.NavMantenimientoCommand));
        Assert.False(EsVisible(window, vm.NavUsuariosCommand));
    }

    // ── Gates simples ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void OperadorConGestionarProductos_VeProductosYNadaMas()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarProductos);

        Assert.True(EsVisible(window, vm.NavProductosCommand));
        Assert.False(EsVisible(window, vm.NavGastosCommand));
        Assert.False(EsVisible(window, vm.NavCategoriasCommand));
    }

    [AvaloniaFact]
    public void OperadorConVerFinanzas_VeLosCincoDeFinanzasPeroNoMaestros()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.VerFinanzas);

        Assert.True(EsVisible(window, vm.NavGastosCommand));
        Assert.True(EsVisible(window, vm.NavIngresosCommand));
        Assert.True(EsVisible(window, vm.NavLibroCajaCommand));
        Assert.True(EsVisible(window, vm.NavControlPoaCommand));
        Assert.True(EsVisible(window, vm.NavCalendarioPagosCommand));

        // Maestros de finanzas pide su propio permiso, no alcanza con VerFinanzas.
        Assert.False(EsVisible(window, vm.NavMaestrosFinanzasCommand));
    }

    [AvaloniaFact]
    public void OperadorConGestionarTablasMaestras_VeLosTresCatalogos()
    {
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarTablasMaestras);

        Assert.True(EsVisible(window, vm.NavCategoriasCommand));
        Assert.True(EsVisible(window, vm.NavProveedoresCommand));
        Assert.True(EsVisible(window, vm.NavUnidadesMedidaCommand));
    }

    // ── Gates COMPUESTOS: aca vive el riesgo ────────────────────────────────────

    [AvaloniaFact]
    public void OperadorConSoloRegistrarMovimientos_NO_VeRegistrarEntrada()
    {
        // Bug de coherencia real, arreglado el 2026-08-16: Registrar Entrada exige
        // RegistrarMovimientos + GestionarProductos, porque el combo de producto pide
        // GestionarProductos del lado del servidor. Con un solo permiso, la pantalla se abria
        // y despues fallaba.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.RegistrarMovimientos);

        Assert.False(EsVisible(window, vm.NavRegistrarEntradaCommand));
        Assert.False(EsVisible(window, vm.NavRegistrarSalidaCommand));

        // El historial si, ese pide solo RegistrarMovimientos.
        Assert.True(EsVisible(window, vm.NavHistorialCommand));
    }

    [AvaloniaFact]
    public void OperadorConLosDosPermisos_VeRegistrarEntradaYSalida()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador, Permisos.RegistrarMovimientos, Permisos.GestionarProductos);

        Assert.True(EsVisible(window, vm.NavRegistrarEntradaCommand));
        Assert.True(EsVisible(window, vm.NavRegistrarSalidaCommand));
    }

    [AvaloniaFact]
    public void OperadorConTresDeLosCuatroPermisos_NO_VeIngresoPorFactura()
    {
        // Bug de coherencia real, arreglado el 2026-08-15: exige CUATRO permisos simultaneos.
        var (window, vm) = Montar(
            RolUsuario.Operador,
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos, Permisos.VerFinanzas);

        Assert.False(EsVisible(window, vm.NavIngresoPorFacturaCommand));
    }

    [AvaloniaFact]
    public void OperadorConLosCuatroPermisos_VeIngresoPorFactura()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador,
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos, Permisos.VerFinanzas,
            Permisos.GestionarProductos);

        Assert.True(EsVisible(window, vm.NavIngresoPorFacturaCommand));
    }

    [AvaloniaFact]
    public void OperadorConSoloVerReportes_VeLosReportesPeroNO_HistorialPorProducto()
    {
        // Historial por producto exige VerReportes + RegistrarMovimientos.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.VerReportes);

        Assert.True(EsVisible(window, vm.NavValorizacionCommand));
        Assert.True(EsVisible(window, vm.NavStockPorCategoriaCommand));
        Assert.True(EsVisible(window, vm.NavProductosMasMovidosCommand));
        Assert.True(EsVisible(window, vm.NavLogAuditoriaCommand));

        Assert.False(EsVisible(window, vm.NavHistorialPorProductoCommand));
    }

    // ── Lo estructural: no se puede simular con permisos ────────────────────────

    [AvaloniaFact]
    public void OperadorConTODOSLosPermisos_SIGUE_SinVerLoAdminOnly()
    {
        // ESTE es el test mas importante del archivo. Importacion, Mantenimiento y Usuarios van
        // por EsAdmin, y AuthorizationService.PermisosEstructuralesAdmin corta ANTES de mirar
        // PermisosActuales: un Operador no llega ahi ni con el permiso en la lista.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.Todos.ToArray());

        Assert.False(EsVisible(window, vm.NavImportarPlanillasCommand));
        Assert.False(EsVisible(window, vm.NavMantenimientoCommand));
        Assert.False(EsVisible(window, vm.NavUsuariosCommand));
    }

    [AvaloniaFact]
    public void Admin_VeTodo()
    {
        var (window, vm) = Montar(RolUsuario.Admin);

        Assert.True(EsVisible(window, vm.NavInicioCommand));
        Assert.True(EsVisible(window, vm.NavProductosCommand));
        Assert.True(EsVisible(window, vm.NavIngresoPorFacturaCommand));
        Assert.True(EsVisible(window, vm.NavHistorialPorProductoCommand));
        Assert.True(EsVisible(window, vm.NavMaestrosFinanzasCommand));
        Assert.True(EsVisible(window, vm.NavImportarPlanillasCommand));
        Assert.True(EsVisible(window, vm.NavMantenimientoCommand));
        Assert.True(EsVisible(window, vm.NavUsuariosCommand));
    }
}
```

- [ ] **Step 2: Correr y arreglar los nombres de comando**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewGatesTests"`

Lo más probable es que falle a compilar por nombres de comando que no existen. Abrí `ShellMainViewModel.cs`, buscá cada `[RelayCommand]` y corregí. **Corregí también la tabla de este plan** para que la Task 5.3 tenga los nombres buenos.

Expected una vez que compile: PASS, 10 tests. Deberían pasar sin tocar producción — los gates ya funcionan, lo que faltaba era la red.

- [ ] **Step 3: Validar por mutación — el paso que hace que esto valga algo**

Un test de gate que nunca se vio fallar no es un guardián. Probá **tres** mutaciones, una por vez, revirtiendo entre cada una:

**Mutación A — borrar un gate.** En `ShellMainView.axaml:69`, quitá `IsVisible="{Binding PuedeGestionarProductos}"` del botón de Productos.
Run: `--filter "FullyQualifiedName~OperadorSinPermisos_SoloVeInicio"` → Expected **FAIL**.

**Mutación B — el typo silencioso.** Restaurá el gate pero escribilo mal: `IsVisible="{Binding PuedeGestionarProducto}"` (sin la `s`). Este es el modo de falla crítico de Avalonia: el binding evalúa a `null` e `IsVisible` queda en su default `true`.
Run: `--filter "FullyQualifiedName~OperadorSinPermisos_SoloVeInicio"` → Expected **FAIL**.

Si esta mutación NO hace fallar el test, pará todo: el test no custodia nada contra el modo de falla que más importa.

**Mutación C — aflojar un gate compuesto.** En `ShellMainViewModel.cs:123-126`, cambiá `PuedeRegistrarEntradaSalida` a solo `_session.PermisosActuales.Contains(Permisos.RegistrarMovimientos)`, sacando el `&&`.
Run: `--filter "FullyQualifiedName~OperadorConSoloRegistrarMovimientos"` → Expected **FAIL**.

Revertí las tres. Corré todo de nuevo: Expected PASS.

### Task 4.3: Los 5 gates de `DocumentoListView`, con permisos mixtos

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/DocumentoListViewTests.cs`

**Interfaces:**
- Consumes: `TareaSessionFake` con permisos (Task 4.1), `ArbolVisual.EsVisibleEnArbol`.
- Produces: cobertura real de los 5 gates por fila.

**El problema:** los tres montajes con `rol: RolUsuario.Admin` (`:150`, `:167`, `:181`) están verdes sin probar nada. Admin sale en `AuthorizationService.cs:65-66` antes de mirar permisos.

**Cuidado con el diagnóstico:** las propiedades `PuedeIniciar`, `PuedeVolverAPendiente`, `PuedeFinalizar`, `PuedeAnular` y `PuedeReabrir` están en el **item de la fila**, no en el ViewModel de la lista, y probablemente combinan **estado del documento** (pendiente/iniciado/finalizado/anulado) **con permiso**. Antes de escribir un solo test, abrí el ViewModel de fila y anotá la fórmula exacta de cada una. Un test que cambie solo el permiso y deje el estado fijo puede dar verde por el estado, no por el permiso.

- [ ] **Step 1: Anotar las cinco fórmulas**

Abrí el ViewModel de la fila de `DocumentoListView` y escribí acá, en este plan, la implementación exacta de las 5 propiedades. Sin eso no se puede diseñar la matriz de casos.

- [ ] **Step 2: Escribir la matriz de tests**

Para cada uno de los 5 gates, tres casos:
1. **Estado correcto + permiso presente** → visible.
2. **Estado correcto + permiso ausente** → oculto. *(Este es el que hoy no existe y es el que importa.)*
3. **Estado incorrecto + permiso presente** → oculto. *(Confirma que el gate no es solo de permiso.)*

Todos con `RolUsuario.Operador` y permisos explícitos. **Ninguno con Admin.**

Usá `ArbolVisual.EsVisibleEnArbol` sobre el botón, no `boton.IsVisible`: los botones viven dentro de un `DataTemplate` de fila y su contenedor puede estar oculto.

- [ ] **Step 3: Cambiar los tres montajes con Admin**

Los tests existentes de `:150`, `:167` y `:181` que montan con `rol: RolUsuario.Admin`: si lo que verifican es comportamiento (navegación, filtros), dejalos con Admin — está bien, no están probando gates. Si verifican visibilidad de botones, pasalos a Operador con permisos explícitos.

**No borres un test que verifica comportamiento solo porque monta con Admin.** El problema es usar Admin para probar un gate, no usar Admin.

- [ ] **Step 4: Validar por mutación**

Por cada uno de los 5 gates, borrá su `IsVisible` del XAML (`DocumentoListView.axaml`, líneas 67, 71, 75, 87 y 165) y comprobá que el test del caso 2 se pone rojo. Revertí. Cinco mutaciones, cinco rojos.

### Task 4.4: Los 5 gates de `DocumentoFormView`

**Files:**
- Create: `tests/StockApp.Presentation.UiTests/DocumentoFormViewGatesTests.cs`

**Interfaces:**
- Consumes: `TareaSessionFake` con permisos, `ArbolVisual.EsVisibleEnArbol`, los fakes de `DocumentoFakes.cs`.
- Produces: cobertura de los gates de `DocumentoFormView.axaml`: `PuedeEditar` (:53) y `PuedeIniciar`/`PuedeVolverAPendiente`/`PuedeFinalizar`/`PuedeAnular`/`PuedeReabrir` (:63-67).

Acá los botones **sí tienen `Content` de tipo `string`** (`"Iniciar"`, `"Finalizar"`, `"Anular"`, `"Reabrir"`, `"Guardar cambios"`, `"Volver"`), así que se pueden localizar por texto. Aun así, preferí `ReferenceEquals(b.Command, vm.IniciarCommand)`: hay dos botones `"Volver"` en la vista (líneas 61 y 71) y el texto no desambigua.

- [ ] **Step 1: Aplicar la misma matriz que la Task 4.3**

Misma estructura: estado correcto + permiso, estado correcto sin permiso, estado incorrecto con permiso. Operador con permisos explícitos, nunca Admin.

Además, un test propio para `IsEnabled="{Binding PuedeEditarCampos}"` (líneas 32/36/40/45/49/53): **es un gate de habilitación, no de visibilidad**, y `EsVisibleEnArbol` no lo detecta. Verificalo con `control.IsEnabled`.

- [ ] **Step 2: Validar por mutación los 5 gates + el de `IsEnabled`**

Seis mutaciones, seis rojos.

- [ ] **Step 3: Correr la suite completa y commitear**

Run: `dotnet test StockApp.sln`
Expected: PASS.

```bash
git add -A
git commit -m "test(ui): red de seguridad de los gates de permisos antes de tocar el shell

Tres huecos que ya estaban en main:
- ShellMainView.axaml: 31 IsVisible gateados, cero tests de UI
- DocumentoListView: 5 gates por fila, tests montando SIEMPRE con Admin, que
  cortocircuita el chequeo en AuthorizationService.cs:65 y los dejaba verdes
  sin probar el gate
- DocumentoFormView: 5 gates sin cobertura

- SesionFake/InfoAppFake/AuthServiceFake compartidos; TareaSessionFake gana
  permisos configurables (devolvia SIEMPRE un set vacio)
- Los tests del sidebar localizan por identidad del Command, no por x:Name:
  sobreviven al colapso a ItemsControl de la tanda 5
- Cubre los tres gates compuestos con casos PARCIALES (los dos bugs de
  coherencia de agosto se colaban por ahi)
- Todos validados por mutacion, incluida la del typo silencioso: un
  {Binding PuedeXxx} mal escrito evalua a null e IsVisible queda en true

Los 11 headers de seccion NO quedan cubiertos aca: se cubren en la tanda 5,
cuando pasen a ser cabecera de GrupoNavegacion con gate propio"
```

---

## Tanda 5: Shell

**Objetivo:** el sidebar pasa a pizarra media y los 26 bloques de navegación copiados literalmente colapsan a un `ItemsControl` con grupos colapsables persistidos. Es la tanda que más código borra y la más riesgosa: toca 31 gates de permisos a la vez. Por eso la tanda 4 va antes.

**Corrección de alcance respecto de la spec:** la spec dice que la tanda 5 "persiste el estado de expansión". Eso da por hecho que los grupos ya existen. **No existen.** No hay `IsExpanded` ni concepto de grupo en `ShellMainViewModel.cs` (456 líneas) ni en `ShellViewModel`. Los "grupos" de hoy son 7 `TextBlock` sueltos con `Classes="caption"` y `Opacity="0.6"`, cada uno con su propio `IsVisible`, seguidos de botones hermanos en el mismo `StackPanel`. La tanda 5 **crea la agrupación entera** y además la persiste.

**Un bug preexistente que esta tanda arregla de paso:** el header "Finanzas" (`ShellMainView.axaml:189`) está gateado por `PuedeVerFinanzas`, pero uno de sus seis botones — Maestros de finanzas (`:220`) — va por `PuedeGestionarMaestrosFinanzas`. Un operador con `GestionarMaestrosFinanzas` y sin `VerFinanzas` ve el botón **sin el título de sección**, colgando suelto entre otros grupos. Al modelar el grupo, su visibilidad pasa a ser "algún hijo visible", que es lo correcto y elimina la clase entera de bug.

**Los 8 grupos:**

| Grupo | Ítems | Visible si |
|---|---|---|
| *(ninguno)* | Inicio | siempre — queda fijo arriba, fuera de todo grupo |
| Movimientos | Productos, Registrar Entrada, Ingreso por factura, Registrar Salida, Historial de movimientos | algún hijo visible |
| Tareas | Tareas | `PuedeGestionarTareas` |
| Documentos | Documentos | `PuedeGestionarDocumentos` |
| Finanzas | Gastos y facturas, Ingresos de caja, Maestros de finanzas, Libro caja, Control POA, Calendario de pagos | algún hijo visible |
| Importación | Importar planillas | `EsAdmin` |
| Tablas maestras | Categorías, Proveedores, Unidades de medida | `PuedeGestionarTablasMaestras` |
| Reportes | Valorización de inventario, Stock por categoría, Historial por producto, Productos más movidos, Log de auditoría | algún hijo visible |
| Administración | Mantenimiento, Usuarios | `EsAdmin` |

**Excepción explícita al Global Constraint de copy:** el primer grupo (Productos y los cuatro de movimientos) **hoy no tiene header** — los cinco botones cuelgan sueltos bajo el título "StockApp". Agrupar exige darle un nombre, y se propone **"Movimientos"**. Es texto nuevo de UI, no un cambio de copy existente: ningún literal actual se modifica y ningún test que dependa de un texto se ve afectado. Los otros 7 headers conservan su texto exacto.

### Task 5.1: Servicio de preferencias del sidebar

**Files:**
- Create: `src/StockApp.Presentation/Services/IServicioPreferenciasSidebar.cs`
- Create: `src/StockApp.Presentation/Services/PreferenciasSidebar.cs`
- Create: `src/StockApp.Presentation/Services/ServicioPreferenciasSidebar.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs:~314` — registro DI
- Test: `tests/StockApp.Presentation.Tests/Services/ServicioPreferenciasSidebarTests.cs` (crear)

**Interfaces:**
- Consumes: nada.
- Produces:
  - `record PreferenciasSidebar(IReadOnlyList<string> GruposAbiertos)`
  - `interface IServicioPreferenciasSidebar` con `PreferenciasSidebar? Cargar()` y `void Guardar(PreferenciasSidebar preferencias)`

  La Task 5.2 lo inyecta en `ShellMainViewModel`.

**No se inventa arquitectura:** el desktop ya tiene exactamente este patrón. `ServicioEstadoVentana` (`src/StockApp.Presentation/Services/ServicioEstadoVentana.cs:15`) persiste tamaño y posición de ventana en `%APPDATA%/StockApp/ventana.json` con `System.Text.Json`, ruta inyectable para tests (`internal ServicioEstadoVentana(string rutaArchivo)`, línea 26) y `try/catch` silencioso ante IO. Se clona ese molde con archivo propio.

**Por qué archivo propio y no extender `ventana.json`:** son ciclos de vida distintos. El estado de ventana se guarda al cerrar; las preferencias de sidebar cambian cada vez que el usuario abre o cierra un grupo. Mezclarlos obligaría a reescribir el archivo entero en cada click.

**Por qué no va al servidor:** es preferencia por máquina y por usuario del sistema operativo. Además, el proyecto tiene la restricción dura de que post-instalación no hay control sobre la configuración del servidor.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/Services/ServicioPreferenciasSidebarTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// &lt;summary&gt;
/// Mismo molde que ServicioEstadoVentanaTests: round-trip contra un path temporal, y los dos
/// casos de falla que importan (archivo inexistente, archivo corrupto) tienen que devolver null
/// en vez de tirar — si el sidebar no puede leer sus preferencias, arranca con los grupos
/// cerrados, no revienta la app.
/// &lt;/summary&gt;
public class ServicioPreferenciasSidebarTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"sidebar-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_ruta)) File.Delete(_ruta);
    }

    [Fact]
    public void Guardar_YCargar_DevuelveLosMismosGrupos()
    {
        var servicio = new ServicioPreferenciasSidebar(_ruta);
        servicio.Guardar(new PreferenciasSidebar(new List&lt;string&gt; { "Finanzas", "Reportes" }));

        var leido = new ServicioPreferenciasSidebar(_ruta).Cargar();

        Assert.NotNull(leido);
        Assert.Equal(new[] { "Finanzas", "Reportes" }, leido!.GruposAbiertos);
    }

    [Fact]
    public void Cargar_SinArchivo_DevuelveNullSinTirar()
    {
        Assert.Null(new ServicioPreferenciasSidebar(_ruta).Cargar());
    }

    [Fact]
    public void Cargar_ArchivoCorrupto_DevuelveNullSinTirar()
    {
        File.WriteAllText(_ruta, "{ esto no es json valido ");

        Assert.Null(new ServicioPreferenciasSidebar(_ruta).Cargar());
    }

    [Fact]
    public void Guardar_EnUnaRutaImposible_NoTira()
    {
        // Guardar se llama al abrir o cerrar un grupo: un fallo de IO no puede romper la
        // navegacion del usuario.
        var servicio = new ServicioPreferenciasSidebar(
            Path.Combine(Path.GetTempPath(), "no-existe-y-no-se-puede-crear\0", "x.json"));

        servicio.Guardar(new PreferenciasSidebar(new List&lt;string&gt; { "Finanzas" }));
    }

    [Fact]
    public void Guardar_ListaVacia_SeGuardaYSeLeeComoVacia()
    {
        // Todos los grupos cerrados es un estado legitimo, distinto de "nunca se guardo nada".
        var servicio = new ServicioPreferenciasSidebar(_ruta);
        servicio.Guardar(new PreferenciasSidebar(Array.Empty&lt;string&gt;()));

        var leido = new ServicioPreferenciasSidebar(_ruta).Cargar();

        Assert.NotNull(leido);
        Assert.Empty(leido!.GruposAbiertos);
    }
}
```

- [ ] **Step 2: Correr para verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ServicioPreferenciasSidebarTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Crear el modelo y la interfaz**

Crear `src/StockApp.Presentation/Services/PreferenciasSidebar.cs`:

```csharp
using System.Collections.Generic;

namespace StockApp.Presentation.Services;

/// &lt;summary&gt;
/// Preferencias locales del menu lateral. Se persiste por MAQUINA y por usuario del sistema
/// operativo, no por usuario logueado y no en la base ni en la API.
/// &lt;/summary&gt;
/// &lt;param name="GruposAbiertos"&gt;Nombres de los grupos que quedaron desplegados.&lt;/param&gt;
public record PreferenciasSidebar(IReadOnlyList&lt;string&gt; GruposAbiertos);
```

Crear `src/StockApp.Presentation/Services/IServicioPreferenciasSidebar.cs`:

```csharp
namespace StockApp.Presentation.Services;

/// &lt;summary&gt;
/// Persistencia local de las preferencias del menu lateral. Mismo contrato que
/// IServicioEstadoVentana: Cargar devuelve null si no hay nada guardado o si el archivo esta
/// roto, y Guardar nunca propaga un fallo de IO.
/// &lt;/summary&gt;
public interface IServicioPreferenciasSidebar
{
    /// &lt;summary&gt;Devuelve null si no hay preferencias guardadas o si el archivo no se pudo leer.&lt;/summary&gt;
    PreferenciasSidebar? Cargar();

    /// &lt;summary&gt;Guarda las preferencias. Si falla el IO, no hace nada y no propaga.&lt;/summary&gt;
    void Guardar(PreferenciasSidebar preferencias);
}
```

Crear `src/StockApp.Presentation/Services/ServicioPreferenciasSidebar.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace StockApp.Presentation.Services;

/// &lt;summary&gt;
/// Clon del molde de ServicioEstadoVentana, con archivo propio: sidebar.json en vez de
/// ventana.json. Van separados porque tienen ciclos de vida distintos — el estado de ventana se
/// guarda al cerrar la app, las preferencias de sidebar cada vez que el usuario abre o cierra un
/// grupo. Mezclarlos obligaria a reescribir todo el archivo en cada click.
/// &lt;/summary&gt;
public class ServicioPreferenciasSidebar : IServicioPreferenciasSidebar
{
    private readonly string _rutaArchivo;

    public ServicioPreferenciasSidebar() : this(RutaPorDefecto()) { }

    /// &lt;summary&gt;Ctor con ruta inyectable, para tests contra un path temporal.&lt;/summary&gt;
    internal ServicioPreferenciasSidebar(string rutaArchivo) =&gt; _rutaArchivo = rutaArchivo;

    private static string RutaPorDefecto()
        =&gt; Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockApp",
            "sidebar.json");

    public PreferenciasSidebar? Cargar()
    {
        try
        {
            if (!File.Exists(_rutaArchivo)) return null;

            return JsonSerializer.Deserialize&lt;PreferenciasSidebar&gt;(File.ReadAllText(_rutaArchivo));
        }
        catch
        {
            // Archivo corrupto, permisos, disco: el sidebar arranca con los grupos cerrados.
            // No vale la pena romper el arranque de la app por una preferencia cosmetica.
            return null;
        }
    }

    public void Guardar(PreferenciasSidebar preferencias)
    {
        try
        {
            var carpeta = Path.GetDirectoryName(_rutaArchivo);
            if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

            File.WriteAllText(_rutaArchivo, JsonSerializer.Serialize(preferencias));
        }
        catch
        {
            // Guardar se dispara al abrir o cerrar un grupo: un fallo de IO no puede
            // interrumpir la navegacion.
        }
    }
}
```

- [ ] **Step 4: Correr para verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ServicioPreferenciasSidebarTests"`
Expected: PASS, 5 tests.

Si `Guardar_ListaVacia` falla al deserializar, el `record` posicional con `IReadOnlyList<string>` puede necesitar que `System.Text.Json` sepa construirlo: verificá que `PreferenciasSidebar` tenga un solo constructor. Si hace falta, agregá `[JsonConstructor]`.

- [ ] **Step 5: Registrar en DI**

En `src/StockApp.Presentation/App.axaml.cs`, junto a la línea 314 donde está `services.AddSingleton<IServicioEstadoVentana, ServicioEstadoVentana>();`:

```csharp
        services.AddSingleton&lt;IServicioPreferenciasSidebar, ServicioPreferenciasSidebar&gt;();
```

### Task 5.2: Grupos en el ViewModel

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs`
- Create: `src/StockApp.Presentation/ViewModels/ItemNavegacion.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs` (crear)

**Interfaces:**
- Consumes: `IServicioPreferenciasSidebar` (Task 5.1).
- Produces:
  - `ItemNavegacion` con `Titulo` (`string`), `Icono` (`string`), `Comando` (`ICommand`), `Seccion` (`string`), `EsVisible` (`bool`)
  - `GrupoNavegacion : ObservableObject` con `Titulo` (`string`), `Items` (`IReadOnlyList<ItemNavegacion>`), `EstaExpandido` (`bool`, observable), `EsVisible` (`bool` — true si algún ítem visible)
  - `ShellMainViewModel.Grupos` (`IReadOnlyList<GrupoNavegacion>`), `ShellMainViewModel.ItemInicio` (`ItemNavegacion`), `ShellMainViewModel.AlternarGrupoCommand`

  La Task 5.3 los bindea.

**El constructor gana un sexto parámetro.** `ShellMainViewModelTests.cs` tiene 52 tests que construyen la VM con cinco (helper `Crear`, líneas 20-35, con Moq). Todos van a romper a compilar. **Eso es esperado y correcto:** actualizá el helper `Crear` agregando `Mock.Of<IServicioPreferenciasSidebar>()`, y los 52 vuelven a verde sin tocar sus asserts. Si algún assert hay que cambiar, pará y entendé por qué.

- [ ] **Step 1: Escribir los tests**

Crear `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs` cubriendo:

1. **`Grupos_ParaUnAdmin_TieneLosOchoGrupos`** — los 8 de la tabla, en orden.
2. **`Grupos_ParaUnOperadorSinPermisos_NingunoEsVisible`** — todos con `EsVisible == false`.
3. **`Grupo_EsVisible_SiAlgunItemEsVisible`** — Operador con solo `GestionarMaestrosFinanzas`: el grupo Finanzas es visible **y** solo su ítem Maestros lo es. *Este test custodia el bug preexistente que la tanda arregla.*
4. **`Grupos_ConPreferenciasGuardadas_RestauraLosAbiertos`** — el fake de preferencias devuelve `["Finanzas"]`; solo ese grupo arranca con `EstaExpandido == true`.
5. **`Grupos_SinPreferenciasGuardadas_ArrancanTodosCerrados`** — `Cargar()` devuelve `null`, ninguno expandido.
6. **`AlternarGrupo_GuardaLaPreferencia`** — ejecutar el comando sobre un grupo llama a `Guardar` con ese grupo en la lista.
7. **`AlternarGrupo_DosVeces_LoSacaDeLaPreferencia`** — vuelve a guardar sin él.
8. **`Navegar_AUnaSeccion_AutoabreSuGrupo`** — ejecutar `NavGastosCommand` deja el grupo Finanzas expandido aunque estuviera cerrado. *(Requisito explícito de la spec: "el grupo que contiene la sección activa se abre solo".)*
9. **`AbrirUnGrupo_NoCierraLosOtros`** — varios abiertos a la vez. *(Requisito explícito de la spec.)*
10. **`Grupos_ConPreferenciaDeUnGrupoQueYaNoExiste_NoTira`** — el JSON tiene `"GrupoViejo"`: se ignora sin romper. *(Pasa si se renombra un grupo entre versiones.)*

Para el fake de preferencias, en `StockApp.Presentation.Tests` usá `Mock<IServicioPreferenciasSidebar>` (ese proyecto sí tiene Moq).

- [ ] **Step 2: Correr para verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ShellMainViewModelGruposTests"`
Expected: FAIL de compilación.

- [ ] **Step 3: Crear los modelos**

Crear `src/StockApp.Presentation/ViewModels/ItemNavegacion.cs`:

```csharp
using System.Windows.Input;

namespace StockApp.Presentation.ViewModels;

/// &lt;summary&gt;
/// Un item del menu lateral. Reemplaza los 26 bloques de XAML copiados literalmente en
/// ShellMainView.axaml.
/// &lt;/summary&gt;
/// &lt;param name="Titulo"&gt;Texto visible. Se conserva EXACTO respecto del XAML actual.&lt;/param&gt;
/// &lt;param name="Icono"&gt;Valor para i:Icon, con prefijo mdi (ej. "mdi-package-variant").&lt;/param&gt;
/// &lt;param name="Comando"&gt;El RelayCommand de navegacion del ShellMainViewModel.&lt;/param&gt;
/// &lt;param name="Seccion"&gt;Clave que se compara contra SeccionActiva para marcar el item activo.&lt;/param&gt;
/// &lt;param name="EsVisible"&gt;Resultado del gate de permiso, evaluado al construir el menu.&lt;/param&gt;
public record ItemNavegacion(
    string Titulo,
    string Icono,
    ICommand Comando,
    string Seccion,
    bool EsVisible);
```

Crear `src/StockApp.Presentation/ViewModels/GrupoNavegacion.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels;

/// &lt;summary&gt;
/// Grupo colapsable del menu lateral. Hasta la tanda 5 los "grupos" eran 7 TextBlock sueltos con
/// su propio IsVisible, seguidos de botones hermanos en el mismo StackPanel.
///
/// EsVisible es "algun hijo visible", NO un permiso propio. Eso arregla un bug preexistente: el
/// header "Finanzas" estaba gateado por PuedeVerFinanzas mientras su item "Maestros de finanzas"
/// va por PuedeGestionarMaestrosFinanzas, asi que un operador con ese permiso y sin VerFinanzas
/// veia el boton sin el titulo de seccion, colgando suelto.
/// &lt;/summary&gt;
public partial class GrupoNavegacion : ObservableObject
{
    public GrupoNavegacion(string titulo, IReadOnlyList&lt;ItemNavegacion&gt; items)
    {
        Titulo = titulo;
        Items = items;
        ItemsVisibles = items.Where(i =&gt; i.EsVisible).ToList();
    }

    public string Titulo { get; }

    public IReadOnlyList&lt;ItemNavegacion&gt; Items { get; }

    /// &lt;summary&gt;Solo los items que el usuario puede ver. Es lo que se bindea al ItemsControl.&lt;/summary&gt;
    public IReadOnlyList&lt;ItemNavegacion&gt; ItemsVisibles { get; }

    public bool EsVisible =&gt; ItemsVisibles.Count &gt; 0;

    [ObservableProperty]
    private bool _estaExpandido;
}
```

- [ ] **Step 4: Armar los grupos en `ShellMainViewModel`**

En `ShellMainViewModel.cs`:

1. Agregar `IServicioPreferenciasSidebar _preferencias` como sexto parámetro del constructor (líneas 157-172) y su campo.
2. Construir `Grupos` e `ItemInicio` al final del constructor, con las propiedades `Puede*` existentes como `EsVisible` de cada ítem. **No dupliques la lógica de permisos: reusá las propiedades que ya están** (`PuedeGestionarProductos`, etc., líneas 61-143).
3. Los títulos e íconos salen **literales del XAML actual** — copiá cada `Text=` y cada `Value="mdi-..."` de `ShellMainView.axaml`. No inventes ni "mejores" ninguno.
4. Restaurar la expansión desde `_preferencias.Cargar()`; si devuelve `null`, todos cerrados. Ignorar nombres de grupo desconocidos.
5. Agregar `[RelayCommand] private void AlternarGrupo(GrupoNavegacion grupo)`: invierte `EstaExpandido` y llama a `_preferencias.Guardar(...)` con los títulos de los expandidos.
6. En cada `NavXxx()` existente, después de `SeccionActiva = "..."`, expandir el grupo que contiene esa sección. Hacelo en un helper privado llamado desde cada uno, o mejor: en el setter parcial `OnSeccionActivaChanged` que CommunityToolkit genera para `[ObservableProperty] private string? _seccionActiva` (línea 244). La segunda opción es una sola implementación en vez de 24 llamadas.

- [ ] **Step 5: Arreglar el helper `Crear` de los 52 tests existentes**

En `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs:20-35`, agregar `Mock.Of<IServicioPreferenciasSidebar>()` como sexto argumento. Igual en el test suelto de las líneas 79-91.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ShellMainViewModelTests"`
Expected: PASS, 52 tests, **sin haber cambiado un solo assert.**

- [ ] **Step 6: Correr los tests nuevos**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ShellMainViewModelGruposTests"`
Expected: PASS, 10 tests.

### Task 5.3: Reescribir `ShellMainView.axaml`

**Files:**
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml` (450 líneas → estimado ~120)
- Modify: `tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs` — ampliar con los 7 gates de grupo

**Interfaces:**
- Consumes: `Grupos`, `ItemInicio`, `AlternarGrupoCommand` (Task 5.2); paleta de sidebar (tanda 1).
- Produces: el shell definitivo. Las tandas 6-13 no lo tocan.

**El momento de la verdad:** los 10 tests de `ShellMainViewGatesTests` (Task 4.2) localizan por identidad del `Command` y **tienen que seguir en verde sin tocar una línea**. Si hay que modificarlos para que pasen, el rediseño perdió un gate. Esa es la única razón por la que la tanda 4 va antes que ésta.

- [ ] **Step 1: Correr la red de seguridad ANTES de tocar el XAML**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewGatesTests"`
Expected: PASS, 10 tests. Anotá el número. Es la línea base.

- [ ] **Step 2: Reescribir el sidebar**

Reemplazar el contenido del `<Border Classes="sidebar">` (líneas 16-~440) por:

```xml
        &lt;Border Grid.Column="0"
                Classes="sidebar"
                Background="{DynamicResource SidebarBrush}"
                Padding="{DynamicResource Espacio2}"&gt;
            &lt;DockPanel&gt;

                &lt;TextBlock Text="{Binding VersionTexto}"
                           Classes="caption"
                           Foreground="{DynamicResource TextoTerciarioBrush}"
                           DockPanel.Dock="Bottom"
                           HorizontalAlignment="Center"
                           Margin="0,8,0,4" /&gt;

                &lt;Button Command="{Binding CerrarSesionCommand}"
                        Classes="ghost"
                        DockPanel.Dock="Bottom"
                        HorizontalAlignment="Stretch"
                        Margin="0,0,0,4"&gt;
                    &lt;Grid ColumnDefinitions="Auto,*"&gt;
                        &lt;i:Icon Grid.Column="0" Value="mdi-logout" Foreground="{DynamicResource SidebarTextoBrush}" /&gt;
                        &lt;TextBlock Grid.Column="1" Text="Cerrar sesión" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" /&gt;
                    &lt;/Grid&gt;
                &lt;/Button&gt;

                &lt;ScrollViewer VerticalScrollBarVisibility="Auto"&gt;
                    &lt;StackPanel Spacing="{DynamicResource Espacio1}"&gt;

                        &lt;TextBlock Text="StockApp"
                                   Classes="titulo-vista"
                                   Foreground="{DynamicResource SidebarTextoBrush}"
                                   Margin="8,8,8,16" /&gt;

                        &lt;!-- Inicio: fijo arriba, fuera de todo grupo, sin gate --&gt;
                        &lt;Button Command="{Binding ItemInicio.Comando}"
                                Classes="ghost"
                                Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Inicio}"
                                HorizontalAlignment="Stretch"&gt;
                            &lt;Grid ColumnDefinitions="Auto,*"&gt;
                                &lt;i:Icon Grid.Column="0" Value="{Binding ItemInicio.Icono}" Foreground="{DynamicResource SidebarTextoBrush}" /&gt;
                                &lt;TextBlock Grid.Column="1" Text="{Binding ItemInicio.Titulo}" VerticalAlignment="Center"
                                           Margin="10,0,0,0" TextTrimming="CharacterEllipsis" /&gt;
                            &lt;/Grid&gt;
                        &lt;/Button&gt;

                        &lt;!-- Los 8 grupos. Reemplaza los 26 bloques copiados literalmente. --&gt;
                        &lt;ItemsControl ItemsSource="{Binding Grupos}"&gt;
                            &lt;ItemsControl.ItemTemplate&gt;
                                &lt;DataTemplate DataType="vm:GrupoNavegacion"&gt;
                                    &lt;StackPanel IsVisible="{Binding EsVisible}" Margin="0,8,0,0"&gt;

                                        &lt;Button Command="{Binding $parent[ItemsControl].((vm:ShellMainViewModel)DataContext).AlternarGrupoCommand}"
                                                CommandParameter="{Binding}"
                                                Classes="ghost"
                                                HorizontalAlignment="Stretch"&gt;
                                            &lt;Grid ColumnDefinitions="*,Auto"&gt;
                                                &lt;TextBlock Grid.Column="0"
                                                           Text="{Binding Titulo}"
                                                           Classes="micro"
                                                           Foreground="{DynamicResource SidebarTextoBrush}"
                                                           VerticalAlignment="Center" /&gt;
                                                &lt;i:Icon Grid.Column="1"
                                                        Value="{Binding EstaExpandido, Converter={StaticResource IconoChevronConverter}}"
                                                        Foreground="{DynamicResource SidebarTextoBrush}" /&gt;
                                            &lt;/Grid&gt;
                                        &lt;/Button&gt;

                                        &lt;ItemsControl ItemsSource="{Binding ItemsVisibles}"
                                                      IsVisible="{Binding EstaExpandido}"&gt;
                                            &lt;ItemsControl.ItemTemplate&gt;
                                                &lt;DataTemplate DataType="vm:ItemNavegacion"&gt;
                                                    &lt;Button Command="{Binding Comando}"
                                                            Classes="ghost"
                                                            Classes.active="{Binding $parent[ItemsControl].((vm:ShellMainViewModel)DataContext).SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter={Binding Seccion}}"
                                                            HorizontalAlignment="Stretch"&gt;
                                                        &lt;Grid ColumnDefinitions="Auto,*"&gt;
                                                            &lt;i:Icon Grid.Column="0" Value="{Binding Icono}" Foreground="{DynamicResource SidebarTextoBrush}" /&gt;
                                                            &lt;TextBlock Grid.Column="1" Text="{Binding Titulo}" VerticalAlignment="Center"
                                                                       Margin="10,0,0,0" TextTrimming="CharacterEllipsis" /&gt;
                                                        &lt;/Grid&gt;
                                                    &lt;/Button&gt;
                                                &lt;/DataTemplate&gt;
                                            &lt;/ItemsControl.ItemTemplate&gt;
                                        &lt;/ItemsControl&gt;

                                    &lt;/StackPanel&gt;
                                &lt;/DataTemplate&gt;
                            &lt;/ItemsControl.ItemTemplate&gt;
                        &lt;/ItemsControl&gt;

                    &lt;/StackPanel&gt;
                &lt;/ScrollViewer&gt;
            &lt;/DockPanel&gt;
        &lt;/Border&gt;
```

**Tres cosas de este XAML que probablemente necesiten ajuste al escribirlo:**

1. **`ConverterParameter={Binding Seccion}`** — `ConverterParameter` **no acepta un binding** en Avalonia. Hay que resolverlo de otra forma: lo más limpio es que `ItemNavegacion` exponga `EstaActivo` como propiedad calculada y que el `ShellMainViewModel` la actualice al cambiar `SeccionActiva`, bindeando `Classes.active="{Binding EstaActivo}"`. Ajustá el modelo de la Task 5.2 en consecuencia — es un cambio chico y hay que hacerlo.
2. **`IconoChevronConverter`** — no existe. Creá un converter que devuelva `"mdi-chevron-down"` cuando expandido y `"mdi-chevron-right"` cuando no, en `src/StockApp.Presentation/Converters/` (la carpeta ya existe, con converters reutilizables del pulido de grillas). Registralo donde se registran los otros.
3. **`$parent[ItemsControl]`** — dentro del template anidado, `$parent[ItemsControl]` resuelve al `ItemsControl` **más cercano**, que es el interno, no el externo. Usá `$parent[UserControl].((vm:ShellMainViewModel)DataContext)`, que es el patrón que `DocumentoListView.axaml:65` ya usa para este mismo problema.

- [ ] **Step 3: Correr la red de seguridad SIN TOCARLA**

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ShellMainViewGatesTests"`
Expected: PASS, los mismos 10 tests del Step 1, **sin haber editado el archivo de tests**.

**Si alguno falla, el rediseño perdió un gate.** Arreglá el XAML, no el test.

Ojo con un detalle: ahora los ítems de un grupo **colapsado** están dentro de un `ItemsControl` con `IsVisible="False"`. `ArbolVisual.EsVisibleEnArbol` los va a reportar como **no visibles** — correcto, pero significa que los tests que esperan `true` necesitan que el grupo esté expandido. Resolvelo en el helper `Montar` de los tests expandiendo todos los grupos después de construir la VM. **Eso no es aflojar el test:** el test verifica que el ítem *exista y esté permitido*, no que el usuario haya dejado ese grupo abierto. Documentá esa decisión con un comentario en el helper.

- [ ] **Step 4: Ampliar con los 7 gates de grupo**

Agregar a `ShellMainViewGatesTests.cs`:

```csharp
    [AvaloniaFact]
    public void OperadorConSoloGestionarMaestrosFinanzas_VeElGrupoFinanzasCONSuTitulo()
    {
        // Bug preexistente que la tanda 5 arregla: el header "Finanzas" estaba gateado por
        // PuedeVerFinanzas mientras Maestros va por PuedeGestionarMaestrosFinanzas, asi que este
        // operador veia el boton colgando suelto, sin titulo de seccion arriba.
        var (window, vm) = Montar(RolUsuario.Operador, Permisos.GestionarMaestrosFinanzas);

        var grupoFinanzas = vm.Grupos.First(g =&gt; g.Titulo == "Finanzas");

        Assert.True(grupoFinanzas.EsVisible);
        Assert.Single(grupoFinanzas.ItemsVisibles);
        Assert.True(EsVisible(window, vm.NavMaestrosFinanzasCommand));
        Assert.False(EsVisible(window, vm.NavGastosCommand));
    }

    [AvaloniaFact]
    public void OperadorSinPermisos_NingunGrupoSeRenderiza()
    {
        var (_, vm) = Montar(RolUsuario.Operador);

        Assert.All(vm.Grupos, g =&gt; Assert.False(g.EsVisible));
    }

    [AvaloniaFact]
    public void OperadorConTODOSLosPermisos_NO_VeLosGruposAdminOnly()
    {
        var (_, vm) = Montar(RolUsuario.Operador, Permisos.Todos.ToArray());

        Assert.False(vm.Grupos.First(g =&gt; g.Titulo == "Importación").EsVisible);
        Assert.False(vm.Grupos.First(g =&gt; g.Titulo == "Administración").EsVisible);
    }
```

- [ ] **Step 5: Validar por mutación el gate de grupo**

En `GrupoNavegacion.cs`, cambiá `EsVisible` a `=> true`.

Run: `--filter "FullyQualifiedName~OperadorSinPermisos_NingunGrupoSeRenderiza"` → Expected **FAIL**.

Revertí.

- [ ] **Step 6: Suite completa**

Run: `dotnet test StockApp.sln`
Expected: PASS.

`ButtonGhostContrasteTests.cs` mide texto sobre el sidebar y debe seguir verde: blanco sobre `#1E293B` da 15.3:1, mejor que sobre el verde bosque anterior.

- [ ] **Step 7: Verificación orgánica — obligatoria al cierre de esta tanda**

Un test verde no dice si la app se ve bien. Levantá la app real (Postgres en `stockapp-pg`, toolkit en `scripts/gui-verificacion/`) y comprobá a mano:

1. El sidebar se ve pizarra media, no verde.
2. Los 8 grupos abren y cierran con click.
3. Varios grupos pueden estar abiertos a la vez.
4. Cerrar la app y volver a abrirla **conserva** qué grupos estaban abiertos.
5. Navegar a una sección de un grupo cerrado lo **abre solo**.
6. `Inicio` queda fijo arriba, fuera de los grupos.
7. Con el SO en **modo oscuro**, la app sigue clara y legible. *(Esto valida la Task 0.1, que no lleva test.)*
8. Entrar con un usuario Operador de permisos mixtos y confirmar que no aparece nada que no deba.

El punto 8 es el que ningún test reemplaza: es la comprobación de que la red de seguridad midió lo que había que medir.

- [ ] **Step 8: Commit de la tanda 5**

```bash
git add -A
git commit -m "feat(ui): sidebar pizarra media con grupos colapsables persistidos

Las 450 lineas de ShellMainView.axaml, con 26 bloques de navegacion copiados
literalmente, colapsan a un ItemsControl con template.

- 8 grupos colapsables; varios abiertos a la vez; el grupo de la seccion
  activa se autoabre; Inicio queda fijo arriba fuera de todo grupo
- Estado de expansion persistido en %APPDATA%/StockApp/sidebar.json, mismo
  molde que ServicioEstadoVentana (archivo propio: distinto ciclo de vida)
- Paleta pizarra media; el verde de marca pasa de fondo a acento

Arregla un bug preexistente: el header 'Finanzas' iba por PuedeVerFinanzas
mientras su item 'Maestros de finanzas' va por otro permiso, asi que un
operador con ese permiso veia el boton sin titulo de seccion. La visibilidad
del grupo ahora es 'algun hijo visible'.

Los 10 tests de gate de la tanda 4 pasaron sin modificar una linea"
```

---

## Self-Review y cierre de la Fase A

### Cobertura de la spec

| Sección de la spec | Dónde se implementa |
|---|---|
| 2.1 Dirección visual | Tandas 1, 2, 5 (fundación y shell). Su aplicación a las 58 vistas es la Fase B |
| 2.2 Tema fijo claro | Task 0.1 |
| 2.3 Fundación de tokens | Tandas 1.1, 1.2, 1.3 |
| 2.4 Componentes | Tandas 3.1, 3.2, 3.3 |
| 2.5 Sidebar colapsable | Tandas 5.1, 5.2, 5.3 |
| 2.6 Política de tests | Task 0.4 (borrado), Task 0.5 (unificación), y la regla de mutación en Global Constraints |
| 2.7 Huecos de seguridad | Tandas 4.2, 4.3, 4.4 |
| Tandas 6-13 | **Fase B, plan aparte** |

### Correcciones a la spec que este plan incorpora

Cinco cosas que la spec afirmaba y que el relevamiento contra el código desmintió. Quedan asentadas acá para que la Fase B no las herede:

1. **Los tests "C pura" son 2, no 5.** Los otros tres candidatos custodian bugs reales documentados.
2. **`MantenimientoViewTests.cs:378` NO se borra.** Su autor lo validó por mutación; custodia una regresión real de `DockPanel` con `LastChildFill`. En la tanda 11 se adapta, conservando su criterio geométrico.
3. **Los gates del sidebar se reparten en 24 botones y 7 headers**, no en "~26 gates" indiferenciados.
4. **Los grupos colapsables no existen.** La tanda 5 los crea, no solo los persiste.
5. **El seam de validación son dos `Setter`, no uno:** `TextBox` (`Controls.axaml:185`) y `ComboBox` (`:203`).
6. **Los íconos usan `<i:Icon Value="mdi-...">` dentro de un `Grid`, no `i:Attached.Icon`.** El riesgo de los 30 asserts `Content as string` sigue mitigado, pero por otro motivo del que decía la spec: el `Content` ya es un `Grid` hoy.
7. **`smcp` es un riesgo de `FontFeatures` tan real como `tnum`**, y la spec no lo listaba. Se verifica en la Task 0.3.

### Qué queda para la Fase B

Las tandas 6 a 13 — el barrido de las 58 vistas — se planifican **después** de cerrar ésta, no antes. Razón: ese plan se escribe consumiendo componentes que todavía no existen, y planificarlo a ciegas garantiza reescribirlo.

Insumos que la Fase A le deja:
- El veredicto de `tnum` y `smcp` (Task 0.3), que decide cómo se estilan las columnas numéricas y los headers.
- La API real de los 5 componentes, ya con tests.
- El helper `ArbolVisual.EsVisibleEnArbol` y los fakes de sesión con permisos, para los gates de las vistas restantes.
- La decisión sobre `MantenimientoViewTests.cs:378`, que la tanda 11 ejecuta.
