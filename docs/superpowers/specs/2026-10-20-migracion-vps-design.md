# StockApp — Migración de los datos de producción al VPS nuevo

**Fecha:** 2026-10-20
**Estado:** diseño para revisión de Martin. Las decisiones marcadas **(decidido por Martin)** ya están tomadas; las marcadas **(decisión del asistente)** son propuestas para que Martin las confirme o corrija antes de planificar.
**Alcance:** dos scripts nuevos en `deploy/` (`restaurar-backup.sh` y `volcado-final-vps.sh`), un runbook nuevo (`deploy/MIGRACION-VPS.md`), cambios chicos en `deploy/deploy-vps.sh` y `build/publicar-release.sh` (§5.6), la preparación del VPS nuevo, el ensayo y el corte.
**Fuera de alcance:** redirigir clientes, multi-tenant, rediseñar el licenciamiento, desmantelar el VPS viejo, el ambiente de testing posterior al corte (queda como pregunta abierta, §5.8).

## 1. Intención y porqué

Hoy la API de StockApp corre en un VPS compartido con otra aplicación, "pinar", y sirve en `stockapp.capuanomartin.dev:8080`. Ahí ya hay datos reales del municipio: lo que cargó Ramiro, con su auditoría y sus adjuntos. La producción se muda a un **VPS nuevo, en blanco, que todavía no existe (decidido por Martin, 2026-10-20)**, con un **dominio nuevo** (decidido por Martin): el dominio viejo no se conserva.

Hay que llevar esos datos de un servidor al otro sin perder una sola fila, y la auditoría menos que nada. Hoy eso no se puede hacer de forma segura, porque **en el repo no existe ningún camino de restauración**: no hay script, ni endpoint, ni runbook. Lo único que prueba que un dump se puede restaurar es un test de integración (`tests/StockApp.Infrastructure.Tests/Backups/RestaurabilidadBackupTests.cs`). `deploy/PROCEDIMIENTOS.md:238-239` dice "restaurar el backup más reciente disponible … con ayuda del proveedor", sin ningún paso concreto.

Hay un hecho que simplifica todo: **la instalación del municipio y la migración ocurren al mismo tiempo (decidido por Martin)**. Las PCs se instalan una sola vez, con el Configurador apuntando al dominio nuevo. Ningún cliente del municipio apunta nunca al servidor viejo, así que no hace falta redirigir a nadie. Y como el dominio es nuevo, **el DNS no está en el camino crítico del corte**: el registro se crea días antes, cuando ya se conoce la IP del VPS nuevo.

## 2. Objetivos

- O1. Los datos del VPS viejo llegan completos al VPS nuevo. Se prueba con el **conteo exacto de filas por tabla**: el que se toma en el origen con las escrituras congeladas tiene que coincidir con el de la base restaurada, antes de que la API nueva arranque.
- O2. Un script reutilizable, `deploy/restaurar-backup.sh`, restaura un dump de StockApp en una base vacía de forma segura: se niega a pisar datos, verifica el dump antes de tocar nada y verifica el resultado. Sirve para esta migración y para cualquier restauración futura, y cierra el hueco de `PROCEDIMIENTOS.md:238`.
- O3. Un script, `deploy/volcado-final-vps.sh`, congela las escrituras del VPS viejo, toma el dump final, genera un manifiesto (conteos, migración y sha256) y lo trae a la máquina del operador verificado de punta a punta.
- O4. Un runbook, `deploy/MIGRACION-VPS.md`, con la preparación, el corte con los comandos exactos, la verificación, el rollback y el post-corte, para que se pueda ejecutar sin preguntarle nada a nadie.
- O5. Antes del corte real, un **ensayo completo** con un dump real del VPS actual, primero en un ambiente descartable y después sobre el VPS nuevo.

## 3. No-objetivos

- **Redirigir clientes (decidido por Martin).** Ninguna PC del municipio apunta al servidor viejo, así que no hay redirección HTTP, ni CNAME transitorio, ni proxy.
- **Multi-tenant y rediseño del licenciamiento (decidido por Martin).** En el VPS nuevo el licenciamiento queda **apagado** (`Licencia__Habilitado=false`). El rediseño va junto con el multi-tenant futuro.
- **Automatizar el corte entero en un solo comando (decisión del asistente).** El corte cruza dos servidores y tiene puntos donde un humano tiene que mirar el resultado antes de seguir: los conteos, el login y el smoke de la GUI. Se entregan piezas verificables y un runbook que las encadena, no un orquestador.
- **Conservar el dominio viejo, sus sesiones o sus tokens.** Ver §5.1.
- **Migrar `licencia.lic`, los logs o el feed viejo.** Ver §5.2.
- **Desmantelar el VPS viejo.** Después de N días es una decisión aparte de Martin (§5.7). Este diseño solo lo deja detenido.
- **Backups fuera del servidor (off-site).** Hoy los backups viven en el mismo VPS. Es un problema real, pero va por separado.

## 4. Anti-objetivos (lo que NO puede pasar)

- **A1. Perder datos.** Ninguna fila del origen puede faltar en el destino. Si los conteos no coinciden, la API nueva **no arranca** y el corte se detiene.
- **A2. Borrar auditoría.** `LogsAuditoria` no se borra nunca (regla del proyecto). Viaja dentro del dump, y su conteo en el destino tiene que ser **mayor o igual** que el del manifiesto, también después de arrancar la API. Ningún script borra una base con datos sin haber tomado antes un dump de ella (§5.3, `--recrear-base`).
- **A3. Que un cliente escriba en el servidor viejo después del dump.** Por eso las escrituras se congelan **deteniendo y enmascarando** `stockapp-api` en el VPS viejo **antes** del dump, y además se verifica que no queden conexiones a la base (§5.4).
- **A4. Tocar pinar.** En el VPS viejo, todo comando se acota por nombre a StockApp: `stockapp-api`, `stockapp-pg`, `/var/lib/stockapp` y `~/stockapp-deploy`. No se toca nginx, no se toca certbot de forma global y no se corre ningún `docker` sin nombre explícito.
- **A5. Restaurar encima de una base migrada o sembrada.** Si la API arranca contra una base vacía, corre `MigrateAsync()` y siembra el admin (`src/StockApp.Api/Program.cs:591-600`). Un `pg_restore` encima de eso choca con las tablas ya creadas. El orden correcto es: Postgres levantado, base vacía, `pg_restore` y **recién entonces** la API. Si la API está activa, `restaurar-backup.sh` se niega a correr.
- **A6. Un dump a medio restaurar.** Si la restauración falla, la base destino tiene que quedar **vacía**, no a medias (`--single-transaction`, §5.3).
- **A7. Dejar el dump de producción tirado en un lugar legible por otros.** El VPS viejo es compartido: el dump nunca toca el `/tmp` del host (§5.4).

## 5. Diseño

### 5.1 Decisiones

**De Martin (2026-10-20):**
1. El VPS nuevo usa un **dominio nuevo**. El viejo no se conserva.
2. En el VPS nuevo, el licenciamiento queda **apagado** (`Licencia__Habilitado=false`).
3. La instalación del municipio y la migración ocurren **al mismo tiempo**. Las PCs se instalan una vez, con el Configurador apuntando al dominio nuevo, y no hace falta redirigir clientes.
4. El VPS nuevo está **en blanco y todavía no existe**.
5. El VPS viejo es **compartido con pinar**: solo se corren comandos acotados a StockApp y nginx no se toca nunca.
6. El VPS viejo **sigue sirviendo testing hasta el corte**.

**Del asistente:**
1. **`JWT_SECRET` y contraseña de Postgres nuevos** en el VPS nuevo. No hay sesiones vivas que preservar: las PCs se instalan de cero. Las contraseñas de los usuarios del sistema no cambian, porque los hashes viajan en el dump.
2. **El feed nuevo arranca solo con la 0.5.3**, publicada con `build/publicar-release.sh`, y las PCs instalan esa versión. El feed viejo no se copia.
3. **Las escrituras se congelan deteniendo `stockapp-api` en el VPS viejo antes del dump final**, y el dump se toma con `docker exec stockapp-pg pg_dump -Fc`. Con la API abajo, `POST /backups` no está disponible, y no hace falta.
4. **El VPS viejo queda detenido pero intacto durante N = 14 días** como rollback, con la unit **enmascarada** (§5.4, por qué `mask` y no solo `stop`).
5. **Verificación:** conteos por tabla, dump contra base restaurada, login, feed `/updates` y un smoke de la GUI hecho por un agente (§7.2).
6. **TLS con Let's Encrypt por HTTP-01, en modo `--standalone`**, una vez que el DNS del dominio nuevo apunte a la IP nueva. En el VPS nuevo no hay nginx de pinar, así que el puerto 80 está libre. La alternativa es DNS-01 (§5.5, P5).
7. **Ensayo completo** con un backup real del VPS actual, primero en un ambiente descartable y después sobre el VPS nuevo, antes del corte real (§5.9).
8. **Puerto 8080 en el VPS nuevo**, igual que hoy, aunque ahí el 443 esté libre. Así se reutiliza todo lo que ya está probado (`API_PORT`, el firewall y los healthchecks de `install.sh` y `deploy-vps.sh`). Si Martin prefiere 443 por estética de la URL, cambia solo `API_PORT` y la regla de `ufw`, porque la URL la escribe el Configurador de todos modos.
9. **El historial de backups viejo se copia** al VPS nuevo (§5.2).
10. **El VPS nuevo es Ubuntu 24.04 LTS.** `install.sh:287-307` instala `postgresql-client-16` por apt, y ese paquete viene en los repos de 24.04. En 22.04 haría falta el repo PGDG.

### 5.2 Qué estado existe y qué viaja

| Estado | Dónde vive (VPS viejo) | ¿Viaja? | Cómo / por qué |
|---|---|---|---|
| Todos los datos, incluidos `LogsAuditoria`, usuarios con sus hashes, `CorridasBackup` y adjuntos (`bytea`, el dump pesa unos 100 KB hoy) | volumen Docker `stockapp_pgdata` (`deploy/docker-compose.postgres.yml`) | **Sí** | Dump final `-Fc` + `restaurar-backup.sh` |
| Archivos `backup_*.dump` del historial | `/var/lib/stockapp/.local/share/StockApp/backups/` | **Sí (decisión del asistente)** | Las filas de `CorridasBackup` viajan en el dump y apuntan a esos archivos por nombre. Sin ellos, `GET /backups/{id}/contenido` de una corrida vieja da 404 (`src/StockApp.Application/Backups/ServicioConsultaBackups.cs:79-80`). Pesan poco y además son puntos de restauración extra. Se copian con `tar -p` (conserva los mtime, ver §5.5 C6) |
| `api.env` | `/etc/stockapp-api/api.env` (600, generado por `install.sh` desde `~/stockapp-deploy/.env`) | **No, se regenera** | Cambian los secretos (§5.1). Las claves de override que no son estándar (por ejemplo, la configuración de alertas) se copian **a mano** al `.env` del servidor nuevo. `volcado-final-vps.sh` lista solo sus **nombres** (§5.4) |
| `LICENCIA_CLAVE_PUBLICA_BASE64` | `api.env` | **Se reutiliza el mismo valor** | `install.sh:158-169` la exige aunque el licenciamiento esté apagado, y el reset de admin firmado sigue usando `ValidadorFirma` con esa clave (`Program.cs:563-581`). Con la misma clave pública, la clave privada de `~/stockapp-claves` sigue sirviendo para un reset |
| `licencia.lic` | `/var/lib/stockapp/...` | **No** | Está atada a `/etc/machine-id` (`src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaLinux.cs:8`), y en el VPS nuevo el licenciamiento está apagado |
| Logs | `/var/lib/stockapp/...` | **No** | Se archivan en la máquina del operador si Martin los quiere. No hacen falta en el servidor nuevo |
| Feed `/updates` | `/var/lib/stockapp/updates/` | **No** | Se publica la 0.5.3 de cero (§5.1, decisión 2) |
| Certificados TLS | `/etc/letsencrypt` + copia en `/var/lib/stockapp/tls/` + deploy-hook `/usr/local/sbin/stockapp-tls-deploy-hook.sh` | **No (cert), sí (hook)** | El certificado es de otro dominio. **El hook no está versionado en el repo**: hay que traerlo y versionarlo (§5.5, P5) |
| Tokens JWT, sesiones | firmados con el `JWT_SECRET` viejo | **No** | Se invalidan solos con el secreto nuevo |

### 5.3 `deploy/restaurar-backup.sh` (nuevo)

Corre **en el servidor donde está el contenedor destino**: el VPS nuevo, o la máquina del operador en el ensayo local. Usa `docker exec` y `docker cp`, igual que el pre-vuelo de `deploy/deploy-vps.sh` (paso 1, `deploy-vps.sh:249-352`). El usuario tiene que estar en el grupo `docker`, y **no** hace falta `sudo`.

**Uso:**
```
deploy/restaurar-backup.sh <dump> [--manifiesto <archivo>] [--contenedor <nombre>]
                           [--base <nombre>] [--usuario <rol>] [--env <.env del servidor>]
                           [--recrear-base <nombre-de-la-base>]
```
- `<dump>`: un archivo `pg_dump --format=custom`. Sirve el `.dump` del volcado final, un `backup_*.dump` del directorio de backups o el `.bin` que baja `deploy-vps.sh` (paso 2, `deploy/dist/backups/pre-deploy-<version>-<ts>.bin`), que es el mismo `-Fc` sin cifrar.
- `--contenedor`: por defecto `stockapp-pg`. **Tiene que matchear `^stockapp[a-z0-9-]*$`**, y si no, el script aborta. Así queda acotado a StockApp (A4) y el ensayo local puede usar `stockapp-ensayo-pg` sin tocar el `stockapp-pg` de desarrollo.
- `--base` / `--usuario`: por defecto se leen `POSTGRES_DB` / `POSTGRES_USER` del `--env` (default `~/stockapp-deploy/.env`). Los nombres se validan con `^[a-z_][a-z0-9_]*$` antes de interpolarlos en SQL (por ejemplo, en `DROP DATABASE`).
- `--manifiesto`: es opcional, pero **en la migración es obligatorio usarlo** (lo exige el runbook). Sin manifiesto, el script solo puede verificar estructura, no conteos (ver el paso 8).

**Bash:** `set -euo pipefail`, con el gotcha conocido de `grep` en un pipe sin coincidencias. Cada consulta captura por separado el exit code y el stderr, con el mismo criterio que `deploy-vps.sh:296-317`: "no pude consultar" **nunca** se interpreta como "vacío" o "sin duplicados".

**Pasos, en este orden:**
1. **La API no está activa.** Si `systemctl is-active --quiet stockapp-api` da 0, el script aborta (A5). Si la unit no existe, como en el ensayo local, cuenta como inactiva.
2. **El contenedor existe y está corriendo** (`docker inspect -f '{{.State.Running}}'`), y Postgres responde (`pg_isready` dentro del contenedor).
3. **Si hay manifiesto, se verifica el dump contra el manifiesto antes de tocar nada:** el `sha256sum` y el tamaño tienen que coincidir.
4. **Copia al contenedor:** se hace `docker cp` del dump a `/tmp/restaurar-<aleatorio>.dump` **dentro del contenedor**. Un `trap EXIT` lo borra (`docker exec <c> rm -f …`).
   - **Por qué `docker cp` y no stdin (decisión del asistente):** `pg_restore` leyendo `-Fc` desde un pipe no puede hacer seek. Con datos fuera de orden puede fallar con "could not find block ID". Con un archivo, ese problema no existe.
   - **Por qué `pg_restore` dentro del contenedor y no el del host:** usa exactamente la versión del servidor (16) y se conecta por el socket local del contenedor, sin pasar contraseñas por argv ni por el entorno del host.
5. **`pg_restore --list`** sobre el archivo. Si falla, el dump es inválido o está truncado, y el script aborta sin tocar la base. De ese listado sale el conjunto de tablas con entrada `TABLE DATA`. Si hay manifiesto, ese conjunto tiene que ser **igual** al conjunto de tablas del manifiesto.
6. **La base destino está vacía.** Se considera vacía si no tiene ninguna relación fuera de `pg_catalog`, `information_schema` y `pg_toast*`, ni ningún schema extra creado por usuarios. Si **no** está vacía:
   - **Sin `--recrear-base`, aborta** e informa la cantidad de tablas y las filas de `LogsAuditoria` y `__EFMigrationsHistory`. Es el caso de A5.
   - **Con `--recrear-base <nombre>`:** el nombre tiene que ser **igual** a `--base`, como confirmación explícita, porque el script no es interactivo. Antes de borrar, el script toma un **dump de seguridad** de la base existente (`pg_dump -Fc`) en `~/stockapp-deploy/migracion/pre-recrear-<base>-<ts>.dump` (600) e informa la ruta (A2). Recién después hace `DROP DATABASE` y `CREATE DATABASE … OWNER <usuario>` conectado a la base `postgres`. Si hay conexiones abiertas, el `DROP` falla, y el script aborta en vez de usar `WITH (FORCE)`.
7. **Restauración:**
   ```
   pg_restore --no-owner --no-privileges --single-transaction --exit-on-error \
              -U <usuario> -d <base> /tmp/restaurar-<aleatorio>.dump
   ```
   - `--no-owner` **(decisión del asistente):** los objetos quedan a nombre del rol que restaura, que es el `POSTGRES_USER` del contenedor nuevo. Así el restore no depende de que el rol del origen exista con el mismo nombre. Se recomienda mantener `stockapp`, pero no hace falta. `RestaurabilidadBackupTests.cs:138` ya restaura con `--no-owner`.
   - `--no-privileges`: no restaura `GRANT`/`REVOKE`. La app no usa privilegios por fuera del dueño, y así no falla por un rol inexistente.
   - `--single-transaction` (implica `--exit-on-error`): si algo falla, **la base queda vacía**, no a medias (A6). Esto hace que el script sea reintentable.
   - No se usa `--clean` (la base está vacía) ni `-j` (es incompatible con `--single-transaction`, y el volumen es chico).
8. **Verificación de conteos (O1).** Se usa una sola consulta determinística sobre la base restaurada, la **misma** que usa el manifiesto (§5.4):
   ```sql
   SELECT table_name,
          (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text::bigint
   FROM information_schema.tables
   WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
   ORDER BY table_name;
   ```
   - **Con manifiesto:** cada tabla tiene que tener **exactamente** el conteo del manifiesto, y la última `MigrationId` de `__EFMigrationsHistory` tiene que coincidir. Si algo no coincide, el script imprime el diff y sale con error. **La base queda restaurada pero la API no se arranca**, y la decisión pasa a un humano.
   - **Sin manifiesto:** solo verifica que las tablas coincidan con el `--list` e imprime los conteos.
9. **Secuencias.** Para cada columna con secuencia (`pg_get_serial_sequence`, que cubre las columnas identity de Npgsql), se verifica que `last_value` sea mayor o igual a `max(columna)`. Una secuencia atrasada haría que el primer alta choque con la PK. Es una comprobación de solo lectura, en lugar de insertar una fila de prueba en producción.
10. **Pre-vuelo de catálogos.** Se corre el mismo chequeo de duplicados case-insensitive que `deploy-vps.sh` (paso 1) sobre los 7 catálogos de `TABLAS_CON_NOMBRE_UNICO` (`deploy-vps.sh:196-199`). Si encuentra duplicados, el script sale con error, imprime la remediación sugerida (sin ejecutarla) y **no** arranca la API.
    - **Por qué se corre siempre:** si el dump es anterior a `20260910225922_AgregaIndiceFuncionalNombreCatalogos`, esa migración corre en el primer arranque y aborta con duplicados. Con systemd, eso deja la unit en `failed` y bloqueada durante 600 s (`deploy/stockapp-api.service:45-46`). Si el dump es posterior, el índice único sobre `LOWER("Nombre")` ya hace imposibles los duplicados y el chequeo pasa trivialmente. Es barato, y así no hace falta razonar en cuál de los dos casos estamos.
11. **Resumen:** dump, sha, cantidad de tablas, total de filas, última migración y "API NO arrancada: el siguiente paso es `sudo systemctl start stockapp-api`" (o el que indique el runbook). El script **nunca** arranca la API.

**Códigos de salida distintos por clase de fallo (decisión del asistente).** Los valores exactos quedan a criterio del implementador, pero tienen que ser estables y estar documentados en el encabezado:
- uso inválido;
- precondición: API activa, contenedor fuera de patrón, base no vacía;
- dump inválido: sha o `--list`;
- falló `pg_restore` (la base queda vacía);
- falló la verificación de conteos o secuencias (la base queda restaurada);
- hay duplicados en los catálogos (la base queda restaurada).

**Idempotencia:**
- Después de un éxito, correr el script otra vez lo hace abortar por "base no vacía".
- Después de un fallo en el paso 7, se puede reintentar tal cual, porque la base quedó vacía.
- Después de un fallo en los pasos 8 a 10, se reintenta con `--recrear-base`, que primero guarda un dump de seguridad.

### 5.4 `deploy/volcado-final-vps.sh` (nuevo)

Corre en la **máquina del operador**, como `deploy-vps.sh`. Reutiliza la carga del `.env` del operador, los mismos `ssh_vps`/`scp_vps` con `-o BatchMode=yes` (`deploy-vps.sh:99-105`) y la exigencia de `VPS_HOST`/`VPS_USER`/`VPS_SSH_PORT` sin defaults (`deploy-vps.sh:145-147`). Asume `sudo` sin contraseña y que `VPS_USER` está en el grupo `docker`, igual que hoy.

**Uso:** `deploy/volcado-final-vps.sh --salida <dir-local> [--ensayo]`

**Pasos:**
1. **Relevamiento de solo lectura.** Estado de `stockapp-api` y de `stockapp-pg` y última migración aplicada. También guarda la salida de `docker ps --format '{{.Names}}'` para compararla al final y probar que no se tocó nada de pinar (A4).
2. **Congelamiento (A3).**
   - Se corre `sudo systemctl stop stockapp-api` y se verifica que `systemctl is-active stockapp-api` devuelva `inactive`. Un fallo de la consulta **no** cuenta como inactivo.
   - **En modo final** (sin `--ensayo`), además se corre `sudo systemctl mask stockapp-api`.
   - **Por qué `mask` (decisión del asistente):** el deploy-hook de certbot del VPS viejo hace `restart` de `stockapp-api` al renovar (está registrado en la memoria del proyecto), y un `restart` sobre una unit detenida **la arranca**. Con la unit enmascarada, el hook falla sin efecto, y tampoco la levanta un reboot ni un `install.sh` corrido por error.
   - Después se verifica que no queden conexiones a la base: `SELECT count(*) FROM pg_stat_activity WHERE datname = '<db>' AND pid <> pg_backend_pid()` tiene que dar `0`. Si da otra cosa, hay otro escritor, y el script aborta en vez de seguir. En modo final, deja la API enmascarada e informa.
3. **Dump dentro del contenedor.**
   ```
   docker exec stockapp-pg sh -c 'umask 077; pg_dump -U <u> -d <db> --format=custom -f /tmp/volcado-<ts>.dump'
   ```
   Se usan los mismos parámetros que `EjecutorPgDumpProceso` (`src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs:90-95`), salvo `--host`/`--port`, porque corre por el socket local. **Nunca** se escribe en el `/tmp` del host compartido (A7): el `/tmp` del contenedor vive en el overlay de Docker, que solo puede leer root. El sha256 y el tamaño se calculan **dentro** del contenedor.
4. **Manifiesto**, con la API todavía abajo. Repite el chequeo de conexiones antes de contar. Es un archivo de texto con:
   - fecha UTC;
   - host de origen;
   - `version()` del servidor;
   - `pg_dump --version`;
   - `POSTGRES_DB`;
   - sha256 y tamaño del dump;
   - última `MigrationId`;
   - los conteos por tabla, con la consulta de §5.3, paso 8.

   El formato exacto (clave=valor + TSV, o lo que el implementador elija) lo leen `restaurar-backup.sh` y los tests.
5. **Traída verificada.** `ssh_vps "docker exec stockapp-pg cat /tmp/volcado-<ts>.dump" > <salida>/volcado-<ts>.dump`, en binario por stdout y sin pasar por el disco del host. Después, el `sha256sum` local tiene que ser igual al remoto, y el tamaño, mayor o igual a 1024 bytes (mismo umbral que `deploy-vps.sh`). El archivo queda con permisos 600 y el directorio de salida con 700.
6. **Historial de backups.** `ssh_vps "sudo tar -C /var/lib/stockapp/.local/share/StockApp -cpf - backups" > <salida>/backups-historial.tar` (600). Solo esa carpeta.
7. **Nombres de los overrides de `api.env`.** Se listan **solo los nombres** de las claves que no están en la lista conocida de `install.sh:104-106`, sin valores, para que el operador sepa qué pasar a mano al `.env` nuevo. Así, en el disco del operador no quedan secretos viejos que no hacen falta.
8. **Limpieza:** `trap EXIT` borra `/tmp/volcado-<ts>.dump` **dentro del contenedor**.
   - **Con `--ensayo`, el mismo `trap` vuelve a arrancar `stockapp-api`** (`sudo systemctl start`) pase lo que pase: el ensayo no puede dejar testing caído.
   - Sin `--ensayo`, la API queda detenida y enmascarada.
9. **Comprobación final de alcance:** `docker ps` tiene que mostrar los mismos nombres que en el paso 1 (A4).

**Dónde guardar la salida (decisión del asistente):** en `~/stockapp-backups-vps/migracion-<fecha>/` del operador, fuera del repo. `deploy/dist/` está ignorado por git (`.gitignore:518`), pero un dump completo de producción no tiene por qué vivir dentro del checkout. El dump contiene todo, incluidos los hashes de contraseñas.

### 5.5 Runbook `deploy/MIGRACION-VPS.md` (nuevo): contenido

Es un documento operativo con comandos exactos y una casilla por paso, en el estilo de `deploy/DEPLOY.md`. Usa dos `.env` del operador: el del VPS viejo y el del nuevo (§5.6). Además está el `.env` del servidor nuevo, en `~/stockapp-deploy/.env`. Son tres archivos distintos, y el runbook lo dice explícitamente.

**Preparación (días antes; nada de esto toca el VPS viejo salvo P5.a):**

- **P1. VPS nuevo.**
  - Ubuntu 24.04 LTS.
  - Usuario SSH con clave, `sudo` sin contraseña y miembro del grupo `docker` (lo exigen `deploy-vps.sh` y los dos scripts nuevos).
  - El puerto SSH queda anotado para `VPS_SSH_PORT`.
- **P2. DNS.** Registro `A` del dominio nuevo → IP nueva, con TTL de 300 s, creado **días antes**. Se verifica con `dig +short <dominio>` desde la máquina del operador. Como el dominio es nuevo, no hay ningún cambio de DNS en el corte.
- **P3. Firewall (`ufw`).** Se habilita el puerto SSH, `80/tcp` (HTTP-01 al emitir y al renovar) y `8080/tcp` (la API). Es el modo "expuesto" de `DEPLOY.md` ("Firewall", `DEPLOY.md:644` en adelante), con `API_BIND=0.0.0.0`. **El SSH se habilita antes de `ufw enable`.**
- **P4. Docker.** `apt-get install -y docker.io docker-compose-v2`, que es el mismo par que usa como fallback `deploy/kit/01-bootstrap.sh:63`, y `usermod -aG docker <usuario>`.
  - **No se usa `01-bootstrap.sh`:** es el bootstrap del kit LAN. Es interactivo, escribe `/etc/stockapp/.env`, pone el compose en `/opt/stockapp`, usa `API_BIND=0.0.0.0` sin TLS y está pensado para trabajar offline.
  - El VPS sigue el layout de `DEPLOY.md` §2: compose y `.env` en `~/stockapp-deploy/`. Ese es el layout que `deploy-vps.sh` asume (`VPS_DIR`, `deploy-vps.sh:41`, y `install.sh … .env` en el paso 5).
- **P5. TLS.**
  - a) **Traer el deploy-hook del VPS viejo** con `sudo cat /usr/local/sbin/stockapp-tls-deploy-hook.sh`, que es una lectura, y **versionarlo** como `deploy/tls/stockapp-tls-deploy-hook.sh` (decisión del asistente). Hoy existe solo en ese servidor. Ese hook copia los PEM a `/var/lib/stockapp/tls/` como `stockapp:stockapp 0600` y reinicia el servicio.
  - b) **Crear el usuario `stockapp` antes del hook,** con el mismo comando que `install.sh:313` (`useradd --system --create-home --home-dir /var/lib/stockapp --shell /usr/sbin/nologin stockapp`). Si no, el `chown` del hook falla. `install.sh:310` lo detecta como existente y no lo recrea. **Orden obligatorio:** con `API_SCHEME=https`, `install.sh:247-262` aborta si los PEM no existen.
  - c) `certbot certonly --standalone -d <dominio> --deploy-hook /usr/local/sbin/stockapp-tls-deploy-hook.sh`.
  - d) **Probar la renovación con `certbot renew --force-renewal`**, que ejecuta los hooks. `--dry-run` **no** los ejecuta, así que deja el hook sin probar. Esto se hace después de P8/E2, cuando la API ya existe.
  - **Alternativa DNS-01:** se usa si el proveedor no permite abrir el 80, o para emitir el certificado antes de que el DNS apunte a la IP nueva. Requiere un plugin del proveedor DNS o un registro TXT manual, y la renovación manual no es automática. Por eso queda solo como alternativa.
- **P6. `.env` del servidor nuevo** (`~/stockapp-deploy/.env`, 600), a partir de `deploy/.env.example`:
  - `POSTGRES_PASSWORD` y `JWT_SECRET` **nuevos**, generados con los comandos que indica la plantilla;
  - `POSTGRES_USER=stockapp` y `POSTGRES_DB=stockapp`;
  - `BOOTSTRAP_ADMIN_USER` y `BOOTSTRAP_PASSWORD`: `install.sh:158-169` los exige, pero con la base restaurada **se ignoran**, porque `BootstrapAdminSeeder.SembrarAsync` no hace nada si ya hay usuarios (`src/StockApp.Api/Auth/BootstrapAdminSeeder.cs:31-32`);
  - el **mismo** `LICENCIA_CLAVE_PUBLICA_BASE64` que el VPS viejo (§5.2);
  - `API_BIND=0.0.0.0`, `API_SCHEME=https`, `API_PORT=8080` y `API_HOSTNAME=<dominio nuevo>`;
  - `Kestrel__Certificates__Default__Path`/`KeyPath` apuntando a `/var/lib/stockapp/tls/…`;
  - `Licencia__Habilitado=false`;
  - `RateLimiting__Login__PermitLimit=10`, que es lo recomendado para el modo expuesto (`deploy/.env.example`, sección de overrides);
  - los overrides listados en §5.4, paso 7.
- **P7. `.env` del operador para el VPS nuevo:** `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`, `POSTGRES_USER`, `POSTGRES_DB`, `API_SCHEME`, `API_HOSTNAME`, `API_PORT` y, si hace falta, `VPS_ADMIN_USER`/`VPS_ADMIN_PASSWORD` (`deploy-vps.sh:189-190`). Ver cómo se selecciona en §5.6.
- **P8. Postgres en el VPS nuevo:** `scp` de `deploy/docker-compose.postgres.yml` a `~/stockapp-deploy/`, y después `docker compose --env-file .env -f docker-compose.postgres.yml up -d` hasta que quede `healthy` (`DEPLOY.md` §2). La imagen crea `POSTGRES_DB` vacía.
- **P9. Ensayo general E2** sobre el VPS nuevo (§5.9). Deja la API 0.5.3 instalada, TLS andando, el feed 0.5.3 publicado y el hook probado.
- **P10. Instalador de las PCs:** el `Setup.exe` de la **misma** 0.5.3 publicada en el feed (`build/pack-win.ps1`).

**Corte (día D).** Los tiempos estimados salen de E2 y quedan anotados en el runbook.

- **C0.** Martin confirma la ventana, avisa que testing deja de existir en el servidor viejo y que **no se hacen deploys** al VPS viejo durante el corte.
- **C1. VPS nuevo:** `sudo systemctl stop stockapp-api` (la que dejó E2), y verificar que quede `inactive`.
- **C2. VPS nuevo: limpiar los restos del ensayo** que harían ruido después del restore.
  - Solo los `backup_*.dump` y `*.tmp` de `/var/lib/stockapp/.local/share/StockApp/backups/`, borrados uno por uno por patrón y dentro de esa ruta. Nada de `rm -rf` sobre variables.
  - **Por qué:** `ReconciliarDumpHuerfanosAsync` (`src/StockApp.Application/Backups/ServicioBackup.cs:226-273`) adoptaría como corridas válidas los dumps del ensayo, que no tienen fila en la base restaurada.
  - El feed `/var/lib/stockapp/updates/` y los certificados **no se tocan**.
- **C3. Máquina del operador, contra el VPS viejo:** `deploy/volcado-final-vps.sh --salida ~/stockapp-backups-vps/migracion-<fecha>/`. Congela, enmascara, hace el dump y genera el manifiesto, y trae el dump y el historial.
- **C4.** `scp` de `volcado-*.dump`, el manifiesto, `backups-historial.tar` y `deploy/restaurar-backup.sh` a `~/stockapp-deploy/migracion/` del VPS nuevo, con `chmod 600` sobre los datos.
- **C5. VPS nuevo:** `./restaurar-backup.sh migracion/volcado-<ts>.dump --manifiesto migracion/<manifiesto> --recrear-base stockapp`. La base tiene los datos del ensayo, así que el script toma primero el dump de seguridad. Tiene que salir con 0.
- **C6. VPS nuevo: historial de backups.** `sudo tar -C /var/lib/stockapp/.local/share/StockApp -xpf migracion/backups-historial.tar` y `sudo chown -R stockapp:stockapp` sobre esa carpeta. Los archivos quedan como 640 y la carpeta como 750, igual que `install.sh`. Con `-p` se conservan los mtime, así que `ReconciliarDumpHuerfanosAsync` no los adopta como recién creados. De todos modos, tienen fila en `CorridasBackup`.
- **C7. VPS nuevo:** `sudo systemctl start stockapp-api`, porque E2 ya instaló la 0.5.3. Si desde E2 se instaló otra versión, en su lugar se corre `deploy/deploy-vps.sh 0.5.3 --sin-backup` con el `.env` del operador nuevo, y su paso 1 repite el pre-vuelo sobre la base restaurada. La API migra hacia adelante si el binario es más nuevo que el dump.
- **C8.** Checklist de verificación (§7.2), punto por punto.
- **C9. "Go":** las PCs se instalan con el `Setup.exe` 0.5.3 y el Configurador apunta a `https://<dominio nuevo>:8080`. **La primera escritura real en el VPS nuevo es el punto de no retorno barato** (ver Rollback).
- **C10.** Se anota en el runbook la fecha del corte y la fecha de fin del rollback (D + 14).

**Rollback:**
- **R-A. Antes de C9, sin escrituras reales en el nuevo.** En el VPS viejo: `sudo systemctl unmask stockapp-api && sudo systemctl enable --now stockapp-api`. Los datos del viejo están intactos, porque el volcado es solo de lectura. El VPS nuevo queda como estaba, para reintentar otro día.
- **R-B. Después de C9.** Corregir hacia adelante es lo preferido. Si el VPS nuevo resulta irrecuperable, se hace la **migración inversa con las mismas piezas**: `volcado-final-vps.sh` contra el nuevo y `restaurar-backup.sh --recrear-base` en el viejo, con el `.env` del operador viejo. El dump de seguridad de `--recrear-base` conserva lo que tenía el viejo. Las PCs se repuntan con el Configurador. El runbook lo describe, pero deja claro que es el último recurso.

**Post-corte:**
- El VPS viejo queda **detenido y enmascarado** durante 14 días. No se borra nada: ni el volumen `stockapp_pgdata`, ni `/var/lib/stockapp`, ni la unit.
- Al día 14, **desmantelarlo es una decisión de Martin** (§3). El runbook deja anotados, solo como referencia, los comandos acotados por nombre:
  - `docker compose -f … down` **sin** `-v`;
  - `docker volume rm stockapp_pgdata`, solo después de confirmar que el conteo de `LogsAuditoria` en el VPS nuevo es mayor o igual al del manifiesto, y que hay un dump archivado fuera del servidor;
  - `systemctl disable`.
  - Nunca se corre nada global, y certbot del VPS viejo solo se toca en la línea del certificado de StockApp.
- Ambiente de testing: ver §5.8.
- Documentación: `deploy/PROCEDIMIENTOS.md:238-239` (paso 7 del Procedimiento 3) pasa a apuntar a `restaurar-backup.sh`.

### 5.6 Cambios chicos en scripts existentes

1. **Elegir el `.env` del operador (decisión del asistente).** `deploy-vps.sh:135` y `build/publicar-release.sh:123` cargan siempre `${REPO_ROOT}/deploy/.env`. Durante el corte, el operador trabaja con dos VPS. Se agrega una variable `STOCKAPP_ENV_LOCAL` que, si está definida, reemplaza esa ruta. Por defecto se mantiene la ruta de hoy, y los scripts nuevos la respetan igual.
   - **Alternativa descartada:** intercambiar archivos a mano a mitad del corte. Es un error clásico apuntarle al VPS equivocado con el `.env` equivocado.
   - Ojo: como el `.env` se carga con `source` **después** del entorno, un `VPS_HOST` definido en el archivo pisa el del entorno. Por eso no alcanza con exportar variables.
2. **El marcador de último deploy es por repo, no por VPS (gotcha encontrado).**
   - `deploy-vps.sh:201` guarda `deploy/dist/.ultimo-deploy-vps-commit`, y el paso 4 (`deploy-vps.sh:571-580`) **no copia** `install.sh`/`stockapp-api.service`/`wait-for-postgres.sh` si no cambiaron desde ese commit.
   - El primer `deploy-vps.sh` contra el VPS nuevo, donde esos archivos **no existen**, saltearía la copia, y el paso 5 fallaría con `./install.sh` inexistente.
   - **Arreglo (decisión del asistente):** el marcador pasa a llevar `VPS_HOST` en el nombre (`.ultimo-deploy-vps-commit-<host>`). Mientras no esté arreglado, el runbook manda borrar el marcador antes del primer deploy al VPS nuevo.

### 5.7 Por qué el orden importa (resumen para el implementador)

```
VPS nuevo:  compose up (base vacía) ─► [API detenida] ─► restaurar-backup.sh ─► historial ─► start API
                                                          (verifica conteos,      (C6)       (migra hacia
                                                           secuencias, catálogos)             adelante; seed no-op)
VPS viejo:  stop + mask API ─► sin conexiones ─► pg_dump -Fc ─► manifiesto ─► traer + sha
```

El conteo **se compara antes de arrancar la API nueva**, porque el arranque escribe. Si la última corrida tiene más de 12 h, `BackupProgramadoService` corre enseguida (`src/StockApp.Api/Backups/BackupProgramadoService.cs:112`) y agrega una fila a `CorridasBackup`. Las migraciones nuevas agregan filas a `__EFMigrationsHistory`. Por eso, la verificación **posterior** al arranque (§7.2, V6) acepta crecimiento solo en una lista cerrada de tablas, y nunca acepta que una tabla tenga menos filas.

### 5.8 Ambiente de testing después del corte

El VPS viejo deja de servir testing en el corte. Qué pasa con testing después lo decide **Martin** (§9, P2). Las opciones, con sus costos:

| Opción | Ventaja | Costo |
|---|---|---|
| Sin testing | No suma nada | Las versiones se prueban directo en producción o solo en local |
| Una segunda instancia en el VPS nuevo | Mismo servidor y mismo TLS | Hoy los scripts asumen un solo `stockapp-api`, un solo `stockapp-pg` y `/var/lib/stockapp` fijos: hay que parametrizar todo |
| Revivir el VPS viejo como testing, con base nueva vacía, después de los 14 días | Infraestructura ya hecha | Sigue compartido con pinar, y tiene que arrancar **sin** copia de los datos de producción |

### 5.9 Ensayo

**E1. Ensayo local, en un ambiente descartable.**
1. Se corre `volcado-final-vps.sh --salida <dir> --ensayo` contra el VPS viejo. Detiene testing alrededor de un minuto, y el `trap` lo vuelve a arrancar. Así se ensaya también el congelamiento.
2. Se levanta un contenedor descartable **con nombre propio**: `docker run -d --name stockapp-ensayo-pg -e POSTGRES_USER=stockapp -e POSTGRES_PASSWORD=<descartable> -e POSTGRES_DB=stockapp -p 127.0.0.1:5434:5432 -v stockapp_ensayo_pgdata:/var/lib/postgresql/data postgres:16-alpine`. **Nunca** se usa el `stockapp-pg` de desarrollo, que tiene que quedar siempre andando. El nombre y el volumen son distintos, y la regla de "base no vacía" es una segunda barrera.
3. Se corre `deploy/restaurar-backup.sh <dump> --manifiesto <m> --contenedor stockapp-ensayo-pg --base stockapp --usuario stockapp`, y tiene que salir con 0.
4. Se levanta la API 0.5.3 local contra esa base, con `ConnectionStrings__Default` en el puerto 5434, `Licencia__Habilitado=false` y `Jwt__Secret`/`Bootstrap__*` de prueba. Se verifica que migre hacia adelante sin error, que `GET /` responda `{"status":"ok"…}` (`Program.cs:682`), el login de un usuario real (credenciales de Martin, pedidas en el momento) y `GET /backups`.
5. Se limpia con `docker rm -f stockapp-ensayo-pg` y `docker volume rm stockapp_ensayo_pgdata`, por nombre exacto. Los procesos de la API se matan por PID exacto y se verifica con `ps`. El dump queda en `~/stockapp-backups-vps/`.

**E2. Ensayo general en el VPS nuevo,** después de P1 a P8. Se hace la secuencia completa del corte (C3 a C8), con estas diferencias:
- `volcado-final-vps.sh --ensayo`: el viejo vuelve a arrancar.
- Se hace el **primer** `deploy/deploy-vps.sh 0.5.3 --sin-backup` contra el VPS nuevo, en lugar de C7. Su paso 2 necesita una API viva con login, y en un VPS en blanco no la hay.
- `build/publicar-release.sh 0.5.3` contra el VPS nuevo.
- P5.d: `certbot renew --force-renewal`, y verificar que la API reinicie y siga sirviendo con el certificado nuevo.
- El smoke de la GUI (V9).
- Se cronometra cada paso.

El VPS nuevo queda en ese estado hasta el corte, que repite C1 a C8 con `--recrear-base`.

## 6. Qué queda a criterio del implementador

- El formato exacto del manifiesto y los códigos de salida numéricos, siempre que sean estables, estén documentados en el encabezado de cada script y los tests los fijen.
- Si el SQL y el array de los 7 catálogos se extraen a un archivo compartido (por ejemplo `deploy/lib/catalogos-nombre-unico.sh`, que usarían `deploy-vps.sh` y `restaurar-backup.sh`) o se duplican con un comentario cruzado. Hoy ya existen dos copias: C# en `NormalizacionCatalogosSql.cs` y bash en `deploy-vps.sh:196-199`. Lo fijo es que un test detecte si se desincronizan.
- Si `volcado-final-vps.sh` valida además el dump localmente con `docker run --rm postgres:16-alpine pg_restore --list`, antes de dar por terminado el volcado.
- Si el dump de migración se copia también al directorio de backups del VPS nuevo con el nombre `backup_<ts>.dump`, para que `ReconciliarDumpHuerfanosAsync` lo muestre en `/backups` como punto de restauración. Si se hace, tiene que ser **después** de la verificación de conteos.
- La redacción del runbook, siempre que cada paso tenga el comando exacto, el resultado esperado y qué hacer si no coincide.

## 7. Verificación

### 7.1 Tests de los scripts (TDD, cada guardián verificado por mutación quitando código, no agregándolo)

**`restaurar-backup.sh`.** Contra **Postgres real en contenedor**, con un contenedor propio por test: `docker run` de `postgres:16-alpine` con nombre `stockapp-test-<guid>`, eliminado en `finally` por nombre exacto. Es el mismo patrón de `docker run` + `EjecutorBash` que `tests/StockApp.Infrastructure.Tests/Kit/InstallEsquemaTests.cs:187-216`, con el helper `tests/_Compartido/EjecutorBash.cs`. El dump de entrada se genera con el ejecutor de producción sobre la base sembrada de `PostgresFixture`, igual que `RestaurabilidadBackupTests.cs:69-80`, para que tenga el esquema real con todas las migraciones. `systemctl` se reemplaza con un stub por `PATH` (patrón `DeployVpsTests.cs`). Casos:
- Base vacía + manifiesto correcto → exit 0, conteos iguales, secuencias bien. **Mutación:** si se saca la comparación de conteos y se manipula una línea del manifiesto, tiene que quedar en rojo.
- Base no vacía, sin `--recrear-base` → aborta y la base queda intacta (mismas tablas y mismas filas).
- `--recrear-base` con un nombre distinto de `--base` → aborta sin tocar nada.
- `--recrear-base` correcto → existe el dump de seguridad, es restaurable y contiene las filas previas. **Mutación:** si se saca el dump previo, tiene que quedar en rojo.
- sha del manifiesto distinto → aborta **antes** de `docker cp` (el rastro lo prueba).
- Dump truncado → `--list` falla y la base sigue vacía.
- Fallo a mitad de la restauración (por ejemplo, un objeto preexistente que choca, creado por el test después del chequeo de vacío, o un dump con un error inyectado) → la base queda **vacía**. **Mutación:** si se saca `--single-transaction`, tiene que quedar en rojo, con tablas a medias.
- Contenedor `postgres-otro` → aborta por patrón.
- API activa (stub de `systemctl is-active` → 0) → aborta.
- Secuencia atrasada (el test hace `setval` por debajo del max) → falla en la verificación de secuencias.
- Duplicados case-insensitive en un catálogo de un dump anterior al índice → error de duplicados con remediación, y la API no arranca. Si construir ese dump es caro, alcanza con insertar duplicados después de bajar el índice en la base origen del test.
- Consulta que falla (stub de `docker exec` que sale con error) → **no** se interpreta como "vacío".

**`volcado-final-vps.sh`.** Con stubs de `ssh`, `scp` y `sha256sum` al frente del `PATH` que registran el orden de las invocaciones (patrón `DeployVpsTests.cs`). Casos:
- `stop` (y `mask` en modo final) ocurre **antes** de `pg_dump`. **Mutación:** invertir el orden tiene que quedar en rojo.
- `is-active` sigue `active` después del stop → aborta sin dump.
- `pg_stat_activity` mayor que 0 → aborta sin dump.
- sha local distinto del remoto → aborta, el archivo local queda marcado o borrado, y no hay "OK".
- `--ensayo` → hay `start` en el `trap` también si falla a mitad. **Mutación:** sacar el `trap` tiene que quedar en rojo.
- Sin `--ensayo`, nunca hay `start` ni `unmask`.
- Todos los `systemctl` del rastro nombran `stockapp-api`, y no hay ningún `docker` sin `stockapp-pg`.
- El dump nunca se escribe en una ruta del host remoto (el rastro no contiene `/tmp/` fuera de `docker exec`).

**`deploy-vps.sh` / `publicar-release.sh`.** `STOCKAPP_ENV_LOCAL` elige el archivo, y sin ella todo queda como hoy. Además, el marcador es por host: con el marcador de otro host, el paso 4 copia `install.sh`.

### 7.2 Checklist del corte (C8; cada punto con su comando en el runbook)

- **V1.** `restaurar-backup.sh` salió con 0: sha OK, `--list` OK, conteos **idénticos** al manifiesto, secuencias OK y sin duplicados.
- **V2.** `systemctl is-active stockapp-api` → `active`, sin errores de arranque en `journalctl -u stockapp-api`, y la última migración es mayor o igual a la del manifiesto.
- **V3.** `GET https://<dominio>:8080/` desde la máquina del operador, **sin `-k`** → `{"status":"ok","service":"StockApp.Api"}`, con certificado válido para el dominio nuevo.
- **V4.** `GET /licencia/estado` → `activada: true` (`Licencia__Habilitado=false`, `Program.cs:609-620`).
- **V5.** Login con un usuario **real** de los datos migrados → 200. Las credenciales las ingresa Martin o se le pasan al agente en el momento, y no se guardan.
- **V6.** Conteos después del arranque comparados con el manifiesto: **ninguna** tabla puede tener menos filas, y solo pueden tener más `CorridasBackup`, `__EFMigrationsHistory` y `LogsAuditoria`. `LogsAuditoria` tiene que ser mayor o igual al manifiesto (A2).
- **V7.** `GET /updates/releases.win.json` → 200 y contiene `0.5.3`. `curl -sI` del `.nupkg` full → 200, con `Content-Length` igual al `Size` del feed.
- **V8.** `GET /backups` lista las corridas viejas, `GET /backups/{id}/contenido` de una vieja → 200 (C6), y `POST /backups` → corrida `Exitosa`, lo que prueba que `pg_dump` anda con la contraseña nueva.
- **V9.** Smoke de la GUI **de solo lectura** hecho por un agente con el toolkit `scripts/gui-verificacion/`. El desktop 0.5.3 apunta al dominio nuevo por el Configurador. El agente:
  - hace login;
  - recorre las grillas principales (productos, movimientos, gastos, tareas, documentos y auditoría) y abre un adjunto;
  - junta capturas y **las mira**, porque `capturar.sh` no es fail-closed y con WSLg caído captura el escritorio de Windows con exit 0.

  El resultado es OK o FALLA, con evidencia. No se escribe nada en producción: las secuencias ya se verificaron por SQL (§5.3, paso 9).
- **V10.** VPS viejo: `systemctl is-enabled stockapp-api` → `masked`, `is-active` → `inactive`, y `docker ps` muestra los mismos contenedores de pinar que en el relevamiento (A4).

## 8. Restricciones

- **El VPS viejo es compartido con pinar (decidido por Martin).** Todo comando se acota por nombre a StockApp: no se toca nginx, no hay comandos globales de Docker ni de certbot, y no se escribe ningún dump en el `/tmp` del host.
- **La auditoría no se borra nunca.** Ningún paso destructivo ocurre sin un dump previo.
- **TDD con verificación por mutación** para los scripts (§7.1). Los contenedores de test llevan nombre propio y se eliminan por nombre exacto. Ningún test toca el `stockapp-pg` de desarrollo.
- **No se compila por rutina.** Los cambios son bash y docs, y los tests de Kit corren el bash real.
- **Bash:** `set -euo pipefail`, el gotcha de `grep` en un pipe, y que "no pude consultar" nunca equivalga a "vacío". Los secretos nunca viajan por argv (mismo criterio que `ssh_vps_autenticado`, `deploy-vps.sh:107-121`).
- **Ubuntu 24.04** en el VPS nuevo (§5.1).
- **Los procesos que lancen los agentes en el ensayo** (API local, contenedores) se matan por PID o por nombre exacto, y se verifica con `ps`/`docker ps` al terminar.

## 9. Riesgos

- **R1. Escritura en el VPS viejo después del dump.** La mitigan el `stop` + `mask` antes del dump, la verificación de `pg_stat_activity` antes y después, y el hecho de que ninguna PC del municipio apunta al viejo. Lo que escriba un tester con la API caída simplemente no ocurre.
- **R2. Que el deploy-hook de certbot del viejo resucite la API.** Lo mitiga `mask` (§5.4, paso 2). Sin `mask`, el riesgo sería real.
- **R3. El marcador de deploy por repo saltea `install.sh` en el VPS nuevo.** Mitigación en §5.6.2.
- **R4. El hook de TLS existe solo en el VPS viejo.** Si ese servidor se pierde antes de P5.a, hay que reescribirlo. Se trae y se versiona en la preparación.
- **R5. Restos del ensayo adoptados como backups válidos** por `ReconciliarDumpHuerfanosAsync`. Mitigación: C2.
- **R6. El binario 0.5.3 trae migraciones posteriores al esquema del VPS viejo.** `MigrateAsync` las aplica en el arranque, como en cualquier deploy. Las que tienen guardianes de datos (catálogos) se cubren con el pre-vuelo del paso 10. Si otra migración tuviera un guardián nuevo, aparece en E1/E2, que usan datos reales. Por eso el ensayo es obligatorio.
- **R7. Los dumps de producción quedan en la máquina del operador.** Contienen todo, incluidos los hashes de contraseñas. Se guardan con 600/700 en `~/stockapp-backups-vps/`, fuera del repo. Cifrarlos queda como mejora.
- **R8. DNS mal configurado o certificado no emitido el día D.** No está en el camino crítico: P2 y P5 se hacen y se verifican días antes, en E2.
- **R9. Un backup en el mismo servidor que la base.** Sigue igual que hoy (§3). Durante los 14 días, el VPS viejo detenido y los dumps del operador son la copia externa de facto.

## 10. Preguntas abiertas que solo Martin puede responder

1. **El dominio nuevo** (nombre exacto) y quién tiene acceso al panel DNS para crear el registro `A` (P2).
2. **El ambiente de testing después del corte:** cuál de las opciones de §5.8, o ninguna.
3. **La ventana del corte:** qué día y a qué hora, coordinado con la instalación de las PCs del municipio, y la confirmación de que lo que haya en el VPS viejo al momento del dump son los datos a migrar (hoy, lo real de Ramiro).
4. **Confirmar o corregir** las decisiones del asistente que tienen costo visible: 14 días de rollback, puerto 8080 contra 443, y copiar el historial de backups.
