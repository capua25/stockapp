# Migración de VPS automatizada (preparar + migrar) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar el runbook manual de la migración por dos orquestadores probados (`deploy/preparar-vps-nuevo.sh` y `deploy/migrar-vps.sh`), con el cambio chico a `deploy-vps.sh` que habilita el primer deploy sobre una base vacía, el arnés local extendido a dos hosts y un runbook corto.

**Architecture:** Dieciséis tasks en serie sobre `feat/migracion-vps-automatizada`. (1) El pre-vuelo de `deploy-vps.sh` acepta "la tabla no existe". (2) `deploy/lib/orquestacion.sh`: lo común a los dos orquestadores (líneas de progreso, estado atómico, lock, lectura de `.env` sin `source`, confirmación, ssh con rc/stderr separados). (3) Un host simulado con estado para los tests de `preparar`: un contenedor `ubuntu:24.04` descartable como root, con shims fieles de `apt-get`, `ufw`, `certbot`, `systemd-run`, `systemctl` y `docker`. (4-7) `preparar-vps-nuevo.sh` paso por paso. (8-12) `migrar-vps.sh`: pre-vuelo, secuencia del corte, `--continuar`/`--ensayo`, `--revertir`, `--desde-backup`/`--post-corte`. (13-14) Arnés local con dos hosts, API real en los dos y los casos de punta a punta. (15) Runbook corto y guardianes de sincronía. (16) Ensayo local completo de los dos orquestadores, merge y push.

**Tech Stack:** bash (`set -euo pipefail`, `flock`, `jq`, `curl`, `sha256sum`), Docker (`postgres:16-alpine`, `ubuntu:24.04`), xUnit 2.5.3 en `tests/StockApp.Infrastructure.Tests` con `EjecutorBash` (`tests/_Compartido/EjecutorBash.cs`), `EsperaMonotonica` (`tests/_Compartido/EsperaMonotonica.cs`), `PostgresFixture` (`tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs`) y `EjecutorPgDumpProceso`. .NET 10 solo para publicar y correr la API en el arnés: este plan NO toca C# de producción.

**Spec:** `docs/superpowers/specs/2026-10-20-migracion-vps-automatizada-design.md` (aprobada por Martin el 2026-10-20, "sí a todo"; §10 resuelve las cuatro preguntas abiertas). Sigue vigente la spec anterior `docs/superpowers/specs/2026-10-20-migracion-vps-design.md` para A1-A7. Las desviaciones están en "Hallazgos contra la spec", al final, con evidencia.

## Global Constraints

- **Worktree:** `git worktree add -b feat/migracion-vps-automatizada /home/capua25/workspace/stockapp-worktrees/migracion-vps-automatizada main`, con `main` limpio y en el commit de este plan o posterior (`git -C /home/capua25/workspace/stockapp status --short` vacío). Todo comando corre como `cd /home/capua25/workspace/stockapp-worktrees/migracion-vps-automatizada && <comando>`. Nunca bajo `/tmp`.
- **CodeGraph:** el worktree tiene su PROPIO índice: `gentle-ai codegraph init --cwd /home/capua25/workspace/stockapp-worktrees/migracion-vps-automatizada` una vez; nunca copiar ni enlazar el `.codegraph/` de otro checkout. Todo prompt de implementador o reviewer dice "usá CodeGraph MCP (`codegraph_explore`) primero para C#; grep/Read solo como fallback". Para `.sh`, `.md`, compose, units y `.env.example` el fallback correcto es Read/`grep` (CodeGraph no indexa ese contenido). Antes de editar C# se contrastan líneas y miembros con `sed -n` (el índice ya devolvió versiones viejas y "already sent" falsos).
- **TDD:** test primero, rojo REAL pegado en el ledger, código mínimo, verde. Ningún paso de código sin su test previo.
- **Mutación QUITANDO, nunca agregando:** cada guardián se verifica así: `export SCRATCH=~/.cache/stockapp-migracion-auto && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la línea o el fragmento; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas; se corre el test y se PEGA el rojo; `cp "$SCRATCH/<nombre>.bak" <archivo>` y `git diff --quiet -- <archivo>` confirma la restauración. Nunca se agrega un efecto destructivo para "ver si el test lo detecta".
- **Bash:** `set -euo pipefail` en todo script nuevo. Un `grep` en un pipe sin coincidencias mata el script: donde "no hay match" es válido se captura el exit code explícito (`rc=0; x="$(...)" || rc=$?` y se distingue 1 de ≥2). PROHIBIDO `|| true` sobre una consulta cuyo resultado importa. Cada consulta remota captura por separado exit code, stdout y stderr: **"no pude consultar" nunca es "vacío", "cero", "no existe" ni "sin duplicados"**, y el 255 de ssh es siempre "no pude consultar" (exit 8). Una respuesta que no es exactamente uno de los valores esperados es "no pude interpretar", no un default.
- **Los shims se comportan como la herramienta real.** Cada shim cita en su encabezado el comportamiento real que reproduce (versión verificada, como hoy `deploy/ensayo/shims/systemctl` con systemd 255) y falla donde la real falla. Un stub que miente esconde bugs: ya pasó con `mask` (el shim viejo enmascaraba sobre un archivo regular y la real no). Si no se puede verificar un comportamiento real, se dice en el encabezado y el test no se apoya en él.
- **VPS viejo compartido:** todo comando remoto contra el viejo está acotado por nombre a StockApp (`stockapp-api`, `stockapp-pg`, `/var/lib/stockapp`, `/etc/stockapp-api`, `~/stockapp-deploy`). Nunca nginx (se dice "el proxy de la otra app"), nunca certbot, ufw ni nada global de Docker o systemd; ningún dump ni archivo temporal pasa por el `/tmp` del host. `migrar-vps.sh` y `deploy/lib/orquestacion.sh` entran en `MigracionVpsSincroniaTests.ScriptsNuevos` con todos los tokens prohibidos (también en comentarios y en los comandos de referencia que imprime `--post-corte`).
- **Secretos:** nunca en argv, en archivos de la máquina del operador ni en logs; viajan por stdin o en el entorno de un único comando (`VAR=… comando`, nunca `export` de sesión). `POSTGRES_PASSWORD`, `JWT_SECRET` y `BOOTSTRAP_PASSWORD` se generan en el servidor. El token de V5/V8 vive en una variable.
- **Sin hosts cableados:** ningún host, IP, puerto ni dominio del VPS actual en scripts, tests, plan o runbook (`ScriptsNuevos_SinHostsNiIpsCableados`, con la única excepción `docker.io`). Los tests usan hosts `*.invalid`.
- **`deploy/.env` y `deploy/.env.vps-nuevo` NUNCA los escribe un agente** (regla deny). Hoy `deploy/.env` NO define `VPS_HOST`, `VPS_USER` ni `VPS_SSH_PORT` (verificado: solo nombres) y `deploy/.env.vps-nuevo` no existe: el runbook se los pide a Martin.
- **Contenedores y procesos:** nombre propio (`stockapp-test-<guid12>`, `stockapp-ensayo-*`), eliminados en `finally` por nombre EXACTO (`docker rm -f -v <nombre>`); procesos por PID exacto, verificados con `ps`/`docker ps` al terminar. Nada de `pkill -f`. **Nunca se toca el contenedor de desarrollo `stockapp-pg` ni ningún `pinar-*`** (ni siquiera una lectura con `docker exec`: los tests pasan su contenedor con un parámetro o un envoltorio de `docker` que solo reescribe el nombre).
- **Tests sin efectos fuera de su temporal:** todo test de orquestador corre con `HOME` apuntando a su directorio temporal (corridas, lock y `~/stockapp-backups-vps` quedan ahí) y con la higiene de entorno de `EnsayoLocalTests.Bash` (sin `STOCKAPP_ENV_LOCAL`, `VPS_*`, `ENSAYO_*`, `STOCKAPP_ENSAYO_*` heredados; ruling F2).
- **Nada de espera en segundo plano:** ningún implementador ni reviewer usa `run_in_background`, la herramienta Monitor, `sleep` ni loops de espera o polling. Las suites largas corren en primer plano con el timeout del Bash (hasta 600000 ms) y, si no entran, partidas por `--filter`. Las esperas acotadas dentro de los scripts de producción y de los tests (`healthy`, arranque, V8) usan reloj monotónico con límite. **Esta regla va textual en cada prompt de implementador y reviewer.**
- **.NET:** tests de a UN `.csproj` por comando y nunca dos `dotnet test` a la vez: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~<Clase>`. No se compila por rutina; el arnés publica la API una vez por corrida (necesario, no rutina). Rojos falsos conocidos en worktree: los de fixtures `.ods` (confirmar por nombre). Cualquier otro rojo es real.
- **Commits:** conventional commits en español, uno por task, SIN `Co-Authored-By` ni ninguna línea de atribución de IA; `git add` con rutas exactas (nunca `-A` ni `.`), verificado con `git diff --cached --name-only`. Sin `git reflog expire`, `git gc` ni `git prune`. Sin referencias al plan en el código ("Task 4" no aparece en `.sh`/`.cs`).
- **Ledger** git-ignored: `.superpowers/sdd/2026-10-20-migracion-vps-automatizada/progress.md` (línea base, rojos, mutaciones, tiempos). Siguen vigentes los rulings F1, F2, F4, F6, F7, F8, F9, F10, F12 y F20 del ledger archivado `/home/capua25/stockapp-sdd-archivo/2026-10-20-migracion-vps/progress.md`.
- **Contra un VPS real no corre NADA en este plan.** La spec no autoriza lecturas reales durante la implementación (§8.3 es el GATE posterior, con el VPS nuevo creado). Ningún paso ejecuta `ssh`, `deploy-vps.sh`, `publicar-release.sh`, `preparar-vps-nuevo.sh` ni `migrar-vps.sh` contra un host que no sea `*.invalid`.
- **El plan termina** con el ensayo local de punta a punta de LOS DOS orquestadores en verde (Task 16), el runbook corto reescrito (Task 15) y `git merge --ff-only` a `main` + push.

## Review Focus

1. **`~/.ssh/config` del operador con `ControlMaster auto`/`ControlPersist`.** El "re-chequeo con una conexión nueva" después de `ufw enable` viajaría por el maestro ya establecido, que sobrevive al firewall, y daría un falso OK que deja al operador afuera cuando el maestro expire. Esperado: el re-chequeo fuerza `-o ControlMaster=no -o ControlPath=none`. Test: `Firewall_ReChequeo_NoReusaElMaestroSsh` (Task 5).
2. **Un `.env` del operador editado en Windows (CRLF) o con valores entre comillas.** `VPS_HOST` terminaría en `\r`, y la identidad, el ssh o la comparación de DNS fallarían con mensajes engañosos. Esperado: exit 3 nombrando la clave, sin ssh. Test: `LeerEnv_ValorConCrLfOComillas_Aborta3NombrandoLaClave` (Task 2) y su uso en `Prevuelo_EnvConCrLf_Aborta3SinSsh` (Task 8).
3. **El operador corta el corte (Ctrl-C, terminal cerrada) después del congelamiento.** Esperado: `congelado_utc` ya está en el estado, no hay `resultado=`, el lock se liberó, y una corrida nueva se niega (3) e imprime `--continuar <corrida>` y `--revertir`. Test: `Corte_InterrumpidoTrasCongelar_EstadoSinResultadoYCorteNuevoSeNiega3` (Task 9).
4. **`VPS_HOST` es un nombre, no una IP.** Comparar la IP de `API_HOSTNAME` con el texto de `VPS_HOST` daría un falso "el DNS no coincide". Esperado: se resuelven los dos y se compara el conjunto de direcciones A. Test: `Prevuelo_VpsHostEsNombre_SeResuelveAntesDeComparar` (Task 4).
5. **El shim de `systemctl` arranca una unit cuyo archivo no existe.** Hoy `start` solo mira `masked`: con la unit movida a `.congelado` y sin `mv` de vuelta, el shim da `active` y systemd real da `Unit stockapp-api.service not found.` (exit 5). Un `--revertir` con el orden roto pasaría en el arnés. Esperado: el shim falla como systemd. Tests: `ShimSystemctl_StartSinArchivoDeUnit_Falla5` (Task 13), `Revertir_OrdenEnElViejo_UnmaskMvReloadStart` y la mutación de la Task 14, Step 3.

## Contratos compartidos

**Códigos de salida de los orquestadores:** la tabla de spec §6.3.2, copiada textual en el encabezado de cada uno; los tests la fijan caso por caso.

**Estado de la corrida** (`<corrida>/estado`, `clave=valor`, sin secretos, escrito con `orq_estado_poner`; `resultado` siempre último): `modo`, `inicio_utc`, `env_viejo`, `env_nuevo`, `machine_id_viejo`, `machine_id_nuevo`, `paso_<n>=ok <utc> <segundos>`, `volcado`, `sha256`, `dir_remoto`, `congelado_utc`, `api_nueva_activa_utc`, `logs_auditoria_post_verificacion`, `fin_rollback`, `resultado=GO|NO-GO|FALLO-<n>|REVERTIDO`.

**Directorios:** local `~/stockapp-backups-vps/corridas/<modo>-<AAAAMMDDTHHMMSSZ>/` (700; `<modo>` ∈ `ensayo|corte|desde-backup|revertir`), con `estado`, `registro.log` (600), `volcado/`, `contenedores-viejo.txt`. Remoto `~/stockapp-deploy/migracion/<modo>-<ts>/` (700). Lock `~/stockapp-backups-vps/.migracion.lock`.

**Tablas de códigos de los bloques en el encabezado del orquestador:** entre `# >>> CODIGOS <ruta-del-bloque>` y `# <<< CODIGOS <ruta-del-bloque>`, las líneas del bloque desde `# Códigos de salida (estables):` hasta la primera línea `#` vacía, sin cambios. `deploy-vps.sh` y `publicar-release.sh` no tienen tabla (salen con 1 ante todo fallo) y no se copian.

**Parámetros solo de arnés** (spec §6.3.6; rechazados con exit 2 si algún host no termina en `.invalid`): `migrar-vps.sh --contenedor-viejo <n> --contenedor-nuevo <n> --raiz-remota-viejo <d> --raiz-remota-nuevo <d> --sin-verificar-preparacion` y la variable `STOCKAPP_ENSAYO_FALLAR_EN=<paso>`.

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `deploy/deploy-vps.sh` (mod.) | pre-vuelo: "la tabla no existe" es un resultado válido |
| `deploy/lib/orquestacion.sh` (nuevo) | funciones comunes de los dos orquestadores (se carga con `source`; no se copia a servidores) |
| `deploy/preparar-vps-nuevo.sh` (nuevo) | VPS en blanco → listo, idempotente, `--verificar`, `--probar-renovacion` |
| `deploy/migrar-vps.sh` (nuevo) | ensayo, corte, `--continuar`, `--revertir`, `--desde-backup`, `--post-corte` |
| `deploy/ensayo/ensayo-local.sh`, `deploy/ensayo/shims/{ssh,scp,systemctl}` (mod./nuevo) | dos hosts simulados, API real en los dos, subcomandos `preparar-migracion`, `migrar`, `limpiar` |
| `deploy/MIGRACION-VPS.md` (reescrito), `deploy/PROCEDIMIENTOS.md`, `deploy/DEPLOY.md` (mod.) | runbook corto y docs |
| `tests/StockApp.Infrastructure.Tests/Kit/HostSimulado/{ufw,certbot,apt-get,systemd-run,systemctl,docker,sudo}` (nuevos) | shims del host simulado con estado (viven en tests: llevan `ufw`/`certbot`, que `deploy/ensayo/` tiene prohibidos) |
| `tests/StockApp.Infrastructure.Tests/Kit/{OrquestacionLibTests,HostSimuladoTests,PrepararVpsNuevoTests,MigrarVpsTests}.cs` (nuevos), `HostSimulado.cs` (helper), `DeployVpsTests.cs`, `EnsayoLocalTests.cs`, `MigracionVpsSincroniaTests.cs` (mod.) | guardianes |

---

### Task 1: El pre-vuelo de `deploy-vps.sh` acepta "la tabla no existe" (spec §6.6, §10.1)

**Files:**
- Modify: `deploy/deploy-vps.sh` (`paso1_prevuelo_migraciones`, ~L266-L340, y el encabezado, paso 1)
- Test: `tests/StockApp.Infrastructure.Tests/Kit/DeployVpsTests.cs`

**Interfaces:**
- Produces: antes de la SQL de duplicados de cada tabla, la consulta `SELECT to_regclass('public."<tabla>"') IS NOT NULL;` por stdin, con el mismo `cmd_remoto` y la misma captura de rc/stderr. `t` → consulta como hoy; `f` → imprime `  "<tabla>": no existe (base sin migrar): nada que verificar` y sigue; rc≠0, stderr no vacío o cualquier otra salida → el mismo aborto con exit 1 de hoy ("no pude consultar"). Mismo criterio que `restaurar-backup.sh:734`.

- [ ] **Step 1: Línea base** — `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m` en primer plano (si no entra en 600 s, en dos filtros: `FullyQualifiedName~Kit` y `FullyQualifiedName!~Kit`). Totales al ledger.

- [ ] **Step 2: Tests en rojo**
  - `SshFalso` gana `simularTablaInexistente` y `simularExistenciaIlegible`: la rama `*"to_regclass"*` responde `t` por defecto, `f` con el primero y `NOTICE: algo` (exit 0) con el segundo. Sin esa rama, todos los caminos felices actuales pasan a abortar: se agrega primero y se corre `DeployVpsTests` completo para confirmar que sigue verde con el script viejo.
  - `Prevuelo_TablaInexistente_InformaYSigue`: `simularTablaInexistente: true`, `--dry-run` → exit 0; stdout contiene `"Categorias": no existe (base sin migrar)`; ninguna línea del rastro de ssh lleva `HAVING`.
  - `Prevuelo_RespuestaDeExistenciaIlegible_Aborta`: → exit ≠0, stderr contiene `no se pudo verificar` y `NO PUDO CONSULTAR`.
  - `PrevueloFallaAlConsultar_AbortaSinConfundirloConSinDuplicados` (existente) sigue igual: el fallo inyectado en la consulta de existencia también aborta (nuevo `[InlineData]` o caso hermano `PrevueloFallaLaConsultaDeExistencia_Aborta`).
  - Con Postgres real (`[Collection("Postgres")]` no hace falta: contenedor propio `postgres:16-alpine` `stockapp-test-<guid12>`, borrado en `finally`): el ssh falso corre el comando remoto localmente y un envoltorio `docker` al frente del `PATH` reemplaza SOLO el argumento `stockapp-pg` por el nombre del contenedor de test (y aborta si ve cualquier otro nombre).
    - `PrevueloReal_BaseVacia_Pasa`: base recién creada → `--dry-run` exit 0, las 7 tablas informan `no existe`.
    - `PrevueloReal_EsquemaRealConDuplicados_Aborta`: dump con el esquema real (`PostgresFixture` + `EjecutorPgDumpProceso`, como `EnsayoLocalTests.EnsayoCompleto_*`) restaurado en el contenedor, `DROP INDEX "IX_Categorias_Nombre";` y dos `Categorias` que difieren en mayúsculas (columnas obligatorias verificadas con CodeGraph sobre la entidad) → exit ≠0 y stdout con `DUPLICADOS en 'Categorias'`.
    - `PrevueloReal_DockerExecFalla_Aborta`: el contenedor detenido → exit ≠0 y stderr con `NO PUDO CONSULTAR`.

- [ ] **Step 3: Ver el rojo** — `--filter "FullyQualifiedName~DeployVpsTests.Prevuelo"`. Esperado: FAIL en `Prevuelo_TablaInexistente_*`, `Prevuelo_RespuestaDeExistenciaIlegible_*` (hoy se consulta como si existiera) y `PrevueloReal_BaseVacia_Pasa`. Pegar.

- [ ] **Step 4: Implementar** en `paso1_prevuelo_migraciones` según Interfaces; documentar en el encabezado (paso 1) que una base sin migrar no tiene nada que verificar.

- [ ] **Step 5: Verde y mutación** — `--filter FullyQualifiedName~DeployVpsTests` PASS. Mutaciones (spec §6.6): (a) quitar la rama `f` (el `continue` con el mensaje) → `PrevueloReal_BaseVacia_Pasa` en rojo; (b) quitar el chequeo de rc/stderr de la consulta de existencia → `PrevueloFallaLaConsultaDeExistencia_Aborta` en rojo; (c) quitar la validación "exactamente `t` o `f`" → `Prevuelo_RespuestaDeExistenciaIlegible_Aborta` en rojo. Pegar y restaurar.

- [ ] **Step 6: Commit** — `git add deploy/deploy-vps.sh tests/StockApp.Infrastructure.Tests/Kit/DeployVpsTests.cs` · `fix(deploy): el pre-vuelo de deploy-vps acepta catálogos inexistentes en una base sin migrar`

### Task 2: `deploy/lib/orquestacion.sh`

**Files:**
- Create: `deploy/lib/orquestacion.sh`
- Test: `tests/StockApp.Infrastructure.Tests/Kit/OrquestacionLibTests.cs` (cada test hace `source` de la lib en un `bash` con `HOME` temporal)

**Interfaces (Produces; las usan las Tasks 4-14):**
- `orq_leer_env <archivo> <PREFIJO> <CLAVE>...` → define `<PREFIJO>_<CLAVE>` por cada clave presente, leyendo línea por línea (`sed -n "s/^CLAVE=//p"`, la última gana), nunca con `source`. Archivo ausente/ilegible, o un valor que empieza con `"`/`'` o contiene `\r` → mensaje con la ruta y SOLO el nombre de la clave, exit 3.
- `orq_hosts_de_ensayo <host>...` → rc 0 sii todos terminan en `.invalid`.
- `orq_ssh <PREFIJO> <comando> [<stdin>]` → `ssh -p "$<P>_VPS_SSH_PORT" -o BatchMode=yes "$<P>_VPS_USER@$<P>_VPS_HOST" "$comando"`; deja `ORQ_RC`, `ORQ_SALIDA`, `ORQ_STDERR` (stderr a un temporal dentro de `ORQ_CORRIDA` si está definida, si no `mktemp` local, nunca remoto); sin stdin explícito usa `</dev/null`. Nunca sale: el llamador clasifica.
- `orq_ssh_nueva_conexion <PREFIJO> <comando>` → igual, más `-o ControlMaster=no -o ControlPath=none -o ConnectTimeout=10`.
- `orq_dir_corrida <modo>` → crea y devuelve por stdout `~/stockapp-backups-vps/corridas/<modo>-<ts>` (700, falla si ya existe); exit 3 si `HOME` resuelve dentro del repo o bajo `/mnt/`. Define `ORQ_CORRIDA`.
- `orq_log_iniciar` → `exec > >(tee -a "$ORQ_CORRIDA/registro.log") 2>&1` con el log en 600.
- `orq_estado_poner <clave> <valor>` (clave `^[a-z0-9_]+$`, valor sin salto de línea; temporal en el mismo directorio + `mv`), `orq_estado_leer <clave>` (stdout; rc 1 si falta), `orq_estado_de <archivo-estado> <clave>` (lo mismo sobre otra corrida).
- `orq_lock` → `flock -n` sobre `~/stockapp-backups-vps/.migracion.lock` en el fd 9; tomado → mensaje y exit 3.
- `orq_paso <n> <total> <nombre>` / `orq_fin_paso <OK|YA ESTABA|OMITIDO|FALLÓ> [detalle]` → la línea de spec §6.3.1 (`[HH:MM:SS] ( n/total) nombre ..... RESULTADO   N s  (detalle)`), durando con `$SECONDS`; si `ORQ_CONGELADO_SEG` está definido, agrega `viejo sin API hace M min S s`.
- `orq_confirmar <palabra> <texto>` → imprime el texto y lee una línea de `/dev/tty`; si no hay terminal: con `ORQ_ENSAYO=1` lee de stdin, si no exit 3; distinta de la palabra exacta (sin recortar ni pasar a mayúsculas) → exit 4.
- `orq_bloque <ruta-relativa> <archivo-env> <args>...` → corre `STOCKAPP_ENV_LOCAL=<archivo-env> "$REPO_ROOT/<ruta>" <args>` como proceso hijo (nunca `source`), con la salida en vivo y sangrada (`sed -u 's/^/    │ /'`) y `PIPESTATUS` para devolver el código del bloque en `ORQ_RC_BLOQUE`.

- [ ] **Step 1: Tests en rojo** (`OrquestacionLibTests`):
  - `LeerEnv_LeeSinEjecutar`: un `.env` con `VPS_HOST=a.invalid` y `X=$(touch <marca>)` → `V_VPS_HOST=a.invalid` y la marca NO existe.
  - `LeerEnv_ValorConCrLfOComillas_Aborta3NombrandoLaClave` (`[Theory]`: `VPS_HOST=a.invalid\r`, `VPS_HOST="a.invalid"`) → exit 3, stderr contiene `VPS_HOST` y no contiene `a.invalid`.
  - `LeerEnv_ArchivoAusente_Aborta3ConLaRuta`.
  - `EstadoPoner_EsAtomicoYResultadoQuedaUltimo`: poner `paso_3`, `resultado`; el archivo termina en `resultado=…`; en el directorio no quedan temporales; un valor con `\n` → rc ≠0 y el archivo intacto.
  - `Lock_Tomado_Aborta3`: un `flock` de otro proceso hijo sobre el mismo archivo (el test lo toma con `flock <lock> -c 'cat'` alimentado por un pipe que el test cierra al final: sin `sleep`) → exit 3.
  - `DirCorrida_HomeDentroDelRepoOBajoMnt_Rechaza3` (`[Theory]`) y `DirCorrida_Crea700BajoHome`.
  - `Confirmar_PalabraExacta_Ok`, `Confirmar_MinusculasOEspacios_Rechaza4` (`congelar`, `CONGELAR `), `Confirmar_SinTerminalNiEnsayo_Rechaza3` (corrido con `setsid` para no tener `/dev/tty`).
  - `Ssh_255_EsNoPudeConsultar`: ssh falso que sale 255 → `ORQ_RC=255`, `ORQ_SALIDA` vacía, la función no sale del shell.
  - `SshNuevaConexion_DesactivaElMaestro`: el rastro del ssh falso contiene `ControlMaster=no` y `ControlPath=none`.
  - `Bloque_NoHeredaStockappEnvLocalDeLaSesion`: el bloque falso imprime su `STOCKAPP_ENV_LOCAL`; con la sesión sin exportarla, la ve igual a `<archivo-env>`, y después de `orq_bloque` la sesión sigue sin la variable; `ORQ_RC_BLOQUE` es el exit del bloque (7), no el del `sed`.
  - `Paso_LineaConNumeroResultadoYDuracion` y `Paso_ConCongelado_AgregaElReloj`.

- [ ] **Step 2: Ver el rojo** — `--filter FullyQualifiedName~OrquestacionLibTests` (la lib no existe). Pegar.
- [ ] **Step 3: Implementar** según Interfaces. Agregar `deploy/lib/orquestacion.sh` a `MigracionVpsSincroniaTests.ScriptsNuevos`.
- [ ] **Step 4: Verde y mutación** — PASS (también `MigracionVpsSincroniaTests`). Mutaciones: quitar el rechazo de `\r` → `LeerEnv_ValorConCrLf…` rojo; quitar la rama "tomado → exit 3" → `Lock_Tomado_Aborta3` rojo; quitar `ControlPath=none` → `SshNuevaConexion_…` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): librería común de los orquestadores de la migración`

### Task 3: Host simulado con estado para los tests de `preparar` (spec §8.1)

**Files:**
- Create: `tests/StockApp.Infrastructure.Tests/Kit/HostSimulado/{apt-get,ufw,certbot,systemd-run,systemctl,docker,sudo}` (bash; el helper los ubica por `CallerFilePath`, como `EnsayoLocalTests.RaizRepo`, sin tocar el `.csproj`)
- Create: `tests/StockApp.Infrastructure.Tests/Kit/HostSimulado.cs`, `tests/StockApp.Infrastructure.Tests/Kit/HostSimuladoTests.cs`

**Interfaces:**
- Produces: `sealed class HostSimulado : IDisposable` con `static HostSimulado Crear(string host = "vps-nuevo.test.invalid", int puertoSsh = 22)` (contenedor `ubuntu:24.04` `stockapp-test-host-<guid12>`, `docker run -d … sleep infinity`, shims copiados a `/usr/local/sbin/` adelante de `/usr/sbin` en el `PATH` de las sesiones; borra el contenedor por nombre en `Dispose`), `string DirOperador` (temporal con `ssh`, `scp`, `curl`, `getent` falsos y el `rastro`), `string Rastro()`, `string Leer(string rutaEnElHost)`, `void Escribir(string ruta, string contenido)`, `(int, string) Ejecutar(string comando)`, `void PlantarContenedorAjeno(string nombre)`, `void OcuparPuerto80()`, `string EstadoUfw()`.
  - ssh falso del operador: registra `ARGS:` con `printf ' %q'`; aplica la regla de red del host (si `ufw` está activo y no hay regla para `puertoSsh/tcp`, una conexión NUEVA sale con 255; con `ControlMaster` sin desactivar y una conexión previa en la misma corrida, el falso simula el maestro y deja pasar, para que el test del Review Focus 1 sea honesto); si no, `docker exec -i <ctr> bash -c "$*"` como `stockapp-deploy-user` (usuario con `sudo` shim).
  - `ufw` (fiel a ufw 0.36 de 24.04, citado en el encabezado): `allow N/tcp` idempotente ("Skipping adding existing rule"), `--force enable`, `disable`, `status` (`Status: active|inactive` + reglas), `reset` sale con 1 y queda en el rastro (no debe aparecer nunca).
  - `certbot certonly --standalone -n --agree-tos -d D --deploy-hook H`: si el puerto 80 está ocupado, falla como el real (`Could not bind TCP port 80`); si no, escribe `/etc/letsencrypt/live/D/{fullchain,privkey}.pem` con un certificado autofirmado generado en el operador (`openssl req -x509 -days 90`) y copiado al crear el host, `renewal/D.conf` con `renew_hook = H`, y corre `RENEWED_LINEAGE=… H`; sale con el código del hook (fiel: certbot real informa el fallo del hook en el exit). `renew --force-renewal --cert-name D` vuelve a correr el hook.
  - `systemctl` (host nuevo): units `stockapp-api` y `stockapp-ufw-reversa.timer`; `start` de una unit sin archivo → `Unit … not found.` exit 5; `stop` de un timer inexistente → exit 5; el timer armado con `systemd-run --unit=NOMBRE --on-active=N CMD` (crea `/run/systemd/transient/NOMBRE.timer`); `list-timers`/`is-active` lo reflejan.
  - `docker` (dentro del host): `compose version`, `compose … up -d` crea el "contenedor" `stockapp-pg` (estado en archivos, `healthy`), `ps -a --format '{{.Names}}'`, `inspect -f '{{.State.Health.Status}}'`, y `exec -i stockapp-pg psql …` que responde a las consultas de base vacía/tablas desde archivos que el test planta.
  - `apt-get install -y P…` registra y crea marcadores; `useradd`, `install`, `stat`, `id`, `chown` son los REALES del contenedor (corre como root).

- [ ] **Step 1: Tests en rojo** (`HostSimuladoTests`, todos con `HostSimulado.Crear()` y `Dispose`):
  - `Ufw_EnableSinReglaSsh_RechazaConexionesNuevas` / `Ufw_EnableConReglaSsh_Acepta`.
  - `Ufw_AllowRepetido_NoDuplicaLaRegla`.
  - `Ssh_ConMaestroActivo_SobreviveAlFirewall` (documenta el peligro del Review Focus 1) y `Ssh_SinMaestro_NoSobrevive`.
  - `Certbot_PrimeraEmisionConHookQueFallaEnRestart_DejaLosPemYSaleDistintoDeCero`.
  - `Certbot_Puerto80Ocupado_Falla`.
  - `Systemctl_StartSinArchivoDeUnit_Falla5`, `SystemdRun_ArmaElTimer_YStopLoDesarma`.
  - `Host_SeBorraPorNombreExacto`: después de `Dispose`, `docker ps -a --filter name=^/<nombre>$ -q` vacío.
- [ ] **Step 2: Ver el rojo** (`--filter FullyQualifiedName~HostSimuladoTests`). Pegar.
- [ ] **Step 3: Implementar** el helper y los shims; cada shim cita la herramienta real y su versión en el encabezado (ubuntu 24.04: `ufw 0.36.2`, `certbot 2.9`, `systemd 255`), y lo que NO simula.
- [ ] **Step 4: Verde y mutación** — PASS. Mutación: quitar la regla de red del ssh falso → `Ufw_EnableSinReglaSsh_…` rojo. Pegar y restaurar. `docker ps -a` al final sin `stockapp-test-host-*`.
- [ ] **Step 5: Commit** — `test(deploy): host simulado con estado para los tests de preparar-vps-nuevo`

### Task 4: `preparar-vps-nuevo.sh`: uso, pre-vuelo, identidad, Docker y usuario (spec §6.4 pasos 0-2)

**Files:**
- Create: `deploy/preparar-vps-nuevo.sh`
- Test: `tests/StockApp.Infrastructure.Tests/Kit/PrepararVpsNuevoTests.cs`; Modify: `MigracionVpsSincroniaTests.cs` (exención)

**Interfaces:**
- Consumes: `orq_*` (Task 2), `HostSimulado` (Task 3).
- Produces: `deploy/preparar-vps-nuevo.sh --version <v> [--env-viejo <archivo>] [--verificar] [--probar-renovacion]`, con `STOCKAPP_ENV_LOCAL` obligatoria. Encabezado con la tabla de códigos de spec §6.3.2 (columna `preparar`). Cada paso es un par de funciones `paso<N>_hecho` (lectura; rc 0 hecho, 1 pendiente, 8 no pude consultar) y `paso<N>_aplicar`; el motor común `correr_paso <n> <nombre>` imprime `YA ESTABA`/`OK`/`FALLÓ`, y con `--verificar` imprime `HECHO`/`PENDIENTE` sin aplicar (exit 0 solo si todos HECHO, 5 si hay PENDIENTE). Las Tasks 5-7 agregan pasos al mismo motor.
- Test helper: `Correr(HostSimulado h, string[] args, IDictionary<string,string>? env = null)` arma un árbol temporal con la forma del repo (`deploy/preparar-vps-nuevo.sh`, `deploy/lib/`, `deploy/tls/…`, `deploy/docker-compose.postgres.yml`, `releases/win/` con un feed mínimo) y **bloques falsos** `deploy/deploy-vps.sh` y `build/publicar-release.sh` que registran `$*` y `STOCKAPP_ENV_LOCAL` en el rastro y salen con `STUB_DEPLOY_EXIT`/`STUB_PUBLICAR_EXIT` (el de deploy, al salir con 0, crea la unit en el host simulado como lo haría `install.sh`).

- [ ] **Step 1: Tests en rojo:**
  - `SinStockappEnvLocal_O_IgualADeployEnv_Aborta3SinSsh` (`[Theory]`).
  - `VpsHostIgualAlDeDeployEnv_Aborta9SinSsh` (el `deploy/.env` del árbol define el mismo `VPS_HOST`).
  - `Prevuelo_FaltaApiHostnameOSchemeNoHttps_Aborta3`.
  - `Prevuelo_SinSudoN_Aborta3`, `Prevuelo_NoEsUbuntu2404_Aborta3` (el test planta `/etc/os-release`).
  - `Prevuelo_PuertoSshNoEscucha_Aborta3` (`ss -ltn` del host sin `:VPS_SSH_PORT`).
  - `Prevuelo_DnsNoCoincide_Aborta3AntesDeCertbot` y `Prevuelo_VpsHostEsNombre_SeResuelveAntesDeComparar` (Review Focus 4: `getent` falso resuelve los dos nombres a la misma IP → sigue).
  - `Prevuelo_FaltaReleasesWinDeLaVersion_Aborta3`, `Prevuelo_ClavePublicaConCambiarOAusente_Aborta3`.
  - `Prevuelo_ContenedorAjeno_Aborta9SinAcciones`, `Prevuelo_Puerto80Ocupado_Aborta9SinAcciones` (en el rastro no hay `apt-get`, `useradd`, `ufw`).
  - `Paso1Y2_HostEnBlanco_InstalanYCrean`: `apt-get install -y docker.io docker-compose-v2`, `usermod -aG docker`; `id stockapp` OK, home `/var/lib/stockapp`, shell `/usr/sbin/nologin`; `.local`, `.local/share`, `.local/share/StockApp` `stockapp:stockapp 750`; `~/stockapp-deploy` y `~/stockapp-deploy/migracion` 700.
  - `Paso1_VerificaConUnaConexionNueva` (después del `usermod`, el `docker ps` sin sudo va en otra invocación de ssh: dos líneas distintas en el rastro).
  - `Verificar_HostEnBlanco_TodoPendienteSinAcciones` → exit 5, el rastro sin escrituras.
- [ ] **Step 2: Ver el rojo.** Pegar.
- [ ] **Step 3: Implementar** pasos 0-2 y el motor. En `MigracionVpsSincroniaTests`: test nuevo `Preparar_ExentoSoloDeUfwCertbotYAptGet` que corre todos los asserts de `ScriptsNuevos_SinComandosGlobalesProhibidos` sobre `deploy/preparar-vps-nuevo.sh` salvo `ufw` y `certbot`, y además `Assert.DoesNotContain("ufw reset", …)`, `DoesNotContain("apt-get upgrade")`, `DoesNotContain("nginx")`; y `ScriptsNuevos_SinHostsNiIpsCableados` lo incluye.
- [ ] **Step 4: Verde y mutación** — quitar el chequeo de contenedores ajenos → `Prevuelo_ContenedorAjeno_Aborta9…` rojo (fila de spec §8.1). Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): preparar-vps-nuevo con pre-vuelo, identidad, Docker y usuario`

### Task 5: `preparar`: firewall con reversa (spec §6.4 paso 3, A8)

**Files:** Modify `deploy/preparar-vps-nuevo.sh`; Test `PrepararVpsNuevoTests.cs`.

**Interfaces:** Produces `paso3_hecho`/`paso3_aplicar` en el orden exacto de spec §6.4 "Firewall": tres `ufw allow` → (si ya activo, re-verificar y terminar) → `systemd-run --unit=stockapp-ufw-reversa --on-active=120 /usr/sbin/ufw disable` → `ufw --force enable` → `orq_ssh_nueva_conexion NUEVO 'sudo -n true'` → `systemctl stop stockapp-ufw-reversa.timer` y verificación de que el timer ya no existe; sin respuesta → exit 6 con el mensaje "la reversa deshace el firewall en 120 s; esperá y re-corré".

- [ ] **Step 1: Tests en rojo:**
  - `Firewall_Orden`: en el rastro, `ufw allow <ssh>/tcp` < `systemd-run --unit=stockapp-ufw-reversa` < `ufw --force enable` < la conexión nueva < `systemctl stop stockapp-ufw-reversa.timer`.
  - `Firewall_ReChequeoFallido_Sale6ConLaReversaArmada` (el test hace que el ssh falso rechace la conexión nueva) → exit 6 y `/run/systemd/transient/stockapp-ufw-reversa.timer` existe.
  - `Firewall_ReChequeo_NoReusaElMaestroSsh` (Review Focus 1).
  - `Firewall_YaActivo_NoReArmaNiReHabilita` → `YA ESTABA`, sin `systemd-run` ni `enable` en el rastro.
  - `Firewall_NuncaReset`: el rastro completo de una corrida feliz no tiene `ufw reset` ni `ufw delete`.
- [ ] **Step 2: Ver el rojo.** Pegar.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Verde y mutación** (filas de spec §8.1): quitar `ufw allow <ssh>` → el host rechaza la conexión nueva y `Firewall_Orden` o el camino feliz da 6; quitar `systemd-run` → `Firewall_ReChequeoFallido_…` rojo (el timer no está armado). Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): preparar habilita el firewall con reversa automática armada`

### Task 6: `preparar`: TLS, hook y `--probar-renovacion` (spec §6.4 pasos 4 y 9, §10.2)

**Files:** Modify `deploy/preparar-vps-nuevo.sh`; Test `PrepararVpsNuevoTests.cs`.

**Interfaces:** Produces `paso4_*` (¿ya está?: `live/<dominio>/` existe, `openssl x509 -checkend 2592000` OK, `renew_hook` del `.conf` igual a `/usr/local/sbin/stockapp-tls-deploy-hook.sh`, PEM de `/var/lib/stockapp/tls/` `stockapp:stockapp 0600`); aplicar: `apt-get install -y certbot`, el hook por scp a `~/stockapp-deploy/` y `sudo install -m 0755 -o root -g root … /usr/local/sbin/`, `certbot certonly --standalone -n --agree-tos -d <dominio> --deploy-hook …` **sin mirar su exit** y re-verificar el resultado; si el certificado existe y los PEM no, `sudo env RENEWED_LINEAGE=/etc/letsencrypt/live/<dominio> /usr/local/sbin/stockapp-tls-deploy-hook.sh`. `paso9_*` corre SOLO con `--probar-renovacion`: marcador `~/stockapp-deploy/.preparacion/renovacion-probada` (con la fecha); sin el flag el paso se informa `OMITIDO (sin --probar-renovacion)` y nunca aparece `--force-renewal` en el rastro.

- [ ] **Step 1: Tests en rojo:** `Tls_CertbotSaleDistintoDeCeroPeroCopio_Ok`; `Tls_EmitioPeroNoCopio_CorreElHookAMano`; `Tls_Vigente_NoReEmite` (segunda corrida sin `certonly` en el rastro); `Tls_UsuarioCreadoAntesDelCertificado` (`useradd` < `certonly`); `Renovacion_SinFlag_NuncaForceRenewal`; `Renovacion_ConFlag_UnaSolaVez` (dos corridas con el flag: un solo `--force-renewal`, la segunda `YA ESTABA`); `Renovacion_VerificaArranquePosterior` (el shim registra el `restart` del hook; sin él → FALLÓ).
- [ ] **Step 2-4:** rojo, implementar, verde. Mutación: quitar la rama "correr el hook a mano" → `Tls_EmitioPeroNoCopio_…` rojo; quitar el chequeo del marcador → `Renovacion_ConFlag_UnaSolaVez` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): preparar emite el certificado y prueba la renovación solo con --probar-renovacion`

### Task 7: `preparar`: `.env` del servidor, Postgres, deploy, feed y verificación final (spec §6.4 pasos 5-8, 10)

**Files:** Modify `deploy/preparar-vps-nuevo.sh`; Test `PrepararVpsNuevoTests.cs`, `MigracionVpsSincroniaTests.cs`.

**Interfaces:**
- `paso5_*`: el script remoto viaja por stdin de `ssh … 'bash -s'` con `umask 077`, `install -m 600 /dev/null ~/stockapp-deploy/.env` y append; genera `POSTGRES_PASSWORD` (`openssl rand -base64 24`), `JWT_SECRET` (`openssl rand -base64 48`) y `BOOTSTRAP_PASSWORD` ahí; la clave pública (de `--env-viejo`, con `orq_leer_env`) va por el mismo stdin. Valores fijos de spec §6.4 "`.env` del servidor". Si el archivo existe: no se escribe nada; si falta una clave, una empieza con `CAMBIAR`, el modo no es 600 o un valor no secreto difiere del `.env` del operador → FALLÓ (5) nombrando solo las claves.
- `paso6_*`: scp del compose, `docker compose --env-file .env -f docker-compose.postgres.yml up -d`, espera acotada (120 s, monotónica) a `healthy`; "base vacía" con la misma consulta que `restaurar-backup.sh:404-458` (relaciones fuera de los schemas del sistema), solo si la unit no existe; unit inexistente y base no vacía → 5 sin borrar; con la unit, informa tablas, filas de `LogsAuditoria` y `Usuarios`.
- `paso7_*`: solo si la unit no existe: `orq_bloque deploy/deploy-vps.sh "$STOCKAPP_ENV_LOCAL" <v> --sin-backup`; exit 0 = OK.
- `paso8_*`: `GET /updates/releases.win.json` sirve una versión ≥ `<v>` → hecho; si no, `orq_bloque build/publicar-release.sh "$STOCKAPP_ENV_LOCAL" <v>`.
- `paso10`: desde el operador, sin `-k`, lo de spec §6.4 paso 10; resumen final con lo pendiente para Martin.

- [ ] **Step 1: Tests en rojo** (casos mínimos de spec §8.1 que faltan):
  - `DosCorridas_SegundaTodoYaEstaba` → exit 0, sin acciones en el rastro (ningún `apt-get`, `ufw allow`, `certonly`, `useradd`, `install`, `compose up`, bloque).
  - `EnvExistente_NoSeReescribe` (byte a byte igual; sin `install -m 600` en el rastro).
  - `EnvExistenteConValorFijoDistinto_FallaNombrandoSoloLaClave` (`API_PORT` distinto → exit 5, stderr con `API_PORT` y sin el valor).
  - `BaseNoVaciaSinUnit_Aborta5SinBorrar`.
  - `ConUnitInstalada_NuncaDeployVps` (rastro sin `deploy-vps.sh` en la segunda corrida; pasos 7 `YA ESTABA`).
  - `PrimerDeploy_ConSinBackupYEnvExplicito` (el bloque falso vio `<v> --sin-backup` y `STOCKAPP_ENV_LOCAL=<env-nuevo>`).
  - `Secretos_NuncaEnRastroArgvNiLog`: lee del host los tres secretos generados y la clave pública, y afirma que no aparecen en el rastro del ssh/scp, en el argv de ningún proceso registrado, en stdout/stderr ni en `registro.log`.
  - `PuntaAPunta_HostEnBlanco_Exit0YVerificarTodoHecho` (host en blanco → exit 0; `--verificar` → exit 0 y 10 líneas `HECHO`, salvo el 9 `OMITIDO`).
  - Sincronía: `Compose_PostgresSoloEnLoopback` (`deploy/docker-compose.postgres.yml` publica solo `127.0.0.1:`).
- [ ] **Step 2-4:** rojo, implementar, verde. Mutaciones (spec §8.1): quitar "no reescribir `.env`" → `EnvExistente_NoSeReescribe` rojo; quitar "no desplegar con la unit instalada" → `ConUnitInstalada_NuncaDeployVps` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): preparar genera el .env en el servidor, levanta Postgres, despliega y publica el feed`

### Task 8: `migrar-vps.sh`: modos, corrida, identidad, pre-vuelo y confirmación (spec §6.5.1-§6.5.3, §6.3)

**Files:**
- Create: `deploy/migrar-vps.sh`
- Test: `tests/StockApp.Infrastructure.Tests/Kit/MigrarVpsTests.cs`; Modify `MigracionVpsSincroniaTests.cs`

**Interfaces:**
- Produces: el uso de spec §6.5.1 más los parámetros de arnés (Contratos). Defaults `--env-viejo deploy/.env`, `--env-nuevo deploy/.env.vps-nuevo`. Lee de los dos `.env` con `orq_leer_env` (prefijos `VIEJO_`, `NUEVO_`): `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`, `API_SCHEME`, `API_HOSTNAME`, `API_PORT`, `POSTGRES_USER`, `POSTGRES_DB` y, del viejo, `VPS_ADMIN_USER` (obligatorio, sin caer a `BOOTSTRAP_ADMIN_USER`: ruling F11) y `VPS_ADMIN_PASSWORD` (entorno > archivo > `read -rs` de `/dev/tty`; en arnés, de stdin antes de la palabra).
- `prevuelo` (solo lectura): spec §6.5.3 puntos 1 a 6. `machine-id` con `orq_ssh <P> 'cat <raiz>/etc/machine-id'` (iguales → 9; ilegible → 8). Disco: `docker exec <ctr> df -P /var/lib/postgresql/data` contra `pg_database_size` (nunca `df` del directorio de volúmenes de Docker, que pide root). Preparación: `orq_bloque deploy/preparar-vps-nuevo.sh <env-nuevo> --version <v> --verificar` con `<v>` leído del feed del nuevo; ≠0 → 3 (con `--sin-verificar-preparacion`, solo en arnés: `OMITIDO (arnés)`). Credencial: login por `ssh` al loopback del viejo con la contraseña por stdin (`PW="$PW" jq -n --arg u "$U" '{nombreUsuario:$u,contrasena:env.PW}' | orq_ssh VIEJO "curl -fsS … --data @-"`); 401 → 3; `GET /backups` 403 → 3.
- La confirmación (`orq_confirmar CONGELAR …`) muestra hosts + machine-id abreviado, `<v>`, la indisponibilidad del último ensayo y los cuatro comandos de R-A; los mismos quedan en `registro.log` antes de tocar nada.
- Tabla de códigos: `# >>> CODIGOS deploy/volcado-final-vps.sh` y `# >>> CODIGOS deploy/restaurar-backup.sh` en el encabezado (Contratos).
- Test helper: árbol temporal con `deploy/migrar-vps.sh`, `deploy/lib/` y **bloques falsos** `deploy/volcado-final-vps.sh` (crea en `--salida` el dump, el manifiesto con `fecha_utc` actual, el tar y `overrides-api-env.txt`, según `STUB_VOLCADO_*`), `deploy/restaurar-backup.sh`, `deploy/preparar-vps-nuevo.sh`; ssh/scp/curl falsos por host (`vps-viejo.test.invalid`, `vps-nuevo.test.invalid`) con estado en archivos (unit activa/enmascarada, `LogsAuditoria`, login 200/401/403). El rastro marca cada línea como `LECTURA` o `ESCRITURA` según una lista blanca de comandos de lectura del test.

- [ ] **Step 1: Tests en rojo:**
  - `MachineIdIguales_Aborta9SinEscrituras`.
  - `Prevuelo_EnvConCrLf_Aborta3SinSsh` (Review Focus 2).
  - `Prevuelo_CredencialRechazada_Aborta3AntesDeConfirmar` y `Prevuelo_SinPermisoBackups_Aborta3`.
  - `Prevuelo_UnitNoEsLaDeInstall_Aborta3` (`FragmentPath` distinto), `Prevuelo_CongeladoExistente_Aborta3`, `Prevuelo_PreparacionIncompleta_Aborta3`, `Prevuelo_OverrideFaltanteEnElNuevo_Aborta3` (con un `overrides-api-env.txt` de un ensayo anterior).
  - `Confirmacion_EsLoUnicoEntrePrevueloYLaPrimeraEscritura` y `Confirmacion_PalabraDistinta_Sale4SinEscrituras`.
  - `SinTerminalConHostsReales_Sale3` (hosts sin `.invalid`: el ssh falso ni se llama).
  - `ParametrosDeArnesConHostReal_Rechaza2` (`[Theory]` con cada parámetro de Contratos y `STOCKAPP_ENSAYO_FALLAR_EN`).
  - `CorridaSinTerminar_CorteNuevoSeNiega3ConLasDosSalidas` y `Lock_Tomado_Sale3`.
  - `PasswordNuncaEnArgvNiLog`.
  - Sincronía (en `MigracionVpsSincroniaTests`): `deploy/migrar-vps.sh` en `ScriptsNuevos`; `Orquestadores_TablasDeCodigosIgualesALasDeLosBloques`.
- [ ] **Step 2-4:** rojo, implementar, verde. Mutaciones: quitar la confirmación → `Confirmacion_PalabraDistinta_…` rojo; quitar la comparación de machine-id → `MachineIdIguales_…` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): migrar-vps con pre-vuelo de solo lectura, identidad por machine-id y confirmación`

### Task 9: `migrar`: la secuencia del corte, A11 y la verificación (spec §6.5.4, §6.5.5)

**Files:** Modify `deploy/migrar-vps.sh`; Test `MigrarVpsTests.cs`.

**Interfaces:** Produces una función por paso `corte_paso<N>` (3 a 13) y `verificar_v<N>` (1-8, 10), todas registrando `paso_<n>=ok …` al terminar; `congelado_utc` se escribe ANTES de `orq_bloque deploy/volcado-final-vps.sh "$ENV_VIEJO" --salida "$ORQ_CORRIDA/volcado"`, y desde ahí `ORQ_CONGELADO_SEG`. Clasificación de fallos ① y ② exactamente como spec §6.5.4. Paso 4: borra en el nuevo con `find <dir> -maxdepth 1 -type f \( -name 'backup_*.dump' -o -name '*.tmp' \) -print -delete` (nunca `rm -rf` sobre variables) y aparta lo suelto en `migracion/legado-<ts>/`. Paso 8: `mkdir -m 700` del remoto nuevo (falla si existe), scp, `chmod`, `sha256sum` remoto. Paso 11: arranque con espera acotada (120 s) a `active` + `GET /` por loopback con `--resolve`. Resumen con tiempos, tabla de V, pendientes (V9, GO de Martin, PCs), `fin_rollback = congelado + 14 días` y el comando de `--revertir`.

- [ ] **Step 1: Tests en rojo:**
  - `Corte_Orden_VolcadoAntesQueRestaurarAntesQueStart` y `Corte_DetieneLaApiNuevaPrimero` (stop del nuevo antes de cualquier escritura en el viejo).
  - `Corte_RestaurarCon6o7_NoArrancaYSale6` (`[Theory]` 6, 7), `Corte_RestaurarCon5_Sale6YContinuarLoReintenta` (solo el estado: `paso_9` sin `ok`).
  - A11: `Corte_DosVolcadosEnLaCarpeta_Sale6SinRestaurar`, `Corte_FechaUtcAnteriorAlInicio_Sale6SinRestaurar`, `Corte_DumpDelManifiestoDistinto_Sale6SinRestaurar`, `Corte_ShaLocalDistinto_Sale6`.
  - `Corte_VolcadoFalla3AntesDelStop_Sale5ViejoActivo` y `Corte_VolcadoFallaDespuesDelStop_Sale6` y `Corte_VolcadoSale9_Sale9SinOfrecerContinuar`.
  - `Corte_FallaUnaV_Sale7NoGoConApiArriba` y `Corte_TodoOk_Sale0GoConFinRollback`.
  - `Corte_TodoComandoAlViejoEnListaBlanca` (spec §5: `systemctl is-active|is-enabled|show stockapp-api`, `docker ps --format`, `docker exec stockapp-pg`, `cat /etc/machine-id`, el curl de login/backups por loopback; lo del bloque falso no cuenta) y `Corte_NingunTmpFueraDeDockerExec`.
  - `Corte_NuncaDeployVps` (el rastro de `migrar` no tiene `deploy-vps.sh`).
  - `Corte_InterrumpidoTrasCongelar_EstadoSinResultadoYCorteNuevoSeNiega3` (Review Focus 3: el bloque falso de volcado se queda leyendo un FIFO del test; el test manda `SIGINT` al PID de `migrar` y después cierra el FIFO; sin `sleep`).
  - `Corte_SinSecretosEnLaSalida`.
- [ ] **Step 2-4:** rojo, implementar, verde. Mutaciones (spec §8.1): quitar el control de volcado único / de `fecha_utc` / de `dump=` (una a la vez) → su caso A11 rojo; quitar "no arrancar si restaurar ≠ 0" → `Corte_RestaurarCon6o7_…` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): migrar ejecuta el corte con identificación del volcado y verificación automática`

### Task 10: `migrar`: `--continuar` y `--ensayo` (spec §6.5.4 "`--continuar`", §6.5.5)

**Files:** Modify `deploy/migrar-vps.sh`; Test `MigrarVpsTests.cs`.

**Interfaces:** `--continuar <corrida>` relee el estado, exige los mismos machine-id, toma el lock, retoma en el primer paso sin `ok` entre 6 y 12 según ① y ② (paso 6 → volcado en `volcado-<n>/` nueva; paso 9 tras 3/4 → desde el 8 con directorio remoto nuevo; tras 6/7 → solo con `--reintentar-restauracion`); anterior al 6 → se niega (3) y sugiere una corrida nueva; todo `ok` → repite paso 12 y 13. `--ensayo`: misma tabla sin el paso 2, `--ensayo` al bloque de volcado, V10 invertido (`active`, no `masked`, mismo `docker ps`), resumen con la tabla de tiempos para pegar y los nombres de overrides faltantes. `STOCKAPP_ENSAYO_FALLAR_EN=<n>` (solo arnés): antes de correr el paso `n`, la primera vez en la corrida, lo marca FALLÓ con la clase que le corresponde (7 → 6; 9 → como restaurar 5; 11 → 7).

- [ ] **Step 1: Tests en rojo:** `Continuar_RetomaEnElPasoQueFallo` (`[Theory]` 6, 7, 8, 9, 10, 11, 12, con el fallo inyectado por bloque falso o ssh falso); `Continuar_CorridaQueNoCongelo_SeNiega3`; `Continuar_MachineIdDistinto_Sale9`; `Continuar_Restaurar6SinReintentarRestauracion_SeNiega3`; `Continuar_TodoOk_SoloVerifica`; `Ensayo_SinConfirmacion_VolcadoConEnsayo_V10ExigeActive`; `Ensayo_ViejoQuedaMasked_EsNoGo` (el bloque falso enmascara: V10 falla → 7).
- [ ] **Step 2-4:** rojo, implementar, verde. Mutación: quitar el `--ensayo` pasado al volcado → `Ensayo_SinConfirmacion_…` rojo (el viejo queda `masked`). Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): migrar retoma corridas interrumpidas y ensaya con la misma secuencia`

### Task 11: `migrar --revertir` (spec §6.5.6)

**Files:** Modify `deploy/migrar-vps.sh`; Test `MigrarVpsTests.cs`.

**Interfaces:** `--revertir [<corrida>]` (sin corrida: la última `corte-*` con `congelado_utc`), `--aun-con-escrituras`. Orden de spec §6.5.6: identidad + `sudo -n`; guardia de `LogsAuditoria` contra `logs_auditoria_post_verificacion` (creció → 3 salvo `--aun-con-escrituras` con el delta y la palabra); `REVERTIR`; stop del nuevo + `inactive`; en el viejo, cada uno con su "ya está": `unmask` (si `masked`), `mv …congelado …service` (si el `.congelado` existe y el original no), `daemon-reload`, `start`; los dos archivos presentes → 8; verificación y `resultado=REVERTIDO` (en el estado de la corrida revertida y en una corrida `revertir-<ts>` propia con su log).

- [ ] **Step 1: Tests en rojo:** `Revertir_StopDelNuevoAntesDeTodoEnElViejo`; `Revertir_ReCorrido_TodoYaEstaba`; `Revertir_LogsAuditoriaMayor_Sale3`; `Revertir_AunConEscrituras_MuestraDeltaYPideLaPalabra`; `Revertir_CorridaQueMurioEnElVolcado_Funciona` (unit detenida pero no enmascarada); `Revertir_LosDosArchivosDeUnit_Sale8SinPisar`; `Revertir_PalabraDistinta_Sale4`.
- [ ] **Step 2-4:** rojo, implementar, verde. Mutaciones (spec §8.1): quitar el `stop` del nuevo → `Revertir_StopDelNuevo…` rojo; quitar la guardia de escrituras → `Revertir_LogsAuditoriaMayor_Sale3` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): migrar --revertir deshace el corte en un comando`

### Task 12: `migrar --desde-backup` y `--post-corte` (spec §6.5.7, §6.5.8, §10.3)

**Files:** Modify `deploy/migrar-vps.sh`; Test `MigrarVpsTests.cs`.

**Interfaces:** `--desde-backup <dump> [--manifiesto <m>]`: pre-vuelo solo del nuevo; dump legible, ≥ 1024 bytes, `pg_restore --list` local (fecha de `Archive created at`), `.sha256` hermano verificado si existe; `RESTAURAR` con "todo lo posterior a <fecha> se pierde"; pasos 3, 4, 8, 9 (con o sin manifiesto), 11 y 12 con V1 reducido, V2-V5, V7, V8 (historial ausente se informa) y V6 solo con manifiesto; sin V10. `--post-corte <corrida>`: exige `resultado=GO` y 14 días desde `congelado_utc` (o `--antes-de-plazo` + `BORRAR`), `LogsAuditoria` ≥ manifiesto (si no puede consultar, se detiene), archiva dump y manifiesto en `~/stockapp-backups-vps/corte-<fecha>/` (700) con sha antes y después y sin temporales fuera de la corrida; después lista candidatos (ruta y tamaño) y pide `BORRAR` por cada uno, borrando por ruta exacta; imprime los comandos de referencia para desmantelar el viejo SIN ejecutarlos y sin tokens prohibidos.

- [ ] **Step 1: Tests en rojo:** `DesdeBackup_DumpChicoOIlegible_Sale3`; `DesdeBackup_ShaHermanoDistinto_Sale3`; `DesdeBackup_SinManifiesto_SinV6NiV10_Go`; `DesdeBackup_NingunComandoAlViejo`; `PostCorte_SinGoOAntesDelPlazo_SeNiega`; `PostCorte_ArchivaConShaYNuncaBorraElCorte`; `PostCorte_BorraSoloLoConfirmado` (palabras por stdin: `BORRAR`, `no`, `BORRAR`); `PostCorte_NuncaEjecutaComandosEnElViejo`; `PostCorte_NoPudoConsultarLogsAuditoria_SeDetiene`.
- [ ] **Step 2-4:** rojo, implementar, verde. Mutación: quitar la exclusión del archivo del corte de la lista de candidatos → `PostCorte_ArchivaConShaYNuncaBorraElCorte` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `feat(deploy): migrar recupera desde un backup y archiva el corte en D+14`

### Task 13: Arnés local con dos hosts (spec §8.2)

**Files:**
- Modify: `deploy/ensayo/ensayo-local.sh`, `deploy/ensayo/shims/ssh`, `deploy/ensayo/shims/systemctl`
- Create: `deploy/ensayo/shims/scp`
- Test: `tests/StockApp.Infrastructure.Tests/Kit/EnsayoLocalTests.cs`; `MigracionVpsSincroniaTests.ScriptsNuevos` gana `deploy/ensayo/shims/scp`

**Interfaces:**
- `shims/ssh`: con `ENSAYO_TRABAJO` definido, enruta por host: `vps-viejo.ensayo.invalid` y `vps-nuevo.ensayo.invalid` → `ENSAYO_ESTADO=<trabajo>/hosts/<host>/estado`, `ENSAYO_RAIZ=<trabajo>/hosts/<host>/raiz`, `HOME=<trabajo>/hosts/<host>/home`; otro `.invalid` → 255. Sin `ENSAYO_TRABAJO`, comportamiento de hoy (E1). `shims/scp`: `-P`/`-o` ignorados, `host:ruta` con la misma regla, `~` contra el `HOME` del host.
- `shims/systemctl`: la raíz válida es `dirname(ENSAYO_ESTADO)/vps-viejo` (E1) o `dirname(ENSAYO_ESTADO)/raiz`; nunca `/`. Nuevo: `start` sin archivo de unit → `Unit stockapp-api.service not found.` y exit 5 (systemd 255); si `<estado>/api.lanzar` existe, `start` lanza la API publicada (`dotnet <dll>`) con el entorno de `<raiz>/etc/stockapp-api/api.env` más `HOME=<raiz>/var/lib/stockapp`, guarda el PID en `<estado>/api.pid`, y `stop` la mata por ese PID y verifica con `ps -p` que no siga viva; `is-active` con PID muerto da `failed`.
- `ensayo-local.sh preparar-migracion --trabajo <d> [--dump <real>] [--sufijo <s>] [--puerto-viejo <n>] [--puerto-nuevo <n>]`: `dotnet publish src/StockApp.Api/StockApp.Api.csproj -c Release -o <d>/api` una vez; los dos Postgres de hoy; el viejo con su API real (base migrada por su primer arranque y admin sembrado con una contraseña aleatoria en `<d>/admin-password`, 600; o el `--dump` restaurado, con la credencial real por entorno); el nuevo en el estado que deja `preparar` (base migrada por un primer arranque, feed mínimo en `Actualizaciones__Directorio`, unit, directorios, `~/stockapp-deploy/.env`); `<raiz>/etc/machine-id` distinto en cada host; `env-vps-viejo-ensayo` y `env-vps-nuevo-ensayo` del operador apuntando a los hosts `.invalid` con `API_SCHEME=http`. `migrar --trabajo <d> -- <args de migrar-vps.sh>`: agrega `--contenedor-*`, `--raiz-remota-*`, `--sin-verificar-preparacion` y las rutas de los `.env`, con los shims al frente del `PATH` y la palabra por stdin. `limpiar`: además de lo de hoy, mata por PID exacto las dos APIs y verifica con `ps`.

- [ ] **Step 1: Tests en rojo:** `ShimSsh_EnrutaPorHost` (cada host ve su `HOME`); `ShimSsh_OtroHostInvalid_Rechaza255`; `ShimScp_CopiaAlHomeDelHost`; `ShimSystemctl_StartSinArchivoDeUnit_Falla5` (Review Focus 5); `ShimSystemctl_RaizEnHosts_Acepta` y `…RaizBarra_Rechaza1`; `ShimSystemctl_StartLanzaYStopMataPorPid` (con un `api.lanzar` que corre un ejecutable trivial del test, no la API); `PrepararMigracion_NombresSoloDeEnsayo`; `Limpiar_NoQuedanProcesosNiContenedores`. Los tests E1 existentes siguen verdes sin cambios.
- [ ] **Step 2-4:** rojo, implementar, verde (`--filter FullyQualifiedName~EnsayoLocalTests`, colección serializada). Mutación: quitar la rama "start sin archivo → 5" → `ShimSystemctl_StartSinArchivoDeUnit_Falla5` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `test(deploy): el arnés de ensayo simula dos hosts con la API real`

### Task 14: Casos de punta a punta de `migrar` en el arnés (spec §8.2)

**Files:** Test `tests/StockApp.Infrastructure.Tests/Kit/EnsayoLocalTests.cs` (colección `EnsayoLocal`, F9).

**Interfaces:** Consumes `ensayo-local.sh preparar-migracion|migrar|limpiar` (Task 13) y `migrar-vps.sh` (Tasks 8-12). Cada test usa un `--sufijo` propio, `HOME` temporal, y `limpiar` en `finally`.

- [ ] **Step 1: Tests:** `Migrar_Ensayo_Go_ViejoActive`; `Migrar_Corte_Go_ViejoMasked`; `Migrar_RevertirTrasCorte_ViejoActiveNuevoInactive`; `Revertir_OrdenEnElViejo_UnmaskMvReloadStart` (Review Focus 5: el `--revertir` real tras un corte; el rastro del shim muestra `unmask` < `mv` < `daemon-reload` < `start`, y el viejo queda `active`); `Migrar_CorteConFalloInyectado_ContinuarDaGo` (`[Theory]` 7, 9, 11); `Migrar_DesdeBackup_DumpDeOtraCorrida_GoReducido`; `Migrar_PostCorteAntesDePlazo_ArchivaYBorraSoloLoConfirmado`. Cada uno afirma también `docker ps` igual al inicial y sin PIDs de API vivos al final.
- [ ] **Step 2: Correr** en primer plano (`--filter FullyQualifiedName~EnsayoLocalTests.Migrar|FullyQualifiedName~EnsayoLocalTests.Revertir`, timeout 600000; si no entra, de a un test). Un rojo acá es un bug de un orquestador o del arnés: se arregla en el archivo que corresponde con su test unitario primero.
- [ ] **Step 3: Mutación:** quitar el `mv …congelado …service` de `--revertir` → `Migrar_RevertirTrasCorte_…` rojo (el shim fiel falla el `start` con 5). Pegar y restaurar.
- [ ] **Step 4: Commit** — `test(deploy): punta a punta de migrar-vps en el arnés local`

### Task 15: Runbook corto, procedimientos y guardianes de texto (spec §6.7)

**Files:**
- Modify: `deploy/MIGRACION-VPS.md` (reescrito), `deploy/PROCEDIMIENTOS.md` (Procedimiento 3 apunta a `preparar` + `migrar-vps.sh --desde-backup`), `deploy/DEPLOY.md` (sección 8: los dos orquestadores)
- Test: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs`

**Interfaces:** el runbook tiene las 10 secciones de spec §6.7 en ese orden, con el comando exacto y la tabla de qué hacer según cada código. Sección 2 (prerrequisitos de Martin) agrega `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`, `VPS_ADMIN_USER` en `deploy/.env` (hoy faltan) y `deploy/.env.vps-nuevo` completo. Sección 4 incluye el **ensayo del corte completo + `--revertir` contra el VPS viejo (testing) antes del día D** (§10.4). Sección 8: `preparar` + `--desde-backup`, con el límite de R8 en una línea y "el backup off-site programado es el próximo proyecto" (§10.3). Sección 3: `--probar-renovacion` una vez, a mano (§10.2). Sección 7 conserva el texto actual de R-B. Sección 10 conserva la tabla de Tiempos y el resultado de E1.

- [ ] **Step 1: Tests en rojo** (reemplazan a los de prosa vieja, que se borran: `IdsDelRunbook` pasa a `R-B`, `E1` y los títulos de las 10 secciones; `Runbook_ReflejaLosHallazgosDeLasReviews` y `Runbook_MencionaElComandoExacto` se reescriben con los fragmentos nuevos):
  - `Runbook_TieneLasDiezSeccionesEnOrden`.
  - `Runbook_MencionaElComandoExacto` con: `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3`, `--probar-renovacion`, `deploy/migrar-vps.sh --ensayo`, `CONGELAR`, `--continuar`, `--revertir`, `--desde-backup`, `--post-corte`, `scripts/gui-verificacion`, `SmartScreen`, `14 días`.
  - `Runbook_TablaDeCodigosDeLosOrquestadores` (cada código 0-9 de §6.3.2 aparece en la tabla de la sección 6).
  - `Runbook_EnsayoDelCorteYRevertirAntesDelDiaD` y `Runbook_OffSiteEsElProximoProyecto`.
  - `Runbook_SinComandosManualesDeCorte`: no contiene `systemctl -q mask`, `restaurar-backup.sh`, `volcado-final-vps.sh` ni `/tmp/` (esa prosa ahora es código).
  - Se conservan: `ScriptsNuevos_SinHostsNiIpsCableados` sobre el runbook, `Runbook_NuncaUsaComandosGlobalesNiRmRfSobreVariables`, `Procedimientos_Paso7ApuntaARestaurarBackup` reescrito como `Procedimientos_RecuperacionApuntaADesdeBackup`.
- [ ] **Step 2-4:** rojo, reescribir, verde. Mutación: quitar la línea del ensayo del corte + `--revertir` → `Runbook_EnsayoDelCorteYRevertirAntesDelDiaD` rojo. Pegar y restaurar.
- [ ] **Step 5: Commit** — `docs(deploy): el runbook de la migración pasa a dos comandos`

### Task 16: Ensayo local de los dos orquestadores, revisión final y merge

**Files:** solo el ledger (y, si el ensayo encuentra un bug, el archivo afectado con su test primero, en un commit `fix(deploy): …`).

- [ ] **Step 1: Suite completa** en primer plano, partida por filtro si hace falta (`FullyQualifiedName~Kit` en un comando, `FullyQualifiedName!~Kit` en otro), timeout 600000 cada uno. Esperado: verde salvo los `.ods` conocidos; totales ≥ línea base de la Task 1 + los nuevos. Pegar.
- [ ] **Step 2: Ensayo de `preparar` de punta a punta sobre el host simulado:** `--filter FullyQualifiedName~PrepararVpsNuevoTests.PuntaAPunta|FullyQualifiedName~PrepararVpsNuevoTests.DosCorridas` → PASS. Pegar las líneas de progreso (primera corrida OK, segunda todo `YA ESTABA`, `--verificar` todo `HECHO`).
- [ ] **Step 3: Ensayo de `migrar` a mano con el arnés** (como E1, por un agente, en primer plano): `T=~/.cache/stockapp-ensayo-auto/$(date -u +%Y%m%dT%H%M%SZ)`; `deploy/ensayo/ensayo-local.sh preparar-migracion --trabajo "$T" --sufijo auto`; `… migrar --trabajo "$T" -- --ensayo` → exit 0, GO; `… migrar --trabajo "$T" --` con `CONGELAR` por stdin → exit 0, GO, viejo `masked`; `… migrar --trabajo "$T" -- --revertir` con `REVERTIR` → viejo `active`, nuevo `inactive`; `… limpiar --trabajo "$T"` → exit 0, `docker ps` igual al inicial, sin PIDs de API (`ps -p` de los dos PIDs guardados vacío); `rm -r -- "$T"` (ruta literal, impresa antes). Pegar los resúmenes y la tabla de tiempos en el ledger.
- [ ] **Step 4: Comprobaciones finales:** `git log main..HEAD --format=%B | grep -c 'Co-Authored-By'` imprime `0` (sale con 1: es lo esperado); `git diff main --stat` sin `deploy/.env*` salvo `.env.example`; `docker ps -a` sin `stockapp-test-*` ni `stockapp-ensayo-*`.
- [ ] **Step 5: Merge y push:** `git -C /home/capua25/workspace/stockapp merge --ff-only feat/migracion-vps-automatizada && git -C /home/capua25/workspace/stockapp push origin main`. Después, `git worktree remove` del worktree y `git branch -d feat/migracion-vps-automatizada`.

---

## Hallazgos contra la spec

1. **`migrar` llama a `preparar --verificar` en su pre-vuelo (§6.5.3 punto 3), pero el arnés no puede satisfacerlo** (§8.2: `preparar` no corre en el arnés: ufw, certbot, el usuario `stockapp` y los dueños de archivos no existen en la máquina del operador). **Decisión del plan:** `--sin-verificar-preparacion`, válido solo con hosts `.invalid` (el mismo criterio de §6.3.6), informado como `OMITIDO (arnés)` en el resumen. Los tests unitarios de la Task 8 cubren que `migrar` aborte con 3 si `preparar --verificar` falla. Costo si está mal: una brecha declarada más del arnés, cerrada en el GATE real (§8.3.2).
2. **El pre-vuelo valida la credencial contra la API VIEJA (§6.5.3 punto 5), pero el arnés de §8.2 solo levanta una API en el host nuevo.** **Decisión del plan:** el arnés levanta la API real también en el host viejo (Task 13). Además hace real el congelamiento: el `stop` del shim mata la API y el chequeo de conexiones abiertas de `volcado-final-vps.sh` deja de ser trivialmente cero.
3. **Los dos hosts del arnés son la misma máquina, así que su `/etc/machine-id` es el mismo** y §6.3.4 daría siempre 9. **Decisión del plan:** con `--raiz-remota-*` el machine-id se lee de `<raiz>/etc/machine-id`, que el arnés escribe distinto en cada host.
4. **`migrar` copia archivos por scp (§6.5.4 paso 8), y el arnés no tiene un `scp` falso** (hoy `volcado-final-vps.sh` trae todo por el stdout de ssh). Se agrega `deploy/ensayo/shims/scp` (Task 13).
5. **El shim actual de `systemctl` arranca una unit sin archivo** (`deploy/ensayo/shims/systemctl`, rama `start`: solo mira `masked`). systemd real responde `Unit … not found.` con 5. Se corrige en la Task 13 (Review Focus 5).
6. **Disco del pre-vuelo (§6.5.3 punto 2, "`df` del volumen de Docker"):** el directorio de volúmenes de Docker es de root, y el pre-vuelo no puede usar `sudo` para un `df` sin salirse de la lista de lecturas de §5. Se mide dentro del contenedor (`docker exec stockapp-pg df -P /var/lib/postgresql/data`), que está en la lista blanca.
7. **§6.6 escribe `to_regclass('"<tabla>"') IS NULL`.** El plan usa `to_regclass('public."<tabla>"') IS NOT NULL`, igual que `restaurar-backup.sh:734`: no depende del `search_path` y es la misma forma en los dos scripts.
8. **§6.5.1: `VPS_ADMIN_USER` "sale del `--env-viejo`".** Hoy `deploy/.env` no define ningún `VPS_*` (verificado por nombres), y `deploy-vps.sh:189` cae a `BOOTSTRAP_ADMIN_USER`. `migrar` lo exige explícito, sin esa caída (ruling F11: una credencial vencida quema intentos del rate limit de login), y el runbook lo pide en los prerrequisitos.
9. **§6.3.5 dice que los orquestadores cargan el `.env` "con el mismo bloque `STOCKAPP_ENV_LOCAL` que los bloques" (`source`).** `migrar` necesita dos `.env` a la vez y un `source` del segundo pisaría al primero. Los dos orquestadores leen línea por línea (`orq_leer_env`), sin ejecutar el archivo, y rechazan comillas y CRLF (Review Focus 2). Los `.env` actuales no usan comillas (verificado sin leer valores).
10. **§8.1 ubica los shims del host simulado "como el shim de `systemctl` del ensayo"**, pero `deploy/ensayo/` está en `ScriptsNuevos`, que prohíbe `ufw` y `certbot`. Viven en `tests/StockApp.Infrastructure.Tests/Kit/HostSimulado/`, y el host simulado es un contenedor `ubuntu:24.04` como root (patrón de `InstallEsquemaTests`), así `useradd`, `install`, `stat` y `chown` son los reales y no hace falta un shim que mienta sobre dueños y modos.

## Self-review

- **Cobertura de la spec:** O1 → Tasks 4-7; O2 → 8-9; O3 → 10; O4 → 11; O5 → 10 (+ Review Focus 3); O6 → 12; O7 → 13-14, 16. A8 → 5; A9 → 4, 8; A10 → 7; A11 → 9; A12 → 2, 8; A13 → 10; A14 → 7, 9. §6.6 → 1. §6.7 → 15. Sincronía de §8.1 → 2, 4, 7, 8, 13. Tabla de mutación de §8.1: cada fila tiene su paso de mutación (confirmación 8; A11 9; machine-id 8; restaurar≠0 9; stop en revertir 11; guardia 11; `--ensayo` 10; `.env` 7; `ufw allow` 5; reversa 5; unit instalada 7; contenedores ajenos 4; rama "no existe" 1). §10: 1 → Task 1; 2 → Task 6; 3 → Tasks 12 y 15; 4 → Task 15.
- **Consistencia de nombres:** `orq_*` de la Task 2 son los únicos que usan las Tasks 4-14; los parámetros de arnés de Contratos son los mismos en las Tasks 8, 10 y 13; los modos de corrida (`ensayo|corte|desde-backup|revertir`) coinciden con Contratos.
- **Proporción:** el plan decide nombres, órdenes, valores y tests; no trae cuerpos de funciones salvo la SQL de la Task 1 (pinada por la spec).
