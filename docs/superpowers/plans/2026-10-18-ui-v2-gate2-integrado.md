# GATE 2 orgánico integrado de las Fases 4e, 5, 6 y 7 (UI v2). Plan de verificación

> **Para el agente que lo ejecuta:** este plan NO cambia código. Maneja la app real con el toolkit de `scripts/gui-verificacion/` en WSLg, contra la base local `stockapp-pg`, y reporta OK / FALLA / NO VERIFICABLE por paso con su captura. Cualquier defecto se ANOTA (no se arregla acá): se arregla después en commits propios con TDD o va a la deuda con el OK de Martin.

**Cuándo:** 2026-10-18 a la mañana, en la PC de Martin, en un bloque de ~2 horas.

**Qué reemplaza:** el Step 3 de la Task 5 de la 4e (`2026-10-17-ui-v2-fase-4e-pagos.md`), el Step 4 de la Task 6 de la Fase 5, el Step 4 de la Task 7 de la Fase 6 y el Step 1 de la Task 11 de la Fase 7. Una sola app, una sola base, un solo backup y una sola limpieza para las cuatro.

**Dónde:** worktree `/home/capua25/workspace/stockapp-worktrees/ui-v2`, rama `feat/ui-v2`. Capturas en `artifacts/ui-v2/gate2/` (ignorado por git). Ledger local en `.superpowers/sdd/2026-10-18-ui-v2-gate2-integrado/progress.md` (ignorado por git): ids creados, rutas de backup, salidas de `curl`, md5 y el reporte.

**Fuentes de cada paso** (no hace falta releerlas: lo que cambia lo que hay que verificar ya está acá):
- 4e: plan Task 5 Step 3 y ledger `.superpowers/sdd/2026-10-17-ui-v2-fase-4e-pagos/progress.md` (GATE 1 con Q-1=A, Q-2=B, Q-3=A, Q-4=B, Q-5=A y los 6 cambios aprobados).
- 5: plan Task 6 Step 4 y ledger del worktree `ui-v2-fase5` (Q-1 a Q-10 = A; GATE 1 con 8 cambios; hallazgo de la selección reubicada en la Task 4).
- 6: plan Task 7 Step 4 y ledger del worktree `ui-v2-fase6` (todas las Q = A; GATE 1 con Historial sin scroll, ACCIÓN legible; review final con Auditoría de Operador, `97f302a`).
- 7: plan Task 11 Step 1 y ledger del worktree `ui-v2-fase7` (todas las Q = A; `7e693ee` orden numérico del código, `f7b7863` contraseñas con ●).

---

## 0. Precondición: la rama tiene que estar integrada (5 min, NO prescindible)

Al escribir este plan (2026-10-17 a la noche), `feat/ui-v2` está en `fc8a53c` y tiene SOLO la 4e: las ramas `feat/ui-v2-fase-5` (`f37c141`), `feat/ui-v2-fase6` (`bcdf5f3`) y `feat/ui-v2-fase-7` (`f7b7863`) todavía NO están integradas, y los worktrees `ui-v2-fase5` y `ui-v2-fase7` tienen cambios sin commitear en tests. El GATE 2 integrado arranca SOLO si la integración ya ocurrió.

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2
git status --short                       # limpio salvo .codegraph/
git log --oneline -1
for s in "tareas en v2 (listado" "documentos en v2" "hub de Reportes y marco común" \
         "Log de auditoría de un Operador con reportes.ver" "catálogos master-detail" \
         "usuarios con panel de permisos de pie fijo" "mantenimiento en v2" \
         "ordenan por número, no por texto" "usan ● como Login"; do
  printf '%-55s ' "$s"; git log --oneline --fixed-strings --grep="$s" feat/ui-v2 | head -1 | grep . || echo "FALTA"
done
grep -n 'PasswordChar' src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml   # tiene que decir "●", no "*"
```

- **OK:** ningún `FALTA`, árbol limpio, `PasswordChar="●"`. Anotar el hash de `HEAD` en el ledger: es el commit que se aprueba.
- **FALLA:** PARAR. No se verifica una rama a medio integrar: avisar al orquestador con la salida.
- Además, la suite tras la integración tiene que estar anotada como verde en el ledger de la integración (Task 8 de la 5 y de la 6, Task 12 de la 7). Si no consta, PARAR: no se corre la suite en este bloque.

---

## Gotchas obligatorios (leer antes de tocar nada)

1. **`capturar.sh` NO es fail-closed del todo:** con WSLg caído captura el escritorio de Windows con exit 0. MIRAR CADA captura con la herramienta `Read` antes de dar un paso por bueno, y comparar `md5sum` con la anterior: dos iguales cuando la pantalla tenía que cambiar = WSLg congelado → PARAR y pedirle a Martin que reinicie WSL. Nunca declarar OK por el exit code.
2. **Título de la ventana `" Municipal"` con un espacio delante** en `capturar.sh`, `click.sh` y `escribir.sh`: `Municipal` a secas matchea las ventanas fantasma `GestionMunicipal` del selector GTK (exit 3).
3. **Popups (calendario, `ComboBox`, `AutoCompleteBox`, tooltips) se capturan con `capturar-x11.py`:** `xdotool search --pid <PID de la app>` → la ventana sin nombre es el popup → `python3 scripts/gui-verificacion/capturar-x11.py <wid> artifacts/ui-v2/gate2/<n>.png`. `capturar.sh` muestra la app SIN el popup. Geometría del popup corrida (-38, -64).
4. **Selector de archivos GTK con ruta ABSOLUTA:** `Ctrl+L` y tipear `/home/capua25/...` completa; una relativa no se resuelve.
5. **Nunca `pkill -f` con un patrón que aparezca en el propio comando** (mata el shell que lo lanza). Matar SIEMPRE por pid exacto: `pgrep -x StockApp.Api` (o `ss -ltnp | grep 5043`) → `kill <pid>`.
6. **Los PDF se verifican por CONTENIDO con pypdf**, no a ojo (no hay visor en WSLg): `python3 -I -c "import pypdf,sys;r=pypdf.PdfReader(sys.argv[1]);print('\n'.join(p.extract_text() for p in r.pages))" <archivo.pdf>`. "Ver" un adjunto = NO VERIFICABLE en WSLg; se verifica que el archivo baja y su contenido con pypdf.
7. **Teclado solo con `escribir.sh` (SendKeys); mouse con `click.sh`.** Evitar `_` en los datos (puede salir `?`). Nunca 3 acciones seguidas sin captura intermedia. Doble click sintético poco confiable: si no dispara, recalibrar antes de declarar FALLA.
8. **Diálogos modales** son ventanas aparte con su título ("Confirmar operación", "Reclasificar…"): capturarlos con ESE título; `{ENTER}`/`{ESC}` por SendKeys no siempre los cierra: click real en el botón (botón primario por color ~RGB(21,128,61) si hace falta).
9. **Serilog en Development no registra requests:** "una sola consulta" o "sin request" NO se verifica por el log de la API; se verifica por el efecto en pantalla, o se marca NO VERIFICABLE.
10. **El token nunca se pega** en el ledger ni en el reporte: va en una variable de shell.
11. **Una app a la vez** (misma ventana, mismo puerto 5043). Nunca contra el VPS: `~/.config/GestionMunicipal/conexion.json` no tiene que existir o tiene que apuntar a `localhost` (hoy NO existe; confirmarlo).

---

## Formato del reporte (por paso)

Una tabla en el ledger y en el mensaje final a Martin:

| Paso | Resultado | Captura(s) | Observación |
|---|---|---|---|
| A4.3 | OK / FALLA / NO VERIFICABLE | `artifacts/ui-v2/gate2/A4-3-error-monto-1280.png` (md5 corto) | qué se vio; si FALLA, qué se esperaba y qué hubo; si NO VERIFICABLE, por qué (entorno) y qué lo custodia (UiTest/comparación) |

- **OK** = se vio exactamente el criterio, con captura mirada.
- **FALLA** = se vio otra cosa; captura + descripción. No se arregla en este bloque.
- **NO VERIFICABLE** = el entorno no lo permite (WSLg, sin visor, sin licencia vencida, falta de tiempo en un paso prescindible). Nunca se usa para tapar una FALLA.
- Pasos prescindibles no ejecutados por tiempo: NO VERIFICABLE con "omitido por tiempo [P]".
- Nombres de captura: `<bloque><paso>-<que>-<1280|1366>.png`.
- Al final: resumen (N OK / N FALLA / N NO VERIFICABLE), lista de FALLAs para decidir con Martin, y el pedido de "aprobado" por fase (4e, 5, 6, 7) para registrar `Fase X: aprobada por Martin 2026-10-18` en cada ledger.

---

## Presupuesto de tiempo (~2 h)

| Bloque | Usuario | Resolución | Tiempo | Prescindibles [P] si no alcanza |
|---|---|---|---|---|
| 0 Precondición | — | — | 5 min | ninguno |
| P Preparación | — | — | 12 min | ninguno |
| A Admin a 1280×760 | admin | 1280×760 | 50 min | A5.6, A6.5, A7.3, A7.4, A3.5 |
| B Admin a 1366×768 + sin servidor | admin | 1366×768 | 18 min | B1.4, B2.3 |
| C Operador mixto | `g2mixto` | 1366×768 | 25 min | C2.3, C6 |
| D Cierre, reporte y limpieza | admin | — | 10 min + la limpieza | ninguno |
| **Total** | | | **~120 min** | ahorro posible con los [P]: ~20 min |

Regla de corte: si al terminar el bloque A pasaron más de 70 min desde el arranque, saltear TODOS los [P] restantes. Los pasos que custodian los ajustes del GATE 1 y de las reviews (marcados ★) NUNCA se saltean.

---

## P. Preparación (12 min)

**P1. Base y backup ANTES de crear nada.**
```bash
docker start stockapp-pg
docker exec stockapp-pg env | grep -E '^POSTGRES_(USER|DB)='        # usuario y base reales (se espera stockapp / stockapp)
mkdir -p ~/stockapp-backups-local
docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-gate2-$(date +%Y%m%d-%H%M%S).sql
ls -l ~/stockapp-backups-local/pre-gate2-*.sql                       # no vacío (~1 MB)
```
Ruta exacta al ledger. Además, anotar con un `SELECT` (solo lectura): el producto de los movimientos 1599-1601 y su stock actual; y que existen los `PRUEBA-4D` (gastos 19-24, pago 13 del gasto 22, usuario 30). Si faltan, PARAR: la 4e se verifica sobre esos datos.

**P2. API y app del worktree integrado.**
```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2
test ! -e ~/.config/GestionMunicipal/conexion.json && echo "sin conexion.json: apunta a localhost por appsettings"
pgrep -x GestionMunicipa; ss -ltnp | grep 5043                       # ambos vacíos; si no, hay otra app/API: PARAR y preguntar
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http > artifacts/ui-v2/gate2/api.log 2>&1 &
dotnet run --project src/StockApp.Presentation > artifacts/ui-v2/gate2/app.log 2>&1 &
```
Anotar los dos pids. `./scripts/gui-verificacion/setup-toolkit.sh`. Login `admin` / `Admin1234`.

**P3. Tamaño de ventana y calibración.** `wid=$(xdotool search --pid <pid app> --name " Municipal" | head -1)`; `xdotool windowsize $wid 1280 760`; `xdotool getwindowgeometry $wid` confirma 1280×760 de cliente. Calibrar `click.sh` con la receta del README (click en "Inicio" del sidebar) y exportar `CLICK_OFFSET_X/Y`. Si la ventana no acepta el tamaño exacto, anotar el real y seguir.

**P4. Token de admin para `curl`** (sin pegarlo):
```bash
TOKEN=$(curl -s -X POST http://localhost:5043/auth/login -H 'Content-Type: application/json' \
  -d '{"nombreUsuario":"admin","contrasena":"Admin1234"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')
```
(Si el campo no se llama `token`, mirar las claves del JSON sin imprimir valores.)

**P5. Archivos de prueba:** un PDF chico con texto conocido en `/home/capua25/stockapp-gate2/PRUEBA-GATE2-comprobante.pdf` (generarlo con pypdf o reusar uno existente) y la carpeta `/home/capua25/stockapp-gate2/descargas/` para lo que se baje.

**Usuarios de prueba (unificados para las 4 fases):** se crean en A2, por la pantalla de Usuarios (así se verifica el alta de la Fase 7):
- `g2mixto` (Operador, contraseña `Gate2Prueba1`, nombre "PRUEBA-GATE2 mixto"): `finanzas.ver`, `finanzas.pagos`, `tareas.gestionar`, `documentos.gestionar`, `reportes.ver`, `movimientos.registrar`, `catalogo.maestras`, `finanzas.maestros`. SIN `catalogo.productos`, SIN `usuarios.gestionar`. Cubre: 4e (pagos y su revocación), 5 (`f5mixto`), 6 (`f6mixto`; con `catalogo.productos` agregado en C5 hace de `f6historial`), 7 (`f7tablas` + `f7finanzas` sumados).
- `g2alta` (Operador, contraseña `Gate2Prueba2`, nombre "PRUEBA-GATE2 alta"): solo para los cambios de rol, contraseña y baja de la pantalla de Usuarios.
- Se descarta: `f4dmixto` (id 30) NO se reusa (se borra en la limpieza).
- Nombres exactos de los permisos: los que muestra el panel de permisos (verificarlos ahí; los de arriba son los del código).

**Datos:** todo lo nuevo lleva `PRUEBA-GATE2` en nombre, título, número, descripción o nota. Cada id se anota en el ledger EN EL MOMENTO (sección "Ids creados", por tabla). Para obtener ids sin buscar en pantalla: `SELECT` de solo lectura por el prefijo.

---

## A. Admin a 1280×760 (50 min)

### A1. Navegación integrada (5 min)

1. ★ **Sidebar.** Captura de la ventana completa en Inicio. **OK:** ANÁLISIS tiene UN solo ítem "Reportes" (sin Valorización, Stock por categoría, Historial por producto, Más movidos, Auditoría ni Tareas sueltos); ADMINISTRACIÓN tiene "Catálogos", "Usuarios", "Mantenimiento" (y "Importar planillas" si corresponde) y NINGUNO de "Categorías", "Proveedores", "Unidades de medida", "Catálogos de tareas", "Maestros de finanzas". GESTIÓN con Tareas y Documentos. Comparar a ojo con `docs/ui-v2/mockups/shell/sidebar-admin.html` (PNG en `artifacts/ui-v2/mockups/`). **FALLA:** cualquier ítem viejo, ítem duplicado o grupo vacío con header. Captura: `A1-1-sidebar-1280.png`.
2. ★ **Sección activa.** Click en Reportes, Catálogos, Tareas, Documentos, Gastos y de vuelta a Inicio, con una captura en cada uno. **OK:** la pastilla marca SOLO el ítem de la pantalla abierta. Además: abrir un reporte desde el hub y un formulario de catálogo desde Catálogos → siguen marcando "Reportes" y "Catálogos" (Q-SECCION de la 6, `Formulario_DeCatalogo_ConservaLaSeccionCatalogos` de la 7). Desde Inicio, el acceso a Valorización (si está) marca "Reportes". **FALLA:** ningún ítem marcado, dos marcados o el ítem de otra sección.
3. ★ **Pulgar del scroll del sidebar.** Con el mouse sobre el sidebar (si el pulgar solo aparece al pasar), capturar. **OK:** a 1280×760 el sidebar de Admin scrollea, el pulgar se ve a la derecha del sidebar (~2 px, gris `#7A7A7A`), su largo es proporcional (referencia del mockup integrado; la Fase 6 midió 421 px antes de integrar la 7: anotar el valor medido en la captura en vez de exigir ese número) y scrolleando hasta abajo se alcanza el último ítem de ADMINISTRACIÓN sin recorte. **FALLA:** sin pulgar con contenido que no entra, último ítem inalcanzable, o pulgar que no llega al final. Capturas: `A1-3-pulgar-arriba-1280.png`, `A1-3-pulgar-abajo-1280.png`.

### A2. Usuarios: alta, contraseña ●, permisos (8 min)

1. ★ **Contraseña con ●.** Usuarios → card "Nuevo usuario": tipear `Gate2Prueba2` en Contraseña. **OK:** se ven círculos `●` (como en Login), no asteriscos ni texto. Captura `A2-1-contrasena-1280.png` (recortar a ojo la zona al mirar).
2. **Alta con error y alta correcta.** Crear `g2alta` (Operador). Volver a crear `g2alta` → `Alerta` de usuario duplicado. Crear `g2mixto`. **OK:** las dos filas nuevas en la grilla (USUARIO, ROL, ESTADO) y el error de duplicado en la `Alerta` de la card. Anotar los dos ids.
3. ★ **Permisos de `g2mixto` y Guardar permisos.** Seleccionar `g2mixto` → detalle con insignia de rol y la sección Permisos; tildar los 8 permisos de arriba; "Guardar permisos". **OK:** "Guardar permisos" está en el pie FIJO de la card (fuera del scroll de permisos), visible sin scrollear también a 1280×760; tras guardar, releer (seleccionar otro y volver) y las casillas quedan como se guardaron. Captura `A2-3-permisos-1280.png`. Verificar con `curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5043/usuarios/<id>/permisos` (pegar la lista, no el token).
4. **Sin selección y Admin.** Sin usuario seleccionado → "Elegí un usuario"; seleccionar `admin` → `Alerta` "Acceso total…" y casillas deshabilitadas. Una captura de cada.

### A3. Catálogos (8 min)

1. **Master.** Catálogos → master con INVENTARIO, FINANZAS y TAREAS y los 10 catálogos; Categorías seleccionado de entrada. Comparar a ojo con `administracion/catalogos.html`. Captura `A3-1-catalogos-1280.png`.
2. ★ **CÓDIGO de Rubros ordena por número.** En Rubros de gasto crear dos rubros `PRUEBA-GATE2 rubro 9` (código `9`) y `PRUEBA-GATE2 rubro 10` (código `10`) — así quedan 9, 10 y el 9940 de PRUEBA-4D. Click en el header CÓDIGO (ascendente y descendente). **OK:** ascendente queda 9 < 10 < 9940 (orden numérico); en texto quedaría 10 < 9 < 9940. CÓDIGO es la PRIMERA columna, antes de NOMBRE. Capturas `A3-2-rubros-asc-1280.png`, `A3-2-rubros-desc-1280.png`. Anotar ids.
3. **Alta, edición y vuelta.** En Proveedores: alta de `PRUEBA-GATE2 proveedor` con los 5 campos (dirección larga, > 60 caracteres) → Guardar → vuelve a Catálogos con PROVEEDORES abierto (no Categorías). Editar y Cancelar → vuelve a Proveedores. Breadcrumb del formulario "Proveedores / …" → vuelve a Proveedores. Un nombre duplicado → `Alerta`. **OK:** las tres vueltas al catálogo de origen. Capturas del formulario y de la vuelta.
4. **Tooltip y baja con búsqueda.** Tooltip de la dirección cortada (`capturar-x11.py`). Buscar `prueba-gate2` (minúsculas, sin tildes) → solo las filas PRUEBA; dar de baja el proveedor con la búsqueda puesta → la fila queda en terciario con "Inactivo" y la búsqueda sigue. Doble click en la fila inactiva → NO edita.
5. [P] **Líneas POA:** alta `PRUEBA-GATE2 línea` con dos asignaciones; EJERCICIO ordena por número. Categoría con productos activos → la baja muestra el aviso (regla `ee461b8`).

### A4. Pagos del gasto (4e) (12 min)

1. **Desde Gastos.** Gastos → gasto 22 (PRUEBA-4D, parcial con el pago 13) → Pagos. **OK:** breadcrumb "Gastos y facturas / Pagos de la factura", resumen con insignia "Parcial", grilla FECHA · MONTO · ESTADO · NOTA · acción; "Registrar pago" DESHABILITADO con Monto vacío. Comparar a ojo con `finanzas/pagos.html`. El primer tramo del breadcrumb vuelve a Gastos. Captura `A4-1-pagos-1280.png`.
2. **Desde Calendario.** Calendario de pagos → "Registrar pago" del gasto 21 → breadcrumb "Calendario de pagos / …"; el primer tramo vuelve al Calendario.
3. ★ **Error por campo de Monto (Q-4 B, texto aprobado).** En el gasto 21, Monto `1.5` y salir del campo. **OK:** el error aparece DEBAJO del campo Monto (no en una `Alerta` bajo la fila) con el texto EXACTO "Usá coma para los decimales: 1,5" (sin prefijo), envuelto en dos líneas dentro de los 160 px del campo; "Registrar pago" sigue deshabilitado y alineado arriba con los inputs (no baja con el error). Vaciar Monto y salir → error de obligatorio. Captura `A4-3-error-monto-1280.png`.
4. **Registrar.** Monto válido (`1500`), nota `PRUEBA-GATE2 pago`, fecha elegida con el calendario (popup con `capturar-x11.py`) en un día distinto de hoy. **OK:** la fila aparece con EL DÍA ELEGIDO (no el anterior), el resumen se actualiza, la selección queda suelta y los errores de campo desaparecen. Anotar id del pago.
5. **Adjuntos del pago.** Seleccionar el pago → la card de adjuntos baja a la vista; Agregar `/home/capua25/stockapp-gate2/PRUEBA-GATE2-comprobante.pdf` (selector GTK, ruta absoluta). "Ver": NO VERIFICABLE (sin visor) salvo que baje un archivo: verificarlo con pypdf. Quitar con confirmación (captura del modal) y volver a agregarlo (queda adjunto para el paso 6).
6. ★ **Pago anulado con adjuntos en solo lectura (Q-2 B).** Anular ese pago (modal; comparar con `pagos-confirmar-anular.html`). **OK:** fila en terciario con insignia "Anulado" y sin "Anular"; seleccionarla → el panel de adjuntos SE MUESTRA con el aviso "Pago anulado: los adjuntos son solo de consulta" entre el encabezado y la lista, con el adjunto listado, "Ver" visible y SIN "Agregar" ni "Quitar". **FALLA:** panel oculto, texto "Este pago está anulado…" en lugar del panel (es el diseño viejo de Q-2 A), o Agregar/Quitar visibles. Captura `A4-6-pago-anulado-1280.png`.
7. ★ **Gasto anulado: saldo "—" (Q-3 A).** Gasto 20 (anulado) → Pagos. **OK:** insignias "Anulado" y "Solo lectura", `Alerta` Info "Este gasto está anulado: se pueden consultar sus pagos, pero no registrar ni anular.", sin card de registro ni "Anular", y SALDO = "—" (no `$ 92.300,00` ni otro importe). Captura `A4-7-gasto-anulado-1280.png`.
8. **Servidor (por la API, sin pegar el token).** Pegar en el ledger el status y el cuerpo de:
   - `curl -s -w ' %{http_code}' -X POST -H "Authorization: Bearer $TOKEN" -F archivo=@/home/capua25/stockapp-gate2/PRUEBA-GATE2-comprobante.pdf http://localhost:5043/finanzas/pagos/<id anulado>/adjuntos` → **409** "No se puede adjuntar a un pago anulado."
   - lo mismo contra `/finanzas/pagos/999999/adjuntos` → **404** "No existe el pago 999999."
   - `curl -s -w ' %{http_code}' -X DELETE -H "Authorization: Bearer $TOKEN" http://localhost:5043/finanzas/adjuntos/<id del adjunto del pago anulado>` → **409** "No se puede quitar un adjunto de un pago anulado."
   (El nombre del campo multipart y la ruta exacta: confirmarlos en `src/StockApp.Api/Endpoints/AdjuntosEndpoints.cs` antes; si difieren, usar los reales y anotarlo.)

### A5. Tareas y Documentos (Fase 5) (10 min)

1. **Tareas.** Tareas → "Cargando tareas…" y la grilla "Abiertas" con insignias (Pendiente neutra, En curso ámbar) y "Vencida" en VENCE. Crear 3 tareas: `PRUEBA-GATE2 tarea vencida` (fecha límite pasada), `PRUEBA-GATE2 tarea en curso`, `PRUEBA-GATE2 tarea clasificada` (clasificación completa). En el alta: tocar Título y salir → error debajo. Anotar ids. Comparar a ojo con `gestion/tareas.html`.
2. ★ **La selección no se reubica al filtrar.** Tomar `PRUEBA-GATE2 tarea en curso` (pasa a En curso), dejarla SELECCIONADA y cambiar el filtro a "Pendientes". **OK:** las acciones de fila (Tomar, Soltar, Terminar, Cancelar tarea) desaparecen del encabezado: no queda seleccionada OTRA tarea. Volver a "Abiertas": ninguna fila seleccionada. **FALLA:** queda resaltada otra fila o las acciones siguen visibles (actuarían sobre una tarea no elegida). Capturas antes y después.
3. ★ **Hora local en notas de tarea (Q-10 A).** Doble click en `PRUEBA-GATE2 tarea clasificada` → detalle; anotar `date '+%H:%M'` y agregar la nota `PRUEBA-GATE2 nota`. **OK:** la hora mostrada en la nota es la LOCAL (±1 min de la anotada), NO +3 h (UTC). Captura `A5-3-nota-hora-1280.png`.
4. ★ **Reclasificar a 500 de ancho.** "Reclasificar" → diálogo (captura con su título). `xdotool getwindowgeometry` del diálogo → **OK:** ancho de cliente 500 (±2). Cambiar un combo y Escape → nada cambió; repetir con Aceptar → valores nuevos en el detalle. Popups de los combos con `capturar-x11.py`.
5. ★ **Documentos y hora local de eventos.** Documentos (Activos) → "Nuevo documento": Expediente `PRUEBA-GATE2-0001` con descripción `PRUEBA-GATE2 expediente`; tocar Número y Descripción vacíos → errores debajo. Guardar; en el detalle: agregar el PDF, "Iniciar" (o una acción que deje evento) y anotar la hora. **OK:** el historial del trámite muestra la hora LOCAL. Anotar id. Combo "Ver" → Historial y vuelta (filtros recargan solos; Año `20a5` → error debajo y la grilla no cambia; "sin request" = NO VERIFICABLE por el log, gotcha 9).
6. [P] Anular el documento con motivo → solo lectura; Reabrir con motivo; Quitar el adjunto con confirmación; Tareas vinculadas.

### A6. Reportes (Fase 6) (7 min)

1. **Hub.** Reportes → 6 tarjetas en 2 filas (Valorización, Stock por categoría, Historial por producto, Productos más movidos, Log de auditoría, Estadística de tareas). Comparar con `reportes/hub.html`. Cada tarjeta abre su reporte con "Cargando reporte…" y luego datos; el breadcrumb "Reportes" vuelve al hub. (Abrir las 6 rápido, una captura de cada una.)
2. ★ **Historial por producto sin scroll horizontal a 1280.** Historial → abre en "Elegí un producto" con Exportar PDF deshabilitado; buscar el producto de los movimientos 1599-1601 (popup del autocompletar con `capturar-x11.py`) → Buscar. **OK:** a 1280×760 las 8 columnas entran SIN barra de scroll horizontal; COMENTARIO es la que cede (~100 px, con elipsis). Captura `A6-2-historial-1280.png`.
3. ★ **ACCIÓN legible en Auditoría (grilla y PDF).** Log de auditoría (Admin): combo Usuario con "Todos" primero (captura del desplegable con `capturar-x11.py`); filtrar el período de hoy. **OK:** la columna ACCIÓN muestra texto legible (p. ej. "Registro de movimiento", no `RegistroMovimiento` ni un número). Exportar PDF a `/home/capua25/stockapp-gate2/descargas/auditoria.pdf` → pypdf: la columna Acción con los MISMOS textos legibles que la grilla, título y período cargado. Pegar el extracto.
4. **PDF de Valorización.** Exportar → pypdf: título, "Sin filtros aplicados.", columnas y el total.
5. [P] Más movidos con Desde > Hasta → `Alerta` de error; período válido → Buscar; cambiar Desde SIN Buscar y exportar → el PDF rotula el período CARGADO. Tareas: "Contar por" Fecha de cierre → aviso; volver → desaparece.

### A7. Mantenimiento (Fase 7) (cuando quede tiempo, ~3 min)

1. **Grilla.** Mantenimiento → backups con FECHA local, insignias "Exitosa"/"Fallida", DETALLE, TAMAÑO y "Descargar". Captura.
2. **Descargar un backup** (selector GTK, ruta absoluta a `/home/capua25/stockapp-gate2/descargas/`) → el archivo existe y no está vacío (`ls -l`).
3. [P] "Hacer backup ahora" → "Iniciando…" (si la API local no lo soporta, anotar por qué). Anotar el id de la corrida nueva si se crea.
4. [P] Alertas: ANTES de tocar, anotar la URL y "Habilitado" actuales (captura). Guardar una URL de prueba y Probar (anotar respuesta); RESTAURAR la URL y el estado originales y capturar.

---

## B. Admin a 1366×768 y sin servidor (18 min)

`xdotool windowsize $wid 1366 768` y confirmar con `getwindowgeometry`. Mismos criterios que en A; capturas `*-1366.png`.

### B1. Repetición a 1366 (10 min)

1. ★ **Guardar permisos alcanzable a 1366×768.** Usuarios → seleccionar `g2mixto` (operador): "Guardar permisos" visible en el pie fijo SIN scrollear, y el último permiso del panel visible scrolleando SOLO el panel (no lo tapa el pie). Repetir con `admin` seleccionado y (después de B1.2) con `g2alta` dado de baja. Captura de cada uno.
2. **Rol, contraseña y baja (con `g2alta`).** Hacer Admin → Hacer Operador; cambiar la contraseña de `g2alta` (confirmación) a `Gate2Prueba3`; campo con ●; dar de baja → insignias "Inactivo" y "Solo lectura", casillas y "Nueva contraseña" deshabilitados. Cambiar la contraseña PROPIA: solo hasta ver el aviso y Cancelar (si no hay Cancelar, poner `Admin1234` de nuevo y verificar el login por `curl`).
3. **Pantallas clave a 1366:** sidebar + pulgar (A1.3), Pagos del gasto 22 (A4.1), Tareas (A5.1) y Documentos, hub de Reportes, Historial por producto (A6.2: ahora COMENTARIO ~187), Catálogos → Proveedores (columnas a lo medido, elipsis). Una captura de cada.
4. [P] Error de Monto (A4.3) y pago anulado (A4.6) a 1366.

### B2. Sin servidor (8 min)

1. `kill <pid exacto de StockApp.Api>` (gotcha 5); confirmar con `ss -ltnp | grep 5043` vacío. Recorrer: Pagos del gasto 21, Tareas, Documentos, Log de auditoría, Catálogos (cambiar de catálogo) y Usuarios. **OK:** en cada una, "No se pudo conectar con el servidor" con "Reintentar" EN LÍNEA (dentro de la card), sin modal global. "Nueva tarea" con la API caída → el Aviso de la clasificación (Q-9 A: comportamiento conservado, no es FALLA).
2. Levantar la API (mismo comando de P2, anotar pid nuevo) y "Reintentar" en la pantalla abierta → carga con los filtros de pantalla (en Auditoría, el combo y el log cargados).
3. [P] Lo mismo con "Actualizar" en Valorización.

Cerrar sesión de Admin al terminar.

---

## C. Operador mixto `g2mixto` a 1366×768 (25 min)

Login `g2mixto` / `Gate2Prueba1`. Todas las revocaciones se hacen POR LA API con el token de Admin de P4 (una sola app abierta, con la sesión del operador): primero `GET /usuarios/<id g2mixto>/permisos` (pegar), después `PUT /usuarios/<id>/permisos` con `{"permisos":[…]}` = la lista completa MENOS el permiso a quitar (el PUT reemplaza el conjunto: confirmarlo en `UsuariosEndpoints.cs` / `GuardarPermisosRequest`). Pegar cada PUT con su status. Después de cada revocación, UNA acción en la app que pegue al servidor.

### C1. Gates de entrada (4 min)

1. **Sidebar del operador.** **OK:** Reportes, Catálogos, Tareas, Documentos y Finanzas (Gastos) visibles; Usuarios y Mantenimiento NO. Captura.
2. ★ **Auditoría con Operador con `reportes.ver` (sin `usuarios.gestionar`).** Reportes → hub con 5 tarjetas (SIN Historial por producto, porque no tiene `catalogo.productos`; sin hueco) → Log de auditoría. **OK:** el log se carga con registros; el combo "Usuario" NO se muestra; NO aparece "Sin permiso" ni un Aviso de 403. **FALLA:** "Sin permiso", Aviso de sesión o pantalla vacía. Exportar PDF → pypdf: rotula "Usuario: Todos." y ACCIÓN legible. Captura `C1-2-auditoria-operador-1366.png`.
3. **Tareas y Documentos del operador:** sin "Cancelar tarea", sin Prioridad ni Reclasificar en el detalle; en Documentos sin "Anular…"/"Reabrir…" ni Quitar adjunto.
4. **Catálogos del operador:** master con INVENTARIO, FINANZAS y TAREAS (tiene `catalogo.maestras` + `finanzas.maestros`).

### C2. Revocación de `finanzas.pagos` (4e) (5 min)

1. Gastos → gasto 21 → Pagos: registrar un pago `PRUEBA-GATE2 pago operador` y anularlo (anotar id).
2. Con Pagos abierto y `PRUEBA-GATE2 pago op 2` tipeado en la nota y `200` en Monto: quitar `finanzas.pagos` por la API → "Registrar pago". **OK:** un Aviso, la pantalla pasa a solo lectura (insignia "Solo lectura", sin card de registro) SIN perder lo tipeado ni navegar. Captura.
3. [P] Volver a Gastos → Pagos de otro gasto: aterriza en la v2 en solo lectura sin Aviso (Q-1 A, comportamiento ACEPTADO: se anota como OK si se ve la insignia). Devolver `finanzas.pagos`.

### C3. Revocación de `documentos.gestionar` y `tareas.gestionar` (5) (5 min)

1. Con Documentos abierto: quitar `documentos.gestionar` → una acción (cambiar el combo Ver). **OK:** UN solo Aviso y "Sin permiso" EN LUGAR de la card. Devolver el permiso.
2. Con el detalle de `PRUEBA-GATE2-0001` abierto: quitar `tareas.gestionar` → una acción. **OK:** la sección "Tareas vinculadas" desaparece. Con Tareas abierto → "Sin permiso". Devolver el permiso.

### C4. Revocación de `finanzas.maestros` y `catalogo.maestras` (7) (5 min)

1. Catálogos → "Líneas POA" abierta: quitar `finanzas.maestros` → una acción (buscar o cambiar de catálogo). **OK:** un solo Aviso, el grupo FINANZAS desaparece del master (con su header) y se abre Categorías. Captura.
2. Quitar además `catalogo.maestras` → una acción. **OK:** "Sin permiso" en lugar del master-detail y el ítem "Catálogos" desaparece del sidebar. Devolver los dos.

### C5. Revocación de `reportes.ver` (6) (4 min)

1. Agregar `catalogo.productos` por la API (hace de `f6historial`) → navegar a Reportes. **OK:** 6 tarjetas (aparece Historial por producto).
2. Con Valorización abierta: quitar `reportes.ver` → "Actualizar". **OK:** un solo Aviso, "Sin permiso" en lugar de la card, Exportar PDF deshabilitado y el ítem "Reportes" fuera del sidebar. Captura.

### C6. [P] Popups del operador (2 min)

Calendario de Fecha límite (Tareas) y de Desde (Más movidos, si sigue con `reportes.ver` devuelto), desplegables de Agrupar por y Contar por: abren, eligen, cierran sin foco atrapado.

Cerrar sesión. Volver a dejar `g2mixto` con sus 8 permisos (no hace falta si se borra en la limpieza).

---

## D. Cierre, reporte y limpieza (10 min + limpieza)

### D1. Reporte (5 min)

Completar la tabla del ledger y mandarle a Martin: el resumen, las FALLAs con sus capturas, los NO VERIFICABLE con su motivo, las salidas de `curl` y de pypdf, y la lista de "Ids creados". Pedir el "aprobado" de cada fase (4e, 5, 6, 7) por separado. Sin "aprobado", ninguna fase se registra como aprobada.

### D2. Limpieza (con la aprobación PREVIA de Martin; NUNCA contra el VPS)

**PARAR y preguntar antes de ejecutar nada de esta sección.** Mostrar el listado del paso 2 y esperar el OK explícito.

1. **Backup nuevo:** `docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-limpieza-gate2-$(date +%Y%m%d-%H%M%S).sql`; `ls -l` no vacío; ruta al ledger.
2. **Listar cada fila a borrar con un `SELECT` acotado por ids**, tabla por tabla. Antes, confirmar nombres de columnas con `\d "Tabla"` (las tablas reales, del `AppDbContextModelSnapshot.cs`: `Adjuntos`, `AdjuntosContenido`, `PagosGasto`, `Gastos`, `MovimientosStock`, `AsignacionesPresupuestales`, `LineasPoa`, `RubrosGasto`, `FuentesFinanciamiento`, `Proveedores`, `Tareas`, `NotasTarea`, `DocumentosAdministrativos`, `EventosDocumento`, `AdjuntosDocumento`, `AdjuntosDocumentoContenido`, `PermisosUsuario`, `Usuarios`, `LogsAuditoria`, `CorridasBackup`). Conjunto:
   - **PRUEBA-4D** (spec, "Deuda abierta tras la Fase 4d"): adjuntos (y su contenido) de los gastos 19-24 y de sus pagos; `PagosGasto` de los gastos 19-24 (incluye el pago 13 y TODOS los pagos `PRUEBA-GATE2` de A4 y C2, que cuelgan de los gastos 21 y 22); `Gastos` 19-24; `MovimientosStock` 1599, 1600, 1601; `AsignacionesPresupuestales` de la línea POA 4; `LineasPoa` 4; `RubrosGasto` 2; `FuentesFinanciamiento` 2; `Proveedores` 8; `PermisosUsuario` y `Usuarios` del id 30 (`f4dmixto`).
   - **PRUEBA-GATE2:** los ids anotados en "Ids creados" (rubros, proveedor, línea POA y asignaciones, tareas y sus notas, documento con eventos y adjuntos y su contenido, cualquier otro con el prefijo); usuarios `g2mixto` y `g2alta` con sus `PermisosUsuario`.
   - **Referencias que impiden borrar un maestro o un usuario:** un `SELECT` por FK (p. ej. gastos o productos que usan el proveedor 8; `LogsAuditoria`, `MovimientosStock`, `Tareas`, `DocumentosAdministrativos`, `CorridasBackup`, `LotesImportacion` que apuntan a los usuarios 30, `g2mixto`, `g2alta`). Si un usuario tiene filas de auditoría propias, listarlas con sus ids y preguntarle a Martin: (a) borrarlas en la misma transacción (son acciones de prueba), o (b) no borrar ese usuario y darlo de baja por la app. Lo mismo con una `CorridasBackup` creada en A7.3 (es un backup real: se conserva salvo que Martin diga lo contrario).
   - Fuera de alcance (solo mencionarlo): otros datos de prueba viejos (`f4bmixto` 28, `f3c1mixto` 23, productos `PRUEBA-3C1`).
3. **Borrar en UNA transacción**, en orden de FK (hijos primero: contenidos → adjuntos → pagos → gastos → movimientos → asignaciones → líneas → rubros/fuentes/proveedores; notas/eventos → tareas/documentos; permisos → usuarios), cada `DELETE … WHERE "Id" IN (…)` con la lista EXPLÍCITA de ids (nunca por prefijo ni por `LIKE`). Correr con `\set ON_ERROR_STOP on` dentro de `BEGIN;`, comparar el conteo de cada `DELETE` con el listado del paso 2 y, si alguno difiere, `ROLLBACK;` y PARAR. `COMMIT;` solo con todos iguales. Pegar el script y las salidas en el ledger.
4. **Recalcular stock** en la app (Admin): Historial de movimientos → "Recalcular stock" del producto de los movimientos 1599-1601. **OK:** su stock queda igual al valor previo a la 4d (dato del backup `~/stockapp-backups-local/pre-4d-20261017-110108.sql`: extraerlo con `grep` sobre el `COPY` de `Productos`, sin restaurar nada). Si difiere, PARAR y reportar.
5. **Verificación final:** `SELECT` de los ids borrados = 0 filas; ningún registro con `PRUEBA-4D` ni `PRUEBA-GATE2` en las tablas listadas; conteos antes/después en el ledger. Dejar `stockapp-pg` andando y la API detenida por pid (o andando, según pida Martin).

---

## Cantidad de pasos

- Precondición: 1 · Preparación: 5 (P1-P5)
- A: 35 (A1 3 · A2 4 · A3 5 · A4 8 · A5 6 · A6 5 · A7 4)
- B: 7 (B1 4 · B2 3) · C: 14 (C1 4 · C2 3 · C3 2 · C4 2 · C5 2 · C6 1)
- D: 6 (D1 1 · D2 5)
- **Total: 68 pasos** (56 de verificación con la app en A, B y C), 9 prescindibles [P] (A3.5, A5.6, A6.5, A7.3, A7.4, B1.4, B2.3, C2.3, C6), 17 obligatorios ★ de los ajustes del GATE 1 y de las reviews.

## Qué queda para la deuda si algo no se puede hacer

Lo NO VERIFICABLE por el entorno (Ver de adjuntos sin visor, conteo de requests sin log, Acceso limitado sin licencia vencida, Hacer backup ahora si la API local no lo soporta) queda custodiado por los UiTests y las comparaciones de cada fase, y se anota en la "Deuda abierta" de cada fase al cerrar la spec (Task 5 de la 4e, Task 8 de la 5 y la 6, Task 11 de la 7).
