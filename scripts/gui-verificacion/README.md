# Verificación orgánica de la GUI (WSLg)

Procedimiento y herramientas para validar manualmente/asistido la GUI real de
StockApp.Presentation (Avalonia) corriendo sobre WSL2 + WSLg, sin acceso root.

Este directorio existe porque el método se perdió DOS veces por vivir solo en
scratchpads efímeros de sesiones anteriores (`click.sh` y el discovery
original desaparecieron). Está pensado para que alguien sin ningún contexto
previo pueda levantarlo y usarlo.

## Por qué esto hace falta (y por qué NO es un test automatizado más)

StockApp.Presentation es una app de escritorio Avalonia. Los tests unitarios
e Avalonia.Headless cubren lógica y layout, pero no reemplazan mirar la
ventana real: composición visual, foco de campos, diálogos modales, y el
comportamiento real de un login/flujo con datos reales de Postgres. Este
directorio cubre ESE último 10%.

## Requisitos

- WSL2 con WSLg (Ubuntu, `DISPLAY` seteado — probá `echo $DISPLAY`, debería
  dar algo como `:0`).
- `powershell.exe` accesible desde WSL (interop de Windows habilitado por
  defecto en WSL2; probá `powershell.exe -Command "echo hola"`).
- `apt-get` y `dpkg-deb` disponibles (vienen de fábrica en Ubuntu; no hace
  falta ningún paquete adicional instalado a nivel de sistema).
- Python 3 con Pillow (`python3 -c "import PIL"`) — opcional, solo para el
  chequeo automático "no es pantalla negra" en `capturar.sh`. Si no está,
  el script avisa y hay que confirmar a ojo.
- NO hace falta sudo/root para nada de lo de abajo.

## El gotcha central: por qué NO se usa scrot/xwd/import

Las herramientas X11 clásicas de captura (`scrot`, `xwd`, `import`,
cualquier cosa basada en `XGetImage` sobre la ventana o el root window)
devuelven **pantalla negra** para ventanas de WSLg, aunque `xwininfo` diga
que la ventana está `IsViewable` con geometría correcta. La razón: WSLg
compone sus ventanas vía RDP hacia el lado Windows (usa `msrdc.exe`/RAIL
para mostrar cada ventana Linux como una ventana nativa de Windows) — esos
píxeles nunca llegan al root pixmap de X11, que es de donde leen las
herramientas de captura clásicas.

**La única captura que funciona de verdad es desde el lado Windows**, vía
`powershell.exe` + `System.Drawing.Graphics.CopyFromScreen`. Por eso
`capturar.sh` no usa ninguna herramienta X11 para el screenshot — solo para
el click (`xdotool`, que sí funciona bien para mouse).

Tecleo: tampoco se usa `xdotool key`/`type`. En más de una sesión (y de
nuevo en la sesión que armó este toolkit) el protocolo XTEST de xdotool
resultó nada confiable bajo WSLg — teclas que llegan re-mapeadas o
desincronizadas. El *mouse* de xdotool sí es confiable. El *teclado* va
siempre por PowerShell `System.Windows.Forms.SendKeys::SendWait`.

## Los 4 scripts

| Script | Qué hace |
|---|---|
| `setup-toolkit.sh` | Deja `xdotool` funcional (extraído de su `.deb` sin root). Idempotente. |
| `capturar.sh <salida.png> [titulo-ventana]` | Screenshot vía PowerShell. Sin título: escritorio completo (ver advertencia abajo). Con título: recorta solo esa ventana. |
| `click.sh <x> <y> [titulo=StockApp] [boton=1]` | Click en coordenadas medidas sobre una captura de `capturar.sh` **con título** (mismo sistema de coordenadas). Restaura/enfoca la ventana antes de clickear. |
| `escribir.sh "texto" [titulo=StockApp]` | Tipea texto vía SendKeys, un carácter por vez. |

### Uso típico

```bash
# Una sola vez por sesión (o cuando haga falta, es idempotente):
./scripts/gui-verificacion/setup-toolkit.sh

# Capturar la ventana de la app (recomendado: SIEMPRE con título):
./scripts/gui-verificacion/capturar.sh /tmp/shots/01-login.png StockApp
# → mirala con la herramienta Read. Confirmá que se ve la app, no negro.

# Click en un campo (coordenadas medidas sobre esa MISMA imagen):
./scripts/gui-verificacion/click.sh 1079 565 StockApp

# Tipear en el campo recién clickeado:
./scripts/gui-verificacion/escribir.sh "admin" StockApp

# Screenshot de verificación ANTES de avanzar (clave: confirmar antes de
# tipear la contraseña o de submitear un formulario):
./scripts/gui-verificacion/capturar.sh /tmp/shots/02-usuario.png StockApp
```

**Regla de oro: nunca encadenes 3+ acciones sin un screenshot intermedio.**
Un click que falla silenciosamente (cae en el lugar equivocado, o la ventana
estaba minimizada en ese instante) no tira ningún error — el único chequeo
real es mirar la pantalla resultante.

## Levantar la app real

```bash
# API (puerto 5043 por defecto vía appsettings.json — el desktop NO lee
# overrides de entorno para Api:BaseUrl, así que si movés el puerto de la
# API tenés que tocar el appsettings.json del build output del desktop):
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http &

# Desktop:
dotnet run --project src/StockApp.Presentation/StockApp.Presentation.csproj &

# Postgres (si no está corriendo ya):
docker start stockapp-pg   # o el comando de creación si no existe el contenedor
```

Credenciales de desarrollo: **`admin` / `test1234`**. (Si ya tenías `admin` /
`test123` seedeado en tu BD local, seguís pudiendo loguearte con esa contraseña
vieja — el login no revalida la complejidad. Pero un bootstrap nuevo contra una
BD vacía exige el mínimo actual: 8+ caracteres, con letra y número.)

## Calibrar `click.sh` (importante, léelo antes de confiar en clicks a ciegas)

`capturar.sh <salida> <titulo>` recorta la ventana usando `GetWindowRect`
de Win32 — esa imagen incluye la barra de título y el borde que dibuja el
compositor de WSLg. `xdotool mousemove --window <id> X Y` en cambio es
relativo al área de CLIENTE X11 (sin esa barra). La diferencia entre ambos
es un offset fijo (borde izquierdo, borde superior+barra de título) que
**hay que calibrar por sesión** — no asumas que el default sigue sirviendo.

Offset por defecto en este script: `CLICK_OFFSET_X=38 CLICK_OFFSET_Y=59`
(medido empíricamente en la sesión que armó este toolkit — funcionó igual
para la ventana principal y para un diálogo modal chico, así que parece ser
constante de la barra de título/borde del compositor, no del tamaño de la
ventana. Pero es un dato empírico de UNA sesión, no una garantía).

### Receta de calibración (si los clicks no caen donde deberían)

1. `capturar.sh /tmp/cal.png StockApp` y mirá la imagen.
2. Elegí un ítem de navegación grande y fácil de verificar (ej. "Inicio" en
   el sidebar) — medí su coordenada (x,y) en la imagen.
3. `click.sh <x> <y> StockApp` y volvé a capturar. Si navegó a Inicio,
   el offset default sirve. Si no:
4. Probá offsets candidatos con las variables de entorno:
   ```bash
   CLICK_OFFSET_X=<candidato_x> CLICK_OFFSET_Y=<candidato_y> \
     ./click.sh <x> <y> StockApp
   ```
   Un borde típico de WM está entre 0-50px; una barra de título entre
   20-60px. Iterá capturando después de cada intento.
5. Una vez calibrado, exportá las variables para el resto de la sesión
   (`export CLICK_OFFSET_X=... CLICK_OFFSET_Y=...`) en vez de repetirlas en
   cada llamada.

## Otros gotchas documentados (de esta sesión y de anteriores)

- **La ventana puede aparecer MINIMIZADA en el lado Windows** aunque
  `xdotool` la vea "mapeada" en X11 con geometría normal (sentinel Win32
  `Left=-32000`). Pasa sobre todo si hay actividad real y concurrente en la
  máquina Windows (alguien usando la compu para otra cosa mientras corre la
  verificación) — el foreground/focus de StockApp se pierde solo. Por eso
  `capturar.sh` y `click.sh` SIEMPRE restauran (`ShowWindow` SW_RESTORE) y
  traen al frente (`SetForegroundWindow`) la ventana antes de actuar. Si
  igual ves contenido raro en una captura, repetí — probablemente perdió el
  foreground justo en esa ventana de tiempo.

- **Captura de escritorio completo (`capturar.sh` SIN título) puede mostrar
  contenido de OTRAS ventanas/monitores ajeno a la app** (en la sesión que
  armó este toolkit, mostró un juego y YouTube de otro monitor en uso real
  por la persona dueña de la máquina). Usá SIEMPRE un título de ventana a
  menos que estés depurando específicamente "¿dónde está la ventana" — y
  tené presente que sin título podés estar mirando/exponiendo la pantalla
  real de quien esté usando la máquina en ese momento.

- **Diálogos modales de la app** (ej. "Confirmar operación", tipo
  `¿Cerrar la sesión?` con botones "Cancelar"/"Sí, continúa") son ventanas
  X11/Win32 **separadas** de la ventana principal, con su propio título
  (buscalo con `Get-Process | Where MainWindowTitle -like ...` — vas a ver
  un proceso `msrdc` con el título del diálogo). Necesitan su propia
  captura/click con ESE título, no el de la ventana principal. `{ENTER}`/
  `{ESC}` vía SendKeys NO cierra estos diálogos custom de Avalonia —
  usá click real sobre el botón (ver más abajo, detección de color).

- **Underscore (`_`) en SendKeys puede salir como `?`** por un mismatch de
  layout de teclado en algunas sesiones. Si tenés que tipear un `_`,
  probalo y verificalo con captura antes de confiar en él; si falla, evitá
  el carácter en los datos de prueba.

- **Encontrar el botón correcto en un diálogo modal por color, no por ojo**:
  si tenés Python+Pillow, es más confiable escanear el PNG del diálogo
  buscando el color de fill sólido del botón (verde de acento de StockApp,
  aprox. RGB `(21,128,61)`) que estimar el centro a ojo — un bounding box
  demasiado laxo puede mezclar el botón sólido con el outline de un botón
  "Cancelar" vecino y hacer que el click caiga entre los dos, sin tocar
  ninguno.

## Qué SÍ se puede verificar con este método

- Que una pantalla renderiza con el layout esperado (columnas visibles,
  textos correctos, formato de moneda, colores de estado).
- Flujos de navegación completos (login, click en sidebar, abrir un
  formulario, submit, ver el resultado).
- Contenido dinámico real contra datos reales de Postgres (no un mock).
- Diálogos modales y su interacción real con mouse.

## Qué NO se puede verificar (o es frágil) con este método

- **Gestos táctiles/Avalonia `Tapped`/`DoubleTapped` sintéticos vía
  xdotool son poco confiables** — en sesiones anteriores, un doble click
  sintético a veces no disparaba el gesto de Avalonia aunque el mouse
  llegara al lugar correcto. Si un doble click no parece funcionar,
  recalibrá coordenadas antes de asumir que es un bug de la app.
- **Diálogos nativos GTK (selector de archivos)** son otra ventana X11
  más, con otro offset — click con mouse ahí falla más seguido; para esos,
  navegá con teclado (`Ctrl+L`/`Ctrl+F` para la barra de ubicación, tipear
  la ruta de a un segmento por vez, `Return`).
- **No hay cursor visible en las capturas** (`CopyFromScreen` no dibuja el
  puntero), así que no podés confirmar visualmente "dónde quedó el mouse"
  — solo el efecto del click (foco, navegación, cambio de estado).
- **Multi-monitor / máquina en uso concurrente**: si la máquina Windows
  tiene más de un monitor y alguien la está usando para otra cosa al mismo
  tiempo, la ventana puede estar en un monitor distinto al que esperás, o
  perder foreground entre pasos. Esto no es un bug de la app, es una
  limitación del entorno — repetí el paso.
- **No reemplaza tests automatizados.** Es verificación manual/asistida
  para lo que un test headless no cubre bien (composición visual real,
  "se ve bien"), no un sustituto de la suite de tests.
- Esta sesión NO hizo el recorrido visual completo de todas las features
  de la app — solo validó que el toolkit (captura + click + tecleo)
  funciona de punta a punta con un login real. El recorrido completo queda
  para otra tarea.

## Estado al cierre de la sesión que armó esto

`setup-toolkit.sh` extrae `xdotool` a `/tmp/x11tools` (efímero, se pierde
al reiniciar WSL — volvé a correr el script, es rápido e idempotente).
Los screenshots de prueba de esta sesión NO se versionaron (se descartaron
al cerrar). Postgres (`stockapp-pg`) y la API quedan corriendo por
convención del proyecto.
