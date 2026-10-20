# Migración de datos al VPS nuevo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar listas, probadas y ensayadas con datos reales todas las piezas para mudar los datos de producción de StockApp del VPS compartido actual a un VPS nuevo, sin ejecutar el corte real (que necesita el VPS nuevo, el dominio nuevo y una fecha, y queda como runbook).

**Architecture:** Once tasks en serie sobre `feat/migracion-vps`. (1-2) `deploy-vps.sh` y `publicar-release.sh` eligen el `.env` del operador con `STOCKAPP_ENV_LOCAL`, y el marcador de último deploy pasa a ser por host. (3-5) `deploy/restaurar-backup.sh`, autocontenido, restaura un dump en una base vacía y verifica todo (manifiesto, conteos, secuencias, catálogos), con `--recrear-base` (dump de seguridad previo) y `--verificar-post-arranque` (V6). (6) `deploy/volcado-final-vps.sh` congela, vuelca, arma el manifiesto y trae todo verificado. (7) `deploy/bajar-backup-vps.sh` baja un backup EXISTENTE por `GET /backups` sin escribir nada en el VPS (lo pide el ensayo de solo lectura). (8) `deploy/ensayo/` simula el VPS viejo en local con shims de `ssh`/`sudo`/`systemctl` y contenedores con nombre propio. (9) El deploy-hook de TLS se trae (lectura) y se versiona. (10) Runbook `deploy/MIGRACION-VPS.md` y docs. (11) Ensayo completo con un backup real del VPS de testing, ejecutado por un agente, y merge.

**Tech Stack:** bash (`set -euo pipefail`, `jq`, `curl`), Docker, `pg_dump`/`pg_restore`/`psql` 16 DENTRO de `postgres:16-alpine` (el host también tiene `postgresql-client-16`, verificado 16.15), xUnit v2 en `tests/StockApp.Infrastructure.Tests` con `EjecutorBash` (`tests/_Compartido/EjecutorBash.cs`) y `EsperaMonotonica.HastaAsync`, stubs por `PATH`, contenedores reales (`postgres:16-alpine`, `ubuntu:24.04`), `PostgresFixture` (Testcontainers, `[Collection("Postgres")]`) + `EjecutorPgDumpProceso` para generar dumps con el esquema real. .NET 10 solo para correr la API en el ensayo: este plan NO toca C# de producción.

**Spec:** `docs/superpowers/specs/2026-10-20-migracion-vps-design.md` (aprobada por Martin el 2026-10-20, con todas las decisiones del asistente aceptadas). Las desviaciones están al final, en "Hallazgos contra la spec", con evidencia.

## Global Constraints

- **Worktree:** `git worktree add -b feat/migracion-vps /home/capua25/workspace/stockapp-worktrees/migracion-vps main` (nunca bajo `/tmp`). Todos los comandos corren como `cd /home/capua25/workspace/stockapp-worktrees/migracion-vps && <comando>`. `main` tiene que estar limpio y en `0d0c2b0` o posterior (verificar con `git -C /home/capua25/workspace/stockapp status --short` y `git log -1`).
- **CodeGraph:** el worktree tiene su PROPIO índice: `gentle-ai codegraph init --cwd /home/capua25/workspace/stockapp-worktrees/migracion-vps` una vez (nunca copiar ni enlazar el `.codegraph/` de otro checkout). Para C# se usa `codegraph_explore` (MCP, responde con el índice del checkout principal) o la CLI (`codegraph query|node|callers`) en el worktree, y antes de editar se contrastan líneas y miembros con `sed -n` (el índice ya devolvió versiones viejas y "already sent" falsos). Para `.sh`, `.md`, systemd, compose y `.env.example` el fallback correcto es Read/`grep` directo: CodeGraph no indexa ese contenido.
- **TDD con verificación por MUTACIÓN.** Test primero, rojo REAL pegado en el ledger, código mínimo, verde. Cada guardián se verifica QUITANDO la regla y pegando el rojo, nunca razonando y nunca agregando un efecto destructivo: `export SCRATCH=~/.cache/stockapp-migracion-vps && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la línea o el fragmento; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas; se corre el test y se PEGA el rojo; `cp "$SCRATCH/<nombre>.bak" <archivo>` y `git diff --quiet -- <archivo>` confirma la restauración.
- **Bash:** `set -euo pipefail` en todo script nuevo. Un `grep` en un pipe sin coincidencias mata el script: donde "no hay match" es válido se captura el exit code explícito (`rc=0; x="$(... )" || rc=$?` y se distingue 1 de ≥2); está PROHIBIDO `|| true` sobre una consulta cuyo fallo importa. Cada consulta remota o a `docker exec … psql` captura por separado exit code y stderr, con el criterio de `deploy-vps.sh:292-312`: "no pude consultar" NUNCA es "vacío", "cero" ni "sin duplicados". SQL por stdin (`psql -tA` leyendo de stdin), nunca por argv. Secretos nunca por argv (criterio de `ssh_vps_autenticado`, `deploy-vps.sh:117-120`).
- **Tests de scripts:** los patrones de `tests/StockApp.Infrastructure.Tests/Kit/`: stubs por `PATH` con rastro `printf ' %q'` y dispatch por contenido (`DeployVpsTests.SshFalso`, drenando stdin donde el script manda algo por pipe); contenedor `ubuntu:24.04` descartable como root (`InstallEsquemaTests.EjecutarInstall`, L170-L216); Postgres real (`PostgresFixture` + `EjecutorPgDumpProceso`, como `RestaurabilidadBackupTests.cs:69-80`, y `docker run postgres:16-alpine` con nombre `stockapp-test-<guid>`). Cada script se prueba en el camino feliz Y en los de falla, afirmando exit code, stderr y rastro.
- **Contenedores de test y de ensayo:** nombre propio (`stockapp-test-<guid12>`, `stockapp-ensayo-*`), eliminados en `finally` por nombre EXACTO con `docker rm -f -v <nombre>` (el `-v` borra el volumen anónimo de la imagen). Ningún test ni paso del plan toca el `stockapp-pg` de desarrollo ni `pinar-postgres-1` (que corre en esta máquina). Procesos lanzados por agentes (API local, loops) se matan por PID exacto y se verifica con `ps`/`docker ps` al terminar; nada de `pkill -f`.
- **VPS viejo compartido con pinar:** todo comando remoto acotado por nombre a StockApp (`stockapp-api`, `stockapp-pg`, `/var/lib/stockapp`, `/etc/stockapp-api`, `~/stockapp-deploy`). No se toca nginx ni certbot global, no hay comandos globales de Docker ni de systemd, y NINGÚN dump pasa por el `/tmp` del host compartido. En este plan el VPS viejo solo se LEE (Task 7, Task 9, Task 11): nada se detiene, se escribe ni se dispara ahí.
- **Sin hosts cableados:** ningún host, IP, puerto ni dominio del VPS actual en scripts, tests ni runbook (`MigracionVpsSincroniaTests.ScriptsNuevos_SinHostsNiIpsCableados`). Los parámetros salen del `.env` del operador que elige `STOCKAPP_ENV_LOCAL` (default `<repo>/deploy/.env`).
- **Auditoría:** `LogsAuditoria` no se borra nunca. Toda operación destructiva sobre una base (`--recrear-base`) toma antes un dump de seguridad verificado; si ese dump falla, no se borra nada.
- **`deploy/.env` está protegido por una regla deny para agentes:** ninguna task lo escribe ni lo copia al worktree. Los scripts lo leen (con `source`, como hoy) vía `STOCKAPP_ENV_LOCAL=/home/capua25/workspace/stockapp/deploy/.env`. Si un paso necesita algo que no está ahí, el agente se DETIENE y le dice a Martin qué agregar (ver Task 10, sección "Lo que Martin tiene que cargar").
- **.NET:** versiones de paquetes solo en `Directory.Packages.props` (este plan no agrega paquetes). Tests de a UN `.csproj` por comando, con `--blame-hang-timeout 5m`, nunca dos `dotnet test` a la vez: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~<Clase>`. Build, cuando haga falta, con `dotnet build StockApp.sln` (varios `.csproj` en un mismo `dotnet build` dan falso verde). Rojos FALSOS conocidos en worktree: los de fixtures `.ods` en `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` (confirmar por nombre). Cualquier otro rojo es real.
- **Commits:** conventional commits en español, uno por task, SIN `Co-Authored-By` ni ninguna línea de atribución de IA; `git add` con rutas exactas (nunca `-A` ni `.`), verificado con `git diff --cached --name-only`. Sin `git reflog expire`, `git gc` ni `git prune`. Merge a `main` con `--ff-only` + push, solo en la Task 11.
- **Ledger** git-ignored: `.superpowers/sdd/2026-10-20-migracion-vps/progress.md` (línea base, rojos, mutaciones, totales y tiempos del ensayo).
- Las referencias `archivo:línea` son las de `0d0c2b0`; tras editar, ubicar por el texto citado. Sin referencias al plan en el código (nada de "Task 4" en `.sh`/`.cs`).
- **El corte real NO se ejecuta.** El plan termina con todo listo y el ensayo E1 hecho por un agente contra un ambiente descartable local con un backup real; E2 y el corte quedan como runbook.

## Review Focus

1. **`STOCKAPP_ENV_LOCAL` apunta a un archivo que no existe (typo en el corte, con dos VPS en juego).** Esperado: aborta antes de cualquier `ssh`/`scp`, nombrando la ruta; nunca cae en silencio a `deploy/.env` ni a variables del entorno, que pueden ser las del OTRO VPS. Tests: `StockappEnvLocalInexistente_AbortaSinTocarSsh` (Task 1, en los dos scripts) y el mismo caso en `VolcadoFinalVpsTests` y `BajarBackupVpsTests`.
2. **Se re-corre `volcado-final-vps.sh` en modo final después de un intento fallido (la API ya quedó detenida y enmascarada).** Esperado: sigue, vuelve a volcar y verifica; nunca hace `unmask` ni `start`. Test: `ModoFinal_Reintento_ConApiYaEnmascarada_Funciona` (Task 6).
3. **`--ensayo` con la API de testing ya caída antes de empezar.** Esperado: el `trap` deja la API como la encontró; no la arranca. Test: `Ensayo_ApiEstabaInactiva_ElTrapNoLaArranca` (Task 6).
4. **Después del arranque, la retención borra filas de `CorridasBackup`** (`ServicioBackup.AplicarRetencionAsync` → `_corridas.EliminarAsync`, L142-L156, disparado por el catch-up de `BackupProgramadoService` si la última corrida tiene ≥ 12 h). Esperado: `--verificar-post-arranque` no da un falso rojo por `CorridasBackup`, pero sigue fallando si `LogsAuditoria` baja. Tests: `PostArranque_CorridasBackupMenor_Ok` y `PostArranque_LogsAuditoriaMenor_Falla6` (Task 5).
5. **`restaurar-backup.sh` con `--base`/`--usuario` explícitos en una máquina sin `~/stockapp-deploy/.env` (el ensayo local).** Esperado: no lee ni exige el `--env` por defecto. Test: `SinEnvPorDefecto_ConBaseYUsuarioExplicitos_NoLoNecesita` (Task 3).

## Contratos compartidos

Los usan varias tasks; cada test que los toca los toma de acá.

**Manifiesto** (`volcado-<ts>.manifiesto`, texto UTF-8, lo escribe la Task 6 y lo leen las Tasks 4-5 y sus tests):
```
# manifiesto-migracion v1
fecha_utc=2026-10-20T13:05:00Z
host_origen=<hostname remoto>
version_servidor=<SELECT version()>
version_pg_dump=<pg_dump --version del contenedor>
base=stockapp
dump=volcado-20261020T130500Z.dump
sha256=<64 hex minúsculas>
tamano_bytes=<entero>
ultima_migracion=<MigrationId mayor de __EFMigrationsHistory>
[conteos]
<tabla>\t<filas>
```
Cabecera `clave=valor` hasta la línea `[conteos]`; después una línea `tabla<TAB>filas` por tabla, en el orden de la consulta. Claves desconocidas se ignoran; una clave obligatoria faltante o una línea de conteo mal formada es "manifiesto inválido" (exit 2).

**Consulta de conteos** (spec §5.3 paso 8, IDÉNTICA en `restaurar-backup.sh` y `volcado-final-vps.sh`, entre las líneas marcadoras `# >>> SQL-CONTEOS` y `# <<< SQL-CONTEOS`; `MigracionVpsSincroniaTests` compara el texto entre marcadores):
```sql
SELECT table_name,
       (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text::bigint
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;
```

**Códigos de salida** (documentados en el encabezado de cada script; los tests los fijan):

| Código | `restaurar-backup.sh` | `volcado-final-vps.sh` | `bajar-backup-vps.sh` |
|---|---|---|---|
| 0 | OK | OK | OK |
| 2 | uso/argumentos/manifiesto inválidos, `--recrear-base` ≠ `--base` | uso | uso |
| 3 | precondición: API activa, contenedor fuera de patrón/ausente/detenido, base no vacía, conexiones abiertas al `DROP` | precondición: config faltante, sin `sudo -n`, contenedor fuera de patrón, API sigue activa, conexiones > 0 | precondición: config faltante, login fallido, sin corrida exitosa, `--id` inexistente o no exitoso, falta `pg_restore`/`jq` |
| 4 | dump inválido: sha/tamaño, `--list`, tablas ≠ manifiesto | traída inválida: sha local ≠ remoto, tamaño < 1024 | descarga inválida: tamaño ≠ listado o < 1024, `--list` falla, nombre inseguro |
| 5 | falló `pg_restore` (la base queda vacía) | — | — |
| 6 | falló conteos/migración/secuencias, o `--verificar-post-arranque` (base restaurada) | — | — |
| 7 | duplicados en catálogos (base restaurada) | — | — |
| 8 | no se pudo consultar (`docker`/`psql` con error) | no se pudo consultar | no se pudo consultar |
| 9 | falló el dump de seguridad de `--recrear-base` (no se borró nada) | alcance: `docker ps` final ≠ inicial | — |

**Patrón de nombres:** contenedor `^stockapp[a-z0-9-]*$`; base y rol `^[a-z_][a-z0-9_]*$` (validados ANTES de interpolarlos en SQL o en un comando).

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `deploy/deploy-vps.sh` (mod.) | `STOCKAPP_ENV_LOCAL`; marcador `.ultimo-deploy-vps-commit-<host>` |
| `build/publicar-release.sh` (mod.) | `STOCKAPP_ENV_LOCAL` |
| `deploy/restaurar-backup.sh` (nuevo) | restaurar un dump en base vacía con verificación; `--recrear-base`; `--verificar-post-arranque`. Autocontenido: se copia SOLO al servidor (C4) |
| `deploy/volcado-final-vps.sh` (nuevo) | congelar el VPS viejo, volcar, manifiesto, traer verificado |
| `deploy/bajar-backup-vps.sh` (nuevo) | bajar por `GET /backups` un backup ya existente, sin escribir en el VPS |
| `deploy/ensayo/ensayo-local.sh`, `deploy/ensayo/shims/{ssh,sudo,systemctl}` (nuevos) | VPS viejo simulado en local para el ensayo E1 |
| `deploy/tls/stockapp-tls-deploy-hook.sh` (nuevo) | hook de certbot versionado |
| `deploy/MIGRACION-VPS.md` (nuevo), `deploy/PROCEDIMIENTOS.md`, `deploy/DEPLOY.md`, `deploy/.env.example` (mod.) | runbook y docs |
| `tests/StockApp.Infrastructure.Tests/Kit/{RestaurarBackupTests,VolcadoFinalVpsTests,BajarBackupVpsTests,EnsayoLocalTests,TlsDeployHookTests,MigracionVpsSincroniaTests}.cs` (nuevos), `DeployVpsTests.cs`, `PublicarReleaseTests.cs` (mod.) | guardianes |

---

### Task 1: `STOCKAPP_ENV_LOCAL` elige el `.env` del operador

**Files:**
- Modify: `deploy/deploy-vps.sh:135-141` (carga del `.env`) y el encabezado (L4-L41, documentar la variable)
- Modify: `build/publicar-release.sh:122-129` y el encabezado (L17-L23)
- Modify: `deploy/.env.example` (bloque "Conexión SSH al VPS", L16-L23: una línea sobre `STOCKAPP_ENV_LOCAL`)
- Test: `tests/StockApp.Infrastructure.Tests/Kit/DeployVpsTests.cs`, `tests/StockApp.Infrastructure.Tests/Kit/PublicarReleaseTests.cs`

**Interfaces:**
- Produces: en los dos scripts, `ENV_LOCAL="${STOCKAPP_ENV_LOCAL:-${REPO_ROOT}/deploy/.env}"`. Si `STOCKAPP_ENV_LOCAL` está definida y el archivo no existe o no es legible → `ERROR: STOCKAPP_ENV_LOCAL apunta a '<ruta>', que no existe o no se puede leer.` y exit 1, antes de cualquier `ssh`/`scp`. Sin la variable, comportamiento idéntico al de hoy (archivo opcional). Las Tasks 6 y 7 copian este mismo bloque.

- [ ] **Step 1: Línea base de la suite**

Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m` (solo este proyecto: el plan no toca otro). Expected: verde salvo los `.ods` conocidos. Pegar totales en el ledger.

- [ ] **Step 2: Tests en rojo**

`DeployVpsTests` (con `CrearRepoFixture` + `Correr`; agregar a `Correr` un parámetro `IDictionary<string,string>? envExtra`):
- `StockappEnvLocal_EligeElArchivo`: escribe `<fixture>/otro.env` con `VPS_HOST=otro.test.invalid` (y el resto de `EnvBase` del fixture), `envExtra = {STOCKAPP_ENV_LOCAL=<ruta>}`, args `0.0.0-test --dry-run` → exit 0 y todas las líneas del rastro de `ssh` contienen `operador@otro.test.invalid`, ninguna `vps.test.invalid`.
- `StockappEnvLocalInexistente_AbortaSinTocarSsh`: `STOCKAPP_ENV_LOCAL=<fixture>/no-existe.env` → exit ≠ 0, stderr contiene `STOCKAPP_ENV_LOCAL` y la ruta, rastros de `ssh` y `scp` vacíos.
- `SinStockappEnvLocal_UsaDeployEnvComoHoy`: el `.env` del fixture define `VPS_HOST=del-repo.test.invalid` y el entorno no define `STOCKAPP_ENV_LOCAL` → el rastro usa `del-repo.test.invalid`.

`PublicarReleaseTests` (con `Entorno.Correr(args, env)`): los mismos tres casos con nombres idénticos; en el feliz, el rastro de `ssh` nombra el host del archivo elegido.

- [ ] **Step 3: Ver el rojo**

Run: `dotnet test … --filter "FullyQualifiedName~DeployVpsTests.StockappEnvLocal|FullyQualifiedName~PublicarReleaseTests.StockappEnvLocal|FullyQualifiedName~SinStockappEnvLocal"`. Expected: FAIL en `StockappEnvLocal_*` (el script ignora la variable). Pegar.

- [ ] **Step 4: Implementar en los dos scripts y documentar** (encabezados y `.env.example`: "con dos VPS en juego, `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh …`; como el `.env` se carga con `source` DESPUÉS del entorno, exportar `VPS_HOST` no alcanza").

- [ ] **Step 5: Verde y mutación**

Run el filtro del Step 3 y después `--filter "FullyQualifiedName~DeployVpsTests|FullyQualifiedName~PublicarReleaseTests"`. Expected: PASS. Mutación: quitar el chequeo de existencia en `deploy-vps.sh` → `StockappEnvLocalInexistente_AbortaSinTocarSsh` en rojo (el rastro de `ssh` no queda vacío o el exit no es el del chequeo). Pegar y restaurar.

- [ ] **Step 6: Commit**

```bash
git add deploy/deploy-vps.sh build/publicar-release.sh deploy/.env.example tests/StockApp.Infrastructure.Tests/Kit/DeployVpsTests.cs tests/StockApp.Infrastructure.Tests/Kit/PublicarReleaseTests.cs
git commit -m "feat(deploy): STOCKAPP_ENV_LOCAL elige el .env del operador en deploy-vps y publicar-release"
```

---

### Task 2: El marcador de último deploy es por host

**Files:**
- Modify: `deploy/deploy-vps.sh:201` (`MARCADOR_ULTIMO_DEPLOY`) y el texto de L589-L595
- Test: `tests/StockApp.Infrastructure.Tests/Kit/DeployVpsTests.cs`

**Interfaces:**
- Consumes: `VPS_HOST` ya validado (L145).
- Produces: `MARCADOR_ULTIMO_DEPLOY="${REPO_ROOT}/deploy/dist/.ultimo-deploy-vps-commit-${VPS_HOST_SEGURO}"`, con `VPS_HOST_SEGURO="${VPS_HOST//[^A-Za-z0-9._-]/_}"`. El marcador viejo sin sufijo se ignora (no se borra ni se migra: con él se arriesga saltear la copia en un host nuevo, que es justo el bug).

- [ ] **Step 1: Tests en rojo**

- `MarcadorDeOtroHost_CopiaInstallSh`: en el repo fixture, `deploy/dist/.ultimo-deploy-vps-commit-otro.test.invalid` con el `HEAD` actual (install.sh sin cambios desde ese commit); corrida completa contra `vps.test.invalid` → el rastro de `scp` contiene `install.sh`, `stockapp-api.service` y `wait-for-postgres.sh`.
- `MarcadorViejoSinHost_CopiaInstallSh`: `deploy/dist/.ultimo-deploy-vps-commit` (nombre de hoy) con `HEAD` → igual, copia.
- `MarcadorDelMismoHostSinCambios_NoCopiaInstallSh`: `…-vps.test.invalid` con `HEAD` → el rastro de `scp` NO contiene `install.sh`.
- `CaminoFeliz_EscribeElMarcadorDelHost`: tras exit 0 existe `deploy/dist/.ultimo-deploy-vps-commit-vps.test.invalid` con `HEAD` y NO existe el marcador sin sufijo.

- [ ] **Step 2: Ver el rojo**

Run: `--filter "FullyQualifiedName~DeployVpsTests.Marcador|FullyQualifiedName~DeployVpsTests.CaminoFeliz_EscribeElMarcadorDelHost"`. Expected: FAIL en `MarcadorDeOtroHost_*`, `MarcadorViejoSinHost_*` y `CaminoFeliz_EscribeElMarcadorDelHost`. Pegar.

- [ ] **Step 3: Implementar** (una línea más la sanitización; el mensaje de L590 nombra el host).

- [ ] **Step 4: Verde y mutación**

Run: `--filter FullyQualifiedName~DeployVpsTests`. Expected: PASS. Mutación: quitar el sufijo `-${VPS_HOST_SEGURO}` → `MarcadorDeOtroHost_CopiaInstallSh` en rojo. Pegar y restaurar.

- [ ] **Step 5: Commit**

```bash
git add deploy/deploy-vps.sh tests/StockApp.Infrastructure.Tests/Kit/DeployVpsTests.cs
git commit -m "fix(deploy): el marcador de último deploy es por VPS_HOST; un VPS nuevo recibe install.sh"
```

---

### Task 3: `restaurar-backup.sh`: precondiciones y restauración transaccional

**Files:**
- Create: `deploy/restaurar-backup.sh` (0755)
- Create: `tests/StockApp.Infrastructure.Tests/Kit/RestaurarBackupTests.cs`
- Create: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs`

**Interfaces:**
- Produces (CLI): `deploy/restaurar-backup.sh <dump> [--manifiesto <archivo>] [--contenedor <nombre>] [--base <nombre>] [--usuario <rol>] [--env <archivo>] [--recrear-base <nombre>] [--dir-seguridad <dir>] [--verificar-post-arranque]`. Defaults: `--contenedor stockapp-pg`, `--env ~/stockapp-deploy/.env` (solo se lee si falta `--base` o `--usuario`, y se LEE con `sed -n 's/^POSTGRES_DB=//p'`, nunca con `source`), `--dir-seguridad ~/stockapp-deploy/migracion`. En esta task: `--manifiesto`, `--recrear-base` y `--verificar-post-arranque` se parsean pero responden exit 2 "todavía no soportado" (las Tasks 4 y 5 los implementan; nunca se ignoran en silencio).
- Produces (helpers internos que las Tasks 4-5 reutilizan): `consultar <base> <sql>` (SQL por stdin a `docker exec -i "$CONTENEDOR" psql -U "$USUARIO" -d <base> -tA -v ON_ERROR_STOP=1`; deja salida en `$SALIDA_CONSULTA`; ante exit ≠ 0 o stderr no vacío sale con 8 nombrando qué quería consultar) y `listar_conteos <base>` (corre la consulta de conteos entre marcadores).
- Produces (tests): en `RestaurarBackupTests`, `[Collection("Postgres")]`, ctor con `PostgresFixture`; clase anidada `ContenedorPg : IDisposable` (`Nombre = "stockapp-test-" + Guid.NewGuid().ToString("N")[..12]`, `docker run -d --name … -e POSTGRES_USER=stockapp -e POSTGRES_PASSWORD=test -e POSTGRES_DB=stockapp postgres:16-alpine`, espera `pg_isready` con `EsperaMonotonica.HastaAsync` hasta 60 s, `Psql(string sql, string base = "stockapp")`, `Snapshot()` = salida de la consulta de conteos, `Dispose` → `docker rm -f -v <Nombre>`); `Task<DumpReal> DumpEsquemaReal()` con `sealed record DumpReal(string Ruta, IReadOnlyDictionary<string, long> Conteos, string Sha256, long Tamano, string UltimaMigracion)` (una vez por clase detrás de un `SemaphoreSlim` estático: `EjecutorPgDumpProceso` sobre `_fixture.ConnectionString`; los conteos se leen INMEDIATAMENTE después con la SQL extraída de entre los marcadores del script, vía Npgsql contra la fixture, para que la Task 4 arme manifiestos); testbin con `systemctl` falso (`STUB_SYSTEMCTL_ACTIVO=1` → `is-active` imprime `active` y sale 0; si no, `inactive` y 3; rastro en `RASTRO_SYSTEMCTL`) y `docker` envoltorio (registra args en `RASTRO_DOCKER` y delega en `$DOCKER_REAL`, resuelto con `command -v docker` ANTES de anteponer el testbin; con `STUB_DOCKER_FALLAR_PSQL=1` y `psql` entre los args → stderr `psql: error: simulado`, exit 2; con `STUB_DOCKER_FALLAR_PGDUMP=1` y `pg_dump` → igual); `Correr(string args, IDictionary<string,string>? env = null)` que corre el script REAL del repo con `HOME=<temp propio>`.
- Produces: `MigracionVpsSincroniaTests.ScriptsNuevos_SinHostsNiIpsCableados` y `ScriptsNuevos_SinComandosGlobalesProhibidos`, con una lista `ScriptsNuevos` que esta task inicia con `deploy/restaurar-backup.sh` y cada task siguiente amplía. Hosts: ninguna IPv4 salvo `127.0.0.1`/`0.0.0.0` (`\b(?:\d{1,3}\.){3}\d{1,3}\b`) y ningún dominio `\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(dev|com|net|org|uy|ar|io|app)\b`. Prohibidos: la misma lista de `DeployVpsTests.ElScript_NuncaContieneUnComandoDeAlcanceGlobalProhibido` (L980-L1001) más `nginx`, `certbot` y `rm -rf "$`.

- [ ] **Step 1: Tests en rojo**

- `ContenedorFueraDePatron_Aborta3SinLlamarADocker` (`--contenedor postgres-otro`): exit 3, `RASTRO_DOCKER` vacío.
- `NombreDeBaseInvalido_Aborta2` (`--base 'x;drop'`): exit 2, `RASTRO_DOCKER` vacío.
- `ApiActiva_Aborta3SinTocarElContenedor` (`STUB_SYSTEMCTL_ACTIVO=1`): exit 3, stderr contiene `stockapp-api está activa`, `RASTRO_DOCKER` vacío.
- `ContenedorDetenido_Aborta3` (`docker stop` del contenedor del test antes de correr): exit 3.
- `BaseNoVacia_SinRecrear_Aborta3YNoTocaNada`: el test crea `CREATE TABLE "LogsAuditoria"(id int); INSERT … 3 filas` → exit 3, stderr contiene `LogsAuditoria: 3` y `__EFMigrationsHistory: no existe`; `Snapshot()` antes == después.
- `ConsultaQueFalla_NoSeInterpretaComoVacia_Aborta8` (`STUB_DOCKER_FALLAR_PSQL=1`): exit 8, stderr contiene `NO es 'base vacía'`, no hubo `pg_restore` en el rastro.
- `BaseVacia_SinManifiesto_Restaura0`: exit 0; los conteos de la base restaurada == los de `DumpEsquemaReal()`; stdout contiene `API NO arrancada`.
- `SinEnvPorDefecto_ConBaseYUsuarioExplicitos_NoLoNecesita` (Review Focus 5): `HOME` sin `~/stockapp-deploy/.env`, `--base stockapp --usuario stockapp` → exit 0.
- `SinEnvYSinBase_Aborta2NombrandoElEnv`: sin `--base` ni env → exit 2, stderr nombra la ruta del env.
- `DumpCorruptoEnElEncabezado_Aborta4YLaBaseSigueVacia`: los primeros 64 bytes del dump real → exit 4, base vacía.
- `FalloAMitadDeRestauracion_LaBaseQuedaVacia5`: dump propio armado en el contenedor del test (base `origen` con `CREATE TABLE a(id int generated by default as identity primary key, t text)` + 1 fila de `(SELECT string_agg(md5(random()::text), '') FROM generate_series(1, 40000))` (~1,3 MB poco comprimible, sin extensiones), y `CREATE TABLE z(id int)`), `pg_dump -Fc` dentro del contenedor y `docker cp` afuera; se trunca al 60 % del tamaño; el test PRIMERO afirma que `pg_restore --list` sobre el truncado sale 0 (si no, el test no prueba lo que dice); restaurar → exit 5 y la base `stockapp` sin ninguna tabla en `public`.
- `ArchivoTemporalDelContenedor_SeBorraSiempre`: tras el caso feliz Y tras el de exit 5, `docker exec <c> sh -c 'ls /tmp'` no lista `restaurar-*`.

`MigracionVpsSincroniaTests`: los dos guardianes de arriba.

- [ ] **Step 2: Ver el rojo**

Run: `--filter "FullyQualifiedName~RestaurarBackupTests|FullyQualifiedName~MigracionVpsSincroniaTests"`. Expected: FAIL (script inexistente). Pegar.

- [ ] **Step 3: Implementar los pasos 1, 2, 4, 5, 6 (sin `--recrear-base`), 7 y 11 de la spec §5.3**

Orden: parseo y validación de nombres → paso 1 (`systemctl is-active --quiet stockapp-api` = 0 → exit 3; si `systemctl` no existe, "inactiva" con aviso) → paso 2 (`docker inspect -f '{{.State.Running}}'` = `true`, `pg_isready` dentro) → paso 6 (vacía = 0 relaciones fuera de `pg_catalog`/`information_schema`/`pg_toast%` y 0 schemas distintos de `public` y de los del sistema) → paso 4 (`docker cp` a `/tmp/restaurar-<$RANDOM$RANDOM>.dump` y `trap` EXIT que hace `docker exec "$CONTENEDOR" rm -f <ruta>`) → paso 5 (`pg_restore --list` dentro del contenedor; falla → 4) → paso 7 (`pg_restore --no-owner --no-privileges --single-transaction --exit-on-error -U … -d …`; falla → 5, mensaje "la base quedó vacía, se puede reintentar tal cual") → resumen. La consulta de conteos va entre los marcadores del contrato. Encabezado: uso, tabla de códigos de salida, "nunca arranca la API".

- [ ] **Step 4: Verde y mutaciones**

Run el filtro del Step 2. Expected: PASS. Mutaciones (pegar cada rojo y restaurar):
- quitar `--single-transaction` → `FalloAMitadDeRestauracion_LaBaseQuedaVacia5` en rojo (quedan tablas a medias);
- quitar el chequeo de base vacía → `BaseNoVacia_SinRecrear_Aborta3YNoTocaNada` en rojo;
- quitar la captura de exit code/stderr de `consultar` → `ConsultaQueFalla_NoSeInterpretaComoVacia_Aborta8` en rojo.

- [ ] **Step 5: Commit**

```bash
git add deploy/restaurar-backup.sh tests/StockApp.Infrastructure.Tests/Kit/RestaurarBackupTests.cs tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "feat(deploy): restaurar-backup.sh restaura un dump en base vacía, transaccional y sin pisar datos"
```

---

### Task 4: `restaurar-backup.sh`: manifiesto, conteos, secuencias y catálogos

**Files:**
- Modify: `deploy/restaurar-backup.sh`
- Modify: `deploy/deploy-vps.sh:192-199` (solo el comentario: nombrar la tercera copia del array)
- Test: `tests/StockApp.Infrastructure.Tests/Kit/RestaurarBackupTests.cs`, `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs`

**Interfaces:**
- Consumes: `consultar`, `listar_conteos`, `DumpEsquemaReal()`, `ContenedorPg` (Task 3); formato del manifiesto y códigos (Contratos).
- Produces: helper de test `EscribirManifiesto(string ruta, DumpReal d, Action<Dictionary<string,long>>? alterarConteos = null, string? ultimaMigracion = null, string? sha = null, long? tamano = null)`, que la Task 5 reutiliza. En el script, `TABLAS_CON_NOMBRE_UNICO` (los 7 de `deploy-vps.sh:196-199`) con comentario cruzado a `NormalizacionCatalogosSql.cs` y a `deploy-vps.sh`.

- [ ] **Step 1: Tests en rojo**

- `ManifiestoCorrecto_Restaura0ConConteosIdenticos`: exit 0, stdout lista tabla por tabla `OK`.
- `ManifiestoConConteoAlterado_Falla6YLaBaseQuedaRestaurada`: `Categorias` +1 en el manifiesto → exit 6, stderr contiene `Categorias` con esperado y real; la base tiene tablas (no se revirtió).
- `ManifiestoConMigracionDistinta_Falla6`.
- `ShaDistinto_Aborta4AntesDelDockerCp`: `RASTRO_DOCKER` no contiene ` cp `; base vacía.
- `TamanoDistinto_Aborta4`.
- `TablasDelListNoCoincidenConElManifiesto_Aborta4`: manifiesto con una tabla extra `Fantasma\t0` → exit 4 antes de `pg_restore`.
- `ManifiestoMalFormado_Aborta2` (sin línea `[conteos]`).
- `SecuenciaAtrasada_Falla6`: en el contenedor del test se crea la base `origen`, se le restaura `DumpEsquemaReal()` con `pg_restore` dentro del contenedor, se inserta una `Categoria`, `SELECT setval(pg_get_serial_sequence('"Categorias"','Id'), (SELECT max("Id") FROM "Categorias") - 1)`, `pg_dump -Fc` de `origen` → restaurar a `stockapp` sin manifiesto → exit 6, stderr nombra `Categorias.Id`.
- `DuplicadosEnCatalogo_Falla7ConRemediacionSinEjecutarla`: igual que el anterior pero `DROP INDEX "IX_Categorias_Nombre"` e inserción de `Tornillos` y `tornillos` → exit 7, stderr contiene `UPDATE "Categorias"` y `NO se ejecuta`; en la base restaurada siguen las 2 filas con esos nombres.
- `MigracionVpsSincroniaTests.CatalogosConNombreUnico_IgualesEnCSharpDeployVpsYRestaurar`: el array de `NormalizacionCatalogosSql.GuardiaColisionesDeNombre` (regex sobre `ARRAY[ … ]`), el de `deploy-vps.sh` y el de `restaurar-backup.sh` son el mismo conjunto de 7.

- [ ] **Step 2: Ver el rojo**

Run: `--filter "FullyQualifiedName~RestaurarBackupTests|FullyQualifiedName~MigracionVpsSincroniaTests"`. Expected: FAIL en los nuevos (exit 2 "todavía no soportado" o exit 0 donde se espera 6/7). Pegar.

- [ ] **Step 3: Implementar los pasos 3, 5 (tablas vs. manifiesto), 8, 9 y 10 de la spec §5.3**

Paso 3 (sha256 y tamaño locales vs. manifiesto) va ANTES del `docker cp`. Paso 5: las tablas con entrada `TABLE DATA public <tabla>` del `--list` deben ser el conjunto de tablas del manifiesto (las tablas sin filas también tienen `TABLE DATA`; verificarlo con el dump real en el Step 1 antes de asumirlo). Paso 9, secuencias (algoritmo fijo; "próximo valor > max"):
```sql
WITH cols AS (
  SELECT table_name, column_name,
         pg_get_serial_sequence(format('%I.%I', table_schema, table_name), column_name) AS seq
  FROM information_schema.columns WHERE table_schema = 'public')
SELECT table_name, column_name,
  (xpath('/row/v/text()', query_to_xml(format('SELECT coalesce(max(%I),0) AS v FROM public.%I', column_name, table_name), false, true, '')))[1]::text::bigint,
  (xpath('/row/v/text()', query_to_xml(format('SELECT CASE WHEN is_called THEN last_value ELSE last_value - 1 END AS v FROM %s', seq), false, true, '')))[1]::text::bigint
FROM cols WHERE seq IS NOT NULL ORDER BY 1, 2;
```
Falla si la última columna < la anterior. Paso 10: la consulta por tabla de `deploy-vps.sh:264-268` y su remediación (L320-L328), sin ejecutarla. El paso 10 corre SIEMPRE (con o sin manifiesto).

- [ ] **Step 4: Verde y mutaciones**

Run el filtro del Step 2. Expected: PASS. Mutaciones:
- quitar la comparación de conteos → `ManifiestoConConteoAlterado_Falla6YLaBaseQuedaRestaurada` en rojo;
- quitar el paso 3 → `ShaDistinto_Aborta4AntesDelDockerCp` en rojo;
- quitar el paso 9 → `SecuenciaAtrasada_Falla6` en rojo;
- quitar `Categorias` del array del script → `CatalogosConNombreUnico_*` y `DuplicadosEnCatalogo_*` en rojo.

- [ ] **Step 5: Commit**

```bash
git add deploy/restaurar-backup.sh deploy/deploy-vps.sh tests/StockApp.Infrastructure.Tests/Kit/RestaurarBackupTests.cs tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "feat(deploy): restaurar-backup.sh verifica manifiesto, conteos, secuencias y catálogos antes de dejar la base"
```

---

### Task 5: `restaurar-backup.sh`: `--recrear-base` y `--verificar-post-arranque`

**Files:**
- Modify: `deploy/restaurar-backup.sh`
- Test: `tests/StockApp.Infrastructure.Tests/Kit/RestaurarBackupTests.cs`

**Interfaces:**
- Consumes: todo lo de las Tasks 3-4.
- Produces: `--recrear-base <nombre>` (debe ser igual a `--base`, si no exit 2 sin tocar nada): dump de seguridad `docker exec … sh -c 'umask 077; pg_dump -U <u> -d <base> --format=custom -f /tmp/seguridad-<rnd>.dump'`, `pg_restore --list` de ese archivo dentro del contenedor, `docker cp` a `<dir-seguridad>/pre-recrear-<base>-<AAAAMMDDTHHMMSSZ>.dump` (dir 700, archivo 600, tamaño > 0); cualquier fallo → exit 9 y NO se borra nada; recién después `DROP DATABASE "<base>"` (conectado a `postgres`; si falla por conexiones abiertas → exit 3, sin `WITH (FORCE)`) y `CREATE DATABASE "<base>" OWNER "<usuario>"`, y sigue el flujo normal. `--verificar-post-arranque --manifiesto <m>`: solo lectura, no mira la API ni si la base está vacía; por cada tabla del manifiesto: debe existir; `LogsAuditoria` y `__EFMigrationsHistory` ≥ manifiesto; `CorridasBackup` cualquier valor (informa el delta); el resto == manifiesto; tablas que no están en el manifiesto se listan como "nuevas (migraciones posteriores)" sin fallar; `ultima_migracion` real ≥ la del manifiesto (comparación lexicográfica de `MigrationId`). Algún incumplimiento → exit 6 con el diff.

- [ ] **Step 1: Tests en rojo**

- `RecrearBase_NombreDistinto_Aborta2SinTocarNada` (`--base stockapp --recrear-base otra`): `Snapshot()` igual, sin archivos en `--dir-seguridad`.
- `RecrearBase_TomaDumpDeSeguridadRestaurableYRestaura`: la base tiene `CREATE TABLE previo(id int)` con 3 filas → exit 0 con manifiesto; existe exactamente un `pre-recrear-stockapp-*.dump` con modo 600; restaurado (con `pg_restore` dentro del contenedor) en una base nueva `verif` devuelve 3 filas en `previo`; la base `stockapp` tiene los conteos del manifiesto y no tiene `previo`.
- `RecrearBase_FallaElDumpDeSeguridad_NoBorraNada9` (`STUB_DOCKER_FALLAR_PGDUMP=1`): exit 9, `previo` sigue con 3 filas.
- `RecrearBase_ConConexionAbierta_Aborta3YLaBaseQuedaIntacta`: `docker exec -d <c> psql -U stockapp -d stockapp -c 'SELECT pg_sleep(120)'` antes → exit 3, `previo` intacto; el test termina esa sesión con `pg_terminate_backend` en `finally`.
- `ExitoSeguidoDeReintento_AbortaPorBaseNoVacia`: segunda corrida idéntica → exit 3.
- `PostArranque_SoloCreceEnTablasPermitidas_Ok`: tras restaurar, el test inserta 1 fila en `LogsAuditoria` (con las columnas obligatorias de la entidad `LogAuditoria`, verificadas con CodeGraph) → `--verificar-post-arranque` exit 0.
- `PostArranque_TablaComunConMasFilas_Falla6`: inserta 1 `Categoria` → exit 6 nombrando `Categorias`.
- `PostArranque_LogsAuditoriaMenor_Falla6`: manifiesto con `LogsAuditoria` = real + 1 → exit 6.
- `PostArranque_CorridasBackupMenor_Ok` (Review Focus 4): manifiesto con `CorridasBackup` = real + 2 → exit 0 y stdout informa el delta.
- `PostArranque_TablaNuevaNoEnManifiesto_OkYLaLista`: manifiesto sin una tabla real → exit 0, stdout la nombra como nueva.
- `PostArranque_SinManifiesto_Aborta2`.

- [ ] **Step 2: Ver el rojo**

Run: `--filter "FullyQualifiedName~RestaurarBackupTests.RecrearBase|FullyQualifiedName~RestaurarBackupTests.PostArranque|FullyQualifiedName~RestaurarBackupTests.ExitoSeguido"`. Expected: FAIL (exit 2 "todavía no soportado"). Pegar.

- [ ] **Step 3: Implementar** según la Interfaces de arriba; el encabezado documenta la idempotencia de la spec §5.3.

- [ ] **Step 4: Verde y mutaciones**

Run: `--filter FullyQualifiedName~RestaurarBackupTests`. Expected: PASS. Mutaciones:
- quitar la llamada al dump de seguridad → `RecrearBase_TomaDumpDeSeguridadRestaurableYRestaura` en rojo;
- quitar la regla "≥ manifiesto" de `LogsAuditoria` → `PostArranque_LogsAuditoriaMenor_Falla6` en rojo;
- quitar la igualdad de `--recrear-base` con `--base` → `RecrearBase_NombreDistinto_*` en rojo.

- [ ] **Step 5: Commit**

```bash
git add deploy/restaurar-backup.sh tests/StockApp.Infrastructure.Tests/Kit/RestaurarBackupTests.cs
git commit -m "feat(deploy): restaurar-backup.sh recrea la base solo tras un dump de seguridad y verifica conteos post-arranque"
```

---

### Task 6: `volcado-final-vps.sh`

**Files:**
- Create: `deploy/volcado-final-vps.sh` (0755)
- Create: `tests/StockApp.Infrastructure.Tests/Kit/VolcadoFinalVpsTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs`

**Interfaces:**
- Consumes: bloque de `STOCKAPP_ENV_LOCAL` (Task 1); `ssh_vps`/`scp_vps` copiados de `deploy-vps.sh:99-105`; exigencia de `VPS_HOST`/`VPS_USER`/`VPS_SSH_PORT` sin defaults (L145-L147); `POSTGRES_USER`/`POSTGRES_DB` con default `stockapp`; formato del manifiesto y SQL de conteos (Contratos).
- Produces (CLI): `deploy/volcado-final-vps.sh --salida <dir> [--ensayo] [--contenedor <nombre>] [--raiz-remota <dir>]`. `--contenedor` (default `stockapp-pg`, patrón `^stockapp[a-z0-9-]*$`) y `--raiz-remota` (default vacío; prefijo de las rutas absolutas remotas `/var/lib/stockapp/...` y `/etc/stockapp-api/api.env`; si se da: `^/[A-Za-z0-9._/-]+$` y sin `..`) existen SOLO para el ensayo local (Task 8); el corte usa los defaults. Salida en `<dir>` (700): `volcado-<ts>.dump` (600), `volcado-<ts>.manifiesto` (600), `backups-historial.tar` (600), `overrides-api-env.txt` (solo nombres, 600), con `<ts>` = `date -u +%Y%m%dT%H%M%SZ` local.
- Pasos (spec §5.4, en este orden): validar argumentos → cargar `.env` → `ssh_vps "sudo -n true"` (falla → 3) → paso 1 relevamiento: `systemctl is-active stockapp-api` (vacío = "no pude consultar" → 8), `systemctl is-enabled stockapp-api`, `docker inspect` del contenedor (no corre → 3), `docker ps --format '{{.Names}}'` guardado → paso 2: `sudo systemctl stop stockapp-api`, en modo final `sudo systemctl mask stockapp-api`, `is-active` debe dar `inactive` (otro valor → 3; vacío → 8), `pg_stat_activity` = 0 (otro número → 3; no numérico/fallo → 8) → paso 3 dump dentro del contenedor (`sh -c 'umask 077; pg_dump -U … -d … --format=custom -f /tmp/volcado-<ts>.dump'`), sha256 y `stat -c %s` dentro del contenedor → paso 4 repite `pg_stat_activity`, arma el manifiesto (fecha, `hostname`, `version()`, `pg_dump --version`, base, sha, tamaño, última `MigrationId`, conteos) → paso 5 `ssh_vps "docker exec <c> cat /tmp/volcado-<ts>.dump" > <salida>/volcado-<ts>.dump`; `sha256sum` local == remoto y tamaño ≥ 1024, si no → borra el archivo local y exit 4 → paso 6 `ssh_vps "sudo tar -C <raiz>/var/lib/stockapp/.local/share/StockApp -cpf - backups" > <salida>/backups-historial.tar` → paso 7 nombres de `<raiz>/etc/stockapp-api/api.env` extraídos DEL LADO REMOTO (`sudo sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p'`: solo nombres cruzan la red), filtrados contra `VARS_CONOCIDAS` de `install.sh:104-106` MÁS las claves fijas que `install.sh:455-469` siempre escribe (`Jwt__Secret`, `Bootstrap__AdminUser`, `Bootstrap__Password`, `Licencia__ClavePublicaBase64`, `Kestrel__Certificates__Default__Path`, `Kestrel__Certificates__Default__KeyPath`, `ConnectionStrings__Default`) → paso 9 `docker ps` final igual al inicial (si no → 9). `trap` EXIT: `docker exec <c> rm -f /tmp/volcado-<ts>.dump`; con `--ensayo`, además `sudo systemctl start stockapp-api` SOLO si el relevamiento la vio `active`. Sin `--ensayo` nunca hay `start` ni `unmask`.

- [ ] **Step 1: Tests en rojo**

Stub `ssh` con rastro y dispatch por contenido (patrón `DeployVpsTests.SshFalso`), con el estado de la unit en un archivo (`stop` → `inactive`, `mask` → `is-enabled` da `masked`, `start` → `active`), y variables `STUB_ACTIVA_TRAS_STOP`, `STUB_CONEXIONES` (default `0`; `x` simula salida basura), `STUB_FALLAR_CONEXIONES`, `STUB_SHA_REMOTO` (default: sha real de los 2048 bytes que devuelve el `cat`), `STUB_TAMANO_DUMP`, `STUB_ESTADO_INICIAL` (`active`/`inactive`/`masked`), `STUB_DOCKER_PS_FINAL_DISTINTO`. `tar` devuelve un tar real de un directorio temporal con un `backup_20261001_000000000.dump`; el `sed` de `api.env` devuelve `Jwt__Secret`, `ConnectionStrings__Default`, `RateLimiting__Login__PermitLimit`, `POSTGRES_USER`. `scp` no se usa. Tests:
- `ModoFinal_StopYMaskAntesDelPgDump`: en el rastro, `systemctl stop stockapp-api` y `systemctl mask stockapp-api` aparecen antes de la línea con `pg_dump`; exit 0.
- `ApiSigueActivaTrasStop_Aborta3SinDump`, `ConexionesAbiertas_Aborta3SinDump` (`STUB_CONEXIONES=2`), `ConsultaDeConexionesFalla_Aborta8NoLaTomaComoCero`, `ConexionesNoNumerico_Aborta8`: ninguna línea con `pg_dump` en el rastro.
- `ShaLocalDistinto_Aborta4YBorraElArchivoLocal`: no queda `volcado-*.dump` en `--salida` y stdout no contiene `OK`.
- `DumpChico_Aborta4` (`STUB_TAMANO_DUMP=100`).
- `Ensayo_ApiEstabaActiva_ElTrapLaArrancaAunqueFalleAMitad` (`--ensayo` + sha distinto): exit 4 y la última línea `systemctl` del rastro es `start stockapp-api`.
- `Ensayo_ApiEstabaInactiva_ElTrapNoLaArranca` (Review Focus 3, `STUB_ESTADO_INICIAL=inactive`): ningún `start`.
- `ModoFinal_NuncaStartNiUnmask` (camino feliz y camino con sha distinto).
- `ModoFinal_Reintento_ConApiYaEnmascarada_Funciona` (Review Focus 2, `STUB_ESTADO_INICIAL=masked`): exit 0, sin `unmask`/`start`.
- `TodoSystemctlNombraStockappApi_YTodoDockerNombraElContenedor`: cada línea con `systemctl` contiene `stockapp-api`; cada `docker exec`/`docker inspect` contiene el contenedor; el único `docker ps` es `--format '{{.Names}}'`.
- `ElDumpNuncaTocaUnaRutaDelHostRemoto`: toda línea del rastro que contiene `/tmp/` contiene `docker exec`.
- `ArchivoTemporalDelContenedor_SeBorraEnElTrap`: hay `rm -f /tmp/volcado-` dentro de `docker exec` en el feliz y en el de sha distinto.
- `Overrides_ListaSoloNombresFueraDeLosConocidos`: `overrides-api-env.txt` == `RateLimiting__Login__PermitLimit`, sin `=`.
- `Manifiesto_TieneLasClavesYLosConteos`: claves del contrato presentes, `sha256` == sha del dump local, conteos == los del stub.
- `Salida_Permisos700Y600`.
- `DockerPsDistintoAlFinal_Aborta9`.
- `ContenedorFueraDePatron_Aborta3SinSsh`, `RaizRemotaConPuntoPunto_Aborta2`, `SudoSinContrasena_Aborta3SinStop`, `SinVariablesDeConexion_Aborta3SinSsh` (`[Theory]` por variable), `StockappEnvLocalInexistente_AbortaSinTocarSsh`.
- `MigracionVpsSincroniaTests.SqlDeConteos_IdenticaEnVolcadoYRestaurar`; `ScriptsNuevos` suma `deploy/volcado-final-vps.sh`.

- [ ] **Step 2: Ver el rojo**

Run: `--filter "FullyQualifiedName~VolcadoFinalVpsTests|FullyQualifiedName~MigracionVpsSincroniaTests"`. Expected: FAIL. Pegar.

- [ ] **Step 3: Implementar** según la Interfaces; encabezado con uso, códigos, "VPS compartido", por qué `mask` (spec §5.4 paso 2) y por qué `--contenedor`/`--raiz-remota` existen.

- [ ] **Step 4: Verde y mutaciones**

Run el filtro del Step 2. Expected: PASS. Mutaciones (la spec pide "invertir el orden"; la convención es QUITAR):
- quitar el `systemctl stop` → `ModoFinal_StopYMaskAntesDelPgDump` en rojo;
- quitar el `trap` (dejar solo la limpieza explícita del final feliz) → `Ensayo_ApiEstabaActiva_ElTrapLaArrancaAunqueFalleAMitad` y `ArchivoTemporalDelContenedor_SeBorraEnElTrap` en rojo;
- quitar la comparación de sha → `ShaLocalDistinto_*` en rojo;
- quitar el chequeo de `pg_stat_activity` → `ConexionesAbiertas_Aborta3SinDump` en rojo;
- quitar la condición "solo si estaba `active`" del `start` → `Ensayo_ApiEstabaInactiva_ElTrapNoLaArranca` en rojo.

- [ ] **Step 5: Commit**

```bash
git add deploy/volcado-final-vps.sh tests/StockApp.Infrastructure.Tests/Kit/VolcadoFinalVpsTests.cs tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "feat(deploy): volcado-final-vps.sh congela la API, vuelca dentro del contenedor y trae dump y manifiesto verificados"
```

---

### Task 7: `bajar-backup-vps.sh` (backup existente, solo lectura)

**Files:**
- Create: `deploy/bajar-backup-vps.sh` (0755)
- Create: `tests/StockApp.Infrastructure.Tests/Kit/BajarBackupVpsTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs` (`ScriptsNuevos`)

**Interfaces:**
- Consumes: bloque `STOCKAPP_ENV_LOCAL` (Task 1); `ssh_vps`, `ssh_vps_autenticado`, `API_SCHEME`/`API_HOSTNAME`/`API_PORT`/`CURL_RESOLVE_REMOTO`/`API_URL_BASE` y `VPS_ADMIN_USER`/`VPS_ADMIN_PASSWORD` con la misma resolución que `deploy-vps.sh:149-190`; contrato de `GET /backups` (`CorridaBackupDto`: `id`, `finalizadaEn`, `resultado`, `nombreArchivo`, `tamanioBytes`, `motivoFallo`, camelCase).
- Produces (CLI): `deploy/bajar-backup-vps.sh --salida <dir> [--id <n>]`. Login (body por stdin, `curl --data @-`), `GET /backups` (exit code y stderr capturados; fallo → 8), imprime SIEMPRE la tabla `id finalizadaEn resultado nombreArchivo tamanioBytes`, elige `--id` o la `Exitosa` de mayor `finalizadaEn` (con `jq`), valida `nombreArchivo` contra `^backup_[0-9]{8}_[0-9]{9}\.dump$` (si no → 4), y baja `GET /backups/<id>/contenido` con `curl -fsS … -o -` POR STDOUT de `ssh` directo a `<salida>/<nombreArchivo>` (umask 077; dir 700). Verifica tamaño == `tamanioBytes` y ≥ 1024, y `pg_restore --list` local OK; si no, borra el archivo y exit 4. Escribe `<salida>/<nombreArchivo>.sha256`. NUNCA hace `POST`, `mktemp` ni escribe en el VPS.

- [ ] **Step 1: Tests en rojo** (stubs `ssh`, `pg_restore` y `jq` real)

- `CaminoFeliz_BajaLaUltimaExitosaPorStdout`: con dos `Exitosa` y una `Fallida`, baja la de mayor `finalizadaEn`; archivo 600, dir 700, `.sha256` correcto, exit 0.
- `ConId_BajaEsaCorrida`; `IdFallidaOInexistente_Aborta3`; `SinCorridasExitosas_Aborta3`.
- `NuncaHacePostNiMktempNiEscribeEnElVps`: ninguna línea del rastro contiene `-X POST /backups`, `mktemp`, ` -o '/` ni `/tmp/`.
- `SecretosNuncaEnArgv`: ni la contraseña ni `FAKE-TOKEN` aparecen en el rastro de argv.
- `ListadoFalla_Aborta8`; `LoginFalla_Aborta3`.
- `TamanoDistintoDelListado_Aborta4YBorra`; `PgRestoreListFalla_Aborta4YBorra`; `NombreArchivoInseguro_Aborta4` (`../x.dump`).
- `ApiSchemeHttps_UsaResolveSinInsecure` (como `DeployVpsTests.ApiSchemeHttps_*`).
- `StockappEnvLocalInexistente_AbortaSinTocarSsh`.

- [ ] **Step 2: Ver el rojo.** Run: `--filter "FullyQualifiedName~BajarBackupVpsTests|FullyQualifiedName~MigracionVpsSincroniaTests"`. Expected: FAIL. Pegar.

- [ ] **Step 3: Implementar.**

- [ ] **Step 4: Verde y mutaciones.** Run el filtro. Expected: PASS. Mutaciones: quitar la comparación de tamaño → `TamanoDistintoDelListado_*` en rojo; quitar el `-K -` (pasar el header por argv) → `SecretosNuncaEnArgv` en rojo.

- [ ] **Step 5: Commit**

```bash
git add deploy/bajar-backup-vps.sh tests/StockApp.Infrastructure.Tests/Kit/BajarBackupVpsTests.cs tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "feat(deploy): bajar-backup-vps.sh baja un backup existente por GET /backups sin escribir en el VPS"
```

---

### Task 8: Ensayo local: VPS viejo simulado

**Files:**
- Create: `deploy/ensayo/ensayo-local.sh` (0755), `deploy/ensayo/shims/ssh`, `deploy/ensayo/shims/sudo`, `deploy/ensayo/shims/systemctl` (0755)
- Create: `tests/StockApp.Infrastructure.Tests/Kit/EnsayoLocalTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs` (`ScriptsNuevos`)

**Interfaces:**
- Consumes: `restaurar-backup.sh` (Tasks 3-5), `volcado-final-vps.sh` (Task 6), `STOCKAPP_ENV_LOCAL`.
- Produces:
  - `shims/ssh`: ignora `-p N` y `-o X`; toma `user@host`; si `host` no termina en `.invalid` → stderr `ensayo: host no permitido` y exit 255 (nunca puede salir a un servidor real); une el resto de los argumentos con espacios (como el ssh real) y lo corre con `bash -c` con `PATH="<dir de shims>:$PATH"`, heredando stdin/stdout.
  - `shims/systemctl`: estado en `$ENSAYO_ESTADO/stockapp-api.{activo,habilitado}`; solo acepta la unit `stockapp-api` (otra → exit 5); `is-active` imprime el estado (exit 0 si `active`, 3 si no); `is-enabled` imprime `enabled`/`masked`; `stop` → `inactive`; `mask` → `masked`; `unmask` → `enabled`; `start` sobre `masked` → stderr `Unit stockapp-api.service is masked.` y exit 1.
  - `shims/sudo`: descarta `-n`; `sudo systemctl …` → el shim de `systemctl`; cualquier otro comando se ejecuta tal cual SIN elevar privilegios.
  - Opciones comunes de `preparar`: `--sufijo <s>` (`^[a-z0-9]{0,12}$`, default vacío; se agrega como `-<s>`/`_<s>` a los cuatro nombres de abajo, para que los tests no choquen con un ensayo en curso) y `--puerto <n>` (default `5434`). `preparar` los guarda en `<trabajo>/config` (600) y los demás subcomandos los leen de ahí.
  - `ensayo-local.sh preparar <dump-real> --trabajo <dir> [--historial <backup_*.dump>]…`: aborta si existe algún contenedor o volumen `stockapp-ensayo-origen-pg`, `stockapp-ensayo-pg`, `stockapp_ensayo_origen_pgdata`, `stockapp_ensayo_pgdata` (nunca reutiliza) o si el puerto 127.0.0.1:5434 está ocupado; guarda `docker ps --format '{{.Names}}'` en `<trabajo>/docker-ps-antes.txt`; crea `stockapp-ensayo-origen-pg` (sin puertos publicados) y `stockapp-ensayo-pg` (`-p 127.0.0.1:5434:5432`), ambos `postgres:16-alpine` con `POSTGRES_USER=stockapp`, `POSTGRES_DB=stockapp` y contraseña aleatoria guardada en `<trabajo>/pg-password` (600); carga el dump real en el origen con `restaurar-backup.sh --contenedor stockapp-ensayo-origen-pg --base stockapp --usuario stockapp`; arma la raíz remota `<trabajo>/vps-viejo/` con `var/lib/stockapp/.local/share/StockApp/backups/` (los `--historial`) y `etc/stockapp-api/api.env` (`Jwt__Secret=ensayo`, `ConnectionStrings__Default=ensayo`, `RateLimiting__Login__PermitLimit=10`); estado inicial del shim `active`/`enabled`; escribe `<trabajo>/env-vps-viejo-ensayo` (`VPS_HOST=vps-viejo.ensayo.invalid`, `VPS_USER=ensayo`, `VPS_SSH_PORT=22`, `POSTGRES_USER=stockapp`, `POSTGRES_DB=stockapp`).
  - `ensayo-local.sh volcar --trabajo <dir>`: `PATH=<shims>:$PATH ENSAYO_ESTADO=<trabajo>/estado STOCKAPP_ENV_LOCAL=<trabajo>/env-vps-viejo-ensayo deploy/volcado-final-vps.sh --salida <trabajo>/salida --contenedor stockapp-ensayo-origen-pg --raiz-remota <trabajo>/vps-viejo` en modo FINAL (ejercita `mask`).
  - `ensayo-local.sh restaurar --trabajo <dir> [--recrear-base]`: `restaurar-backup.sh <trabajo>/salida/volcado-*.dump --manifiesto <…manifiesto> --contenedor stockapp-ensayo-pg --base stockapp --usuario stockapp --dir-seguridad <trabajo>/seguridad` (con `PATH` sin shims: `systemctl` real, la unit no existe, cuenta como inactiva).
  - `ensayo-local.sh limpiar --trabajo <dir>`: `docker rm -f -v` y `docker volume rm` de esos cuatro nombres EXACTOS; compara `docker ps` con `docker-ps-antes.txt` y sale ≠ 0 si difieren. Cada subcomando imprime su duración en segundos.

- [ ] **Step 1: Tests en rojo**

- `ShimSsh_HostNoInvalid_Rechaza255` y `ShimSsh_CorreElComandoConLosShimsEnElPath` (`ssh -p 22 -o BatchMode=yes e@x.ensayo.invalid 'sudo -n true && systemctl is-active stockapp-api'` → `active`).
- `ShimSystemctl_OtraUnit_Rechaza5`; `ShimSystemctl_StopMaskYStartSobreMasked_Falla`.
- `ShimSudo_NoEleva`: `sudo id -u` == `id -u`.
- `EnsayoLocal_SoloNombresDeEnsayo`: el texto de `ensayo-local.sh` no contiene `stockapp-pg` salvo como parte de `stockapp-ensayo-`, ni `pinar`; todo `docker rm`/`docker volume rm` nombra un literal `stockapp-ensayo-…`/`stockapp_ensayo_…`.
- `Preparar_ConContenedorDeEnsayoExistente_AbortaSinTocarlo`: el test crea `stockapp-ensayo-pg-<sufijo>` (`alpine` con `sleep`) → `preparar --sufijo <sufijo>` sale ≠ 0 y el contenedor sigue con el mismo ID; `finally` lo borra por nombre.
- `EnsayoCompleto_ConDumpDeEsquemaReal_TerminaLimpio` (`[Collection("Postgres")]`; dump con `EjecutorPgDumpProceso` sobre `PostgresFixture`, como `RestaurabilidadBackupTests.cs:69-80`; `--sufijo <guid8> --puerto <libre>`): `preparar`, `volcar`, `restaurar` y `limpiar` salen 0; `estado/stockapp-api.habilitado` == `masked`; `overrides-api-env.txt` == `RateLimiting__Login__PermitLimit`; tras `limpiar`, ningún contenedor ni volumen con el sufijo y `docker ps` igual al de antes. `finally` hace `limpiar` igual.
- `ScriptsNuevos` suma `deploy/ensayo/ensayo-local.sh` y los tres shims.

- [ ] **Step 2: Ver el rojo.** Run: `--filter "FullyQualifiedName~EnsayoLocalTests|FullyQualifiedName~MigracionVpsSincroniaTests"`. Expected: FAIL. Pegar.

- [ ] **Step 3: Implementar.**

- [ ] **Step 4: Verde y mutación.** Run el filtro. Expected: PASS. Mutación: quitar el chequeo `.invalid` del shim → `ShimSsh_HostNoInvalid_Rechaza255` en rojo.

- [ ] **Step 5: Commit**

```bash
git add deploy/ensayo tests/StockApp.Infrastructure.Tests/Kit/EnsayoLocalTests.cs tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "test(deploy): ensayo local de la migración con el VPS viejo simulado por shims y contenedores propios"
```

---

### Task 9: Deploy-hook de TLS versionado

**Files:**
- Create: `deploy/tls/stockapp-tls-deploy-hook.sh` (0755)
- Create: `tests/StockApp.Infrastructure.Tests/Kit/TlsDeployHookTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs` (`ScriptsNuevos`)

**Interfaces:**
- Produces: hook que certbot llama con `RENEWED_LINEAGE` (dir con `fullchain.pem` y `privkey.pem`): `install -o stockapp -g stockapp -m 0600` de los dos a `/var/lib/stockapp/tls/` (creando el dir 0750 `stockapp:stockapp`) y `systemctl restart stockapp-api`. `RENEWED_LINEAGE` vacía, inexistente o sin los dos PEM → exit 2 sin copiar ni reiniciar. Sin dominio cableado: todo sale de `RENEWED_LINEAGE`.

- [ ] **Step 1: Traer el hook vigente (solo lectura del VPS viejo)**

```bash
( set -a; source /home/capua25/workspace/stockapp/deploy/.env; set +a
  ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" "sudo -n cat /usr/local/sbin/stockapp-tls-deploy-hook.sh" ) > "$SCRATCH/hook-vps-viejo.sh"; echo "exit=$?"
```
Expected: `exit=0` y archivo no vacío. Exit ≠ 0 o archivo vacío es "no pude leerlo", no "no existe": el agente se DETIENE y le pide a Martin `VPS_USER` o acceso. Leer el archivo completo y pegar en el ledger las diferencias de comportamiento con la Interfaces de arriba (si el viejo cablea el dominio, se reemplaza por `RENEWED_LINEAGE`; si hace algo más, se conserva solo si está acotado a StockApp y se anota).

- [ ] **Step 2: Tests en rojo** (contenedor `ubuntu:24.04` como root, patrón `InstallEsquemaTests.EjecutarInstall`: `useradd --system stockapp`, `systemctl` falso que registra en `/tmp/rastro-systemctl`, lineage falso en `/etc/letsencrypt/live/ensayo.invalid/`)
- `LineageValido_CopiaLosPemComo0600DeStockappYReinicia`: `stat -c '%U:%G %a'` de los dos == `stockapp:stockapp 600`, contenido idéntico, rastro == `restart stockapp-api`.
- `SinLineage_Aborta2SinCopiarNiReiniciar`; `LineageSinPrivkey_Aborta2SinCopiarNiReiniciar`.

- [ ] **Step 3: Ver el rojo.** Run: `--filter FullyQualifiedName~TlsDeployHookTests`. Expected: FAIL. Pegar.

- [ ] **Step 4: Implementar** (encabezado: origen, cómo se registra con `certbot certonly --standalone -d <dominio> --deploy-hook /usr/local/sbin/stockapp-tls-deploy-hook.sh`, y que `--dry-run` no ejecuta hooks).

- [ ] **Step 5: Verde y mutación.** Run el filtro. Expected: PASS. Mutación: quitar `-m 0600` → `LineageValido_*` en rojo.

- [ ] **Step 6: Commit**

```bash
git add deploy/tls/stockapp-tls-deploy-hook.sh tests/StockApp.Infrastructure.Tests/Kit/TlsDeployHookTests.cs tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "feat(deploy): versionar el deploy-hook de certbot que copia los certificados y reinicia stockapp-api"
```

---

### Task 10: Runbook `deploy/MIGRACION-VPS.md` y documentación

**Files:**
- Create: `deploy/MIGRACION-VPS.md`
- Modify: `deploy/PROCEDIMIENTOS.md:238-240` (paso 7 del Procedimiento 3 → `deploy/restaurar-backup.sh`, con el comando y un enlace al runbook)
- Modify: `deploy/DEPLOY.md` (sección 8, "Actualizar a una versión nueva", L580 en adelante: `STOCKAPP_ENV_LOCAL` y el marcador por host)
- Modify: `tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs`

**Interfaces:**
- Consumes: los CLI y códigos de las Tasks 1-9.
- Produces: runbook en el estilo de `deploy/DEPLOY.md`, con casilla, comando exacto, resultado esperado y qué hacer si no coincide en CADA paso: los tres `.env` (operador viejo = `deploy/.env`, operador nuevo = `deploy/.env.vps-nuevo` vía `STOCKAPP_ENV_LOCAL`, servidor nuevo = `~/stockapp-deploy/.env`); P1-P10 (spec §5.5), con P5.a ya resuelto por la Task 9 (instalar con `sudo install -m 0755 deploy/tls/stockapp-tls-deploy-hook.sh /usr/local/sbin/`); E1 (el procedimiento de la Task 11, con `bajar-backup-vps.sh` y `ensayo-local.sh`: E1 ya NO detiene testing); E2 (spec §5.9, incluido `volcado-final-vps.sh --ensayo` contra el VPS viejo, que sí detiene testing ~1 min); C0-C10 (C5 con `--recrear-base stockapp` y `--dir-seguridad ~/stockapp-deploy/migracion`; V6 con `restaurar-backup.sh … --verificar-post-arranque`); V1-V10 de la spec §7.2; R-A y R-B; post-corte. Una tabla "Tiempos" que la Task 11 llena para E1 y que E2 completa. Sección **"Lo que Martin tiene que cargar"**: en `deploy/.env` (existente, para el ensayo) `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`, `API_SCHEME`, `API_HOSTNAME`, `API_PORT` y `VPS_ADMIN_PASSWORD` (o un `BOOTSTRAP_PASSWORD` todavía válido) de un usuario con `GestionarDiagnostico`; en `deploy/.env.vps-nuevo` (nuevo, ignorado por `deploy/.env*` en `.gitignore:516`) lo de P7; en el servidor nuevo lo de P6. Ningún agente escribe esos archivos. Advertencia explícita: `deploy-vps.sh` sin `--sin-backup` usa el `/tmp` del host (L494) y por eso en la migración NUNCA se corre contra el VPS viejo.

- [ ] **Step 1: Tests en rojo**

- `Runbook_CubreCadaPasoDeLaSpec`: el texto contiene cada id `P1`…`P10`, `E1`, `E2`, `C0`…`C10`, `V1`…`V10`, `R-A`, `R-B` como encabezado o casilla, y los comandos `deploy/restaurar-backup.sh`, `deploy/volcado-final-vps.sh`, `deploy/bajar-backup-vps.sh`, `deploy/ensayo/ensayo-local.sh`, `--verificar-post-arranque`, `--recrear-base`, `STOCKAPP_ENV_LOCAL`, `systemctl mask stockapp-api`, `certbot renew --force-renewal`.
- `Runbook_NuncaUsaComandosGlobalesNiRmRfSobreVariables`: no contiene `docker volume rm` sin `stockapp_`, `docker compose down -v`, `rm -rf $`, `systemctl restart nginx`, `certbot delete` sin `--cert-name`.
- `ScriptsNuevos_SinHostsNiIpsCableados` se extiende al runbook (placeholders `<dominio-nuevo>`, `<ip-nueva>`).
- `Procedimientos_Paso7ApuntaARestaurarBackup`.

- [ ] **Step 2: Ver el rojo.** Run: `--filter FullyQualifiedName~MigracionVpsSincroniaTests`. Expected: FAIL. Pegar.

- [ ] **Step 3: Escribir el runbook y los cambios de docs.**

- [ ] **Step 4: Verde.** Run el filtro. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add deploy/MIGRACION-VPS.md deploy/PROCEDIMIENTOS.md deploy/DEPLOY.md tests/StockApp.Infrastructure.Tests/Kit/MigracionVpsSincroniaTests.cs
git commit -m "docs(deploy): runbook de la migración al VPS nuevo y restauración con restaurar-backup.sh"
```

---

### Task 11: Ensayo E1 con un backup real del VPS de testing y merge

Lo ejecuta un AGENTE, no un checklist para Martin. Contra el VPS de testing solo hay lecturas (login, `GET /backups`, `GET /backups/{id}/contenido`). Todo lo demás corre en contenedores `stockapp-ensayo-*` y una API local en `127.0.0.1:5199`.

**Files:**
- Modify: `deploy/MIGRACION-VPS.md` (tabla "Tiempos" y resultado de E1)

**Interfaces:**
- Consumes: todo lo anterior. `ENV_REAL=/home/capua25/workspace/stockapp/deploy/.env`, `TRABAJO=~/stockapp-backups-vps/ensayo-$(date -u +%Y%m%d)` (700, fuera del repo).

- [ ] **Step 1: Suite completa.** Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m`. Expected: línea base + los nuevos, verde salvo `.ods`. Pegar. Guardar `docker ps --format '{{.Names}}'` en `$TRABAJO/docker-ps-inicio.txt`.

- [ ] **Step 2: Backup real, solo lectura**

Run: `STOCKAPP_ENV_LOCAL=$ENV_REAL deploy/bajar-backup-vps.sh --salida "$TRABAJO/real"`. Expected: exit 0, tabla de corridas impresa, un `backup_*.dump` ≥ 1024 bytes. Después, con un `id` ANTERIOR de esa tabla: `STOCKAPP_ENV_LOCAL=$ENV_REAL deploy/bajar-backup-vps.sh --salida "$TRABAJO/historial" --id <id-anterior>` (su fila está dentro del dump más nuevo; sirve para V8). Exit 3 por login = el agente se DETIENE y le pide a Martin la credencial en `deploy/.env` (Task 10, "Lo que Martin tiene que cargar").

- [ ] **Step 3: Volcado y restauración**

Run, cronometrando: `deploy/ensayo/ensayo-local.sh preparar "$TRABAJO/real/backup_….dump" --trabajo "$TRABAJO/e1" --historial "$TRABAJO/historial/backup_….dump"`, luego `volcar`, luego `restaurar`. Expected: los tres con exit 0; `estado/stockapp-api.habilitado` = `masked`; `salida/overrides-api-env.txt` = `RateLimiting__Login__PermitLimit`; el resumen de `restaurar` con conteos idénticos al manifiesto, secuencias OK y sin duplicados (V1). Después `restaurar` otra vez SIN `--recrear-base` → exit 3; y `restaurar --recrear-base` → exit 0 con un `pre-recrear-stockapp-*.dump` en `$TRABAJO/e1/seguridad`.

- [ ] **Step 4: Historial (C6) y API local 0.5.3 contra la base restaurada**

`mkdir -p "$TRABAJO/e1/home-api/.local/share/StockApp" && tar -C "$TRABAJO/e1/home-api/.local/share/StockApp" -xpf "$TRABAJO/e1/salida/backups-historial.tar"`. Arrancar en background guardando el PID:
```bash
HOME="$TRABAJO/e1/home-api" ASPNETCORE_URLS=http://127.0.0.1:5199 \
ConnectionStrings__Default="Host=127.0.0.1;Port=5434;Database=stockapp;Username=stockapp;Password=$(cat "$TRABAJO/e1/pg-password")" \
Licencia__Habilitado=false Jwt__Secret="$(openssl rand -base64 48)" Bootstrap__AdminUser=ensayo Bootstrap__Password=Ensayo12345 \
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --no-launch-profile > "$TRABAJO/e1/api.log" 2>&1 &
echo $! > "$TRABAJO/e1/api.pid"
```
Expected: `curl -fsS http://127.0.0.1:5199/` → `{"status":"ok","service":"StockApp.Api"}` en menos de 120 s; `api.log` sin excepciones de migración (V2 local).

- [ ] **Step 5: Verificación V4-V8 locales** (pegar cada salida; vacío o error de conexión = "no se pudo verificar", con exit code y stderr)

- V4: `curl -fsS http://127.0.0.1:5199/licencia/estado` → `"activada":true`.
- V5: login con el usuario REAL del dump, sin que la contraseña pase por argv ni por la salida:
  ```bash
  ( set -a; source "$ENV_REAL"; set +a
    printf '{"nombreUsuario":"%s","contrasena":"%s"}' "${VPS_ADMIN_USER:-${BOOTSTRAP_ADMIN_USER:-admin}}" "${VPS_ADMIN_PASSWORD:-${BOOTSTRAP_PASSWORD:-}}" \
    | curl -fsS -X POST http://127.0.0.1:5199/auth/login -H 'Content-Type: application/json' --data @- ) \
    | jq -r .token > "$TRABAJO/e1/token"; chmod 600 "$TRABAJO/e1/token"
  ```
  Expected: token no vacío.
- V8: con `printf 'header = "Authorization: Bearer %s"\n' "$(cat "$TRABAJO/e1/token")" | curl -fsS -K - …`: `GET /backups` lista la corrida del `--historial`; `GET /backups/<id-anterior>/contenido -o /dev/null -w '%{http_code}'` → `200`; `POST /backups` → 202 y, en ≤ 60 s, una corrida nueva `Exitosa` en `GET /backups`.
- V6: `deploy/restaurar-backup.sh "$TRABAJO/e1/salida/volcado-….dump" --manifiesto "$TRABAJO/e1/salida/volcado-….manifiesto" --contenedor stockapp-ensayo-pg --base stockapp --usuario stockapp --verificar-post-arranque` → exit 0 (crece `CorridasBackup`; `LogsAuditoria` ≥ manifiesto).

- [ ] **Step 6: Limpieza verificada**

`kill "$(cat "$TRABAJO/e1/api.pid")"`, y confirmar con `ps -p <pid>` que ya no existe (si `dotnet run` dejó un hijo `StockApp.Api`, matarlo por su PID exacto, obtenido con `ps --ppid <pid>`). `deploy/ensayo/ensayo-local.sh limpiar --trabajo "$TRABAJO/e1"` → exit 0. `docker ps --format '{{.Names}}'` == `docker-ps-inicio.txt` (incluido `pinar-postgres-1`). `rm -f "$TRABAJO/e1/token"`. Los dumps quedan en `$TRABAJO` (600/700). Pegar todo en el ledger.

- [ ] **Step 7: Resultado en el runbook, commit, merge y push**

Anotar en la tabla "Tiempos" de `deploy/MIGRACION-VPS.md` la fecha y la duración de `bajar`, `preparar`, `volcar`, `restaurar`, arranque de la API y verificación; y que E2 y el corte quedan PENDIENTES (requieren el VPS nuevo, el dominio y la fecha). Run: `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --blame-hang-timeout 5m --filter FullyQualifiedName~MigracionVpsSincroniaTests` → PASS.
```bash
git add deploy/MIGRACION-VPS.md
git commit -m "docs(deploy): resultado y tiempos del ensayo E1 de la migración con un backup real"
git -C /home/capua25/workspace/stockapp merge --ff-only feat/migracion-vps
git -C /home/capua25/workspace/stockapp push origin main
```
Si el `--ff-only` falla porque `main` avanzó, se rebasa la rama sobre `main` en el worktree, se repite el Step 1 y se reintenta; nunca un merge con commit.

---

## Hallazgos contra la spec (decididos en este plan, con evidencia)

1. **E1 de la spec detiene testing; el ensayo de este plan no.** La spec §5.9 E1.1 corre `volcado-final-vps.sh --ensayo` contra el VPS viejo (detiene la API ~1 min). El pedido es un ensayo de solo lectura sobre el VPS actual, así que E1 usa `deploy/bajar-backup-vps.sh` (nuevo, Task 7: `GET /backups` + `GET /backups/{id}/contenido`, nada de `POST`) y simula el VPS viejo en local (Task 8). El `volcado --ensayo` contra el VPS viejo pasa a E2. La descarga no reutiliza `deploy-vps.sh` paso 2 porque ese camino hace `POST /backups` (escribe) y deja el dump en el `/tmp` del host compartido (`deploy-vps.sh:494`, `mktemp /tmp/stockapp-backup-XXXXXX.bin`), que este plan prohíbe; el contenido viaja por el stdout de `ssh`.
2. **Overrides de `api.env` (spec §5.4 paso 7).** Comparar las claves de `api.env` contra `VARS_CONOCIDAS` (`install.sh:104-106`) listaría como "overrides" las que `install.sh` SIEMPRE escribe con otro nombre (`Jwt__Secret`, `Bootstrap__AdminUser`, `Bootstrap__Password`, `Licencia__ClavePublicaBase64`, `Kestrel__Certificates__*`, `ConnectionStrings__Default`, `install.sh:455-469`). El plan excluye las dos listas y extrae los nombres del lado remoto.
3. **V6 y `CorridasBackup` (spec §5.7 y §7.2 V6).** "Ninguna tabla puede tener menos filas" no vale para `CorridasBackup`: si la última corrida tiene ≥ 12 h, el catch-up de `BackupProgramadoService` corre al arrancar y `ServicioBackup.AplicarRetencionAsync` (L142-L156) borra filas por `PoliticaRetencion`. `--verificar-post-arranque` acepta cualquier valor ahí e informa el delta; `LogsAuditoria` sigue exigiendo ≥.
4. **Secuencias (spec §5.3 paso 9).** "`last_value` ≥ `max`" ignora `is_called`: una secuencia recién restaurada con `is_called = false` y `last_value = max` daría el próximo valor = `max` y chocaría. El plan compara el próximo valor real (Task 4).
5. **Parámetros que la spec no tiene y el ensayo necesita:** `--dir-seguridad` en `restaurar-backup.sh` (el default `~/stockapp-deploy/migracion` es el del servidor) y `--contenedor`/`--raiz-remota` en `volcado-final-vps.sh` (en esta máquina `stockapp-pg` es el contenedor de desarrollo y corre `pinar-postgres-1`; sin ellos el ensayo apuntaría a datos reales de otro uso). Defaults = los de la spec.
6. **`--ensayo` y el `trap` (spec §5.4 paso 8, "pase lo que pase").** Si la API de testing ya estaba caída, arrancarla cambia el estado del VPS viejo; el `trap` la arranca solo si el relevamiento la vio `active` (Review Focus 3).
7. **Mutación "invertir el orden" (spec §7.1, volcado).** Por convención se muta quitando: se quita el `systemctl stop` (Task 6 Step 4).
8. **`restaurar-backup.sh` es autocontenido** (C4 copia solo ese archivo al VPS nuevo): el array de catálogos queda triplicado (C#, `deploy-vps.sh`, `restaurar-backup.sh`) y la SQL de conteos duplicada, con `MigracionVpsSincroniaTests` como guardián (spec §6 lo permite).
