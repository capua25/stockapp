# Kit de instalación LAN (pendrive) y orquestador de deploy al VPS — Plan de Implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan sintaxis de checkbox (`- [ ]`) para seguimiento.

**Goal:** Construir un kit autocontenido en pendrive que instale StockApp.Api en un servidor Ubuntu virgen de la LAN del municipio de Carmelo sin depender de internet, más un orquestador `deploy-vps.sh` que automatice el deploy al VPS que hoy se hace a mano.

**Architecture:** Cinco scripts bash numerados (`00`–`04`) que envuelven — sin modificar — el `deploy/install.sh` ya verificado en producción. La lógica bash que tiene un oráculo en C# (fingerprint de licencia, validaciones) se extrae a una librería sourceable `deploy/kit/lib/` y se testea desde xUnit lanzando `bash` como proceso, de modo que hay UN solo test runner y los guardianes corren en la suite existente. Lo que no tiene oráculo (systemd, Docker, ufw, red) no se finge testeable: se verifica por mutación en una VM limpia con snapshot.

**Tech Stack:** bash 5 (`set -euo pipefail`), Docker + `docker compose`, systemd, Ubuntu Server LTS (versión a fijar — Decisión 7), .NET 10 / xUnit v2 (2.5.3) para los guardianes, Velopack/`vpk` para el cliente Windows.

**Spec:** `docs/superpowers/specs/2026-09-10-kit-instalacion-deploy-design.md` (APROBADO 2026-09-10). Leelo junto con este plan: el plan argumenta desde el diseño y no lo reemplaza.

---

## Global Constraints

Aplican a TODAS las tareas. Los valores son literales verificados, no aproximaciones.

- **`deploy/install.sh` NO SE MODIFICA.** Única excepción posible: si la Decisión 4 se resuelve por la opción B. Hasta entonces, cualquier tarea que necesite tocarlo está bloqueada, no es libre de improvisar.
- **Puertos verificados:** API `5080` (configurable vía `API_PORT` del `.env`, `install.sh:175-180`, inyectado por `sed` en la unit en `install.sh:424-425`). Postgres `5433` (HARDCODEADO en tres lugares: `deploy/docker-compose.postgres.yml:28`, `deploy/wait-for-postgres.sh:19`, y la connection string de `install.sh:377`). SSH del VPS `34377`. Puerto de fábrica del desktop `5043`.
- **`ASPNETCORE_URLS` está PROHIBIDO en cualquier `.env`.** `install.sh:227-238` rechaza la instalación si lo encuentra, junto con todo prefijo `ASPNETCORE_`, `DOTNET_`, `LD_` y los nombres `HOME` y `PATH`. Para cambiar puerto o interfaz se usa `API_PORT` / `API_BIND`.
- **Reglas transversales de los scripts del kit:** `set -euo pipefail`; exit 0 en éxito / 1 en fallo; todo lo impreso va también a `/var/log/stockapp-kit-<fecha>.log`; TODOS idempotentes.
- **La clave PRIVADA de licenciamiento nunca viaja al pendrive ni al servidor.** Vive en `~/stockapp-claves/clave-privada.pem` en la máquina del proveedor. Al servidor va solo `LICENCIA_CLAVE_PUBLICA_BASE64`.
- **El fingerprint es público.** `GET /licencia/estado` es anónimo y está en la allowlist de `BloqueoLicenciaMiddleware` (`EsRutaPermitida`, `path.StartsWithSegments("/licencia")`): responde 200 con `{"activada":bool,"codigoMaquina":"..."}` con licencia activada y sin ella.
- **Reglas de contraseña del admin (a espejar en bash):** mínimo **8** caracteres, al menos **una letra** y al menos **un número**. Fuente única: `src/StockApp.Application/Auth/ContrasenaValidator.cs:9,23-25`.
- **Fingerprint Linux:** SHA-256 de los bytes UTF-8 de `/etc/machine-id` **trimeado** (fallback `/var/lib/dbus/machine-id`), hex en MAYÚSCULAS, agrupado de a 4 con guiones → 16 grupos. Fuente: `FingerprintMaquinaLinux.cs:18` (`File.ReadAllText(ruta).Trim()`) y `FingerprintMaquinaBase.cs:18-27`.
- **Restricción dura del proyecto:** el proveedor NO tendrá acceso al servidor del municipio después de la instalación. Todo control post-instalación vive en el cliente o en la base de datos, nunca en config del servidor.
- **Central Package Management:** las versiones de paquetes van en `Directory.Packages.props`, nunca en los `.csproj`. `Infrastructure.Tests` corre **xUnit v2 (2.5.3)** — **no existe `Assert.Skip`**, no lo uses.
- **TDD estricto con verificación POR MUTACIÓN:** test rojo primero; y para cada guardián, reintroducir el defecto debe poner el test en rojo. Pegar el rojo, no razonarlo.
- **Conventional commits en español.** Ramas de feature con merge `--ff-only` a `main`.
- **Verificación orgánica obligatoria:** probar con la app real, no solo tests.

---

## DECISIONES — RESUELTAS (Fase 0 cerrada, 2026-09-13)

Trece decisiones. Las dos primeras son las inconsistencias señaladas en el encargo; las nueve siguientes salieron de leer el diseño contra el código; las dos últimas (#12 y #13) surgieron del relevamiento del 2026-09-13. **Las Decisiones 7 y 8 eran bloqueantes de fases enteras — ya desbloqueadas.** Se conserva el planteo original y las opciones evaluadas de cada una como registro del razonamiento; el veredicto final está en el bloque **RESUELTA** al pie de cada sección.

### Resumen de las 13 resoluciones

| # | Decisión | Opción elegida |
|---|---|---|
| 1 | Puerto de la API | Configurable (ya existe vía `API_PORT`/`.env`); falta que el preflight lo lea |
| 2 | Desktop 5043 vs servidor 5080 | C — pre-sembrar `conexion.json` desde el pendrive |
| 3 | Configurador → `/licencia/estado` | A — dado de baja; mitigación en `03-verificar.sh` |
| 4 | Puerto Postgres 5433 | A — fijo, sin escape |
| 5 | Severidad del preflight | A — tres niveles OK/INFO/ROJO |
| 6 | Credenciales de los chequeos 6-7 | C — `.env` con fallback interactivo |
| 7 | Versión de Ubuntu | Ubuntu 24.04 LTS, `.deb` bajados en contenedor (A) |
| 8 | VM de ensayo | Hyper-V (A) |
| 9 | Fuentes y testing bash | 9a-A, 9b-A, 9c-B (shellcheck) |
| 10 | `--con-soporte` | B — sacarlo de la firma |
| 11 | Duplicados en pre-vuelo | B — abortar con script de remediación sugerido, sin ejecutar |
| 12 | Firewall del kit LAN | No configurar `ufw`; solo imprimir aviso (persistido en `POST-INSTALACION.txt`) |
| 13 | Paridad de versión Postgres | Anotada como deuda no bloqueante; tarea agregada al preflight |

### Decisión 1 — El puerto de la API en el kit: constante fija o configurable (INCONSISTENCIA #1)

**El problema.** El diseño (línea 85) hace que `01-bootstrap.sh` escriba `API_PORT=5080` y `API_BIND=0.0.0.0` como **constantes fijas**: pide interactivamente usuario y contraseña del admin, pero el puerto no lo pregunta. Y el preflight (líneas 69 y 156) **aborta en rojo si el 5080 está ocupado** en vez de ofrecer otro. Es **más restrictivo que el `install.sh` que envuelve**: `install.sh:175-180` lee `API_PORT` del `.env`, acepta cualquier puerto numérico y lo inyecta en la unit de systemd. O sea, el kit renuncia a una capacidad que la herramienta de abajo ya tiene, y la renuncia se paga el día de la instalación, sin internet y sin posibilidad de volver.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Dejarlo como está** | 5080 fijo; preflight rojo si está ocupado | Un solo camino que probar; `03-verificar` y el `LEEME` pueden hablar de "5080" sin variables | Un puerto ocupado (otro servicio del municipio, un proxy, cualquier cosa que no controlás) bloquea la instalación entera sin escape. Estás sentado ahí sin internet |
| **B — Configurable de punta a punta** | `00-preflight` avisa en amarillo; `01-bootstrap` pregunta el puerto (default 5080, validado numérico + libre); `03-verificar` y `04-licencia` lo leen del `.env` | Escape real; usa la capacidad que `install.sh` ya tiene | El puerto pasa a ser variable en 4 scripts + el `LEEME` + la URL que le dictás al Configurador en cada PC. Más superficie de error humano el día de la instalación |
| **C — Fijo con override del proveedor** | 5080 por default, más un flag `01-bootstrap.sh --puerto N`; `00-preflight` avisa en amarillo y nombra el flag; `03-verificar` lee el puerto del `.env` (no lo asume) | Camino feliz idéntico a A (el operador no ve una pregunta más); escape disponible cuando hace falta; blast radius chico | Sigue habiendo una variable que `03-verificar` debe leer en vez de asumir (costo inevitable si querés el escape) |

**Recomendación: C.** El caso "5080 ocupado" es poco probable pero catastrófico e irreversible en el contexto del municipio; C compra el seguro sin cargarle una decisión más al operador en el camino feliz. **Consecuencia obligatoria en cualquier opción salvo A:** `03-verificar.sh` debe **leer** `API_PORT` de `/etc/stockapp/.env` y usarlo en su línea final ("la URL exacta para el Configurador"), nunca hardcodear 5080.

**RESUELTA (2026-09-13):** Configurable. El usuario dijo "la API en el puerto que sea". NO hace falta el flag `--puerto` que proponía la opción C: `API_PORT` YA existe vía `deploy/.env` (default 5080 en `install.sh:175`, inyectado con `sed` a la unit systemd en `install.sh:425`). Único trabajo restante: **el preflight debe leer `API_PORT` del `.env` en vez de asumir 5080.**

### Decisión 2 — El desktop sale de fábrica en 5043 y el servidor escucha en 5080 (INCONSISTENCIA #2)

**El problema.** El valor de fábrica del cliente es `http://localhost:5043` en dos lugares atados por un test: `src/StockApp.Presentation/appsettings.json:3` y `src/StockApp.Configuracion/ConexionDefaults.cs:24` (el test `ConexionDefaultsTests.UrlPorDefecto_CoincideConElApiBaseUrlDeAppsettingsDeFabrica` lee el `appsettings.json` real del repo y revienta si uno cambia sin el otro). La precedencia del cliente es `conexion.json` → `appsettings.json` → `ConexionDefaults` (`src/StockApp.Presentation/App.axaml.cs:458-471`, documentada en el docblock de `ConstruirConfiguracion`). Si el operador olvida correr el Configurador en una PC, esa PC apunta a `localhost:5043` y falla contra el servidor real — y el síntoma que ve el usuario final es "no conecta", sin decir por qué.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Mover el default de fábrica a 5080** | Cambiar `appsettings.json` + `ConexionDefaults.UrlPorDefecto` juntos (el test los obliga) | Alinea los dos números | **Rompe el loop de desarrollo**: 5043 es el puerto de `src/StockApp.Api/Properties/launchSettings.json` (profiles `http` y `https`), el que usás cuando corrés la API local. Y sigue diciendo `localhost`, que nunca es el servidor del municipio: no arregla el failure mode real |
| **B — Dejar 5043 y hacer ruidoso el fallo en el cliente** | Mensaje de error del desktop que nombre explícitamente al Configurador y la URL que está intentando | El usuario final entiende qué le falta | Trabajo en el cliente; y sigue dependiendo de que alguien pase por cada PC |
| **C — Pre-sembrar `conexion.json` desde el pendrive** | `armar-kit.sh` deja un `clientes/configurar-cliente.cmd` que escribe `%AppData%\GestionMunicipal\conexion.json` con la URL real del servidor (la que imprime `03-verificar`), antes o después del Setup.exe | Elimina el paso humano por PC, que es el failure mode real. Aprovecha la precedencia #1 que ya existe y está testeada (`ResolucionApiBaseUrlTests`) | Requiere conocer la IP del servidor al momento de instalar clientes — ya la tenés, `03-verificar` la imprime. El `.cmd` hay que probarlo en Windows real |
| **D — C + B** | Las dos | Cinturón y tiradores | Más alcance |

**Recomendación: C, dejando 5043 intacto.** El número de fábrica no es el problema; el problema es que hay un paso manual por PC que se puede olvidar. C lo borra. B es un buen segundo si querés red de contención. **A no la recomiendo:** rompe el F5 local y no arregla nada real.

**RESUELTA (2026-09-13):** Opción C — pre-sembrar `conexion.json` desde el pendrive, dejando el default 5043 INTACTO. No se toca `ConexionDefaults` ni `ConexionDefaultsTests`; no se rompe el F5 local. Elimina el paso manual por PC, que es el failure mode real.

### Decisión 3 — El arreglo #1 del diseño ya se resolvió por otra vía (INCONSISTENCIA #3, nueva)

**El problema.** El diseño (línea 139) pide cambiar el Configurador para que pruebe contra `GET /licencia/estado` en vez de `GET /`, porque `GET /` devolvía 423 sin licencia y producía una advertencia amarilla falsa. **Eso ya está arreglado, pero server-side:** el commit `b9b1d71` (2026-09-11) agregó la raíz a la allowlist de `BloqueoLicenciaMiddleware` (`EsRaiz(path) => path.Value is null or "" or "/"`, con igualdad exacta a propósito) y dejó el guardián `BloqueoLicenciaTests.Bloqueada_Raiz_DevuelveBannerAnonimo`. El falso amarillo ya no ocurre. El arreglo #1 del diseño, **tal como está redactado, quedó obsoleto.**

Pero el diseño daba un segundo motivo, que sigue en pie: probar `/licencia/estado` permitiría mostrar *"conecta, pero la licencia no está activada"* — y ese es exactamente el estado por el que pasa la instalación del kit.

Dato adicional: `tools/StockApp.Configurador/Servicios/ProbadorConexion.cs` valida **el shape del body** (`status == "ok"`), no solo el status code — por eso el test de `b9b1d71` también asserta `status` y `service`.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Dar de baja el arreglo #1** | Cero trabajo. `GET /` queda como sonda | Lo más barato; el falso amarillo ya está muerto | Se pierde el diagnóstico "conecta pero sin licencia" justo en el estado por el que pasa la instalación. Y un cliente apuntado a una API **anterior** a `b9b1d71` vuelve al falso amarillo |
| **B — Migrar la sonda a `/licencia/estado` + 4º caso** | Agregar `ResultadoPruebaConexion.OkLicenciaSinActivar`, mensaje en `ConfiguradorViewModel` (`:73-80`), tests | Da el diagnóstico; funciona contra APIs viejas; `install.sh:457-463` ya eligió este endpoint por el mismo motivo (consistencia) | Enum nuevo + mensaje + binding + tests. Toca la UI del Configurador |
| **C — Mantener `GET /` y enriquecer con una 2ª llamada** | Solo si `GET /` da Ok, pegarle a `/licencia/estado` para enriquecer el mensaje | No toca la semántica de los 3 casos existentes ni sus tests | Dos requests; y no arregla el caso "API vieja" (la primera sonda ya falló) |

**Recomendación: B.** Es lo que el diseño quería en el fondo, alinea al Configurador con el criterio que `install.sh` ya documentó en `:457-463`, y el diagnóstico extra se paga solo el día de la instalación. Si se elige A, **hay que editar el diseño** para que no quede un arreglo fantasma en la lista de entregables.

**RESUELTA (2026-09-13):** Opción A (contra la recomendación B del plan) — dar de baja el arreglo #1. El bug que lo motivaba ya murió con el commit `b9b1d71` (resuelto server-side). Cero trabajo en el Configurador. **Mitigación acordada:** el costo restante (que el día de instalación el Configurador muestre verde con la licencia sin activar) lo cubre `03-verificar.sh`, que ya golpea la API — agregar ahí el chequeo del estado de licencia como tarea explícita en la fase donde vive ese script.

### Decisión 4 — El 5433 de Postgres no tiene escape, y dárselo rompe "install.sh NO se toca" (nueva)

**El problema.** `00-preflight.sh` verifica que el 5433 esté libre (diseño línea 69) y aborta si no. Pero el 5433 **no es configurable en ninguna parte**: está hardcodeado en `deploy/docker-compose.postgres.yml:28` (`"127.0.0.1:5433:5432"`), en `deploy/wait-for-postgres.sh:19` (`readonly PORT="5433"`) y en la connection string que arma `install.sh:377` (`Host=127.0.0.1;Port=5433;...`). Es una situación **peor que la Decisión 1**: ahí el escape existía y el kit lo tapaba; acá no existe. Y abrirlo exige editar `install.sh`, que el diseño (línea 92) declara intocable.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Aceptar el abort** | `00-preflight` en rojo; el `LEEME` documenta que un conflicto en 5433 requiere editar 3 archivos a mano, en el momento | Honesto, cero código, `install.sh` intacto | Cirugía a mano en un servidor ajeno, sin internet, en el peor momento posible. Es el escenario que el kit existe para evitar |
| **B — `POSTGRES_PORT` configurable** | `compose` → `"127.0.0.1:${POSTGRES_PORT:-5433}:5432"`; `wait-for-postgres.sh` lo lee del entorno con default 5433; `install.sh:377` lo interpola | Escape real y retrocompatible: un `.env` sin `POSTGRES_PORT` (el del VPS hoy) sigue dando 5433 exacto | **Toca `install.sh`** → requiere levantar explícitamente la regla del diseño. Y `wait-for-postgres.sh` corre como `ExecStartPre` de systemd: hay que verificar de dónde toma el entorno ahí, no asumirlo |
| **C — Mover el puerto fijo a uno improbable** | Nueva constante fija, p. ej. `55433`, en los 3 lugares | Mantiene un solo camino; colisión muchísimo menos probable; `install.sh` igual se toca (la connection string) — salvo que se acepte 5433 en el VPS y 55433 en la LAN, que es peor | Sigue sin escape. Y divergir el puerto entre VPS y LAN rompe la decisión 2 del diseño ("idéntico al VPS") |

**Recomendación: B, con autorización explícita para tocar `install.sh`.** Es un cambio de tres líneas, retrocompatible por default, y convierte un ladrillo en una pregunta. Si preferís no abrir `install.sh` en este alcance, tomá **A** y que el `LEEME` lleve el procedimiento de emergencia **escrito y probado** — no improvisado. **C no la recomiendo:** paga el costo de tocar `install.sh` sin comprar el escape.

**RESUELTA (2026-09-13):** Opción A (contra la recomendación B del plan) — FIJO. Razón del usuario: "eso no lo tiene que tocar nadie, y la comunicación con el exterior es mediante la API en el puerto que sea". Ya está bindeado a `127.0.0.1:5433` (`docker-compose.postgres.yml:28`), nunca a `0.0.0.0`. **Consecuencia aceptada explícitamente:** si el 5433 estuviera ocupado en el servidor del municipio, el preflight aborta y no hay escape salvo cirugía manual sobre `install.sh`.

### Decisión 5 — Severidad del preflight en un servidor virgen (nueva)

**El problema.** La tabla de `00-preflight.sh` (diseño líneas 61-75) lista "Docker — instalado / corriendo" y "Cliente Postgres — `postgresql-client-16`" como cosas que verifica, pero **no dice con qué severidad**. En un servidor **virgen** —que es el caso de uso— ninguno de los dos va a estar: instalarlos es justamente el trabajo de `01-bootstrap.sh` (diseño líneas 81-82). Si el preflight los marca en rojo, **falla en todos los servidores legítimos** y el operador aprende a ignorar los rojos, que es exactamente cómo un preflight deja de servir.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Tres niveles explícitos** | `OK` / `INFO` (ausente pero lo instala `01`) / `ROJO` (bloqueante: SO, arquitectura, systemd, disco, RAM, machine-id, puertos) | El rojo vuelve a significar algo. El operador sabe qué lo bloquea | Hay que decidir el nivel de cada fila, una por una |
| **B — Todo informativo** | El preflight nunca bloquea, solo informa | Nunca da un falso bloqueo | Inútil como guardia: la mutación "puerto 5080 ocupado → preflight en rojo" (diseño línea 156) deja de existir |

**Recomendación: A.** Y el mapa de severidades queda escrito en el plan (Fase 2, Task 2.2), no al criterio de quien codee. Ojo: el preflight **no puede** verificar el puerto 5433 como "libre" de la misma manera en una segunda corrida — si `01` ya levantó el contenedor, el 5433 está ocupado **por nosotros**; esa fila necesita distinguir "ocupado por `stockapp-pg`" de "ocupado por otra cosa", o el preflight deja de ser idempotente (regla transversal del diseño, línea 55).

**RESUELTA (2026-09-13):** Opción A — tres niveles explícitos OK / INFO / ROJO, con el mapa de severidades escrito en el plan (Fase 2, Task 2.2), no al criterio del que codea. Docker y `postgresql-client-16` son **INFO, no ROJO**: en un servidor virgen nunca están, los instala `01-bootstrap`.

### Decisión 6 — Con qué credenciales corren los chequeos 6 y 7 de `03-verificar.sh` (no especificado)

**El problema.** El chequeo 6 es `POST /auth/login` "con el admin" y el 7 es `GET /backups/salud` (que necesita el JWT del 6). El diseño no dice de dónde salen esas credenciales. Y hay un detalle que muerde: `BOOTSTRAP_ADMIN_USER`/`BOOTSTRAP_PASSWORD` solo se usan **la primera vez** que la API arranca contra una base vacía (seed idempotente, `deploy/.env.example:46-53`), y al operador se le indica **cambiar esa contraseña apenas entra**. Un `03-verificar` que lea el `.env` funciona el día 1 y miente para siempre después.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Leer del `/etc/stockapp/.env`** | `source` del archivo (root, 600) | Cero tipeo, corrida no interactiva, loguéable | Falso rojo en cuanto se cambie la contraseña del admin — o sea, desde el día 2 |
| **B — Preguntar siempre** | `read -rs` | Siempre correcto | Interactivo (rompe el piping y el log limpio); el operador tipea |
| **C — A con fallback a B** | Intenta con el `.env`; si da 401, pregunta una vez e imprime "la contraseña del admin fue cambiada — eso es lo esperado, no un error" | Correcto el día 1 y el día 200; el mensaje educa en vez de alarmar | Un camino más que probar |

**Recomendación: C.** Y que el chequeo distinga **401 (credenciales)** de **423 (licencia)** de **no responde**: los tres significan cosas distintas y el operador tiene que poder actuar sobre cada una.

**RESUELTA (2026-09-13):** Opción C — leer del `.env` con fallback interactivo. Debe distinguir **401** (credenciales) de **423** (licencia) de **no responde**. `BOOTSTRAP_PASSWORD` solo sirve el día 1.

### Decisión 7 — Versión exacta de Ubuntu y procedencia de los `.deb` de `offline/` (BLOQUEANTE)

**El problema.** La decisión 1 del diseño dice "Ubuntu Server LTS, **versión FIJA definida por el proveedor**" — y esa versión sigue en blanco. No es un detalle: **determina todo el payload de `offline/`**. Los `.deb` de Docker y de `postgresql-client-16` tienen cadenas de dependencias reales (`containerd.io`, `docker-ce-cli`, `docker-compose-plugin`, `libpq5`...) que resuelven distinto en cada release. Un set de `.deb` de la versión equivocada falla en `dpkg -i` **en el municipio, sin internet**. Además, en esta máquina **no hay `multipass`, `vagrant`, `VBoxManage` ni `lxc`** (verificado): el único contenedor disponible es Docker.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Fijar la versión y bajar los `.deb` dentro de un contenedor `ubuntu:<ver>`** | `armar-kit.sh` corre `apt-get download` (con `apt-rdepends` o `apt-get install --download-only`) dentro de `docker run --rm ubuntu:<ver>`, y **verifica el set instalándolo** en un contenedor limpio de la misma imagen | Reproducible y verificable en la máquina de desarrollo con la única herramienta que hay. Convierte un fallo del día de instalación en un fallo del día de build | Hay que elegir la versión **ya** y que el proveedor del servidor la respete |
| **B — `apt-get download` en el host** | Bajarlos desde WSL2 | Más simple | La distro WSL2 **no es** el Ubuntu Server destino: el set sale silenciosamente equivocado. Es el peor de los dos mundos |

**Recomendación: A**, y fijar la versión antes de empezar la Fase 4. **Esto bloquea la Fase 4 entera.** Anotá la versión elegida en un archivo `VERSION` dentro del kit y hacé que `00-preflight.sh` compare contra ese valor (diseño línea 63: "SO + versión vs. la fijada en el kit") — es decir, el número vive en **un** lugar y los dos scripts lo leen.

**RESUELTA (2026-09-13):** **Ubuntu 24.04 LTS.** Los `.deb` de `offline/` se bajan DENTRO de un contenedor `ubuntu:24.04` (opción A: reproducible y verificable), NO con `apt-get download` en el host WSL2 (opción B, descartada). Anotar la versión en `deploy/kit/VERSION` antes de arrancar la Fase 4. **La Fase 4 queda desbloqueada.**

### Decisión 8 — En qué VM se ensaya, dado que no hay ninguna instalada (BLOQUEANTE de la Fase 5)

**El problema.** El diseño (línea 145) pide "VM descartable con snapshot (multipass/VirtualBox)". **Ninguna de las dos está instalada, ni vagrant ni lxc** (verificado). Y el host es WSL2, lo que cambia las opciones reales.

| Opción | Qué implica | A favor | En contra |
|---|---|---|---|
| **A — Hyper-V en el Windows anfitrión** | `New-VM` + `Checkpoint-VM` con una ISO de Ubuntu Server | Hyper-V **ya está habilitado** (WSL2 lo usa): cero instalación nueva. Snapshots reales, Ubuntu Server real, red real, systemd real, `machine-id` real | Se maneja desde PowerShell, fuera de WSL; hay que bajar la ISO |
| **B — VirtualBox en Windows** | Instalar VirtualBox + ISO | Lo que pide el diseño; snapshots muy cómodos | **Convive mal con Hyper-V/WSL2** (el backend de virtualización se pelea). Riesgo de romper el WSL2 de trabajo |
| **C — Segunda distro WSL2 importada** | `wsl --import` de un rootfs de Ubuntu Server | Barato, rápido, descartable | **No es un servidor fiel**: systemd es opt-in, la red es NAT del host, `ufw` se comporta distinto, comparte kernel. Sirve para humear la lógica de los scripts, **NO como prueba** |

**Recomendación: A (Hyper-V), y usar C solo como banco de pruebas rápido durante el desarrollo.** Lo importante es decirlo sin vueltas: **la prueba que vale es A**; si se ensaya únicamente en C, el kit sigue sin estar probado y se entera en el municipio.

**RESUELTA (2026-09-13):** **Hyper-V** en Windows (opción A) — ya está habilitado porque WSL2 lo usa; da snapshots reales y Ubuntu Server fiel. Descartados: VirtualBox (convive mal con Hyper-V/WSL2) y segunda distro WSL2 (systemd capado, red distinta, sin snapshots). **La Fase 5 queda desbloqueada.**

### Decisión 9 — Dónde viven los fuentes del kit y con qué herramienta se testea bash

**El problema.** El diseño muestra el layout del kit **en tiempo de ejecución** (`stockapp-kit-<version>/`) pero nunca dice dónde viven los fuentes en el repo ni qué copia `armar-kit.sh`. Y para testear bash: **no hay `shellcheck` ni `bats` instalados** (verificado).

- **9a — Ubicación:** (A) `deploy/kit/` con `00-preflight.sh`…`04-licencia.sh`, `LEEME.md`, `VERSION` y `lib/`, y `armar-kit.sh` copiando `deploy/kit/*` a la raíz del kit y `deploy/*` a `servidor/`. (B) todo plano en `deploy/`. → **Recomendación: A.** `deploy/` es el camino del VPS y ya tiene 9 archivos; mezclar dos audiencias en un directorio es cómo alguien copia el script equivocado al servidor equivocado. `armar-kit.sh` queda en `deploy/` como dice el diseño (línea 49).
- **9b — Runner de tests de bash:** (A) **xUnit lanza `bash`** como proceso sobre funciones sourceables de `deploy/kit/lib/` — un solo runner, los guardianes corren en la suite que ya existe, y hay precedente en el repo (`RestaurabilidadBackupTests.cs:121-149` lanza `pg_restore` con `ProcessStartInfo`/`ArgumentList`). (B) instalar `bats` → segundo runner, segunda historia de CI. → **Recomendación: A.**
- **9c — `shellcheck` como gate:** instalarlo (`apt install shellcheck`) y correrlo sobre los scripts del kit. Es un linter, no un test: barato y atrapa justo la clase de error que en bash no se ve (quoting, `set -e` que no dispara dentro de un `if`, variables sin usar). → **Recomendación: sí, instalarlo**, y que sea un paso de la definición de "listo" de cada script, no un test automatizado aparte.

**RESUELTA (2026-09-13):** 9a-A (`deploy/kit/` con estructura propia), 9b-A (tests bash lanzados desde xUnit, NO instalar `bats`), 9c-B (`shellcheck` instalado, como paso de la definición de "listo" de cada script).

### Decisión 10 — `--con-soporte` no está definido en ninguna parte (nueva)

El diseño (línea 124) declara la firma `deploy/deploy-vps.sh <version> [--dry-run] [--sin-backup] [--con-soporte]` y **nunca explica qué hace `--con-soporte`**. No aparece en ningún otro lugar del documento. Opciones: (A) definirlo ahora y especificarlo — p. ej. dejar abierto algo de acceso/diagnóstico extra durante el deploy; (B) **sacarlo de la firma** y no implementarlo hasta que tenga un requerimiento escrito. → **Recomendación: B.** Un flag sin semántica definida es un flag que alguien va a usar suponiendo qué hace. Si se saca, hay que editar la línea 124 del diseño.

**RESUELTA (2026-09-13):** Opción B — sacarlo de la firma de `deploy-vps.sh`. Está en la línea 124 del design doc (`docs/superpowers/specs/2026-09-10-kit-instalacion-deploy-design.md`) y nunca se explicó qué hace. Un flag sin semántica es un flag que alguien va a malinterpretar. **Pendiente, fuera de alcance de este plan:** editar esa línea del design doc — no se edita acá.

### Decisión 11 — Qué hace `deploy-vps.sh` cuando el pre-vuelo encuentra duplicados

El paso 1 (diseño línea 128) consulta duplicados case-insensitive porque la migración del guardián hace `RAISE EXCEPTION` a propósito, `MigrateAsync()` corre antes de `app.Run()` sin try/catch, y tras 5 fallos systemd deja la unit en `failed`. El diseño dice que `--dry-run` corre solo el paso 1, pero **no dice qué hace una corrida normal si el paso 1 encuentra algo**. Opciones: (A) abortar imprimiendo el SQL exacto para inspeccionar; (B) abortar imprimiendo además un script de remediación sugerido, **sin ejecutarlo nunca**; (C) ofrecer arreglarlo interactivamente. → **Recomendación: B.** (C) es un `UPDATE` a ciegas sobre datos del municipio decidido por un script; la deduplicación de nombres es un juicio humano.

**RESUELTA (2026-09-13):** Opción B — abortar mostrando un script de remediación sugerido, sin ejecutarlo nunca. La deduplicación de nombres es juicio humano sobre datos del municipio; la opción (C) sería un `UPDATE` a ciegas.

### Decisión 12 — Firewall del kit LAN (nueva, del relevamiento 2026-09-13)

**El problema.** El kit LAN necesita `API_BIND != 127.0.0.1` para que el desktop llegue por la red — a diferencia del VPS, donde el firewall es un problema ya resuelto en otra capa. Queda por decidir si el kit LAN debe configurar `ufw` automáticamente o solo avisar, como hace hoy `install.sh` para el caso VPS.

**RESUELTA (2026-09-13):** el kit LAN **NO configura `ufw` automáticamente**, solo imprime el aviso, igual que el VPS. Razón: el servidor del municipio probablemente sirve más cosas que la API (impresoras de red, compartidos, accesos de terceros); un `ufw enable` con solo las 3 reglas que conoce el instalador tira abajo todo lo demás. El bloque que imprime el aviso está en `install.sh:211-221` (y el recordatorio final en `install.sh:482`), y **siempre se va a disparar** en instalaciones del municipio, porque el kit LAN requiere `API_BIND != 127.0.0.1` para que el desktop llegue por la red.

**Detalle de implementación propuesto:** como en el municipio no hay operador leyendo la terminal y `stdout` se pierde, el kit debe escribir un `POST-INSTALACION.txt` persistente junto al informe de instalación, con los comandos ya expandidos (el puerto resuelto, no `${API_PORT}` literal). Mismo modelo — imprimir, no ejecutar — pero en un archivo que sobrevive.

### Decisión 13 — Deuda: paridad de versión Postgres sin validación cruzada (nueva, del relevamiento 2026-09-13)

**El problema.** `deploy/docker-compose.postgres.yml:24` fija `postgres:16-alpine` e `install.sh:254` instala `postgresql-client-16` (match correcto hoy, y `DEPLOY.md:36` ya lo documenta). Pero `pg_dump` corre **desde el host**, no vía `docker exec` (`src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs:77-88` usa `Process.Start()` conectando por TCP a `127.0.0.1:5433`), así que las dos versiones quedan hardcodeadas en archivos separados sin ninguna validación cruzada entre ellas.

**ANOTADA (2026-09-13), no bloqueante:** el riesgo es estructural. Si alguien sube la imagen a `postgres:17-alpine` sin tocar `install.sh`, la instalación completa sin avisar y el backup pre-deploy revienta después con "server version mismatch" — justo el paso que protege todos los deploys. **Tarea agregada al preflight (Fase 2):** comparar `pg_dump --version` contra el tag de la imagen del compose (~3 líneas).

---

## Correcciones factuales al diseño (no son decisiones — son erratas verificadas)

Aplicarlas al ejecutar; y si el usuario aprueba, corregir el design doc en un commit aparte (este plan **no** lo toca).

1. **Ruta mal escrita.** Diseño línea 139 dice `tools/StockApp.Configurador/ProbadorConexion.cs`. La ruta real es **`tools/StockApp.Configurador/Servicios/ProbadorConexion.cs`**.
2. **El arreglo #1 ya se resolvió por otra vía.** Ver Decisión 3 (`b9b1d71`).
3. **El Configurador ya viaja dentro del Setup.exe.** `build/pack-win.ps1:36-42,120-132` publica `StockApp.Configurador` en el **mismo** `PublishDir` que la app y `vpk` empaqueta el directorio entero: `GestionMunicipal.Configurador.exe` queda instalado sin acceso directo propio. El `clientes/` del kit **no** necesita un ejecutable aparte. Pero ojo: `pack-win.ps1:161` dice que el artefacto se llama **`Setup.exe`**, no `GestionMunicipal-win-Setup.exe` como asume el diseño (línea 46) — `armar-kit.sh` debe **buscar** el artefacto en `releases/win/`, no adivinar el nombre.
4. **El chequeo 2 de `03-verificar.sh` es casi tautológico en una instalación nueva.** `systemctl cat stockapp-api | grep ASPNETCORE_URLS` distingue "antes/después" en el contexto de una **actualización del VPS**; en una instalación desde cero `install.sh:425` siempre escribe esa línea. Para que el chequeo sirva en el kit tiene que asertar los **valores esperados** (`http://<API_BIND>:<API_PORT>`, leídos del `.env`), no la mera presencia de la línea.

---

## File Structure

### Archivos nuevos

| Ruta | Responsabilidad |
|---|---|
| `deploy/kit/VERSION` | Una línea: la versión de Ubuntu fijada (Decisión 7). Fuente única que leen `armar-kit.sh` y `00-preflight.sh` |
| `deploy/kit/lib/fingerprint.sh` | `fingerprint_de_archivo <ruta>` → código de máquina agrupado. Paridad con C# garantizada por test |
| `deploy/kit/lib/validaciones.sh` | `es_puerto_valido`, `es_ipv4_valida`, `puerto_libre`, `password_admin_valida`, `trim` |
| `deploy/kit/lib/log.sh` | `iniciar_log`, `info`, `ok`, `aviso`, `error_fatal` — el tee a `/var/log/stockapp-kit-<fecha>.log` en un solo lugar |
| `deploy/kit/00-preflight.sh` | Diagnóstico read-only + imprime el código de máquina |
| `deploy/kit/01-bootstrap.sh` | Docker + Postgres + `.env` + firewall |
| `deploy/kit/02-instalar.sh` | Wrapper de `servidor/install.sh` |
| `deploy/kit/03-verificar.sh` | Healthcheck end-to-end de 8 chequeos |
| `deploy/kit/04-licencia.sh` | `fingerprint` / `activar <archivo>` |
| `deploy/kit/LEEME.md` | Procedimiento del día, reserva DHCP, rescate offline |
| `deploy/kit/clientes/LEEME-clientes.md` | Instalación del cliente Windows + Configurador |
| `deploy/kit/clientes/configurar-cliente.cmd` | Solo si Decisión 2 = C: pre-siembra `conexion.json` |
| `deploy/armar-kit.sh` | Arma el pendrive en la máquina del proveedor, con internet |
| `deploy/deploy-vps.sh` | Orquestador local del deploy al VPS |
| `deploy/publish-licencias-cli.sh` | Publica la CLI de licencias self-contained |
| `deploy/PROCEDIMIENTOS.md` | E3: bajada periódica de backups, monitoreo de `/backups/salud` |
| `tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintParidadBashTests.cs` | Guardián de paridad bash ↔ C# |
| `tests/StockApp.Infrastructure.Tests/Kit/ValidacionesBashTests.cs` | Guardianes de `lib/validaciones.sh` |
| `tests/_Compartido/EjecutorBash.cs` | Helper compartido: corre un snippet bash y devuelve stdout/exit code |

### Archivos modificados

| Ruta | Cambio | Gatillado por |
|---|---|---|
| `tools/StockApp.Configurador/Servicios/ResultadoPruebaConexion.cs` | 4º caso `OkLicenciaSinActivar` | Decisión 3 = B |
| `tools/StockApp.Configurador/Servicios/ProbadorConexion.cs` | Sonda a `/licencia/estado` | Decisión 3 = B |
| `tools/StockApp.Configurador/ViewModels/ConfiguradorViewModel.cs:73-80` | Mensaje del 4º caso | Decisión 3 = B |
| `tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj` | `RuntimeIdentifier` + `SelfContained` | Arreglo #3 del diseño |
| `deploy/docker-compose.postgres.yml:28` | `${POSTGRES_PORT:-5433}` | Decisión 4 = B |
| `deploy/wait-for-postgres.sh:19` | Puerto del entorno, default 5433 | Decisión 4 = B |
| `deploy/install.sh:377` | Puerto en la connection string | Decisión 4 = B (**requiere autorización**) |
| `deploy/.env.example` | Documentar `POSTGRES_PORT` | Decisión 4 = B |
| `deploy/DEPLOY.md` | Enlace a `deploy-vps.sh` y a `PROCEDIMIENTOS.md` | Fases 6 y 7 |
| `.gitignore` | Ignorar la salida de `armar-kit.sh` | Fase 4 |

**Nota de decomposición:** `lib/` existe por una razón concreta, no por prolijidad: es la frontera entre "bash con oráculo en C#, testeable hoy en la suite" y "bash que solo se prueba en una VM". Cuanta más lógica viva en `lib/`, más del kit queda cubierto por tests que corren en segundos.

---

## Orden de dependencias entre fases

```
Fase 0 (decisiones + pins)
   │
   ├──────────────► Fase 1 (arreglos C#, TDD real) ──┐
   │                  └─ Task 1.1 produce lib/fingerprint.sh
   │                                                  │
   ├──────────────► Fase 2 (lib bash + preflight) ◄───┘  (consume fingerprint.sh)
   │                        │
   │                        ▼
   │                 Fase 3 (01/02/03/04)
   │                        │
   │                 Fase 4 (armar-kit + offline)  ◄── bloqueada por Decisión 7
   │                        │
   │                        ▼
   │                 Fase 5 (ensayo en VM limpia)  ◄── bloqueada por Decisión 8
   │
   └──────────────► Fase 6 (deploy-vps.sh)   ← INDEPENDIENTE, paralelizable
                    Fase 7 (procedimientos)  ← INDEPENDIENTE, paralelizable
```

**Fases 6 y 7 no dependen de nada del kit.** Son el mejor candidato para correr en paralelo (o primero, si se quiere valor entregado antes de resolver las decisiones bloqueantes).

---

## Fase 0 — Resolver decisiones y fijar el entorno

**CERRADA el 2026-09-13.** Entregable: las trece decisiones (1-11 más 12 y 13, agregadas en el relevamiento de esa sesión) resueltas por el usuario, más los valores fijados por escrito. Ver el bloque `## DECISIONES — RESUELTAS` al inicio de este documento para el detalle de cada resolución.

- [x] **Paso 1: Resolver las Decisiones 1 a 11** con el usuario. Anotada cada respuesta elegida. Además surgieron y se resolvieron las Decisiones 12 (firewall del kit LAN) y 13 (deuda de paridad de versión Postgres, anotada como no bloqueante).
- [x] **Paso 2: Fijar la versión de Ubuntu Server** (Decisión 7): **Ubuntu 24.04 LTS**. Pendiente de ejecución (no de decisión): escribirla en `deploy/kit/VERSION` al arrancar la Fase 4.
- [x] **Paso 3: Elegir el host de VM** (Decisión 8): **Hyper-V**. Pendiente de ejecución: dejar la ISO descargada antes de llegar a la Fase 5.
- [ ] **Paso 4: Instalar `shellcheck`** (Decisión 9c): `sudo apt-get install -y shellcheck`. Verificar con `shellcheck --version`. Sigue pendiente de ejecución.
- [ ] **Paso 5: Crear la rama de trabajo.**

```bash
git checkout -b feat/kit-instalacion-lan
```

**Gate (ya cumplido):** no empezar la Fase 4 sin la Decisión 7 resuelta — resuelta: Ubuntu 24.04 LTS. No empezar la Fase 5 sin la Decisión 8 resuelta — resuelta: Hyper-V. **Ambas fases quedan desbloqueadas** para la ejecución; los pasos 4 y 5 de esta Fase 0 (instalar `shellcheck`, crear la rama) siguen pendientes de ejecución.

---

## Fase 1 — Arreglos con oráculo en C# (el único TDD de verdad del plan)

Las tres cosas de esta fase comparten una propiedad que nada del resto del kit tiene: **se pueden probar hoy, en la suite que ya existe, con un rojo y un verde reales.** El diseño las marca como parte de E1 porque bloquean la instalación real.

### Task 1.1: Paridad del fingerprint bash ↔ C#

Este es el arreglo #2 del diseño (línea 140) y es, de todo el kit, **lo que más barato sale arreglar hoy y más caro sale descubrir allá.** Si el snippet bash difiere del C#, `00-preflight.sh` imprime un código de máquina que no sirve, se emite una licencia contra ese código, y la activación falla en el municipio con "la licencia fue emitida para otra máquina" — un mensaje que te manda a buscar el problema en el lugar equivocado.

**Files:**
- Create: `tests/_Compartido/EjecutorBash.cs`
- Create: `deploy/kit/lib/fingerprint.sh`
- Create: `tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintParidadBashTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj` (agregar el `<Compile Include>` de `EjecutorBash.cs`)

**Interfaces:**
- Produce: `EjecutorBash.Ejecutar(string script)` → `(int ExitCode, string Stdout, string Stderr)`. Lo consumen la Task 1.1 y toda la Fase 2.
- Produce: función bash `fingerprint_de_archivo <ruta>` en `deploy/kit/lib/fingerprint.sh`. La consumen `00-preflight.sh` (Fase 2) y `04-licencia.sh` (Fase 3).
- Consume: `FingerprintMaquinaBase.CodigoAgrupado` (ya existe, `src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaBase.cs:13-29`).

**Por qué el test necesita una subclase propia:** `FingerprintMaquinaLinux` lee `/etc/machine-id` de la máquina real. Para comparar contra un input controlado hay que fijar el id crudo, igual que hace `FingerprintMaquinaTests.FingerprintFijo` (`:11-16`) — pero esa clase es `private` en su archivo, así que este test declara la suya.

- [ ] **Paso 1: Crear el helper de ejecución de bash**

Sigue el patrón de `ProcessStartInfo`/`ArgumentList` que ya usa `RestaurabilidadBackupTests.cs:121-149`.

`tests/_Compartido/EjecutorBash.cs`:

```csharp
using System.Diagnostics;

namespace StockApp.Tests.Compartido;

/// <summary>
/// Corre un snippet de bash y devuelve su salida. Existe para poder testear la librería de
/// shell del kit de instalación (deploy/kit/lib/) desde la MISMA suite de xUnit que todo lo
/// demás, en vez de agregar un segundo runner (bats) con su propia historia de CI.
///
/// Nada de esto se saltea en silencio si falta bash: un guardián que se auto-desactiva es un
/// guardián que se pudre sin que nadie se entere. Este proyecto corre sobre Linux/WSL2 (ver
/// decisión 1 del diseño 2026-09-10: el servidor es Linux), así que la ausencia de bash es un
/// entorno roto, no un caso a tolerar. xUnit v2 (2.5.3, ver Directory.Packages.props) no tiene
/// Assert.Skip, así que tampoco habría forma limpia de saltear.
/// </summary>
internal static class EjecutorBash
{
    internal static (int ExitCode, string Stdout, string Stderr) Ejecutar(string script)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException(
                "Estos tests verifican la librería bash del kit de instalación (servidor Linux) " +
                "y necesitan correr en Linux. Corré la suite desde WSL2.");

        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proceso.StartInfo.ArgumentList.Add("-c");
        proceso.StartInfo.ArgumentList.Add(script);

        proceso.Start();
        var stdout = proceso.StandardOutput.ReadToEnd();
        var stderr = proceso.StandardError.ReadToEnd();
        proceso.WaitForExit();

        return (proceso.ExitCode, stdout.TrimEnd('\n'), stderr.TrimEnd('\n'));
    }

    /// <summary>Raíz del repo, derivada del path de compilación de este archivo.</summary>
    internal static string RaizDelRepo(string archivoDeEsteTest)
    {
        var dir = Path.GetDirectoryName(archivoDeEsteTest)!;   // tests/_Compartido
        return Path.GetFullPath(Path.Combine(dir, "..", ".."));
    }
}
```

- [ ] **Paso 2: Enlazar el helper en el csproj de Infrastructure.Tests**

Mismo mecanismo de `Link` que ya usa `EsperaMonotonica.cs` en ese archivo. Agregar dentro del `<ItemGroup>` que ya contiene ese `<Compile Include>`:

```xml
    <Compile Include="..\_Compartido\EjecutorBash.cs">
      <Link>Compartido\EjecutorBash.cs</Link>
    </Compile>
```

- [ ] **Paso 3: Escribir el test que falla**

`tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintParidadBashTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using StockApp.Infrastructure.Licenciamiento;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Licenciamiento;

/// <summary>
/// Ata el snippet bash de deploy/kit/lib/fingerprint.sh (el que usa 00-preflight.sh para
/// imprimir el código de máquina ANTES de instalar nada) con FingerprintMaquinaBase, que es
/// lo que la API usa para validar la licencia.
///
/// POR QUÉ existe: si los dos difieren, el preflight imprime un código inútil, se emite una
/// licencia contra ese código, y la activación falla EN EL MUNICIPIO con "la licencia fue
/// emitida para otra máquina" -- un mensaje que manda a buscar el problema al lugar
/// equivocado (¿reinstalaron el SO?) cuando en realidad el bug está en el script. Sin este
/// test, el defecto se descubre sin internet y sin poder volver al servidor.
///
/// El borde que importa es el Trim: FingerprintMaquinaLinux.ObtenerIdCrudo hace
/// File.ReadAllText(ruta).Trim() (FingerprintMaquinaLinux.cs:18) y systemd escribe
/// /etc/machine-id CON newline final. Un snippet bash que no trimee no da un valor "parecido":
/// da un hash COMPLETAMENTE distinto (verificado: 4617-8EFE-... con trim vs DBDA-82E1-... sin
/// trim para el mismo archivo).
/// </summary>
public class FingerprintParidadBashTests
{
    // Misma técnica que FingerprintMaquinaTests.FingerprintFijo (:11-16), redeclarada acá
    // porque esa es private en su archivo.
    private sealed class FingerprintFijo : FingerprintMaquinaBase
    {
        private readonly string _id;
        public FingerprintFijo(string id) => _id = id;
        protected override string ObtenerIdCrudo() => _id;
    }

    private static string RutaLibFingerprint([CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeEsteTest = Path.GetDirectoryName(archivoDeEsteTest)!;
        return Path.GetFullPath(Path.Combine(dirDeEsteTest, "..", "..", "..",
            "deploy", "kit", "lib", "fingerprint.sh"));
    }

    private static string FingerprintSegunBash(string contenidoDelArchivo)
    {
        var lib = RutaLibFingerprint();
        Assert.True(File.Exists(lib), $"No se encontró la librería bash en: {lib}");

        var tmp = Path.Combine(Path.GetTempPath(), $"machine-id-{Guid.NewGuid():N}");
        File.WriteAllText(tmp, contenidoDelArchivo);
        try
        {
            var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(
                $"set -euo pipefail; source '{lib}'; fingerprint_de_archivo '{tmp}'");

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            return stdout;
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Bash_CoincideConCSharp_ParaUnMachineIdConNewlineFinal()
    {
        // systemd escribe /etc/machine-id con newline final: este es el caso REAL.
        const string id = "d9f1c0a24b6e4f7a8c3b5d1e9f20a731";

        var segunBash = FingerprintSegunBash(id + "\n");
        var segunCSharp = new FingerprintFijo(id).CodigoAgrupado;

        Assert.Equal(segunCSharp, segunBash);
    }

    [Fact]
    public void Bash_CoincideConCSharp_SinNewlineFinal()
    {
        const string id = "d9f1c0a24b6e4f7a8c3b5d1e9f20a731";

        var segunBash = FingerprintSegunBash(id);
        var segunCSharp = new FingerprintFijo(id).CodigoAgrupado;

        Assert.Equal(segunCSharp, segunBash);
    }

    [Fact]
    public void Bash_TieneElMismoFormatoDe16BloquesHexMayuscula()
    {
        var codigo = FingerprintSegunBash("otra-maquina-cualquiera\n");

        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){15}$", codigo);
    }

    [Fact]
    public void Bash_DifiereEntreMachineIdsDistintos()
    {
        var a = FingerprintSegunBash("maquina-1\n");
        var b = FingerprintSegunBash("maquina-2\n");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Bash_FallaConMensajeClaroSiElArchivoNoExiste()
    {
        var lib = RutaLibFingerprint();

        var (exitCode, _, stderr) = EjecutorBash.Ejecutar(
            $"set -euo pipefail; source '{lib}'; fingerprint_de_archivo '/no/existe/machine-id'");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("machine-id", stderr);
    }
}
```

- [ ] **Paso 4: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter FullyQualifiedName~FingerprintParidadBashTests`

Esperado: FALLA. Los 5 tests fallan en el `Assert.True(File.Exists(lib), ...)` con "No se encontró la librería bash en: .../deploy/kit/lib/fingerprint.sh" — porque todavía no existe.

- [ ] **Paso 5: Escribir la implementación mínima**

`deploy/kit/lib/fingerprint.sh`. **El snippet está verificado empíricamente** (ver el comentario): produce `4617-8EFE-BE2F-9570-6C99-6BED-2874-10E2-7FC4-790A-3B7F-4C7C-81ED-3665-505C-EFC5` para el id `d9f1c0a24b6e4f7a8c3b5d1e9f20a731`, con y sin newline final.

```bash
#!/usr/bin/env bash
# Librería sourceable: calcula el código de máquina de licenciamiento sin necesidad de que la
# API esté instalada. Eso es lo que permite que 00-preflight.sh imprima el fingerprint apenas
# llegás al servidor, y que la licencia viaje en paralelo mientras seguís instalando.
#
# PARIDAD CON C#: tiene que dar EXACTAMENTE lo mismo que
# FingerprintMaquinaBase.CodigoAgrupado sobre el mismo id crudo
# (src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaBase.cs:13-29), que es lo que
# la API usa para validar la licencia. El guardián que lo ata es
# tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintParidadBashTests.cs -- si
# tocás este archivo, ese test tiene que seguir verde.
#
# Algoritmo (los 3 pasos importan y ninguno es decorativo):
#   1. TRIM del contenido. FingerprintMaquinaLinux.cs:18 hace .Trim() y systemd escribe
#      /etc/machine-id CON newline final. Sin trim el hash sale completamente distinto
#      (verificado: 4617-8EFE-... con trim, DBDA-82E1-... sin trim, mismo archivo) -- no es un
#      "casi", es un fingerprint inútil.
#   2. SHA-256 de los bytes UTF-8 de ese string (no del archivo: ojo, 'sha256sum archivo'
#      hashea el newline; hay que pipear el string trimeado).
#   3. Hex en MAYÚSCULAS (Convert.ToHexString de C# devuelve mayúsculas) agrupado de a 4 con
#      guiones -> 16 grupos para los 64 chars del SHA-256.

# Rutas del id crudo, en el MISMO orden que FingerprintMaquinaLinux.Rutas (:6-10).
readonly KIT_RUTAS_MACHINE_ID=(
    "/etc/machine-id"
    "/var/lib/dbus/machine-id"
)

# fingerprint_de_archivo <ruta> -> imprime el código agrupado por stdout.
fingerprint_de_archivo() {
    local ruta="$1" id

    if [[ ! -f "$ruta" ]]; then
        echo "ERROR: no se pudo leer el machine-id en '${ruta}'." >&2
        return 1
    fi

    # $(<archivo) descarta los newlines finales; las dos expansiones siguientes completan el
    # trim de cualquier whitespace al principio y al final, igual que String.Trim() de C#.
    id="$(<"$ruta")"
    id="${id#"${id%%[![:space:]]*}"}"
    id="${id%"${id##*[![:space:]]}"}"

    if [[ -z "$id" ]]; then
        echo "ERROR: el machine-id en '${ruta}' está vacío." >&2
        return 1
    fi

    printf '%s' "$id" \
        | sha256sum \
        | cut -d' ' -f1 \
        | tr 'a-f' 'A-F' \
        | sed 's/.\{4\}/&-/g; s/-$//'
}

# fingerprint_de_esta_maquina -> recorre KIT_RUTAS_MACHINE_ID como hace FingerprintMaquinaLinux.
fingerprint_de_esta_maquina() {
    local ruta
    for ruta in "${KIT_RUTAS_MACHINE_ID[@]}"; do
        if [[ -f "$ruta" && -s "$ruta" ]]; then
            fingerprint_de_archivo "$ruta"
            return 0
        fi
    done

    echo "ERROR: no se pudo leer /etc/machine-id (ni el fallback de dbus)." >&2
    return 1
}
```

- [ ] **Paso 6: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter FullyQualifiedName~FingerprintParidadBashTests`

Esperado: PASS, 5 de 5.

- [ ] **Paso 7: Verificación POR MUTACIÓN del guardián**

Un test que pasa no prueba que vigile algo. Hay que romper la implementación y ver el rojo.

```bash
cp deploy/kit/lib/fingerprint.sh /tmp/fingerprint.sh.bak
```

Mutación: sacar el trim. Reemplazar las tres líneas del trim por `id="$(cat "$ruta")"` y el `printf '%s' "$id" | sha256sum` por `sha256sum < "$ruta"`. Correr el filtro de nuevo.

Esperado: **ROJO** en `Bash_CoincideConCSharp_ParaUnMachineIdConNewlineFinal`. Pegar el rojo en el commit o en el reporte.

Restaurar (`git checkout --` borraría el archivo entero porque todavía no está commiteado — usar la copia):

```bash
cp /tmp/fingerprint.sh.bak deploy/kit/lib/fingerprint.sh && rm /tmp/fingerprint.sh.bak
```

Segunda mutación: cambiar `tr 'a-f' 'A-F'` por nada (dejar el hex minúscula). Esperado: **ROJO** en los 5 tests. Restaurar igual.

- [ ] **Paso 8: Pasar shellcheck**

Correr: `shellcheck deploy/kit/lib/fingerprint.sh`

Esperado: sin hallazgos. Si aparece algo, arreglarlo y volver al Paso 6 (el test tiene que seguir verde).

- [ ] **Paso 9: Commit**

```bash
chmod +x deploy/kit/lib/fingerprint.sh
git add deploy/kit/lib/fingerprint.sh \
        tests/_Compartido/EjecutorBash.cs \
        tests/StockApp.Infrastructure.Tests/Licenciamiento/FingerprintParidadBashTests.cs \
        tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj
git commit -m "feat(kit): calcula el fingerprint de licencia en bash con paridad verificada contra C#"
```

### Task 1.2: La CLI de licencias, publicable sin SDK

Arreglo #3 del diseño (línea 141). Hoy emitir una licencia exige `dotnet run --project tools/StockApp.Licencias.Cli` (ver `deploy/DEPLOY.md:265-269`), o sea el **SDK de .NET 10** en la máquina desde la que se emite. El día de la instalación eso es una dependencia de más en la laptop del proveedor.

**Files:**
- Modify: `tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj`
- Create: `deploy/publish-licencias-cli.sh`

**Interfaces:**
- Consume: los comandos ya existentes de `tools/StockApp.Licencias.Cli/Program.cs`: `generar-claves --salida <dir> [--forzar]`, `emitir-licencia --clave <pem> --cliente <nombre> --maquina <codigo>`, `emitir-reset --clave <pem> --maquina <codigo> --desafio <texto>`.
- Produce: ejecutable self-contained en `deploy/dist/licencias-cli-<rid>/` + `stockapp-licencias-<version>-<rid>.tar.gz`.

**Nota de alcance:** el `.csproj` declara `<IsPackable>false</IsPackable>` con el comentario "Herramienta interna del desarrollador: NUNCA se empaqueta ni distribuye" (`:9-10`). Eso **sigue siendo cierto y no cambia**: esto publica un binario para la máquina del proveedor, no algo que viaje al pendrive ni al servidor. El comentario merece una línea aclaratoria, no una baja.

- [ ] **Paso 1: Verificar el estado actual (el rojo de esta tarea)**

Correr: `dotnet publish tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj -c Release -r linux-x64 --self-contained true -o /tmp/licencias-probe 2>&1 | tail -20`

Anotar el resultado. Si publica bien sin tocar nada, el `.csproj` solo necesita los defaults para no tener que pasar los flags a mano; si falla, el mensaje dice qué falta. **No inventar el diagnóstico: pegarlo.**

- [ ] **Paso 2: Agregar RID y SelfContained al csproj**

En el `<PropertyGroup>` existente, después de `<IsPackable>false</IsPackable>`:

```xml
    <!-- Publicación self-contained (deploy/publish-licencias-cli.sh): emitir una licencia el
         día de una instalación no debería exigir el SDK de .NET en la laptop del proveedor.
         IsPackable sigue en false -- esto NO se distribuye al cliente ni viaja al pendrive ni
         al servidor: es un binario para la máquina de quien tiene la clave privada. -->
    <RuntimeIdentifiers>linux-x64;win-x64</RuntimeIdentifiers>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <InvariantGlobalization>true</InvariantGlobalization>
```

- [ ] **Paso 3: Escribir el script de publicación**

`deploy/publish-licencias-cli.sh`. Sigue el molde de `deploy/publish-api.sh` (mismas guardas de prerrequisitos, mismo `deploy/dist/`, mismo estilo de mensajes `[publish-...]`):

```bash
#!/usr/bin/env bash
set -euo pipefail

# Publica tools/StockApp.Licencias.Cli self-contained, para poder emitir licencias y tokens de
# reset sin tener el SDK de .NET instalado.
#
# POR QUÉ: el día de una instalación, emitir la licencia es el paso que está en el camino
# crítico (ver deploy/kit/LEEME.md: el fingerprint sale del preflight y la licencia se emite en
# paralelo mientras seguís instalando). Depender del SDK ahí es una dependencia de más en la
# única máquina que tiene la clave privada.
#
# La clave PRIVADA no se empaqueta acá ni en ningún otro lado: vive en ~/stockapp-claves y se
# le pasa por --clave en cada invocación.
#
# Uso:
#   deploy/publish-licencias-cli.sh                 # linux-x64, versión = timestamp UTC
#   deploy/publish-licencias-cli.sh 1.0.0 win-x64   # versión y RID explícitos

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROYECTO="${REPO_ROOT}/tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj"
SALIDA_DIR="${REPO_ROOT}/deploy/dist"

VERSION="${1:-$(date -u +%Y%m%d%H%M%S)}"
RID="${2:-linux-x64}"
PUBLISH_DIR="${SALIDA_DIR}/licencias-cli-${RID}"
TARBALL="${SALIDA_DIR}/stockapp-licencias-${VERSION}-${RID}.tar.gz"

echo "[publish-licencias] Verificando prerrequisitos..."

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: 'dotnet' no está en el PATH. Este script necesita el SDK -- justamente para" >&2
    echo "       producir un binario que después NO lo necesite." >&2
    exit 1
fi

if [[ ! -f "$PROYECTO" ]]; then
    echo "ERROR: no se encontró el proyecto en '${PROYECTO}'." >&2
    exit 1
fi

echo "[publish-licencias] Publicando (Release, ${RID}, self-contained) — versión ${VERSION}..."
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$PROYECTO" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -o "$PUBLISH_DIR"

EJECUTABLE="${PUBLISH_DIR}/StockApp.Licencias.Cli"
[[ "$RID" == win-* ]] && EJECUTABLE="${EJECUTABLE}.exe"

if [[ ! -f "$EJECUTABLE" ]]; then
    echo "ERROR: el publish no generó '${EJECUTABLE}'. Revisá la salida de 'dotnet publish' arriba." >&2
    exit 1
fi

echo "[publish-licencias] Empaquetando en '${TARBALL}'..."
rm -f "$TARBALL"
tar -czf "$TARBALL" -C "$PUBLISH_DIR" .

echo "[publish-licencias] OK: ${TARBALL}"
echo "[publish-licencias] Ejecutable listo: ${EJECUTABLE}"
echo "[publish-licencias] Probalo:  ${EJECUTABLE}"
```

- [ ] **Paso 4: Verificar que el binario corre y no necesita el SDK**

```bash
chmod +x deploy/publish-licencias-cli.sh
deploy/publish-licencias-cli.sh
deploy/dist/licencias-cli-linux-x64/StockApp.Licencias.Cli
```

Esperado: imprime la ayuda y sale con código 1 (ver `Program.cs:6-10`: sin argumentos imprime ayuda y `return 1`).

Prueba real de punta a punta, con una clave descartable:

```bash
deploy/dist/licencias-cli-linux-x64/StockApp.Licencias.Cli generar-claves --salida /tmp/claves-prueba
deploy/dist/licencias-cli-linux-x64/StockApp.Licencias.Cli emitir-licencia \
  --clave /tmp/claves-prueba/clave-privada.pem \
  --cliente "Prueba" --maquina 4617-8EFE-BE2F-9570-6C99-6BED-2874-10E2-7FC4-790A-3B7F-4C7C-81ED-3665-505C-EFC5
```

Esperado: imprime un string de licencia de una línea. Limpiar después: `rm -rf /tmp/claves-prueba`.

- [ ] **Paso 5: shellcheck**

Correr: `shellcheck deploy/publish-licencias-cli.sh`. Esperado: sin hallazgos.

- [ ] **Paso 6: Confirmar que no rompiste la suite de la CLI**

Correr: `dotnet test tests/StockApp.Licencias.Cli.Tests/StockApp.Licencias.Cli.Tests.csproj`

Esperado: verde. Si el `RuntimeIdentifiers` agregado rompe la referencia del proyecto de test, **reportarlo** en vez de quitar la propiedad a ciegas.

- [ ] **Paso 7: Commit**

```bash
git add deploy/publish-licencias-cli.sh tools/StockApp.Licencias.Cli/StockApp.Licencias.Cli.csproj
git commit -m "feat(licencias): publica la CLI self-contained para emitir licencias sin el SDK"
```

### Task 1.3: El Configurador distingue "conecta pero sin licencia"

**GATE: esta tarea solo se ejecuta si la Decisión 3 se resolvió como B o C.** Si se resolvió A, saltearla y en su lugar corregir el arreglo #1 del design doc.

Lo que sigue asume **B** (sonda a `/licencia/estado` + 4º caso). Si se eligió C, la diferencia es que `GET /` sigue siendo la sonda primaria y `/licencia/estado` se consulta solo tras un Ok.

**Files:**
- Modify: `tools/StockApp.Configurador/Servicios/ResultadoPruebaConexion.cs`
- Modify: `tools/StockApp.Configurador/Servicios/ProbadorConexion.cs`
- Modify: `tools/StockApp.Configurador/ViewModels/ConfiguradorViewModel.cs:73-80`
- Test: `tests/StockApp.Configurador.Tests/ProbadorConexionTests.cs`

**Interfaces:**
- Consume: `GET /licencia/estado` → `200 {"activada":bool,"codigoMaquina":"..."}` (`LicenciaEndpoints.cs:18-19`, `LicenciaEstadoResponse`). Anónimo y en la allowlist, responde igual con y sin licencia.
- Produce: `ResultadoPruebaConexion` con cuatro casos: `Ok`, `OkLicenciaSinActivar`, `RespondeOtraCosa`, `NoResponde`.

**Cambio de contrato a tener presente:** los tests existentes de `ProbadorConexionTests` montan un `HttpListener` que responde `{"status":"ok","service":"StockApp.Api"}` — el shape de `GET /`. Al mover la sonda a `/licencia/estado`, **el shape esperado pasa a ser `{"activada":...,"codigoMaquina":"..."}`** y esos fixtures hay que actualizarlos. No es un test que "se rompió": es el contrato que cambió, y los tests lo están reportando bien.

- [ ] **Paso 1: Escribir los tests que fallan**

Reemplazar el cuerpo de los tests de `tests/StockApp.Configurador.Tests/ProbadorConexionTests.cs` manteniendo intactos los helpers `IniciarListener` y `ObtenerPuertoLibre` (`:18-57`), y actualizar el docblock de la clase. Los casos nuevos y actualizados:

```csharp
    [Fact]
    public async Task ProbarAsync_LicenciaActivada_DevuelveOk()
    {
        var (listener, url) = IniciarListener(
            "{\"activada\":true,\"codigoMaquina\":\"4617-8EFE-BE2F-9570\"}");
        try
        {
            var resultado = await new ProbadorConexion().ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.Ok, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    /// <summary>
    /// El estado por el que pasa TODA instalación nueva: la API está viva y es la correcta,
    /// pero la licencia todavía no se activó. Antes esto era indistinguible de "funciona": el
    /// operador veía verde y no sabía que le faltaba un paso.
    /// </summary>
    [Fact]
    public async Task ProbarAsync_ApiVivaPeroLicenciaSinActivar_DevuelveOkLicenciaSinActivar()
    {
        var (listener, url) = IniciarListener(
            "{\"activada\":false,\"codigoMaquina\":\"4617-8EFE-BE2F-9570\"}");
        try
        {
            var resultado = await new ProbadorConexion().ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.OkLicenciaSinActivar, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    /// <summary>
    /// Guardia contra el falso positivo inverso: otra cosa escuchando en ese puerto que
    /// devuelve 200 con JSON cualquiera NO es la API de Gestión Municipal. El shape se valida,
    /// no solo el status code -- mismo criterio que el guardián de la API
    /// (BloqueoLicenciaTests.Bloqueada_Raiz_DevuelveBannerAnonimo).
    /// </summary>
    [Fact]
    public async Task ProbarAsync_JsonSinElShapeDeLicenciaEstado_DevuelveRespondeOtraCosa()
    {
        var (listener, url) = IniciarListener("{\"status\":\"ok\",\"service\":\"otra-cosa\"}");
        try
        {
            var resultado = await new ProbadorConexion().ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.RespondeOtraCosa, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }
```

Los cuatro tests restantes (`RespondeConHttpErrorStatus`, `RespondeConCuerpoNoJson`, `SinNadieEscuchandoEnElPuerto`, `UrlMalformada`) se conservan tal cual: su comportamiento esperado no cambia.

- [ ] **Paso 2: Correr y verificar que fallan**

Correr: `dotnet test tests/StockApp.Configurador.Tests/StockApp.Configurador.Tests.csproj --filter FullyQualifiedName~ProbadorConexionTests`

Esperado: FALLA. `OkLicenciaSinActivar` no compila (no existe en el enum) — o sea, el rojo es un error de compilación, que es un rojo legítimo para un caso nuevo de enum.

- [ ] **Paso 3: Agregar el cuarto caso al enum**

En `tools/StockApp.Configurador/Servicios/ResultadoPruebaConexion.cs`, después de `Ok`:

```csharp
    /// <summary>
    /// HTTP 200 con el shape de /licencia/estado, pero "activada": false. La API es la correcta
    /// y responde — falta activar la licencia. Es el estado por el que pasa toda instalación
    /// nueva (ver deploy/kit/LEEME.md: el paso de licencia va ANTES de tocar los clientes), así
    /// que mostrarlo como un simple "OK" esconde un paso pendiente.
    /// </summary>
    OkLicenciaSinActivar,
```

Y actualizar el docblock de `Ok` para que diga que el cuerpo es el de `/licencia/estado` con `activada: true`, no el de `GET /`.

- [ ] **Paso 4: Cambiar la sonda**

En `tools/StockApp.Configurador/Servicios/ProbadorConexion.cs`, reemplazar la construcción de la URL (`:26`) y el bloque de parseo (`:55-73`):

```csharp
            var url = baseUrl.TrimEnd('/') + "/licencia/estado";
```

```csharp
            var contenido = await respuesta.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(contenido);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("activada", out var activada) &&
                    (activada.ValueKind == JsonValueKind.True || activada.ValueKind == JsonValueKind.False) &&
                    doc.RootElement.TryGetProperty("codigoMaquina", out var codigo) &&
                    codigo.ValueKind == JsonValueKind.String)
                {
                    return activada.GetBoolean()
                        ? ResultadoPruebaConexion.Ok
                        : ResultadoPruebaConexion.OkLicenciaSinActivar;
                }
            }
            catch (JsonException)
            {
                // Respondió pero el cuerpo no es JSON: es "otra cosa" respondiendo en ese puerto.
            }

            return ResultadoPruebaConexion.RespondeOtraCosa;
```

Y reescribir el docblock de la clase (`:9-14`), que hoy dice "Pega a GET / de la API (... Program.cs:675)":

```csharp
/// <summary>
/// Pega a GET /licencia/estado de la API y distingue cuatro casos. Timeout corto (4s, no los
/// 10s del cliente principal del desktop): acá "no responde" es el caso más común y el usuario
/// está mirando la ventana en vivo, esperando el resultado.
///
/// POR QUÉ /licencia/estado y no GET /: es anónimo, está en la allowlist de
/// BloqueoLicenciaMiddleware, y responde 200 con un shape verificable TANTO con la licencia
/// activada como sin ella (LicenciaEndpoints.cs:18-19) -- así que además de probar
/// conectividad, dice si falta activar la licencia, que es el estado por el que pasa toda
/// instalación nueva. Es el mismo endpoint y el mismo motivo por el que install.sh eligió este
/// healthcheck (install.sh:457-463).
/// </summary>
```

- [ ] **Paso 5: Agregar el mensaje en el ViewModel**

En `tools/StockApp.Configurador/ViewModels/ConfiguradorViewModel.cs`, dentro del `switch` de resultado (`:73-80`), agregar el brazo del caso nuevo. Usar la misma forma `(MensajeEstado, ClaseEstado)` que los otros tres y la clase visual de advertencia que ya exista en ese archivo — **leer los tres brazos existentes y copiar su convención exacta**, no inventar un nombre de clase.

El texto debe ser accionable, no un código de estado: conecta con el servidor, pero la licencia todavía no está activada; que el proveedor la active antes de usar el sistema.

- [ ] **Paso 6: Correr y verificar que pasan**

Correr: `dotnet test tests/StockApp.Configurador.Tests/StockApp.Configurador.Tests.csproj`

Esperado: verde, incluidos `ConfiguradorViewModelTests` y `BrandingConfiguradorTests`. Si `ConfiguradorViewModelTests` falla por el brazo nuevo del switch, actualizarlo — es parte de esta tarea.

- [ ] **Paso 7: Verificación POR MUTACIÓN**

Revertir el ternario del Paso 4 a `return ResultadoPruebaConexion.Ok;` (sin mirar `activada`). Correr el filtro.

Esperado: **ROJO** en `ProbarAsync_ApiVivaPeroLicenciaSinActivar_DevuelveOkLicenciaSinActivar`. Pegar el rojo. Restaurar el ternario.

- [ ] **Paso 8: Confirmar que el guardián de la API sigue verde**

La raíz sigue estando en la allowlist (`b9b1d71`) y eso no cambia acá, pero conviene confirmar que no se rompió nada del lado servidor:

Correr: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter FullyQualifiedName~BloqueoLicenciaTests`

Esperado: verde.

- [ ] **Paso 9: Verificación orgánica**

Levantar la API local contra el Postgres de desarrollo, **sin licencia activada**, abrir el Configurador y pulsar "Probar conexión". Confirmar que el mensaje dice que conecta pero falta la licencia, y que no es ni el verde pleno ni el amarillo de "no es la API". Capturar la ventana con `scripts/gui-verificacion/capturar.sh`.

- [ ] **Paso 10: Commit**

```bash
git add tools/StockApp.Configurador/Servicios/ProbadorConexion.cs \
        tools/StockApp.Configurador/Servicios/ResultadoPruebaConexion.cs \
        tools/StockApp.Configurador/ViewModels/ConfiguradorViewModel.cs \
        tests/StockApp.Configurador.Tests/ProbadorConexionTests.cs
git commit -m "feat(configurador): distingue API viva con licencia sin activar al probar conexion"
```

---

## Fase 2 — Librería bash testeable y `00-preflight.sh`

**Depende de:** Fase 1 Task 1.1 (el helper `EjecutorBash` y `lib/fingerprint.sh`).

La idea de esta fase es mover la mayor cantidad posible de lógica del preflight a funciones puras que se testean en la suite, y dejar en el script solo el cableado (qué función se llama, con qué severidad se reporta). Lo que queda en el script se verifica en la Fase 5.

### Task 2.1: `lib/validaciones.sh` y `lib/log.sh` con guardianes

**Files:**
- Create: `deploy/kit/lib/validaciones.sh`
- Create: `deploy/kit/lib/log.sh`
- Create: `tests/StockApp.Infrastructure.Tests/Kit/ValidacionesBashTests.cs`

**Interfaces:**
- Consume: `EjecutorBash.Ejecutar` (Task 1.1).
- Produce, en `validaciones.sh`: `es_puerto_valido <n>`, `es_ipv4_valida <ip>`, `puerto_libre <n>`, `password_admin_valida <pass>`, `subred_de <ip> <mascara>`. Las consumen `00-preflight.sh` (Task 2.2) y `01-bootstrap.sh` (Fase 3).
- Produce, en `log.sh`: `iniciar_log`, `info <msg>`, `ok <msg>`, `aviso <msg>`, `error_fatal <msg>` (este último hace `exit 1`). Las consumen los cinco scripts numerados.

**Decisión de implementación:** `es_ipv4_valida` se copia **literalmente** de `install.sh:187-197`, incluido el `10#$octeto` que fuerza base 10 (sin eso, un octeto como `008` se interpreta como octal y da un error de runtime feo en vez de un rechazo limpio — el comentario de `install.sh:191-193` ya lo explica). Duplicar 10 líneas es preferible a que el kit haga `source` de `install.sh`, que tiene efectos al cargarse.

- [ ] **Paso 1: Escribir los tests que fallan**

`tests/StockApp.Infrastructure.Tests/Kit/ValidacionesBashTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/kit/lib/validaciones.sh. La razón de que esta lógica viva en funciones
/// sourceables y no inline en 00-preflight.sh es exactamente esta: así se puede probar acá, en
/// segundos, en vez de únicamente en una VM limpia.
///
/// password_admin_valida espeja ContrasenaValidator (mínimo 8, al menos una letra y un número,
/// src/StockApp.Application/Auth/ContrasenaValidator.cs:9,23-25). Si el kit acepta una
/// contraseña que la API va a rechazar, el bootstrap del admin falla DESPUÉS de que ya se
/// generó el .env y se levantó Postgres -- el peor momento para descubrirlo.
/// </summary>
public class ValidacionesBashTests
{
    private static string RutaLib([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..",
            "deploy", "kit", "lib", "validaciones.sh"));
    }

    private static int Ejecutar(string funcionYArgs)
    {
        var lib = RutaLib();
        Assert.True(File.Exists(lib), $"No se encontró la librería bash en: {lib}");

        // Sin 'set -e': acá nos interesa el exit code de la función, no abortar al primer fallo.
        var (exitCode, _, _) = EjecutorBash.Ejecutar($"source '{lib}'; {funcionYArgs}");
        return exitCode;
    }

    [Theory]
    [InlineData("5080")]
    [InlineData("5433")]
    [InlineData("1")]
    [InlineData("65535")]
    public void EsPuertoValido_AceptaPuertosEnRango(string puerto)
        => Assert.Equal(0, Ejecutar($"es_puerto_valido '{puerto}'"));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("99999")]
    [InlineData("")]
    [InlineData("5080a")]
    [InlineData("-1")]
    [InlineData("50 80")]
    public void EsPuertoValido_RechazaLoQueNoEsUnPuerto(string puerto)
        => Assert.NotEqual(0, Ejecutar($"es_puerto_valido '{puerto}'"));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.50")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void EsIpv4Valida_AceptaIpsBienFormadas(string ip)
        => Assert.Equal(0, Ejecutar($"es_ipv4_valida '{ip}'"));

    /// <summary>
    /// El caso "008" es el que motivó el 10# de install.sh:191-193: sin forzar base 10, bash
    /// interpreta el cero a la izquierda como octal y "8" no es un dígito octal válido -- error
    /// de runtime ("value too great for base") en vez de un rechazo limpio. Acá tiene que
    /// RECHAZAR con exit != 0, no explotar.
    /// </summary>
    [Theory]
    [InlineData("256.1.1.1")]
    [InlineData("1.1.1")]
    [InlineData("1.1.1.1.1")]
    [InlineData("192.168.1.300")]
    [InlineData("no-es-una-ip")]
    [InlineData("")]
    [InlineData("::1")]
    public void EsIpv4Valida_RechazaLoQueNoEsUnaIpv4(string ip)
        => Assert.NotEqual(0, Ejecutar($"es_ipv4_valida '{ip}'"));

    [Theory]
    [InlineData("admin123")]
    [InlineData("Carmelo2026")]
    [InlineData("a1b2c3d4")]
    public void PasswordAdminValida_AceptaLoQueLaApiAcepta(string pass)
        => Assert.Equal(0, Ejecutar($"password_admin_valida '{pass}'"));

    [Theory]
    [InlineData("corto1")]         // 6 caracteres
    [InlineData("abcdefgh")]       // 8 pero sin número
    [InlineData("12345678")]       // 8 pero sin letra
    [InlineData("")]               // vacía
    [InlineData("       ")]        // solo whitespace
    public void PasswordAdminValida_RechazaLoQueLaApiVaARechazar(string pass)
        => Assert.NotEqual(0, Ejecutar($"password_admin_valida '{pass}'"));
}
```

- [ ] **Paso 2: Correr y verificar que fallan**

Correr: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter FullyQualifiedName~ValidacionesBashTests`

Esperado: FALLA, todos, en `Assert.True(File.Exists(lib), ...)`.

- [ ] **Paso 3: Escribir `lib/validaciones.sh`**

```bash
#!/usr/bin/env bash
# Validaciones puras del kit de instalación. Sin efectos secundarios y sin imprimir nada: cada
# función comunica por exit code, para que el script que la llama decida la severidad y el
# mensaje. Testeadas en
# tests/StockApp.Infrastructure.Tests/Kit/ValidacionesBashTests.cs.

# es_puerto_valido <n> -> 0 si es un puerto TCP en rango 1-65535.
es_puerto_valido() {
    local p="${1:-}"
    [[ "$p" =~ ^[0-9]+$ ]] || return 1
    (( 10#$p >= 1 && 10#$p <= 65535 ))
}

# es_ipv4_valida <ip> -> 0 si es una IPv4 bien formada.
#
# Copiado LITERALMENTE de install.sh:187-197, incluido el "10#$octeto": sin forzar base 10, un
# octeto con cero a la izquierda (p.ej. "008") se interpreta como octal y "8" no es un dígito
# octal válido -- error de runtime feo en vez de un rechazo limpio. Es duplicación deliberada:
# hacer source de install.sh desde el kit tendría efectos al cargarse (valida argumentos, toca
# el sistema), que es exactamente lo que no queremos en una función de validación.
es_ipv4_valida() {
    local ip="${1:-}" octeto
    [[ "$ip" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ ]] || return 1
    for octeto in ${ip//./ }; do
        (( 10#$octeto <= 255 )) || return 1
    done
    return 0
}

# password_admin_valida <pass> -> 0 si la API la va a aceptar.
#
# Espeja ContrasenaValidator.Validar (src/StockApp.Application/Auth/ContrasenaValidator.cs:
# 9,23-25): mínimo 8 caracteres, al menos una letra y al menos un número. Validar acá, ANTES de
# escribir el .env, evita que el bootstrap del admin falle recién cuando la API arranca -- con
# Postgres ya levantado y el .env ya generado, que es el estado más incómodo para corregir.
password_admin_valida() {
    local pass="${1:-}"
    [[ -n "${pass//[[:space:]]/}" ]] || return 1
    (( ${#pass} >= 8 )) || return 1
    [[ "$pass" == *[[:alpha:]]* ]] || return 1
    [[ "$pass" == *[[:digit:]]* ]] || return 1
    return 0
}

# puerto_libre <n> -> 0 si NADIE escucha en ese puerto TCP.
#
# ss viene en iproute2, presente en Ubuntu Server base (no necesita el netstat de net-tools,
# que NO está instalado por defecto desde hace varias releases).
puerto_libre() {
    local p="$1"
    ! ss -lntH "sport = :${p}" 2>/dev/null | grep -q .
}

# quien_escucha <n> -> imprime el/los procesos que escuchan en ese puerto (para diagnóstico).
quien_escucha() {
    local p="$1"
    ss -lntpH "sport = :${p}" 2>/dev/null || true
}
```

- [ ] **Paso 4: Correr y verificar que pasan**

Correr: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter FullyQualifiedName~ValidacionesBashTests`

Esperado: PASS en los 27 casos.

- [ ] **Paso 5: Verificación POR MUTACIÓN**

```bash
cp deploy/kit/lib/validaciones.sh /tmp/validaciones.sh.bak
```

Mutación 1: en `es_ipv4_valida`, quitar el `10#` (dejar `(( octeto <= 255 ))`). Correr el filtro. Esperado: **ROJO** o error de runtime en el caso de octeto con cero a la izquierda. Restaurar con `cp`.

Mutación 2: en `password_admin_valida`, bajar el mínimo a 6. Esperado: **ROJO** en `PasswordAdminValida_RechazaLoQueLaApiVaARechazar("corto1")`. Restaurar con `cp`.

Mutación 3: en `password_admin_valida`, borrar la línea del `[[:digit:]]`. Esperado: **ROJO** en el caso `"abcdefgh"`. Restaurar y `rm /tmp/validaciones.sh.bak`.

- [ ] **Paso 6: Escribir `lib/log.sh`**

No lleva tests propios: es puro efecto de E/S, y su corrección se observa en la Fase 5 (que el log exista y contenga lo impreso).

```bash
#!/usr/bin/env bash
# Salida unificada de los scripts del kit. Regla transversal del diseño 2026-09-10 (línea 55):
# todo lo que se imprime va TAMBIÉN a /var/log/stockapp-kit-<fecha>.log.
#
# POR QUÉ importa: el proveedor no va a poder volver al servidor. Si algo sale raro, el log es
# la única evidencia de qué pasó -- y tiene que quedar EN el servidor (para quien tenga acceso
# local después) y poder copiarse al pendrive antes de irse.

KIT_LOG="${KIT_LOG:-/var/log/stockapp-kit-$(date +%Y%m%d).log}"

# iniciar_log -> redirige stdout y stderr a tee, para que todo quede en el log sin tener que
# acordarse de pipear cada echo. Idempotente: llamarla dos veces no duplica la redirección
# porque se invoca una sola vez por script, al principio.
iniciar_log() {
    if ! touch "$KIT_LOG" 2>/dev/null; then
        echo "AVISO: no se puede escribir en '${KIT_LOG}' (¿hace falta sudo?). Sigo sin log a archivo." >&2
        return 0
    fi
    chmod 600 "$KIT_LOG" 2>/dev/null || true
    exec > >(tee -a "$KIT_LOG") 2>&1
    echo "=== $(date -Is) — $(basename "${BASH_SOURCE[1]:-script}") ==="
}

info()  { echo "      $*"; }
ok()    { echo "  OK  $*"; }
aviso() { echo "  !!  $*"; }
rojo()  { echo "  XX  $*"; }

# error_fatal <msg> -> imprime y termina con 1.
error_fatal() {
    echo
    echo "ERROR: $*" >&2
    echo "       Nada más se ejecutó. El log está en: ${KIT_LOG}" >&2
    exit 1
}
```

- [ ] **Paso 7: shellcheck**

Correr: `shellcheck deploy/kit/lib/validaciones.sh deploy/kit/lib/log.sh`

Esperado: sin hallazgos. `log.sh` probablemente dispare un aviso por el `exec > >(tee ...)`; si es un falso positivo, silenciarlo con un `# shellcheck disable=` **que incluya el motivo en un comentario**, nunca a secas.

- [ ] **Paso 8: Commit**

```bash
chmod +x deploy/kit/lib/validaciones.sh deploy/kit/lib/log.sh
git add deploy/kit/lib/validaciones.sh deploy/kit/lib/log.sh \
        tests/StockApp.Infrastructure.Tests/Kit/ValidacionesBashTests.cs
git commit -m "feat(kit): agrega validaciones bash con guardianes que espejan las reglas de la API"
```

### Task 2.2: `00-preflight.sh`

**Depende de:** Tasks 1.1 y 2.1.

Es read-only por contrato: **no toca nada**. Su valor es que el operador sepa, antes de modificar el servidor, si va a poder terminar — y que se lleve el código de máquina para emitir la licencia en paralelo.

**Files:**
- Create: `deploy/kit/00-preflight.sh`
- Create: `deploy/kit/VERSION` (si la Fase 0 no lo creó)

**Interfaces:**
- Consume: `lib/log.sh`, `lib/validaciones.sh`, `lib/fingerprint.sh`.
- Produce: exit 0 si no hay bloqueantes, 1 si hay al menos uno. Imprime el código de máquina en un bloque destacado.

**Mapa de severidades (resuelve la Decisión 5).** Esto no queda al criterio de quien codee:

| Fila | Severidad si falla | Por qué |
|---|---|---|
| SO + versión vs. `VERSION` | **ROJO** | El payload de `offline/` está compilado contra esa release; los `.deb` no van a resolver |
| Arquitectura x86_64 | **ROJO** | El tarball de la API es `linux-x64` self-contained |
| systemd presente | **ROJO** | `install.sh` instala una unit; sin systemd no hay instalación |
| RAM > 2GB | **ROJO** | |
| Disco > 10GB en `/opt`, `/var/lib`, `/var/backups` | **ROJO** | `install.sh` guarda 3 releases de ~100MB más la base y los backups |
| `/etc/machine-id` legible | **ROJO** | Sin fingerprint no hay licencia, y no hay forma de seguir |
| Puerto de la API libre | **ROJO** (o AVISO si Decisión 1 = B/C) | Ver Decisión 1 |
| Puerto de Postgres libre | **ROJO**, salvo que lo ocupe `stockapp-pg` → **OK** | Ver Decisión 4, y la nota de idempotencia de la Decisión 5 |
| Docker instalado / corriendo | **AVISO** si falta | En un servidor virgen es lo esperado: lo instala `01-bootstrap.sh` |
| `postgresql-client-16` | **AVISO** si falta | Ídem |
| `curl` | **AVISO** si falta | Ídem |
| Instalación previa de `stockapp-api` | **AVISO** | No es un error: `install.sh` es idempotente y respalda. Pero el operador tiene que saberlo |
| Red: IP y DHCP vs. estática | **AVISO GRANDE** si DHCP | Diseño línea 113: es el riesgo principal del despliegue |
| Internet saliente | **INFO** | Informativo, nunca bloqueante (diseño línea 75) |

- [ ] **Paso 1: Escribir el script**

`deploy/kit/00-preflight.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

# 00-preflight.sh — diagnóstico READ-ONLY del servidor. NO TOCA NADA.
#
# Correlo apenas llegás. Además de decirte si vas a poder terminar la instalación, imprime el
# CÓDIGO DE MÁQUINA: con eso la licencia se puede emitir en paralelo (desde la laptop del
# proveedor, que es la única que tiene la clave privada) mientras seguís con 01-bootstrap.
# Cero tiempo muerto.
#
# Uso:  sudo ./00-preflight.sh
#
# Salida: 0 si no hay bloqueantes, 1 si hay al menos uno. Los AVISOS no bloquean.

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"
# shellcheck source=lib/fingerprint.sh
source "${DIR_KIT}/lib/fingerprint.sh"

iniciar_log

BLOQUEANTES=0
marcar_bloqueante() { BLOQUEANTES=$((BLOQUEANTES + 1)); rojo "$*"; }

# El puerto de la API sale del kit, no de una constante suelta: si la Decisión 1 quedó en
# configurable, quien corre 01-bootstrap con --puerto tiene que poder verificar ESE puerto acá.
API_PORT="${API_PORT:-5080}"
PG_PORT="${PG_PORT:-5433}"

echo
echo "======================================================================"
echo "  PREFLIGHT — Gestión Municipal / StockApp"
echo "  Este script NO modifica nada. Solo mira."
echo "======================================================================"
echo

# ── Sistema operativo ──────────────────────────────────────────────────────
echo "-- Sistema operativo --"
VERSION_ESPERADA="$(tr -d '[:space:]' < "${DIR_KIT}/VERSION")"
if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091  # /etc/os-release no existe en la máquina de desarrollo
    . /etc/os-release
    info "Detectado: ${PRETTY_NAME:-desconocido}"
    if [[ "${VERSION_ID:-}" == "$VERSION_ESPERADA" ]]; then
        ok "Coincide con la versión fijada en el kit (${VERSION_ESPERADA})."
    else
        marcar_bloqueante "El kit está armado para Ubuntu ${VERSION_ESPERADA} y este servidor es ${VERSION_ID:-desconocido}."
        info "Los paquetes .deb de offline/ están compilados contra ${VERSION_ESPERADA} y no van a instalar acá."
    fi
else
    marcar_bloqueante "No se pudo leer /etc/os-release: no puedo confirmar la versión del sistema."
fi

ARQ="$(uname -m)"
if [[ "$ARQ" == "x86_64" ]]; then
    ok "Arquitectura x86_64."
else
    marcar_bloqueante "Arquitectura '${ARQ}': el kit trae binarios linux-x64 self-contained."
fi

if [[ -d /run/systemd/system ]]; then
    ok "systemd presente."
else
    marcar_bloqueante "systemd no está corriendo: install.sh instala una unit y no hay camino alternativo."
fi

# ── Recursos ───────────────────────────────────────────────────────────────
echo
echo "-- Recursos --"
RAM_MB="$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)"
if (( RAM_MB > 2048 )); then
    ok "RAM: ${RAM_MB} MB."
else
    marcar_bloqueante "RAM: ${RAM_MB} MB (se necesitan más de 2048)."
fi

for punto in /opt /var/lib /var/backups; do
    mkdir -p "$punto" 2>/dev/null || true
    LIBRE_GB="$(df -BG --output=avail "$punto" 2>/dev/null | tail -1 | tr -dc '0-9')"
    if [[ -n "$LIBRE_GB" ]] && (( LIBRE_GB > 10 )); then
        ok "Disco en ${punto}: ${LIBRE_GB} GB libres."
    else
        marcar_bloqueante "Disco en ${punto}: ${LIBRE_GB:-?} GB libres (se necesitan más de 10)."
    fi
done

# ── Puertos ────────────────────────────────────────────────────────────────
echo
echo "-- Puertos --"
if puerto_libre "$API_PORT"; then
    ok "Puerto ${API_PORT} (API) libre."
else
    marcar_bloqueante "Puerto ${API_PORT} (API) OCUPADO por:"
    quien_escucha "$API_PORT"
    info "Si la Decisión 1 quedó en 'configurable': podés instalar en otro puerto con"
    info "  sudo ./01-bootstrap.sh --puerto <otro>"
fi

# Idempotencia: en una segunda corrida el 5433 lo ocupa NUESTRO contenedor. Eso es OK, no un
# conflicto -- distinguirlo es lo que permite correr el preflight de nuevo sin falsos rojos.
if puerto_libre "$PG_PORT"; then
    ok "Puerto ${PG_PORT} (Postgres) libre."
elif docker ps --format '{{.Names}}' 2>/dev/null | grep -qx 'stockapp-pg'; then
    ok "Puerto ${PG_PORT} ocupado por el contenedor stockapp-pg (nuestro): esperado en una re-corrida."
else
    marcar_bloqueante "Puerto ${PG_PORT} (Postgres) OCUPADO por algo que no es stockapp-pg:"
    quien_escucha "$PG_PORT"
fi

# ── Software que instala 01-bootstrap (ausente = esperado en servidor virgen) ──
echo
echo "-- Software (lo instala 01-bootstrap.sh si falta) --"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    ok "Docker instalado y corriendo."
elif command -v docker >/dev/null 2>&1; then
    aviso "Docker instalado pero el daemon no responde. 01-bootstrap.sh va a intentar arrancarlo."
else
    aviso "Docker no está instalado. Lo instala 01-bootstrap.sh desde offline/docker/."
fi

dpkg -s postgresql-client-16 >/dev/null 2>&1 \
    && ok "postgresql-client-16 instalado." \
    || aviso "postgresql-client-16 no está. Lo instala 01-bootstrap.sh desde offline/pgclient/."

command -v curl >/dev/null 2>&1 \
    && ok "curl instalado." \
    || aviso "curl no está. Lo instala 01-bootstrap.sh desde offline/pgclient/."

# ── Instalación previa ─────────────────────────────────────────────────────
echo
echo "-- Instalación previa --"
if systemctl list-unit-files 2>/dev/null | grep -q '^stockapp-api\.service'; then
    aviso "Ya hay una instalación de stockapp-api en este servidor."
    info "Estado: $(systemctl is-active stockapp-api 2>/dev/null || echo desconocido)"
    info "No es un error: install.sh actualiza y respalda la anterior. Pero confirmá que es lo que querés."
    if [[ -f /etc/stockapp/.env ]]; then
        aviso "Existe /etc/stockapp/.env — 01-bootstrap.sh NO lo va a pisar (y hace bien: regenerarlo"
        info "contra un volumen de Postgres ya creado deja la API sin poder conectar, en silencio)."
    fi
else
    ok "No hay instalación previa: este es un servidor limpio."
fi

# ── Red ────────────────────────────────────────────────────────────────────
echo
echo "-- Red --"
IP_LAN="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}' | head -1)"
IFAZ="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="dev") print $(i+1)}' | head -1)"
if [[ -n "$IP_LAN" ]] && es_ipv4_valida "$IP_LAN"; then
    ok "IP de este servidor: ${IP_LAN} (interfaz ${IFAZ})."
else
    marcar_bloqueante "No pude determinar la IP de este servidor."
fi

# DHCP: el riesgo principal de todo el despliegue (diseño 2026-09-10, línea 113).
ES_DHCP=0
if command -v networkctl >/dev/null 2>&1 && networkctl status "$IFAZ" 2>/dev/null | grep -qi 'DHCP'; then
    ES_DHCP=1
elif ls /run/systemd/netif/leases/* >/dev/null 2>&1; then
    ES_DHCP=1
elif grep -rqs 'dhcp4:[[:space:]]*true' /etc/netplan/ 2>/dev/null; then
    ES_DHCP=1
fi

if (( ES_DHCP == 1 )); then
    echo
    echo "  **********************************************************************"
    echo "  *  ATENCIÓN — ESTE SERVIDOR TOMA SU IP POR DHCP                      *"
    echo "  *                                                                    *"
    echo "  *  Si la IP cambia, TODAS las PC clientes pierden la conexión y hay  *"
    echo "  *  que pasar el Configurador por cada una, a mano. Y vos no vas a    *"
    echo "  *  tener acceso a este servidor para arreglarlo.                     *"
    echo "  *                                                                    *"
    echo "  *  ANTES DE SEGUIR: pedile al área de sistemas del municipio una     *"
    echo "  *  RESERVA DHCP POR MAC en el router para esta máquina.              *"
    echo "  *  MAC de ${IFAZ}: $(cat "/sys/class/net/${IFAZ}/address" 2>/dev/null || echo '?')"
    echo "  *                                                                    *"
    echo "  *  Este script NO toca la configuración de red a propósito: tocar    *"
    echo "  *  netplan en una red ajena es cómo te quedás sin conectividad, y    *"
    echo "  *  la política de IPs es del área de sistemas. Ver LEEME.md.         *"
    echo "  **********************************************************************"
    echo
else
    ok "La IP parece estática (no detecté DHCP). Confirmalo igual con el área de sistemas."
fi

if curl -fsS --max-time 5 https://deb.debian.org >/dev/null 2>&1; then
    info "Internet saliente: HAY. (Informativo: el kit funciona igual sin internet.)"
else
    info "Internet saliente: NO detectado. (Informativo: el kit está preparado para esto.)"
fi

# ── Código de máquina ──────────────────────────────────────────────────────
echo
echo "-- Licenciamiento --"
if CODIGO="$(fingerprint_de_esta_maquina)"; then
    echo
    echo "======================================================================"
    echo "  CÓDIGO DE MÁQUINA DE ESTE SERVIDOR"
    echo
    echo "      ${CODIGO}"
    echo
    echo "  Pasáselo YA a la máquina que tiene la clave privada y emití la"
    echo "  licencia mientras seguís con 01-bootstrap.sh:"
    echo
    echo "      StockApp.Licencias.Cli emitir-licencia \\"
    echo "        --clave ~/stockapp-claves/clave-privada.pem \\"
    echo "        --cliente \"Municipio de Carmelo\" \\"
    echo "        --maquina ${CODIGO}"
    echo
    echo "  OJO: este código se REGENERA si alguien reinstala el sistema"
    echo "  operativo (systemd crea un /etc/machine-id nuevo). Si eso pasa,"
    echo "  hay que emitir una licencia nueva."
    echo "======================================================================"
    echo
else
    marcar_bloqueante "No se pudo calcular el código de máquina: sin esto no hay licencia posible."
fi

# ── Resultado ──────────────────────────────────────────────────────────────
echo "======================================================================"
if (( BLOQUEANTES == 0 )); then
    echo "  PREFLIGHT OK — no hay bloqueantes. Siguiente paso: sudo ./01-bootstrap.sh"
    echo "======================================================================"
    exit 0
else
    echo "  PREFLIGHT CON ${BLOQUEANTES} BLOQUEANTE(S) — resolvelos antes de seguir."
    echo "  No se modificó nada en este servidor."
    echo "======================================================================"
    exit 1
fi
```

- [ ] **Paso 2: shellcheck**

Correr: `shellcheck -x deploy/kit/00-preflight.sh`

El `-x` hace que siga los `source`. Esperado: sin hallazgos más allá de los `disable` justificados.

- [ ] **Paso 3: Prueba de humo en un contenedor (lo poco que se puede automatizar acá)**

El preflight es el único de los cinco scripts que se puede ejercitar sin systemd, porque no instala nada. Un contenedor de la versión fijada alcanza para verificar que **no explota** y que las secciones se imprimen:

```bash
UBU="$(tr -d '[:space:]' < deploy/kit/VERSION)"
docker run --rm -v "$PWD/deploy/kit:/kit:ro" "ubuntu:${UBU}" \
  bash -c 'apt-get update -qq && apt-get install -y -qq iproute2 curl >/dev/null 2>&1; /kit/00-preflight.sh; echo "EXIT=$?"'
```

Esperado: corre de punta a punta e imprime un código de máquina (el contenedor tiene su propio `/etc/machine-id`). Va a marcar bloqueantes de disco/RAM/systemd — **eso está bien**: prueba que los bloqueantes se cuentan y que el exit code es 1.

**Sé honesto sobre qué prueba esto y qué no:** prueba que el script no tiene errores de sintaxis ni de quoting y que el conteo de bloqueantes funciona. **NO** prueba la detección de DHCP, ni la de systemd en un servidor real, ni la de instalación previa. Eso es Fase 5.

- [ ] **Paso 4: Mutación — "puerto ocupado → preflight en rojo"**

Es una de las seis mutaciones que el diseño exige (línea 156) y **esta sí se puede automatizar**, en contenedor:

```bash
UBU="$(tr -d '[:space:]' < deploy/kit/VERSION)"
docker run --rm -v "$PWD/deploy/kit:/kit:ro" "ubuntu:${UBU}" bash -c '
  apt-get update -qq && apt-get install -y -qq iproute2 curl netcat-openbsd >/dev/null 2>&1
  nc -l -p 5080 >/dev/null 2>&1 &
  sleep 1
  /kit/00-preflight.sh 2>&1 | grep -E "Puerto 5080.*OCUPADO"
  echo "GREP_EXIT=$?"'
```

Esperado: `GREP_EXIT=0` — el preflight detectó el puerto ocupado. Si da 1, el chequeo no detecta nada y hay que arreglarlo.

- [ ] **Paso 5: Commit**

```bash
chmod +x deploy/kit/00-preflight.sh
git add deploy/kit/00-preflight.sh deploy/kit/VERSION
git commit -m "feat(kit): agrega preflight read-only que diagnostica el servidor e imprime el codigo de maquina"
```

---

## Fase 3 — Los scripts que modifican el servidor

**Depende de:** Fase 2 completa.

Acá se acaba el TDD. Estos cuatro scripts instalan Docker, levantan contenedores, escriben `/etc/`, tocan systemd y el firewall. **No hay forma honesta de testearlos en la suite.** Cada uno se entrega con su mutación de la tabla del diseño, que se ejecuta en la Fase 5. Lo que sí se puede hacer acá es que cada script **falle temprano y claro**, y que la guardia que lo protege sea lo más simple posible.

Orden: `02` primero (es el más chico y su guardia sí se puede probar en contenedor), después `04`, después `03`, y `01` al final (el más grande y el que menos se puede probar antes de la VM).

### Task 3.1: `02-instalar.sh` — el wrapper

**Files:**
- Create: `deploy/kit/02-instalar.sh`

**Interfaces:**
- Consume: `lib/log.sh`; `servidor/install.sh <tarball> <env>` (**sin modificar**); el tarball en `offline/stockapp-api-<v>-linux-x64.tar.gz`; `/etc/stockapp/.env` que escribió `01-bootstrap.sh`.
- Produce: exit 0/1.

- [ ] **Paso 1: Escribir el script**

```bash
#!/usr/bin/env bash
set -euo pipefail

# 02-instalar.sh — resuelve rutas y le pasa el trabajo a servidor/install.sh.
#
# install.sh NO SE MODIFICA (diseño 2026-09-10, línea 92): está verificado en producción en el
# VPS, es idempotente, hace swap atómico, respalda la instalación previa y poda backups viejos.
# Este wrapper existe solo para dos cosas: (1) que no tengas que tipear dos rutas largas y
# exactas parado en el servidor, y (2) que correr 02 sin haber corrido 01 falle con un mensaje
# que diga qué hacer, en vez de con un error de install.sh sobre un .env que no existe.
#
# Uso:  sudo ./02-instalar.sh

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"

iniciar_log

readonly ENV_SERVIDOR="/etc/stockapp/.env"
readonly INSTALL_SH="${DIR_KIT}/servidor/install.sh"

[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root: sudo $0"

# Guardia principal: 01 tiene que haber corrido.
if [[ ! -f "$ENV_SERVIDOR" ]]; then
    error_fatal "No existe ${ENV_SERVIDOR}.
       Eso significa que 01-bootstrap.sh todavía no corrió (o falló antes de generarlo).
       Corré primero:  sudo ./01-bootstrap.sh
       Ese script instala Docker, levanta Postgres y genera el archivo de secretos que
       install.sh necesita para instalar la API."
fi

if ! docker ps --format '{{.Names}}' 2>/dev/null | grep -qx 'stockapp-pg'; then
    error_fatal "El contenedor 'stockapp-pg' no está corriendo.
       install.sh arranca la API, y la API corre las migraciones contra Postgres ANTES de
       empezar a responder: sin la base arriba, el servicio queda colgado o falla.
       Revisá:  docker ps -a --filter name=stockapp-pg
       Y si hace falta, volvé a correr:  sudo ./01-bootstrap.sh"
fi

[[ -f "$INSTALL_SH" ]] || error_fatal "No se encontró ${INSTALL_SH}. ¿El pendrive está completo?"

# El tarball: uno solo, y su nombre exacto. install.sh exige EXACTAMENTE 2 argumentos
# (install.sh:40-46) justamente porque un glob que expande a varios tarballs terminaba
# corriendo el .env a $3 y haciendo 'source' de un gzip como root. Acá resolvemos el nombre
# nosotros y abortamos si hay ambigüedad, en vez de pasarle un patrón.
mapfile -t TARBALLS < <(find "${DIR_KIT}/offline" -maxdepth 1 -name 'stockapp-api-*-linux-x64.tar.gz' | sort)

case "${#TARBALLS[@]}" in
    0) error_fatal "No encontré ningún stockapp-api-*-linux-x64.tar.gz en ${DIR_KIT}/offline/." ;;
    1) TARBALL="${TARBALLS[0]}" ;;
    *) error_fatal "Hay ${#TARBALLS[@]} tarballs en ${DIR_KIT}/offline/ y no sé cuál querés:
$(printf '         %s\n' "${TARBALLS[@]}")
       Dejá uno solo. El kit se arma con una única versión de la API (armar-kit.sh)." ;;
esac

echo
info "Tarball : ${TARBALL}"
info "Secretos: ${ENV_SERVIDOR}"
info "Delegando en servidor/install.sh (no se modifica; es el mismo que corre en el VPS)."
echo

# Sin 'exec': queremos que el tee del log siga vivo hasta el final.
bash "$INSTALL_SH" "$TARBALL" "$ENV_SERVIDOR"

echo
ok "install.sh terminó bien."
info "Siguiente paso: activar la licencia."
info "  sudo ./04-licencia.sh activar <archivo-de-licencia>"
info "y recién después:  sudo ./03-verificar.sh"
```

- [ ] **Paso 2: shellcheck**

Correr: `shellcheck -x deploy/kit/02-instalar.sh`

- [ ] **Paso 3: Mutación — "correr 02 sin 01 aborta claro" (automatizable en contenedor)**

Segunda de las seis mutaciones del diseño, y de las pocas que no necesitan la VM:

```bash
UBU="$(tr -d '[:space:]' < deploy/kit/VERSION)"
docker run --rm -v "$PWD/deploy/kit:/kit:ro" "ubuntu:${UBU}" bash -c '
  /kit/02-instalar.sh 2>&1 | tee /tmp/salida
  echo "EXIT=${PIPESTATUS[0]}"
  grep -q "01-bootstrap.sh" /tmp/salida && echo "NOMBRA_EL_PASO_FALTANTE=si"'
```

Esperado: `EXIT=1` y `NOMBRA_EL_PASO_FALTANTE=si`. Un abort que no diga **qué correr** no sirve: el operador está parado en un servidor ajeno.

- [ ] **Paso 4: Commit**

```bash
chmod +x deploy/kit/02-instalar.sh
git add deploy/kit/02-instalar.sh
git commit -m "feat(kit): agrega wrapper de install.sh que exige el bootstrap previo"
```

### Task 3.2: `04-licencia.sh`

**Files:**
- Create: `deploy/kit/04-licencia.sh`

**Interfaces:**
- Consume: `lib/log.sh`, `lib/fingerprint.sh`; `GET /licencia/estado`; `POST /licencia/activar` con body `{"licencia":"<texto>"}` (`LicenciaEndpoints.cs:21-49`).
- Produce: subcomandos `fingerprint` y `activar <archivo>`.

**Regla que hay que dejar escrita en el script:** si el fingerprint que calcula bash y el que devuelve la API **difieren**, gana siempre el de la API — es el que se usa para validar. Una diferencia ahí es un síntoma (¿el `/etc/machine-id` cambió entre el preflight y ahora?), no un empate a resolver por gusto.

- [ ] **Paso 1: Escribir el script**

```bash
#!/usr/bin/env bash
set -euo pipefail

# 04-licencia.sh — consulta el código de máquina y activa la licencia.
#
# Va DESPUÉS de 02-instalar.sh y ANTES de tocar las PC clientes: sin licencia la API responde
# 423 en casi todo, y no tiene sentido configurar clientes contra un servidor bloqueado.
#
# La clave PRIVADA nunca toca este servidor. Acá solo entra el texto de una licencia ya firmada
# en la máquina del proveedor.
#
# Uso:
#   sudo ./04-licencia.sh fingerprint
#   sudo ./04-licencia.sh activar /ruta/a/licencia.txt

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/fingerprint.sh
source "${DIR_KIT}/lib/fingerprint.sh"

iniciar_log

readonly ENV_SERVIDOR="/etc/stockapp/.env"

# El puerto sale del .env, nunca hardcodeado (ver Decisión 1 del plan): si se instaló en otro
# puerto, este script tiene que seguirlo.
API_PORT=5080
if [[ -f "$ENV_SERVIDOR" ]]; then
    API_PORT="$(grep -E '^API_PORT=' "$ENV_SERVIDOR" | tail -1 | cut -d= -f2- | tr -d '[:space:]')"
    API_PORT="${API_PORT:-5080}"
fi
readonly BASE_URL="http://127.0.0.1:${API_PORT}"

estado_licencia() {
    curl -fsS --max-time 10 "${BASE_URL}/licencia/estado" 2>/dev/null
}

campo_json() {
    # Extractor mínimo: el shape de /licencia/estado es plano y conocido
    # (LicenciaEstadoResponse: activada, codigoMaquina). No metemos jq como dependencia nueva
    # del servidor solo para leer dos campos.
    sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\{0,1\}\([^,\"}]*\)\"\{0,1\}.*/\1/p"
}

cmd_fingerprint() {
    local desde_api="" desde_bash=""

    if RESPUESTA="$(estado_licencia)"; then
        desde_api="$(printf '%s' "$RESPUESTA" | campo_json codigoMaquina)"
    fi
    desde_bash="$(fingerprint_de_esta_maquina || true)"

    if [[ -n "$desde_api" ]]; then
        echo
        echo "  CÓDIGO DE MÁQUINA (según la API, que es la que valida):"
        echo
        echo "      ${desde_api}"
        echo
        if [[ -n "$desde_bash" && "$desde_bash" != "$desde_api" ]]; then
            aviso "El código calculado desde /etc/machine-id (${desde_bash}) NO coincide con el"
            info  "que reporta la API. GANA EL DE LA API: es el que se usa para validar."
            info  "Una diferencia acá suele significar que /etc/machine-id cambió después de"
            info  "instalar, o que la API está corriendo en otra máquina/contenedor de la que creés."
        fi
    elif [[ -n "$desde_bash" ]]; then
        echo
        echo "  CÓDIGO DE MÁQUINA (calculado de /etc/machine-id; la API todavía no responde):"
        echo
        echo "      ${desde_bash}"
        echo
        info "Cuando la API esté arriba, volvé a correr esto para confirmar contra ella."
    else
        error_fatal "No pude obtener el código de máquina ni de la API ni de /etc/machine-id."
    fi
}

cmd_activar() {
    local archivo="${1:-}"
    [[ -n "$archivo" ]] || error_fatal "Falta el archivo de licencia. Uso: $0 activar <archivo>"
    [[ -f "$archivo" ]] || error_fatal "No existe el archivo de licencia '${archivo}'."

    local licencia
    licencia="$(tr -d '\r\n' < "$archivo")"
    [[ -n "$licencia" ]] || error_fatal "El archivo '${archivo}' está vacío."

    estado_licencia >/dev/null 2>&1 \
        || error_fatal "La API no responde en ${BASE_URL}.
       ¿Corriste 02-instalar.sh? Revisá:  systemctl status stockapp-api --no-pager"

    info "Activando la licencia contra ${BASE_URL}..."

    local cuerpo codigo_http
    # --write-out separa cuerpo de status: necesitamos el status para distinguir 400 de 429.
    cuerpo="$(curl -sS --max-time 20 -o /tmp/licencia-respuesta.json -w '%{http_code}' \
        -X POST "${BASE_URL}/licencia/activar" \
        -H 'Content-Type: application/json' \
        --data-binary "$(printf '{"licencia":"%s"}' "$licencia")" || true)"
    codigo_http="$cuerpo"
    cuerpo="$(cat /tmp/licencia-respuesta.json 2>/dev/null || true)"
    rm -f /tmp/licencia-respuesta.json

    if [[ "$codigo_http" == "200" ]]; then
        # Confirmación independiente: no confiamos en la respuesta del POST, releemos el estado.
        local confirmacion
        confirmacion="$(estado_licencia | campo_json activada)"
        if [[ "$confirmacion" == "true" ]]; then
            echo
            ok "LICENCIA ACTIVADA. Confirmado contra GET /licencia/estado."
            info "Siguiente paso:  sudo ./03-verificar.sh"
            return 0
        fi
        error_fatal "El POST devolvió 200 pero /licencia/estado sigue diciendo activada=false.
       Revisá:  journalctl -u stockapp-api -n 100 --no-pager"
    fi

    # Traducción de errores a algo accionable. Los títulos salen de
    # LicenciaEndpoints.MotivoDe (:54-62).
    echo
    if printf '%s' "$cuerpo" | grep -q 'emitida para otra máquina'; then
        rojo "La licencia fue emitida para OTRA máquina."
        info "Casi siempre significa una de dos cosas:"
        info "  1. Se emitió contra un código viejo. Volvé a correr:  sudo $0 fingerprint"
        info "     y emití una licencia nueva contra el código que imprime AHORA."
        info "  2. Reinstalaron el sistema operativo de este servidor. systemd genera un"
        info "     /etc/machine-id nuevo en cada instalación, así que el código cambió y la"
        info "     licencia anterior quedó inservible. Hay que emitir una nueva."
    elif printf '%s' "$cuerpo" | grep -q 'firma de la licencia'; then
        rojo "La firma de la licencia no es válida."
        info "La clave PÚBLICA instalada en este servidor no corresponde a la clave PRIVADA con"
        info "la que se firmó. Verificá LICENCIA_CLAVE_PUBLICA_BASE64 en ${ENV_SERVIDOR} contra"
        info "la clave pública del par que usaste para emitir."
    elif printf '%s' "$cuerpo" | grep -q 'formato'; then
        rojo "El texto de la licencia no tiene un formato válido."
        info "¿El archivo tiene la línea completa que imprimió 'emitir-licencia', sin cortes?"
    elif [[ "$codigo_http" == "429" ]]; then
        rojo "Demasiados intentos de activación (rate limit)."
        info "Esperá un minuto y volvé a intentar."
    else
        rojo "La activación falló (HTTP ${codigo_http})."
        info "Respuesta: ${cuerpo}"
    fi
    exit 1
}

case "${1:-}" in
    fingerprint) cmd_fingerprint ;;
    activar)     shift; cmd_activar "${1:-}" ;;
    *)
        echo "Uso:"
        echo "  sudo $0 fingerprint              # imprime el código de máquina"
        echo "  sudo $0 activar <archivo>        # activa la licencia de ese archivo"
        exit 1
        ;;
esac
```

- [ ] **Paso 2: shellcheck**

Correr: `shellcheck -x deploy/kit/04-licencia.sh`

- [ ] **Paso 3: Verificar el `fingerprint` contra la API local (verificación orgánica parcial)**

Con la API corriendo localmente sin licencia activada:

```bash
curl -s http://127.0.0.1:5043/licencia/estado
API_PORT=5043 deploy/kit/04-licencia.sh fingerprint
```

Esperado: el código que imprime el script coincide con el `codigoMaquina` del curl. **Esto es paridad real end-to-end**, complementaria al test de la Task 1.1 (que compara contra la clase, no contra la API viva).

- [ ] **Paso 4: Commit**

```bash
chmod +x deploy/kit/04-licencia.sh
git add deploy/kit/04-licencia.sh
git commit -m "feat(kit): agrega consulta de codigo de maquina y activacion de licencia con errores traducidos"
```

### Task 3.3: `03-verificar.sh` — los 8 chequeos

**Files:**
- Create: `deploy/kit/03-verificar.sh`

**Interfaces:**
- Consume: `lib/log.sh`, `lib/validaciones.sh`; `/etc/stockapp/.env`; `systemctl`; la API.
- Produce: exit 0 si los 8 pasan, 1 si alguno falla. Cierra con la URL exacta para el Configurador y el recordatorio de copiarse el `.env`.

**Los 8 chequeos, con las correcciones del plan aplicadas:**

| # | Chequeo | Nota |
|---|---|---|
| 1 | `systemctl is-active stockapp-api` | |
| 2 | La unit declara `ASPNETCORE_URLS=http://<API_BIND>:<API_PORT>` **con los valores esperados** | Corrección 4: no basta con que la línea exista |
| 3 | `curl` loopback a `/licencia/estado` | |
| 4 | `curl` a `<ip-lan>:<puerto>/licencia/estado` | El único que prueba el bind real |
| 5 | `__EFMigrationsHistory` vs. migraciones del tarball | |
| 6 | `POST /auth/login` | Credenciales según Decisión 6 |
| 7 | `GET /backups/salud` con el JWT del 6 | |
| 8 | Licencia activada | |

- [ ] **Paso 1: Escribir el script**

```bash
#!/usr/bin/env bash
set -euo pipefail

# 03-verificar.sh — healthcheck end-to-end. Read-only: no arregla nada, solo comprueba.
#
# Corré esto DESPUÉS de activar la licencia (04-licencia.sh). Es lo último que mirás antes de
# empezar con las PC clientes, y lo último que mirás antes de irte del municipio.

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"

iniciar_log

readonly ENV_SERVIDOR="/etc/stockapp/.env"
readonly SERVICIO="stockapp-api"

[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root (lee ${ENV_SERVIDOR}): sudo $0"
[[ -f "$ENV_SERVIDOR" ]] || error_fatal "No existe ${ENV_SERVIDOR}. ¿Corrieron 01 y 02?"

# Leemos el .env en una subshell para no contaminar el entorno de este script.
leer_env() { grep -E "^$1=" "$ENV_SERVIDOR" | tail -1 | cut -d= -f2-; }

API_PORT="$(leer_env API_PORT | tr -d '[:space:]')"; API_PORT="${API_PORT:-5080}"
API_BIND="$(leer_env API_BIND | tr -d '[:space:]')"; API_BIND="${API_BIND:-127.0.0.1}"
ADMIN_USER="$(leer_env BOOTSTRAP_ADMIN_USER)"
ADMIN_PASS="$(leer_env BOOTSTRAP_PASSWORD)"

IP_LAN="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}' | head -1)"

FALLOS=0
fallo() { FALLOS=$((FALLOS + 1)); rojo "$*"; }

echo
echo "======================================================================"
echo "  VERIFICACIÓN — Gestión Municipal / StockApp"
echo "======================================================================"

# ── 1 ──────────────────────────────────────────────────────────────────────
echo
echo "[1/8] El servicio está activo"
if systemctl is-active --quiet "$SERVICIO"; then
    ok "stockapp-api activo."
else
    fallo "stockapp-api NO está activo (estado: $(systemctl is-active "$SERVICIO" 2>/dev/null || echo '?'))."
    info "  systemctl status ${SERVICIO} --no-pager"
    info "  journalctl -u ${SERVICIO} -n 100 --no-pager"
fi

# ── 2 ──────────────────────────────────────────────────────────────────────
# El chequeo que distingue "instalaste la versión nueva" de "quedó la vieja andando": un curl
# OK no prueba nada porque una API previa también respondía. Asertamos el VALOR esperado, no la
# mera presencia de la línea: install.sh:425 siempre escribe una, lo que importa es cuál.
echo
echo "[2/8] La unit de systemd declara el bind y el puerto correctos"
ESPERADO="ASPNETCORE_URLS=http://${API_BIND}:${API_PORT}"
if systemctl cat "$SERVICIO" 2>/dev/null | grep -qF "$ESPERADO"; then
    ok "La unit declara ${ESPERADO}."
else
    fallo "La unit NO declara '${ESPERADO}'."
    info "Lo que declara:"
    systemctl cat "$SERVICIO" 2>/dev/null | grep -F 'ASPNETCORE_URLS' || info "  (ninguna línea ASPNETCORE_URLS)"
    info "Si esto sorprende: install.sh inyecta API_BIND/API_PORT del .env por sed (install.sh:424-425)."
fi

# ── 3 ──────────────────────────────────────────────────────────────────────
echo
echo "[3/8] La API responde en loopback"
if curl -fsS --max-time 10 "http://127.0.0.1:${API_PORT}/licencia/estado" >/dev/null; then
    ok "Responde en 127.0.0.1:${API_PORT}."
else
    fallo "No responde en 127.0.0.1:${API_PORT}."
fi

# ── 4 ──────────────────────────────────────────────────────────────────────
# EL chequeo que importa para la LAN: es el único que detecta un API_BIND en loopback. Con
# API_BIND=127.0.0.1 el chequeo 3 pasa igual y ninguna PC del municipio puede conectarse.
echo
echo "[4/8] La API responde desde la IP de la LAN (esto es lo que van a usar las PC)"
if [[ -z "$IP_LAN" ]]; then
    fallo "No pude determinar la IP de la LAN de este servidor."
elif curl -fsS --max-time 10 "http://${IP_LAN}:${API_PORT}/licencia/estado" >/dev/null; then
    ok "Responde en ${IP_LAN}:${API_PORT}."
else
    fallo "NO responde en ${IP_LAN}:${API_PORT} (sí en loopback)."
    info "Casi siempre es una de dos:"
    info "  a) API_BIND quedó en 127.0.0.1 -> la API solo escucha loopback. Corregí"
    info "     ${ENV_SERVIDOR} (API_BIND=0.0.0.0) y volvé a correr 02-instalar.sh."
    info "  b) El firewall bloquea el puerto ${API_PORT}. Revisá:  ufw status"
fi

# ── 5 ──────────────────────────────────────────────────────────────────────
echo
echo "[5/8] Las migraciones de la base están al día"
POSTGRES_USER="$(leer_env POSTGRES_USER)"
POSTGRES_DB="$(leer_env POSTGRES_DB)"
POSTGRES_PASSWORD="$(leer_env POSTGRES_PASSWORD)"
PG_PORT="$(leer_env POSTGRES_PORT | tr -d '[:space:]')"; PG_PORT="${PG_PORT:-5433}"

APLICADAS="$(PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "$PG_PORT" \
    -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc \
    'SELECT count(*) FROM "__EFMigrationsHistory"' 2>/dev/null || echo "")"

if [[ -z "$APLICADAS" ]]; then
    fallo "No pude consultar __EFMigrationsHistory. ¿Postgres está arriba y las credenciales son las correctas?"
elif (( APLICADAS > 0 )); then
    ok "${APLICADAS} migraciones aplicadas."
    info "Última: $(PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p "$PG_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1' 2>/dev/null)"
else
    fallo "La tabla __EFMigrationsHistory está vacía: la API no llegó a migrar."
fi

# ── 6 ──────────────────────────────────────────────────────────────────────
# Decisión 6 del plan (opción C): probamos con las credenciales del .env y, si dan 401,
# preguntamos -- porque al operador se le indica cambiar esa contraseña apenas entra, y desde
# ese momento el .env deja de reflejar la realidad. Un 401 acá NO es un fallo del sistema.
echo
echo "[6/8] Login del administrador"
intentar_login() {
    curl -sS --max-time 15 -o /tmp/login-resp.json -w '%{http_code}' \
        -X POST "http://127.0.0.1:${API_PORT}/auth/login" \
        -H 'Content-Type: application/json' \
        --data-binary "$(printf '{"usuario":"%s","password":"%s"}' "$1" "$2")" 2>/dev/null || echo "000"
}

TOKEN=""
HTTP="$(intentar_login "$ADMIN_USER" "$ADMIN_PASS")"
if [[ "$HTTP" == "401" ]]; then
    aviso "Las credenciales de bootstrap ya no sirven — eso es LO ESPERADO si el admin ya cambió"
    info  "su contraseña (que es lo que se le pide hacer apenas entra). No es un error del sistema."
    read -rp "      Usuario admin actual [${ADMIN_USER}]: " U; U="${U:-$ADMIN_USER}"
    read -rsp "      Contraseña: " P; echo
    HTTP="$(intentar_login "$U" "$P")"
fi

case "$HTTP" in
    200)
        TOKEN="$(sed -n 's/.*"token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' /tmp/login-resp.json)"
        [[ -n "$TOKEN" ]] && ok "Login correcto." || fallo "Login devolvió 200 pero sin token en la respuesta."
        ;;
    401) fallo "Login rechazado (401): usuario o contraseña incorrectos." ;;
    423) fallo "Login bloqueado (423): la licencia NO está activada. Corré 04-licencia.sh primero." ;;
    429) fallo "Login limitado (429): demasiados intentos. Esperá un minuto." ;;
    000) fallo "La API no respondió al login." ;;
    *)   fallo "Login devolvió HTTP ${HTTP}." ;;
esac
rm -f /tmp/login-resp.json

# ── 7 ──────────────────────────────────────────────────────────────────────
echo
echo "[7/8] Salud del sistema de backups"
if [[ -z "$TOKEN" ]]; then
    fallo "No se pudo verificar: hace falta el login del chequeo 6."
else
    SALUD="$(curl -fsS --max-time 15 -H "Authorization: Bearer ${TOKEN}" \
        "http://127.0.0.1:${API_PORT}/backups/salud" 2>/dev/null || echo "")"
    if [[ -z "$SALUD" ]]; then
        fallo "GET /backups/salud no respondió."
    else
        ok "Responde: ${SALUD}"
        info "Recién instalado puede no haber corrido todavía el primer backup (cada 12h)."
        info "Lo que NO puede pasar es que quede así para siempre: ver deploy/PROCEDIMIENTOS.md."
    fi
fi

# ── 8 ──────────────────────────────────────────────────────────────────────
echo
echo "[8/8] Licencia activada"
ESTADO="$(curl -fsS --max-time 10 "http://127.0.0.1:${API_PORT}/licencia/estado" 2>/dev/null || echo "")"
if printf '%s' "$ESTADO" | grep -q '"activada"[[:space:]]*:[[:space:]]*true'; then
    ok "Licencia activada."
else
    fallo "La licencia NO está activada. La API va a devolver 423 en casi todo."
    info "  sudo ./04-licencia.sh fingerprint      # para emitirla"
    info "  sudo ./04-licencia.sh activar <archivo>"
fi

# ── Cierre ─────────────────────────────────────────────────────────────────
echo
echo "======================================================================"
if (( FALLOS == 0 )); then
    echo "  LOS 8 CHEQUEOS PASARON."
else
    echo "  ${FALLOS} CHEQUEO(S) FALLARON — resolvelos antes de instalar clientes."
fi
echo "======================================================================"
echo
echo "  URL PARA EL CONFIGURADOR (esto es lo que se carga en CADA PC):"
echo
echo "      http://${IP_LAN}:${API_PORT}"
echo
echo "======================================================================"
echo
echo "  !!!  ANTES DE IRTE DEL MUNICIPIO  !!!"
echo
echo "  COPIATE ${ENV_SERVIDOR} AL PENDRIVE."
echo
echo "  Ese archivo tiene la contraseña de Postgres, el JWT_SECRET y la clave"
echo "  pública de licencia. NO vas a tener acceso a este servidor de nuevo."
echo "  Si lo perdés y algún día hace falta entrar a la base, no hay forma de"
echo "  recuperarlo."
echo
echo "      cp ${ENV_SERVIDOR} /media/<pendrive>/env-carmelo-\$(date +%F).txt"
echo
echo "  Copiate también el log de esta instalación:"
echo "      ${KIT_LOG}"
echo
echo "======================================================================"

(( FALLOS == 0 )) && exit 0 || exit 1
```

- [ ] **Paso 2: shellcheck**

Correr: `shellcheck -x deploy/kit/03-verificar.sh`

- [ ] **Paso 3: Verificar el shape del login contra el código real**

El script asume que `POST /auth/login` recibe `{"usuario":..., "password":...}` y devuelve `{"token": ...}`. **No lo des por sentado**: leé `src/StockApp.Api/Endpoints/AuthEndpoints.cs` y `deploy/DEPLOY.md:348-352`, y corregí los nombres de campo si difieren. Un error acá se descubre en el municipio.

- [ ] **Paso 4: Commit**

```bash
chmod +x deploy/kit/03-verificar.sh
git add deploy/kit/03-verificar.sh
git commit -m "feat(kit): agrega verificacion end-to-end de 8 chequeos post-instalacion"
```

### Task 3.4: `01-bootstrap.sh`

El más grande y el que menos se puede probar antes de la VM. Es el único que instala paquetes y genera secretos.

**Files:**
- Create: `deploy/kit/01-bootstrap.sh`

**Interfaces:**
- Consume: `lib/log.sh`, `lib/validaciones.sh`; `offline/docker/*.deb`, `offline/pgclient/*.deb`, `offline/postgres-16-alpine.tar`; `servidor/docker-compose.postgres.yml`, `servidor/wait-for-postgres.sh`.
- Produce: `/etc/stockapp/.env` (600), el contenedor `stockapp-pg` corriendo, `ufw` configurado.

**La guardia más importante de todo el kit** (diseño línea 88): **si ya existe `/etc/stockapp/.env`, NO se pisa.** Regenerarlo con el volumen de Postgres ya creado deja la API sin poder conectar — y Npgsql no arrastra la connection string a sus excepciones, así que el error **no dice** que la contraseña cambió. Es el fallo más difícil de diagnosticar del kit entero y hay que impedirlo, no documentarlo.

- [ ] **Paso 1: Escribir el script**

```bash
#!/usr/bin/env bash
set -euo pipefail

# 01-bootstrap.sh — prepara el servidor: Docker, Postgres, secretos y firewall.
#
# Offline-first (decisión 4 del diseño 2026-09-10): NUNCA asume que hay internet. Instala desde
# offline/ y solo cae a apt si hay internet CONFIRMADO.
#
# Uso:
#   sudo ./01-bootstrap.sh                # puerto de API por defecto (5080)
#   sudo ./01-bootstrap.sh --puerto 8080  # solo si la Decisión 1 quedó en configurable

DIR_KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/log.sh
source "${DIR_KIT}/lib/log.sh"
# shellcheck source=lib/validaciones.sh
source "${DIR_KIT}/lib/validaciones.sh"

iniciar_log

readonly ENV_DIR="/etc/stockapp"
readonly ENV_SERVIDOR="${ENV_DIR}/.env"
readonly COMPOSE_DIR="/opt/stockapp"
readonly PG_PORT=5433

API_PORT=5080
API_BIND="0.0.0.0"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --puerto) API_PORT="${2:-}"; shift 2 ;;
        *) error_fatal "Opción desconocida: $1" ;;
    esac
done

[[ "${EUID}" -eq 0 ]] || error_fatal "Este script necesita root: sudo $0"
es_puerto_valido "$API_PORT" || error_fatal "'${API_PORT}' no es un puerto válido."

echo
echo "======================================================================"
echo "  BOOTSTRAP — Gestión Municipal / StockApp"
echo "  Puerto de la API: ${API_PORT}   Bind: ${API_BIND}"
echo "======================================================================"

# ── 1. Docker ──────────────────────────────────────────────────────────────
echo
echo "-- Docker --"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    ok "Docker ya está instalado y corriendo — no se toca."
else
    if compgen -G "${DIR_KIT}/offline/docker/*.deb" >/dev/null; then
        info "Instalando Docker desde offline/docker/ (sin red)."
        # Todos juntos en un solo dpkg: las dependencias entre containerd.io, docker-ce-cli,
        # docker-ce y docker-compose-plugin se resuelven entre sí en la misma invocación. Uno
        # por uno fallaría por orden de dependencias.
        dpkg -i "${DIR_KIT}"/offline/docker/*.deb \
            || error_fatal "Falló la instalación de Docker desde los .deb del kit.
       Casi siempre significa que el kit se armó para otra versión de Ubuntu.
       Este servidor: $( . /etc/os-release && echo "${VERSION_ID}" )
       El kit espera : $(tr -d '[:space:]' < "${DIR_KIT}/VERSION")"
    elif curl -fsS --max-time 5 https://download.docker.com >/dev/null 2>&1; then
        aviso "No hay .deb en offline/docker/, pero SÍ hay internet: instalando por apt."
        apt-get update && apt-get install -y docker.io docker-compose-v2
    else
        error_fatal "Docker no está instalado, no hay .deb en offline/docker/ y no hay internet.
       No hay forma de continuar. El kit está incompleto."
    fi

    systemctl enable --now docker
    docker info >/dev/null 2>&1 || error_fatal "Docker quedó instalado pero el daemon no responde."
    ok "Docker instalado y corriendo."
fi

# ── 2. postgresql-client-16 y curl ─────────────────────────────────────────
# ESTO es lo que hace que install.sh no toque la red después: install.sh:243-258 solo llama a
# apt si alguno de los dos falta.
echo
echo "-- postgresql-client-16 y curl --"
FALTA=0
dpkg -s postgresql-client-16 >/dev/null 2>&1 || FALTA=1
command -v curl >/dev/null 2>&1 || FALTA=1

if (( FALTA == 0 )); then
    ok "Ya están instalados."
elif compgen -G "${DIR_KIT}/offline/pgclient/*.deb" >/dev/null; then
    info "Instalando desde offline/pgclient/ (sin red)."
    dpkg -i "${DIR_KIT}"/offline/pgclient/*.deb \
        || error_fatal "Falló la instalación de postgresql-client-16/curl desde los .deb del kit."
    ok "Instalados."
else
    error_fatal "Faltan postgresql-client-16 y/o curl, y no hay .deb en offline/pgclient/.
       Sin ellos install.sh va a intentar usar apt (install.sh:252-255) y va a fallar sin internet."
fi

command -v pg_isready >/dev/null 2>&1 || error_fatal "pg_isready no quedó disponible."
command -v pg_dump    >/dev/null 2>&1 || error_fatal "pg_dump no quedó disponible (los backups lo necesitan)."

# ── 3. Imagen de Postgres ──────────────────────────────────────────────────
echo
echo "-- Imagen de Postgres --"
if docker image inspect postgres:16-alpine >/dev/null 2>&1; then
    ok "La imagen postgres:16-alpine ya está cargada."
elif [[ -f "${DIR_KIT}/offline/postgres-16-alpine.tar" ]]; then
    info "Cargando la imagen desde offline/ (sin red)."
    docker load < "${DIR_KIT}/offline/postgres-16-alpine.tar"
    ok "Imagen cargada."
else
    error_fatal "No está la imagen postgres:16-alpine ni offline/postgres-16-alpine.tar."
fi

# ── 4. Secretos ────────────────────────────────────────────────────────────
# LA GUARDIA MÁS IMPORTANTE DEL KIT. Ver diseño 2026-09-10, línea 88.
echo
echo "-- Secretos (${ENV_SERVIDOR}) --"
if [[ -f "$ENV_SERVIDOR" ]]; then
    ok "Ya existe ${ENV_SERVIDOR} — NO se toca."
    aviso "Regenerarlo sería un error grave: el volumen de Postgres ya se creó con la"
    info  "contraseña que está en ese archivo. Con una contraseña nueva la API no puede"
    info  "conectar, y Npgsql NO incluye la connection string en sus excepciones -- el error"
    info  "que verías no dice nada de contraseñas y se pierden horas buscando en otro lado."
    info  "Si de verdad querés empezar de cero, hay que borrar TAMBIÉN el volumen:"
    info  "    docker compose -f ${COMPOSE_DIR}/docker-compose.postgres.yml down -v"
    info  "    rm ${ENV_SERVIDOR}"
    info  "Eso BORRA LA BASE DE DATOS. En un servidor ya en uso, no lo hagas."
else
    info "Generando secretos nuevos."
    mkdir -p "$ENV_DIR"
    chmod 700 "$ENV_DIR"

    JWT_SECRET="$(openssl rand -base64 48)"
    POSTGRES_PASSWORD="$(openssl rand -base64 24 | tr -d '/+=;#')"

    echo
    info "Ahora elegí el usuario y la contraseña del ADMINISTRADOR del sistema."
    info "Con estos datos entra la primera vez desde el desktop, y después los cambia."
    read -rp "      Usuario administrador [admin]: " ADMIN_USER
    ADMIN_USER="${ADMIN_USER:-admin}"

    while true; do
        read -rsp "      Contraseña (mín. 8, con letra y número): " ADMIN_PASS; echo
        if ! password_admin_valida "$ADMIN_PASS"; then
            aviso "No cumple las reglas de la API: mínimo 8 caracteres, al menos una letra y un número."
            continue
        fi
        read -rsp "      Repetila: " ADMIN_PASS2; echo
        [[ "$ADMIN_PASS" == "$ADMIN_PASS2" ]] && break
        aviso "No coinciden."
    done

    echo
    info "Pegá la CLAVE PÚBLICA de licenciamiento en Base64 (la que generaste con"
    info "'generar-claves'). La clave PRIVADA nunca va acá ni a este servidor."
    read -rp "      Clave pública: " CLAVE_PUBLICA
    [[ -n "$CLAVE_PUBLICA" ]] || error_fatal "La clave pública es obligatoria: sin ella la API no puede validar la licencia."

    UMASK_PREVIO="$(umask)"
    umask 077
    cat > "$ENV_SERVIDOR" <<ENVEOF
# Generado por 01-bootstrap.sh el $(date -Is) en $(hostname).
#
# NO REGENERAR este archivo mientras exista el volumen de Postgres: la contraseña de abajo es
# la que quedó grabada en la base la primera vez. Cambiarla acá deja a la API sin poder
# conectar, con un error que no menciona la contraseña.
#
# COPIALO AL PENDRIVE ANTES DE IRTE. No vas a tener acceso a este servidor de nuevo.

POSTGRES_USER=stockapp
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
POSTGRES_DB=stockapp

API_BIND=${API_BIND}
API_PORT=${API_PORT}

JWT_SECRET=${JWT_SECRET}

BOOTSTRAP_ADMIN_USER=${ADMIN_USER}
BOOTSTRAP_PASSWORD=${ADMIN_PASS}

LICENCIA_CLAVE_PUBLICA_BASE64=${CLAVE_PUBLICA}
ENVEOF
    umask "$UMASK_PREVIO"
    chmod 600 "$ENV_SERVIDOR"
    ok "Secretos generados en ${ENV_SERVIDOR} (permisos 600)."
fi

# ── 5. Postgres ────────────────────────────────────────────────────────────
echo
echo "-- Postgres --"
mkdir -p "$COMPOSE_DIR"
install -m 0644 "${DIR_KIT}/servidor/docker-compose.postgres.yml" "${COMPOSE_DIR}/docker-compose.postgres.yml"

docker compose --env-file "$ENV_SERVIDOR" \
    -f "${COMPOSE_DIR}/docker-compose.postgres.yml" up -d

info "Esperando a que Postgres acepte conexiones..."
bash "${DIR_KIT}/servidor/wait-for-postgres.sh" \
    || error_fatal "Postgres no llegó a responder en 127.0.0.1:${PG_PORT}.
       Revisá:  docker compose -f ${COMPOSE_DIR}/docker-compose.postgres.yml logs"
ok "Postgres arriba en 127.0.0.1:${PG_PORT} (nunca expuesto a la red)."

# ── 6. Firewall ────────────────────────────────────────────────────────────
# Pregunta antes de activarse (diseño línea 115). Y la regla de SSH va PRIMERO: si alguien
# administra este servidor por SSH, activar ufw sin esa regla lo deja afuera.
echo
echo "-- Firewall --"
if ! command -v ufw >/dev/null 2>&1; then
    aviso "ufw no está instalado. La API va a quedar accesible en ${API_BIND}:${API_PORT} sin filtro."
    info  "El tráfico es HTTP PLANO: usuario, contraseña y JWT viajan sin cifrar por la LAN."
else
    SUBRED="$(ip -4 -o addr show scope global | awk '{print $4}' | head -1)"
    info "Subred detectada: ${SUBRED}"
    if ufw status 2>/dev/null | grep -q '^Status: active'; then
        info "ufw ya está activo. Agrego la regla del puerto ${API_PORT} para ${SUBRED}."
        ufw allow from "$SUBRED" to any port "$API_PORT" proto tcp
        ok "Regla agregada."
    else
        aviso "ufw está INACTIVO."
        info  "Si lo activo, primero agrego la regla de SSH (si no, quien administre este"
        info  "servidor remotamente pierde el acceso) y después la del puerto ${API_PORT},"
        info  "limitada a la subred ${SUBRED}."
        read -rp "      ¿Activo el firewall? [s/N]: " RESP
        if [[ "${RESP,,}" == "s" ]]; then
            ufw allow 22/tcp
            ufw allow from "$SUBRED" to any port "$API_PORT" proto tcp
            ufw --force enable
            ok "Firewall activo. Reglas:"
            ufw status numbered
        else
            aviso "Firewall NO activado, por tu decisión. La API queda accesible sin filtro"
            info  "desde cualquier máquina que llegue a ${API_BIND}:${API_PORT}."
        fi
    fi
fi

echo
echo "======================================================================"
echo "  BOOTSTRAP COMPLETO."
echo "  Siguiente paso:  sudo ./02-instalar.sh"
echo "======================================================================"
```

- [ ] **Paso 2: shellcheck**

Correr: `shellcheck -x deploy/kit/01-bootstrap.sh`

- [ ] **Paso 3: Reconocer explícitamente qué NO se probó todavía**

Este script **no se puede verificar acá**. Instala paquetes del sistema, arranca un daemon, escribe en `/etc/`, levanta contenedores y toca el firewall. Cualquier "prueba" en el entorno de desarrollo sería teatro. Su verificación real es la Fase 5, con estas mutaciones:

- "Apagar `stockapp-pg` antes de instalar → `01-bootstrap.sh` no sigue"
- "Correr todo dos veces → idempotente, sin daño" (en particular: **la guardia del `.env` no lo pisa**)

- [ ] **Paso 4: Commit**

```bash
chmod +x deploy/kit/01-bootstrap.sh
git add deploy/kit/01-bootstrap.sh
git commit -m "feat(kit): agrega bootstrap offline de docker, postgres, secretos y firewall"
```

---

## Fase 4 — `armar-kit.sh`, el payload offline y la documentación

**BLOQUEADA por la Decisión 7** (versión de Ubuntu). No empezar sin eso resuelto.

La idea rectora de esta fase: **todo fallo que pueda ocurrir el día de la instalación debería ocurrir acá, el día del build.** Si `armar-kit.sh` termina bien, el payload offline ya se probó.

### Task 4.1: `deploy/armar-kit.sh`

**Files:**
- Create: `deploy/armar-kit.sh`
- Modify: `.gitignore`

**Interfaces:**
- Consume: `deploy/kit/*`, `deploy/*` (para `servidor/`), `deploy/dist/stockapp-api-<v>-linux-x64.tar.gz` (de `publish-api.sh`), `releases/win/` (de `pack-win.ps1`), `deploy/kit/VERSION`.
- Produce: `deploy/kit-dist/stockapp-kit-<version>/` y su `.tar.gz`.

**Puntos donde el diseño necesita precisión (no redefinición):**

1. **El nombre del Setup.exe no se adivina.** `pack-win.ps1:161` lo llama `Setup.exe`; el diseño (línea 46) dice `GestionMunicipal-win-Setup.exe`. `armar-kit.sh` debe **buscar** el instalador en `releases/win/` y abortar si no encuentra exactamente uno.
2. **Los `.deb` se bajan dentro de un contenedor de la versión fijada** (Decisión 7 = A), nunca desde el host WSL2.
3. **El payload se verifica instalándolo**, en un contenedor limpio de la misma imagen, sin red.

- [ ] **Paso 1: Escribir el script**

```bash
#!/usr/bin/env bash
set -euo pipefail

# armar-kit.sh — arma el pendrive de instalación. Corre en la máquina del proveedor, CON
# internet (es el único momento del proceso en que hay internet garantizado).
#
# PRINCIPIO: todo lo que pueda fallar en el municipio tiene que fallar ACÁ. Este script no se
# limita a copiar archivos: BAJA los .deb dentro de un contenedor de la versión exacta de Ubuntu
# destino, y después VERIFICA que se instalen en un contenedor limpio y SIN RED. Si eso pasa
# acá, no va a sorprender allá.
#
# Uso:  deploy/armar-kit.sh <version-del-kit>
# Ejemplo:  deploy/armar-kit.sh 1.0.0

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR_FUENTE="${REPO_ROOT}/deploy/kit"
SALIDA_BASE="${REPO_ROOT}/deploy/kit-dist"

VERSION_KIT="${1:-}"
[[ -n "$VERSION_KIT" ]] || { echo "Uso: $0 <version-del-kit>   (ej: $0 1.0.0)" >&2; exit 1; }

UBUNTU_VER="$(tr -d '[:space:]' < "${DIR_FUENTE}/VERSION")"
IMAGEN="ubuntu:${UBUNTU_VER}"
DESTINO="${SALIDA_BASE}/stockapp-kit-${VERSION_KIT}"

echo "[armar-kit] Versión del kit : ${VERSION_KIT}"
echo "[armar-kit] Ubuntu destino  : ${UBUNTU_VER}  (imagen ${IMAGEN})"

for cmd in docker tar; do
    command -v "$cmd" >/dev/null 2>&1 || { echo "ERROR: falta '${cmd}'." >&2; exit 1; }
done

# ── Artefactos que tienen que existir antes ────────────────────────────────
echo "[armar-kit] Buscando el tarball de la API..."
mapfile -t TARBALLS < <(find "${REPO_ROOT}/deploy/dist" -maxdepth 1 -name 'stockapp-api-*-linux-x64.tar.gz' -printf '%T@ %p\n' 2>/dev/null | sort -rn | cut -d' ' -f2-)
if [[ "${#TARBALLS[@]}" -eq 0 ]]; then
    echo "ERROR: no hay ningún tarball de la API en deploy/dist/." >&2
    echo "       Corré primero:  deploy/publish-api.sh ${VERSION_KIT}" >&2
    exit 1
fi
TARBALL_API="${TARBALLS[0]}"
echo "[armar-kit]   -> $(basename "$TARBALL_API")  (el más reciente)"

echo "[armar-kit] Buscando el instalador de Windows..."
# pack-win.ps1:161 lo genera como "Setup.exe"; el diseño lo llamaba
# GestionMunicipal-win-Setup.exe. No adivinamos: buscamos cualquier *Setup.exe y exigimos uno.
mapfile -t SETUPS < <(find "${REPO_ROOT}/releases/win" -maxdepth 1 -iname '*Setup.exe' 2>/dev/null | sort)
case "${#SETUPS[@]}" in
    0) echo "ERROR: no hay ningún *Setup.exe en releases/win/." >&2
       echo "       Corré, en Windows:  .\\build\\pack-win.ps1" >&2
       exit 1 ;;
    1) SETUP_WIN="${SETUPS[0]}" ;;
    *) echo "ERROR: hay ${#SETUPS[@]} instaladores en releases/win/ y no sé cuál:" >&2
       printf '       %s\n' "${SETUPS[@]}" >&2
       echo "       Dejá uno solo." >&2
       exit 1 ;;
esac
echo "[armar-kit]   -> $(basename "$SETUP_WIN")"

# ── Estructura ─────────────────────────────────────────────────────────────
echo "[armar-kit] Armando ${DESTINO}..."
rm -rf "$DESTINO"
mkdir -p "${DESTINO}"/{servidor,offline/docker,offline/pgclient,clientes}

# Scripts del kit + librería + LEEME + VERSION
install -m 0755 "${DIR_FUENTE}"/0*.sh "$DESTINO/"
mkdir -p "${DESTINO}/lib"
install -m 0755 "${DIR_FUENTE}"/lib/*.sh "${DESTINO}/lib/"
install -m 0644 "${DIR_FUENTE}/LEEME.md" "${DIR_FUENTE}/VERSION" "$DESTINO/"

# servidor/ = copia fiel de deploy/ (install.sh NO se modifica: viaja tal cual)
install -m 0755 "${REPO_ROOT}/deploy/install.sh"           "${DESTINO}/servidor/"
install -m 0755 "${REPO_ROOT}/deploy/wait-for-postgres.sh" "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/stockapp-api.service" "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/docker-compose.postgres.yml" "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/.env.example"         "${DESTINO}/servidor/"
install -m 0644 "${REPO_ROOT}/deploy/DEPLOY.md"            "${DESTINO}/servidor/"

# El .env REAL nunca viaja: se genera en el servidor. Guardia explícita.
if [[ -e "${DESTINO}/servidor/.env" ]]; then
    echo "ERROR: se copió un .env real al kit. Eso no puede pasar." >&2
    exit 1
fi

install -m 0644 "$TARBALL_API" "${DESTINO}/offline/"
install -m 0644 "$SETUP_WIN"   "${DESTINO}/clientes/"
install -m 0644 "${DIR_FUENTE}/clientes/LEEME-clientes.md" "${DESTINO}/clientes/"
[[ -f "${DIR_FUENTE}/clientes/configurar-cliente.cmd" ]] \
    && install -m 0644 "${DIR_FUENTE}/clientes/configurar-cliente.cmd" "${DESTINO}/clientes/"

# ── Imagen de Postgres ─────────────────────────────────────────────────────
echo "[armar-kit] Guardando la imagen postgres:16-alpine..."
docker pull postgres:16-alpine
docker save postgres:16-alpine -o "${DESTINO}/offline/postgres-16-alpine.tar"

# ── .deb, bajados DENTRO de la imagen de la versión destino ────────────────
# POR QUÉ en contenedor y no en el host: este host es WSL2, NO es el Ubuntu Server destino.
# 'apt-get download' acá resolvería contra los repos y la versión de ESTA distro, y el set
# saldría silenciosamente equivocado -- para descubrirlo en el municipio, sin internet.
echo "[armar-kit] Bajando los .deb de Docker dentro de ${IMAGEN}..."
docker run --rm -v "${DESTINO}/offline/docker:/salida" "$IMAGEN" bash -c '
    set -euo pipefail
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg >/dev/null
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
        | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    . /etc/os-release
    echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
        > /etc/apt/sources.list.d/docker.list
    apt-get update -qq
    cd /salida
    # --reinstall fuerza la descarga aunque algo ya esté presente en la imagen base.
    apt-get install -y --reinstall --download-only \
        docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    cp /var/cache/apt/archives/*.deb /salida/
    ls -1 /salida/*.deb | wc -l'

echo "[armar-kit] Bajando los .deb de postgresql-client-16 y curl dentro de ${IMAGEN}..."
docker run --rm -v "${DESTINO}/offline/pgclient:/salida" "$IMAGEN" bash -c '
    set -euo pipefail
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg >/dev/null
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        | gpg --dearmor -o /etc/apt/keyrings/pgdg.gpg
    . /etc/os-release
    echo "deb [signed-by=/etc/apt/keyrings/pgdg.gpg] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list
    apt-get update -qq
    cd /salida
    apt-get install -y --reinstall --download-only postgresql-client-16 curl
    cp /var/cache/apt/archives/*.deb /salida/
    ls -1 /salida/*.deb | wc -l'

# ── VERIFICACIÓN del payload: instalarlo SIN RED ───────────────────────────
# Acá es donde este script gana su sueldo. --network none garantiza que si falta una
# dependencia, falla -- no la baja por atrás y nos miente.
echo "[armar-kit] VERIFICANDO el payload offline (contenedor limpio, SIN RED)..."
docker run --rm --network none \
    -v "${DESTINO}/offline:/offline:ro" "$IMAGEN" bash -c '
    set -euo pipefail
    echo "  Instalando Docker desde los .deb del kit..."
    dpkg -i /offline/docker/*.deb
    echo "  Instalando postgresql-client-16 y curl desde los .deb del kit..."
    dpkg -i /offline/pgclient/*.deb
    command -v docker     >/dev/null || { echo "FALLO: no quedó el binario docker"; exit 1; }
    command -v pg_isready >/dev/null || { echo "FALLO: no quedó pg_isready"; exit 1; }
    command -v pg_dump    >/dev/null || { echo "FALLO: no quedó pg_dump"; exit 1; }
    command -v curl       >/dev/null || { echo "FALLO: no quedó curl"; exit 1; }
    echo "  OK: el payload offline se instala sin red y deja todos los binarios."' \
    || {
        echo "ERROR: el payload offline NO se instala en un contenedor limpio sin red." >&2
        echo "       Esto es exactamente el fallo que el kit no puede tener en el municipio." >&2
        echo "       Revisá qué dependencia falta y agregala al set de .deb." >&2
        exit 1
    }

# ── Empaquetado ────────────────────────────────────────────────────────────
echo "[armar-kit] Empaquetando..."
TAR_KIT="${SALIDA_BASE}/stockapp-kit-${VERSION_KIT}.tar.gz"
rm -f "$TAR_KIT"
tar -czf "$TAR_KIT" -C "$SALIDA_BASE" "stockapp-kit-${VERSION_KIT}"

echo
echo "[armar-kit] OK."
echo "[armar-kit]   Directorio: ${DESTINO}"
echo "[armar-kit]   Tarball   : ${TAR_KIT}"
echo "[armar-kit]   Tamaño    : $(du -sh "$DESTINO" | cut -f1)"
echo
echo "[armar-kit] Copiá el DIRECTORIO al pendrive (no el tarball: en el servidor no querés"
echo "[armar-kit] depender de poder descomprimir nada)."
echo
echo "[armar-kit] ANTES DE IR AL MUNICIPIO: ensayá el kit completo en una VM limpia."
echo "[armar-kit] Un kit que nunca se probó en una máquina limpia no es un kit."
```

- [ ] **Paso 2: Ignorar la salida en git**

Agregar a `.gitignore`, junto a la línea `deploy/dist/` que ya existe (`:512`):

```
deploy/kit-dist/
```

- [ ] **Paso 3: shellcheck**

Correr: `shellcheck deploy/armar-kit.sh`

- [ ] **Paso 4: Armar el kit de verdad**

```bash
chmod +x deploy/armar-kit.sh
deploy/publish-api.sh 1.0.0
deploy/armar-kit.sh 1.0.0
```

Esperado: termina con "OK" **y con la verificación del payload offline en verde**. Si la verificación falla, el trabajo de esta tarea no está hecho — no lo pases por alto con la excusa de que "en el servidor real va a andar".

**Nota sobre el instalador de Windows:** si `releases/win/` está vacío porque `pack-win.ps1` todavía no se corrió en Windows (es la validación pendiente de Inc7 Fase A), el script aborta con el mensaje correcto. Eso es una **dependencia externa real**, no un bug del plan: el kit no puede estar completo sin el instalador del cliente.

- [ ] **Paso 5: Commit**

```bash
git add deploy/armar-kit.sh .gitignore
git commit -m "feat(kit): arma el pendrive y verifica el payload offline instalandolo sin red"
```

### Task 4.2: `LEEME.md` y `LEEME-clientes.md`

La documentación del kit **es parte del kit**, no un extra: el día de la instalación no hay internet, no hay acceso al repo y no hay nadie a quien preguntar.

**Files:**
- Create: `deploy/kit/LEEME.md`
- Create: `deploy/kit/clientes/LEEME-clientes.md`
- Create: `deploy/kit/clientes/configurar-cliente.cmd` (solo si Decisión 2 = C)

- [ ] **Paso 1: Escribir `LEEME.md`** con estas secciones, en este orden (el orden importa: es el orden en que se necesitan):

1. **El flujo del día, en seis líneas.** `00-preflight` → (emitir licencia en paralelo) → `01-bootstrap` → `02-instalar` → `04-licencia activar` → `03-verificar` → clientes. Con la advertencia de que el paso de licencia va **antes** de tocar las PC.
2. **Qué hacer con la IP, antes de instalar.** Reserva DHCP por MAC en el router (preferible: lo hace el área de sistemas, es reversible, no toca el servidor) o IP estática en netplan. Explicitar que los scripts **no tocan la red a propósito** y por qué (tocar netplan en una red ajena es cómo te quedás sin conectividad, y la política de IPs no es tuya). Y la consecuencia sin adornos: **si el servidor cambia de IP, todas las PC pierden la conexión y hay que pasar el Configurador por cada una, a mano, sin tu ayuda.**
3. **Rescate: qué hacer si algo falla a mitad de camino.** Una subsección por escenario, cada una con los comandos exactos:
   - `01-bootstrap` falló instalando los `.deb` → el kit es para otra versión de Ubuntu; no hay arreglo en el lugar; **no sigas**.
   - `01-bootstrap` falló después de crear el `.env` → **no borres el `.env`**; volvé a correr `01` (es idempotente y no lo pisa).
   - Necesitás empezar de cero de verdad → `docker compose -f /opt/stockapp/docker-compose.postgres.yml down -v` **y** `rm /etc/stockapp/.env`, con la advertencia en mayúsculas de que eso **borra la base**.
   - `02-instalar` falló → `install.sh` ya respaldó lo anterior en `/var/backups/stockapp-api/<timestamp>/`; cómo leer `journalctl -u stockapp-api -n 100`; y el caso de la unit en `failed` por el límite de 5 arranques (`systemctl reset-failed stockapp-api`).
   - La API no responde en la IP de la LAN pero sí en loopback → `API_BIND`, o el firewall.
   - La licencia dice "emitida para otra máquina" → `04-licencia.sh fingerprint` y emitir de nuevo.
4. **El checklist de antes de irse.** Copiar `/etc/stockapp/.env` al pendrive. Copiar el log. Anotar la IP y el código de máquina. Confirmar la reserva DHCP. Dejar escrito en el municipio quién mira `/backups/salud` y cada cuánto (apunta a `deploy/PROCEDIMIENTOS.md`).
5. **Lo que este kit NO hace.** No configura la red. No pone HTTPS (el tráfico de la LAN va en HTTP plano). No instala Windows ni toca las PC más allá del Setup. No deja acceso remoto.

- [ ] **Paso 2: Escribir `clientes/LEEME-clientes.md`**

Para quien instala las PC, probablemente no el mismo día ni la misma persona. Tiene que decir: correr el `Setup.exe`; que el Configurador **viene dentro** del mismo instalador (`GestionMunicipal.Configurador.exe`, sin acceso directo propio — verificado en `pack-win.ps1:36-42`); la URL exacta a cargar (`http://<IP>:<PUERTO>`, el valor que imprimió `03-verificar`); y qué significa cada resultado de "Probar conexión", **incluido** el caso "conecta pero la licencia no está activada" si se implementó la Task 1.3.

- [ ] **Paso 3: `configurar-cliente.cmd`** (solo si Decisión 2 = C)

Escribe `%AppData%\GestionMunicipal\conexion.json` con el contenido `{"Api":{"BaseUrl":"http://<IP>:<PUERTO>"}}`. Debe crear el directorio si no existe, y **preguntar antes de sobreescribir** un `conexion.json` existente. La clave es exactamente `Api:BaseUrl` (`ConexionDefaults.ClaveApiBaseUrl`) y el archivo exactamente `conexion.json` en la carpeta `GestionMunicipal` (`RutaConexion.cs:18-19`). **Requiere prueba en Windows real** — no se puede verificar desde WSL2.

- [ ] **Paso 4: Commit**

```bash
git add deploy/kit/LEEME.md deploy/kit/clientes/
git commit -m "docs(kit): agrega procedimiento del dia, rescate offline y guia de clientes"
```

---

## Fase 5 — El ensayo en máquina limpia

**BLOQUEADA por la Decisión 8.** Depende de las Fases 2, 3 y 4 completas.

Esta fase no produce código. Produce **la única evidencia que vale** de que el kit funciona. Sin ella, todo lo anterior es una hipótesis bien escrita.

> Un kit que nunca se probó en una máquina limpia no es un kit. — diseño 2026-09-10, línea 145

### Task 5.1: Preparar la VM y el snapshot base

- [ ] **Paso 1: Crear la VM** con la versión exacta de `deploy/kit/VERSION`, 4GB de RAM, 40GB de disco, red en bridge (para que tenga una IP de LAN real, no NAT — el chequeo 4 de `03-verificar.sh` depende de eso).
- [ ] **Paso 2: Instalar solo el sistema base.** Nada de Docker, nada de `postgresql-client`. Ese es el punto.
- [ ] **Paso 3: Tomar el snapshot `base-limpia`.** Todas las corridas de esta fase empiezan restaurando este snapshot.
- [ ] **Paso 4: Montar el kit** (pendrive real, o carpeta compartida) y verificar que los scripts tienen permiso de ejecución después de pasar por el medio de transporte — **un pendrive FAT32 pierde el bit de ejecución**. Si pasa, el `LEEME.md` tiene que decir `chmod +x *.sh` en la primera línea.

### Task 5.2: La corrida completa, de punta a punta

- [ ] **Paso 1:** Restaurar `base-limpia`.
- [ ] **Paso 2:** `sudo ./00-preflight.sh`. **Anotar el código de máquina.** Verificar que los AVISOS de Docker/pg-client aparecen como AVISO y **no** como bloqueante (Decisión 5), y que el exit code es 0.
- [ ] **Paso 3:** Emitir la licencia desde la máquina de desarrollo contra ese código, con el binario de la Task 1.2.
- [ ] **Paso 4:** `sudo ./01-bootstrap.sh`. Responder las preguntas. Cronometrarlo: el tiempo total importa para planificar el día.
- [ ] **Paso 5:** `sudo ./02-instalar.sh`.
- [ ] **Paso 6:** `sudo ./04-licencia.sh activar licencia.txt`.
- [ ] **Paso 7:** `sudo ./03-verificar.sh`. **Los 8 chequeos tienen que pasar.** Si alguno falla, arreglar el script (o el kit) y volver al Paso 1 — no parchear a mano en la VM.
- [ ] **Paso 8:** Verificación orgánica desde otra máquina: instalar el cliente Windows, cargar la URL en el Configurador, probar conexión, **entrar con el admin y hacer una operación real** (crear un producto, registrar un movimiento). Si esto no se prueba, no se probó nada.
- [ ] **Paso 9:** Tomar el snapshot `instalado-ok`.

### Task 5.3: Las seis mutaciones del diseño

Cada una parte de un snapshot y verifica que el script **detecta** el problema. El diseño es explícito: *"Si alguna queda en verde, el script no detecta nada."*

- [ ] **Mutación 1 — Sacar `API_BIND`.** Desde `instalado-ok`: borrar la línea `API_BIND` de `/etc/stockapp/.env` (queda el default `127.0.0.1` de `install.sh:185`), correr `02-instalar.sh`, correr `03-verificar.sh`.
  **Esperado: el chequeo 4 en ROJO** (y el 3 en verde — eso es lo que lo hace valioso: loopback anda y la LAN no). Pegar la salida.
- [ ] **Mutación 2 — Apagar `stockapp-pg` antes de instalar.** Desde `base-limpia`: correr `01`, `docker stop stockapp-pg`, correr `02`.
  **Esperado: `02-instalar.sh` aborta** nombrando el contenedor, sin llegar a `install.sh`.
- [ ] **Mutación 3 — Licencia de otra máquina.** Desde `instalado-ok`: emitir una licencia contra un código inventado y activarla.
  **Esperado:** `04-licencia.sh` dice "emitida para OTRA máquina" y menciona la reinstalación del SO como causa posible.
- [ ] **Mutación 4 — Correr `02` sin `01`.** Desde `base-limpia`, directamente `sudo ./02-instalar.sh`.
  **Esperado: aborta** diciendo que falta `/etc/stockapp/.env` y **nombrando `01-bootstrap.sh`**. (Ya se verificó en contenedor en la Task 3.1; repetirlo acá confirma que nada cambió.)
- [ ] **Mutación 5 — Correr todo dos veces.** Desde `instalado-ok`: `00`, `01`, `02`, `03` otra vez, en orden.
  **Esperado:** todo idempotente. En particular: (a) `01` **no pisa** el `.env` y lo dice; (b) `install.sh` respalda en `/var/backups/stockapp-api/<nuevo-timestamp>/`; (c) `00-preflight` **no** marca el 5433 como conflicto, porque lo ocupa `stockapp-pg` (Decisión 5); (d) los 8 chequeos siguen verdes; (e) **los datos que creaste en el Paso 8 de la Task 5.2 siguen ahí.** Si se perdieron, hay un bug grave.
- [ ] **Mutación 6 — Puerto de la API ocupado.** Desde `base-limpia`: `nc -l -p 5080 &`, después `00-preflight.sh`.
  **Esperado: ROJO antes de tocar nada**, nombrando el proceso que lo ocupa. Si la Decisión 1 quedó en B/C, además tiene que nombrar la salida (`--puerto`), y hay que **verificar que esa salida funciona**: correr `01-bootstrap.sh --puerto 8080` y llegar hasta `03-verificar` en verde con ese puerto.

### Task 5.4: Dos mutaciones que el diseño no pide y conviene hacer

Salen de los riesgos reales del contexto, no del documento.

- [ ] **Mutación 7 — Sin internet de verdad.** Desde `base-limpia`, **con la interfaz de red del host desconectada** (no solo sin DNS: desconectada), correr el flujo completo hasta `03-verificar`.
  **Esperado:** todo pasa. Esta es la condición del municipio, y es la que `armar-kit.sh` ya simuló con `--network none` en el build — acá se confirma en el sistema real.
- [ ] **Mutación 8 — Regenerar el `.env` con el volumen ya creado.** Desde `instalado-ok`: `rm /etc/stockapp/.env`, correr `01` (que genera uno nuevo con otra contraseña de Postgres), correr `02`.
  **Esperado:** la API no arranca. **El objetivo de esta mutación no es que el kit lo evite —** la guardia del `.env` ya lo evita por el camino normal — **es comprobar de qué se ve el fallo**, para que el `LEEME.md` lo describa con las palabras exactas que el operador va a ver en `journalctl`. El diseño (línea 88) advierte que Npgsql no pone la connection string en la excepción: hay que confirmar el mensaje real y escribirlo en el rescate del `LEEME`.

### Task 5.5: Cerrar la fase

- [ ] **Paso 1: Volcar los resultados** de las 8 mutaciones en el commit o el reporte, **con la salida real pegada**. Una mutación "razonada" no cuenta (convención del proyecto: repro ≠ guardián).
- [ ] **Paso 2: Cronometrar el día.** Tiempo total de la corrida limpia, y cuánto tarda cada script. El operador necesita saber si esto es una hora o cuatro.
- [ ] **Paso 3: Corregir lo que salió mal** y **volver a correr la Task 5.2 completa** desde `base-limpia`. Un kit arreglado a mitad de un ensayo no está ensayado.
- [ ] **Paso 4: Commit** de las correcciones que surgieron del ensayo.

---

## Fase 6 — `deploy/deploy-vps.sh`

**INDEPENDIENTE de todo el kit.** Se puede hacer en paralelo o primero. Es la E2 del diseño.

Ventaja grande: **el flujo que automatiza se ejecutó con éxito hoy, a mano** (`main` en `f1b0759` instalado en el VPS, 4 migraciones aplicadas, API 200 en `194.163.142.86:5080`). O sea, la especificación de este script no es una hipótesis: es un procedimiento probado horas atrás. Eso lo hace el entregable de menor riesgo del plan.

### Task 6.1: El orquestador

**Files:**
- Create: `deploy/deploy-vps.sh`
- Modify: `deploy/DEPLOY.md` (enlazar el script desde las secciones 3 y 8)

**Interfaces:**
- Consume: `deploy/publish-api.sh`; `ssh -p 34377`; `scp -P 34377`; `deploy/install.sh` en el VPS; `GET /licencia/estado`.
- Produce: firma `deploy/deploy-vps.sh <version> [--dry-run] [--sin-backup]`. (Sin `--con-soporte`: ver Decisión 10.)

**Los seis pasos, con las precisiones del plan:**

| Paso | Qué hace | Nota |
|---|---|---|
| 1 | Pre-vuelo de migraciones por SSH | Consulta de duplicados case-insensitive contra `stockapp-pg`. Abortar según Decisión 11 |
| 2 | Backup con la versión vieja viva + `scp` de bajada | El backup vive **dentro** de la API: si la API no levanta, no hay backup. Por eso va **antes** de tocar nada |
| 3 | `publish-api.sh` | |
| 4 | `scp` del tarball; de `install.sh`/`.service` **solo si cambiaron** | Decidir "cambiaron" con `git diff` contra el commit del último deploy |
| 5 | `ssh` + `install.sh` | Nombre exacto del tarball, nunca un glob (`install.sh:40-46`) |
| 6 | Verificación remota | `/licencia/estado` + conteo de `__EFMigrationsHistory` + `systemctl is-active` |

**Por qué el paso 1 existe y no es paranoia:** la migración del guardián de catálogos hace `RAISE EXCEPTION` a propósito si encuentra duplicados case-insensitive. `MigrateAsync()` corre **antes** de `app.Run()` y **sin try/catch**, así que una migración que falla tumba el arranque; y tras 5 arranques fallidos en 10 minutos, `StartLimitIntervalSec=600`/`StartLimitBurst=5` dejan la unit en `failed` y systemd **rechaza** cualquier `start` hasta que pasen esos 600s (`install.sh:432-441` ya hace `reset-failed` por eso). Sin el pre-vuelo, el modo de fallo es: deploy a medias, servicio caído, y una espera de 10 minutos antes de poder reintentar.

- [ ] **Paso 1: Escribir el esqueleto con variables, nunca hardcodeo**

El host y el puerto SSH van en variables de entorno con default, no incrustados:

```bash
VPS_HOST="${VPS_HOST:-194.163.142.86}"
VPS_USER="${VPS_USER:?Definí VPS_USER (usuario SSH del VPS)}"
VPS_SSH_PORT="${VPS_SSH_PORT:-34377}"
VPS_DIR="${VPS_DIR:-~/stockapp-deploy}"
```

- [ ] **Paso 2: Implementar el paso 1 (pre-vuelo) y probarlo con `--dry-run`**

`--dry-run` corre **solo** el paso 1 y no toca nada. Es el primer entregable útil y se puede probar contra el VPS real **sin riesgo**, hoy mismo.

La consulta corre por `docker exec` contra `stockapp-pg`. **Ojo con un falso positivo conocido de este proyecto:** `docker exec` + `psql -h 127.0.0.1` puede saltear la autenticación por password vía la regla `trust` del propio contenedor. Para el pre-vuelo eso no importa (solo leemos), pero **no tomes ese éxito como prueba de que las credenciales del `.env` son correctas** — esa validación se hace desde el host.

- [ ] **Paso 3: Implementar el paso 2 (backup) y verificar que el archivo bajado no está vacío**

No alcanza con que el `scp` devuelva 0: verificar que el archivo existe localmente y tiene un tamaño razonable. Un backup de 0 bytes es peor que no tener backup, porque parece que tenés uno.

- [ ] **Paso 4: Implementar los pasos 3 a 6**

El `--sin-backup` existe porque el usuario prefiere no hacerlo, pero **el default hace backup**: un script que por omisión deja sin punto de retorno está mal diseñado (diseño línea 135). El flag debe imprimir una advertencia cuando se usa.

- [ ] **Paso 5: shellcheck**

Correr: `shellcheck deploy/deploy-vps.sh`

- [ ] **Paso 6: Probar contra el VPS real, en este orden**

1. `deploy/deploy-vps.sh <version> --dry-run` → solo el pre-vuelo. Verificar que no toca nada.
2. Un deploy real de una versión sin cambios funcionales (un no-op), para ejercitar los 6 pasos con riesgo mínimo.
3. Verificar que la API sigue respondiendo 200 y que el conteo de migraciones no cambió.

- [ ] **Paso 7: Enlazar desde `DEPLOY.md`**

En la sección 3 ("Publicar, copiar e instalar la API") y en la 8 ("Actualizar a una versión nueva"), agregar el camino automatizado **sin borrar el manual**: el manual es el fallback cuando el script falla, y es el que está verificado en producción.

- [ ] **Paso 8: Commit**

```bash
git add deploy/deploy-vps.sh deploy/DEPLOY.md
git commit -m "feat(deploy): agrega orquestador de deploy al VPS con prevuelo de migraciones y backup"
```

---

## Fase 7 — Procedimientos escritos (E3)

**INDEPENDIENTE.** Es la entrega que más barato sale y que cubre el riesgo más grande de todos.

El diseño lo dice sin eufemismos (línea 119): los backups van al **mismo disco** que la base; muere el disco, se van los dos, y no hay acceso al servidor. La solución técnica **ya está construida y verificada** (`GET /backups` + descarga desde el desktop admin). Lo que falta no es código: es que alguien lo haga, con una frecuencia definida.

### Task 7.1: `deploy/PROCEDIMIENTOS.md`

**Files:**
- Create: `deploy/PROCEDIMIENTOS.md`
- Modify: `deploy/DEPLOY.md` (enlazarlo), `deploy/kit/LEEME.md` (enlazarlo desde el checklist de cierre)

- [ ] **Paso 1: Escribir el procedimiento de bajada de backups**

Con: **quién** (un rol nombrado en el municipio, no "alguien"), **cada cuánto** (un número, no "periódicamente"), **cómo** (los clics exactos en el desktop admin), **dónde se guardan** (fuera del servidor — ese es el punto entero), y **cómo se verifica** que el archivo bajado sirve. Un backup que nunca se restauró no es un backup.

- [ ] **Paso 2: Escribir el procedimiento de monitoreo de `/backups/salud`**

El diseño es claro: si el firewall bloquea la salida, el webhook de alerta no existe, y **el reemplazo en LAN es que alguien mire `/backups/salud` en el desktop admin**. Hay que escribir quién mira, cada cuánto, y qué hace si ve `vencido: true`. Sin esa persona nombrada, el dead-man's-switch no existe.

- [ ] **Paso 3: Documentar el escenario "reinstalaron el servidor"**

Qué se pierde (la licencia, porque el `machine-id` se regenera), qué hace falta para recuperarse (el `.env` del pendrive + una licencia nueva emitida con la clave privada), y quién puede hacerlo (solo quien tenga la clave privada). Si esa clave se pierde, el sistema no se puede reactivar en ninguna máquina: la copia de `~/stockapp-claves` no es opcional.

- [ ] **Paso 4: Commit**

```bash
git add deploy/PROCEDIMIENTOS.md deploy/DEPLOY.md deploy/kit/LEEME.md
git commit -m "docs(deploy): agrega procedimientos de bajada de backups y monitoreo de salud"
```

---

## Estrategia de verificación — qué se automatiza y qué no

Esta es la parte más difícil del plan y merece franqueza en vez de optimismo. **La mayor parte de este entregable no es código C# con xUnit: son scripts de bash que instalan paquetes, levantan daemons, escriben en `/etc/` y tocan el firewall.** Pretender que eso se cubre con tests sería teatro, y el teatro es peor que la ausencia de tests porque genera confianza falsa.

La estrategia tiene tres niveles, y cada nivel cubre **menos** superficie que el anterior pero da **más** certeza.

### Nivel 1 — Tests reales en la suite de xUnit (corren en segundos, en cada build)

Cubre la lógica bash **pura** — la que tiene un oráculo independiente contra el que comparar.

| Qué | Oráculo | Archivo |
|---|---|---|
| Fingerprint bash ↔ C# | `FingerprintMaquinaBase.CodigoAgrupado` | `FingerprintParidadBashTests.cs` |
| `es_puerto_valido` | Rango TCP | `ValidacionesBashTests.cs` |
| `es_ipv4_valida` | `install.sh:187-197` | `ValidacionesBashTests.cs` |
| `password_admin_valida` | `ContrasenaValidator.cs:9,23-25` | `ValidacionesBashTests.cs` |
| Sonda del Configurador | `LicenciaEndpoints.cs:18-19` | `ProbadorConexionTests.cs` |

**El mecanismo** (Decisión 9b): `tests/_Compartido/EjecutorBash.cs` lanza `bash -c 'source lib/...; funcion args'` y compara exit code o stdout. Un solo test runner, sin `bats`, sin una segunda historia de CI.

**Por qué esto vale la pena y no es ceremonia:** el test de paridad del fingerprint es el ejemplo perfecto. La mutación "olvidarse del `Trim()`" no produce un valor "parecido" — produce `DBDA-82E1-...` en lugar de `4617-8EFE-...` (verificado empíricamente al escribir este plan). Un fingerprint equivocado no falla hasta el momento de activar la licencia, **en el municipio, sin internet**, y con un mensaje de error ("la licencia fue emitida para otra máquina") que manda a buscar el problema al lugar equivocado. Ese defecto cuesta 30 segundos de test acá y un viaje perdido allá.

**Qué NO cubre este nivel:** absolutamente nada de lo que el kit le hace al sistema.

### Nivel 2 — Contenedores Docker efímeros (corren en minutos, a mano)

Docker es **la única herramienta de aislamiento instalada en esta máquina** (verificado: no hay `multipass`, `vagrant`, `VBoxManage` ni `lxc`). Sirve para tres cosas concretas y para nada más:

1. **El payload offline se instala sin red.** `armar-kit.sh` corre `dpkg -i` sobre los `.deb` del kit en un contenedor limpio de la versión destino con `--network none`. Esto convierte el fallo más caro del kit (una dependencia faltante descubierta en el municipio) en un fallo del día del build. **Es la verificación automatizada de mayor valor de todo el plan después del test de fingerprint.**
2. **`00-preflight.sh` no explota y cuenta bloqueantes.** Incluida la mutación "puerto 5080 ocupado → rojo".
3. **`02-instalar.sh` aborta si falta `01`.** La mutación "correr 02 sin 01" completa.

**Qué NO cubre este nivel, y hay que decirlo claro:** un contenedor **no tiene systemd**, así que nada de lo que hace `install.sh` (instalar la unit, arrancar el servicio, el límite de 5 arranques) se puede ejercitar. Tampoco tiene netplan real, ni `ufw` con sentido, ni una IP de LAN propia. Y `01-bootstrap.sh` **instala Docker**: probarlo dentro de Docker requiere Docker-in-Docker, que no se parece al caso real en lo que importa — justamente en la instalación de los paquetes del sistema.

### Nivel 3 — VM limpia con snapshot (la única prueba que vale)

**Respuesta honesta a la pregunta "qué requiere una prueba manual en una máquina limpia": casi todo lo que importa.**

Esto **solo** se prueba en una VM desde cero:

- `01-bootstrap.sh` completo: instalación de Docker desde `.deb`, arranque del daemon, carga de la imagen, generación de secretos, `ufw`.
- `install.sh` vía `02`: la unit de systemd, el arranque del servicio, las migraciones contra Postgres, el healthcheck.
- Los chequeos 1, 2, 5, 6 y 7 de `03-verificar.sh` (systemd, la unit, `__EFMigrationsHistory`, login, backups).
- **El chequeo 4**, que es el más importante del kit para la LAN y necesita una IP de red real, no NAT.
- La activación de licencia de punta a punta, contra un `machine-id` real.
- **La idempotencia del flujo completo**, que es la propiedad que el diseño exige de los cinco scripts y la que más fácil se rompe sin que nadie lo note.
- La detección de DHCP.
- La instalación del cliente Windows y el Configurador (y eso necesita, además, **una máquina Windows** — es la validación que Inc7 Fase A todavía tiene pendiente).

**Seis de las seis mutaciones que el diseño exige** (líneas 149-157) se ejecutan acá; dos de ellas (4 y 6, parcialmente) se pueden adelantar a contenedor, y el plan lo hace, pero se repiten en la VM porque el entorno es parte de lo que se prueba.

**El costo de no hacer el Nivel 3 no es "menos cobertura": es que el kit no está probado.** Y el momento en que se descubriría está a la máxima distancia posible de poder arreglarlo: sentado en un servidor ajeno, sin internet, sin poder volver.

### Qué NO se puede verificar antes de la instalación real, ni siquiera en la VM

Hay que nombrarlo porque de otro modo alguien va a suponer que está cubierto:

- **Que el hardware del municipio sea el que dijeron.** El preflight lo detecta el día de la instalación, que es tarde pero mejor que nada. Es el motivo por el que el preflight existe.
- **Que el firewall del municipio deje pasar lo que hace falta entre las PC y el servidor.** Hay un switch, reglas y políticas que no controlás.
- **Que la reserva DHCP se haga efectivamente**, y que siga vigente en seis meses.
- **Que la versión de Ubuntu que instale el proveedor del servidor sea la fijada.** Si no lo es, el kit aborta en el preflight — correctamente, pero igual de bloqueado.
- **Que alguien mire `/backups/salud`.** Esto no es verificable por ningún medio técnico: es la Fase 7, y es un acuerdo humano.

### La convención del proyecto que aplica a todo lo anterior

**Repro ≠ guardián.** Cada mutación se verifica **ejecutándola y pegando el rojo**, nunca razonando que "debería fallar". Y cada guardián nuevo se valida al revés: reintroducir el defecto tiene que ponerlo en rojo. Un test que pasa sin haber fallado primero no custodia nada.

---

## Riesgos

### El escenario que define el diseño: el kit falla a mitad de la instalación, sin internet, sin volver

Este es el riesgo que ordena todas las decisiones del plan, así que vale desarmarlo por etapas. La pregunta en cada una es la misma: **¿en qué estado queda el servidor y se puede salir de ahí sin internet?**

| Falla en | Estado del servidor | ¿Recuperable en el lugar? | Mitigación del plan |
|---|---|---|---|
| `00-preflight` | **Intacto.** Es read-only por contrato | Sí: nada que deshacer | Que el preflight corra **antes** de todo es precisamente esto. Su valor es que el fallo más barato ocurra primero |
| `01-bootstrap`, en los `.deb` | Sistema con paquetes a medio instalar | **No del todo.** Si el kit es para otra versión de Ubuntu, no hay arreglo posible sin internet | `armar-kit.sh` verifica el payload con `--network none` el día del build. Y el preflight compara la versión contra `VERSION` y aborta en rojo **antes** |
| `01-bootstrap`, después del `.env` | `.env` generado, quizá sin Postgres | Sí: `01` es idempotente y **no pisa el `.env`** | La guardia del `.env` (diseño línea 88). Documentado en el rescate del `LEEME` |
| `02-instalar` | `install.sh` respaldó lo anterior en `/var/backups/stockapp-api/<ts>/`; swap atómico, así que no hay un `/opt` a medias | Sí | Es mérito de `install.sh`, no del kit: staging + swap atómico + backup + poda ya están verificados en producción |
| `02-instalar`, unit en `failed` | El servicio no arranca y systemd rechaza `start` por 600s | Sí, pero con espera | `install.sh:441` ya hace `reset-failed`. El `LEEME` documenta el comando y el síntoma |
| `04-licencia` | API viva pero bloqueada (423 en casi todo) | Sí, si tenés la clave privada a mano | El preflight imprime el fingerprint al principio: la licencia se emite en paralelo, no al final |
| `03-verificar` en rojo | Instalado pero algo no anda | Depende del chequeo | Cada fallo imprime **el diagnóstico y el comando**, no solo el rojo |
| **Después de irte** | Cualquier cosa | **NO.** Sin acceso, sin excepciones | Es el riesgo que no se mitiga con scripts: se mitiga con el `.env` copiado al pendrive, el log guardado, y la Fase 7 |

### Los riesgos que sobreviven a cualquier script

1. **Pérdida de `/etc/stockapp/.env`.** Si el operador se va sin copiarlo, y algún día hace falta entrar a la base de datos, **no hay forma de recuperar la contraseña de Postgres**. No hay backdoor, y es correcto que no lo haya. Mitigación: el recordatorio en rojo al final de `03-verificar.sh`. **Sugerencia a considerar:** que ese recordatorio sea una **confirmación que bloquea** (`read -p "Escribí COPIADO para terminar"`) en vez de un cartel. Un cartel al final de 60 líneas de salida se lee el 50% de las veces.
2. **Cambio de IP del servidor.** El diseño ya lo llama el riesgo principal del despliegue (línea 113). Todas las PC pierden la conexión y hay que pasar el Configurador por cada una, a mano, **sin tu ayuda**. Mitigación: reserva DHCP por MAC (lo hace sistemas, es reversible), el aviso grande del preflight, y —si se resuelve la Decisión 2 como C— el `.cmd` que al menos hace que reconfigurar sea un doble clic en vez de tipear una URL.
3. **Reinstalación del sistema operativo.** Invalida la licencia (`machine-id` nuevo) y el sistema deja de funcionar, sin que nadie en el municipio pueda arreglarlo. Mitigación: documentarlo en los términos más fuertes en el `LEEME` y en `PROCEDIMIENTOS.md`.
4. **Pérdida de la clave privada de licenciamiento** (`~/stockapp-claves/clave-privada.pem`). Si se pierde, **ningún servidor se puede relicenciar nunca más**. Es un punto único de fallo de todo el modelo de licenciamiento, fuera de este repo. Mitigación: no es técnica — es tener una copia, y ya estaba anotado como pendiente.
5. **Backups en el mismo disco que la base** (diseño línea 119). Muere el disco y se van los dos. Es el riesgo de mayor impacto de todo el despliegue y **la Fase 7 es su única mitigación.** Por eso la Fase 7 no es opcional ni "documentación que se hace al final": es la entrega con mejor relación costo/riesgo de todo el plan.
6. **El dead-man's-switch no existe en la LAN.** El webhook de alerta de backup necesita salida a internet; si el firewall la bloquea, nadie se entera de que los backups dejaron de correr. El reemplazo es humano: alguien mira `/backups/salud`. Si no hay nombre y frecuencia escritos, no hay monitoreo.
7. **HTTP plano en la LAN.** Usuario, contraseña y JWT viajan sin cifrar. `install.sh:211-221` ya advierte de esto. Está **aceptado y fuera de alcance**, pero tiene que estar escrito en el `LEEME` para que nadie lo descubra como sorpresa en una auditoría.
8. **`ufw` mal configurado deja al servidor inaccesible por SSH.** En el VPS sería catastrófico; acá el proveedor está sentado en la consola, así que es recuperable. Pero si alguien del municipio administra esa máquina remotamente, sí es grave. Mitigación: la regla de SSH va **primero** y el script **pregunta** antes de activar `ufw`.
9. **`/opt/stockapp/` (el compose) y `/opt/stockapp-api/` (los binarios) son dos rutas casi idénticas.** Un `rm -rf /opt/stockapp*` durante un troubleshooting se lleva las dos. Impacto moderado (el compose se reinstala; el volumen de datos vive en `stockapp_pgdata`, no ahí), pero es una trampa gratuita. **Sugerencia:** que el compose vaya a `/opt/stockapp-postgres/`. Es un cambio de una constante en `01-bootstrap.sh` y no toca `install.sh`.
10. **El `.env` real filtrándose al pendrive o al repo.** `armar-kit.sh` tiene una guardia explícita contra copiar `deploy/.env` a `servidor/`, y `deploy/.env` ya está en `.gitignore:511`. Pero el `.env` **generado** que el operador copia al pendrive es un archivo con secretos viajando en un dispositivo extraíble. Mitigación: decir en el `LEEME` que ese pendrive se guarda como se guarda una llave, no en un cajón de la oficina.
11. **Un pendrive FAT32 pierde el bit de ejecución de los `.sh`.** Fallo trivial y desmoralizante en el minuto uno. Mitigación: la primera línea del `LEEME` es `chmod +x *.sh lib/*.sh servidor/*.sh`, y la Task 5.1 lo verifica con el medio de transporte real.
12. **El instalador de Windows todavía no se generó en Windows real.** `build/pack-win.ps1` existe pero su validación es la pendiente de Inc7 Fase A. **Sin ese `Setup.exe`, el kit no está completo** — `armar-kit.sh` aborta correctamente, pero es una dependencia externa a este plan que hay que destrabar antes del día de la instalación.

---

## Qué NO entra en este alcance

Explícito para que nadie lo interprete como un olvido:

- **El camino Windows Server.** La decisión 1 del diseño lo deja como alternativa solo si el municipio lo impone. Implicaría rehacer `install.sh` con NSSM o un servicio de Windows, Docker Desktop, y el fingerprint por registro — que existe (`FingerprintMaquinaWindows.cs`) pero **nunca se probó en un servidor real**.
- **Modificar `deploy/install.sh`**, salvo que la Decisión 4 se resuelva por la opción B y el usuario lo autorice explícitamente.
- **Configurar la red del servidor.** Ni netplan, ni IP estática, ni DHCP. Es deliberado (diseño línea 113): tocar la red de un tercero es cómo te quedás sin conectividad, y la política de IPs es del área de sistemas del municipio. El kit **detecta y avisa**.
- **HTTPS/TLS en la LAN.** El tráfico va en HTTP plano. Riesgo conocido, aceptado y documentado, no resuelto acá.
- **Firmar digitalmente el instalador de Windows.** Es la deuda D7 de Inc7 Fase A (`pack-win.ps1:149-151`), sigue abierta y no se cierra en este alcance.
- **Un orquestador SSH para la LAN.** Decisión 3 del diseño: lo ejecuta el propio proveedor, presencialmente, sentado en el servidor.
- **Generar el paquete Velopack del cliente.** `build/pack-win.ps1` ya existe y necesita Windows. El kit **transporta** el artefacto, no lo produce.
- **`--con-soporte`.** Ver Decisión 10: no tiene semántica definida y no se implementa hasta que la tenga.
- **CI automatizado del ensayo en VM.** Sería lo correcto a futuro, pero montar la infraestructura para eso es un proyecto propio, más grande que este.
- **Vencimiento o renovación de licencias.** `LicenciaPayload` no lleva fecha de expiración (`Program.cs` de la CLI, `:40`): una licencia emitida vale para siempre en esa máquina. No se cambia acá.
- **Acceso remoto de soporte post-instalación.** Choca de frente con la restricción dura del proyecto y con lo que se le prometió al municipio.
- **Migrar datos de un sistema anterior.** No hay nada que migrar: es una instalación nueva.
- **Cambiar el puerto de fábrica del desktop.** Ver Decisión 2, opción A, no recomendada.

---

## Self-Review

**1. Cobertura del diseño.** Recorrido sección por sección:

| Sección del diseño | Dónde se implementa |
|---|---|
| Estructura del kit (líneas 34-49) | File Structure + Task 4.1 |
| Flujo del día (línea 51) | Task 4.2 (`LEEME.md`) + el orden de las Tasks 3.x |
| Reglas transversales (línea 55) | Task 2.1 (`lib/log.sh`) + Global Constraints |
| `00-preflight.sh` (líneas 59-77) | Task 2.2, con el mapa de severidades de la Decisión 5 |
| `01-bootstrap.sh` (líneas 79-88) | Task 3.4 |
| `02-instalar.sh` (líneas 90-92) | Task 3.1 |
| `03-verificar.sh` (líneas 94-105) | Task 3.3, con la corrección 4 del chequeo 2 |
| `04-licencia.sh` (líneas 107-109) | Task 3.2 |
| La IP (línea 113) | Task 2.2 (aviso DHCP) + Task 4.2 (`LEEME`) + Riesgo 2 |
| El firewall (línea 115) | Task 3.4, paso 6 |
| Riesgo de backups (línea 119) | Fase 7 + Riesgo 5 |
| `deploy-vps.sh` (líneas 121-135) | Fase 6 |
| Arreglo 1 (Configurador) | Task 1.3, **gateado por la Decisión 3** (quedó obsoleto tal como está redactado) |
| Arreglo 2 (paridad fingerprint) | Task 1.1 |
| Arreglo 3 (CLI self-contained) | Task 1.2 |
| Estrategia de prueba (líneas 143-158) | Fase 5 + la sección de Estrategia de verificación |
| E1 / E2 / E3 (líneas 160-163) | E1 = Fases 1-5; E2 = Fase 6; E3 = Fase 7 |

**Sin huecos de cobertura.** Lo único que el diseño menciona y este plan **no** implementa es `--con-soporte` (Decisión 10) y el arreglo 1 tal como está escrito (Decisión 3) — ambos señalados como decisión abierta, no omitidos en silencio.

**2. Barrido de placeholders.** Sin "TBD", sin "agregar validación apropiada", sin "similar a la Task N". Donde el plan pide leer un archivo antes de escribir (el shape del login en la Task 3.3, paso 3; la convención de `ClaseEstado` en la Task 1.3, paso 5) es **a propósito y está dicho explícitamente**: inventar el nombre de un campo que no verifiqué sería exactamente la clase de error que este plan existe para evitar.

**3. Consistencia de tipos y nombres.** Verificado: `EjecutorBash.Ejecutar` se define en la Task 1.1 y se consume en la 2.1 con la misma firma. `fingerprint_de_archivo` / `fingerprint_de_esta_maquina` se definen en la 1.1 y se consumen en la 2.2 y la 3.2 con los mismos nombres. `es_puerto_valido` / `es_ipv4_valida` / `password_admin_valida` / `puerto_libre` / `quien_escucha` se definen en la 2.1 y se usan en la 2.2 y la 3.4. `iniciar_log` / `info` / `ok` / `aviso` / `rojo` / `error_fatal` se definen en la 2.1 y se usan en los cinco scripts. `OkLicenciaSinActivar` se agrega en la 1.3 y no se referencia en ninguna otra tarea. `/etc/stockapp/.env` es la misma ruta en las Tasks 3.1, 3.2, 3.3 y 3.4.

**Una inconsistencia encontrada y dejada a propósito:** `lib/validaciones.sh` expone `subred_de` en el bloque de Interfaces de la Task 2.1 pero la implementación no la incluye — porque `01-bootstrap.sh` resuelve la subred con `ip -4 -o addr show scope global` inline, que es más directo. **Quien ejecute la Task 2.1 debe borrar `subred_de` de la lista de Interfaces**, no escribir una función que nadie llama.

---

## Por dónde arrancar

**Task 1.1 (paridad del fingerprint).** Tres razones:

1. **No depende de ninguna decisión abierta.** Se puede empezar hoy, mientras las once decisiones se discuten.
2. **Es el defecto más asimétrico del plan:** 30 segundos de test acá contra un viaje perdido al municipio allá, con un mensaje de error que apunta al lugar equivocado.
3. **Desbloquea las Fases 2 y 3:** produce `lib/fingerprint.sh`, que consumen `00-preflight.sh` y `04-licencia.sh`, y produce `EjecutorBash.cs`, que es el mecanismo de todo el Nivel 1 de verificación.

Después, dos caminos en paralelo: **Tasks 1.2 y 1.3** (los otros dos arreglos de E1, también sin decisiones bloqueantes), y **Fase 6** (`deploy-vps.sh`), que es independiente del kit, tiene el riesgo más bajo porque automatiza un flujo ejecutado con éxito hoy, y da valor en el próximo deploy al VPS.

**Lo que hay que destrabar mientras tanto, porque bloquea fases enteras:** la Decisión 7 (versión de Ubuntu → Fase 4), la Decisión 8 (host de VM → Fase 5) y el `Setup.exe` de Windows (dependencia externa → Fase 4).
