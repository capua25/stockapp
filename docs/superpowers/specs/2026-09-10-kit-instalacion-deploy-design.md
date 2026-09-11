# Kit de instalación en pendrive (LAN) y orquestador de deploy al VPS

Fecha: 2026-09-10
Estado: APROBADO — pendiente de plan de implementación

## Contexto y premisa corregida

El pedido original fue "un script de deploy para VPS y otro para servidor local". La premisa estaba equivocada: el deploy del VPS YA EXISTE y está verificado en producción (`deploy/publish-api.sh`, `install.sh`, `stockapp-api.service`, `docker-compose.postgres.yml`, `wait-for-postgres.sh`, `seed-demo.sh`, `.env.example`, `DEPLOY.md`).

Lo que falta de verdad es:
- (a) el lado local del deploy al VPS — hoy son 4 pasos a mano en 2 máquinas, sin pre-vuelo de migraciones ni backup previo;
- (b) el bootstrap de un servidor virgen para la LAN del municipio;
- (c) los arreglos de código que hacen que la instalación no mienta.

## Decisiones cerradas con el usuario

1. **SO del servidor: Linux, Ubuntu Server LTS, versión FIJA definida por el proveedor.** `install.sh`/systemd/compose ya están verificados (en Windows habría que rehacer todo con NSSM/servicio Windows/Docker Desktop) y el fingerprint de licencia en Linux (`/etc/machine-id`) es estable y probado, mientras que la implementación Windows nunca se probó en servidor real. Windows queda como camino alternativo SOLO si el municipio lo impone.
2. **Postgres en Docker, idéntico al VPS** — un solo camino que probar y mantener.
3. **Ejecuta el propio proveedor, presencialmente, sentado en el servidor.** No hace falta orquestador SSH para la LAN.
4. **Internet en el servidor: INCIERTO** (detrás de firewall, puertos desconocidos) → regla de diseño **offline-first con detección**. Nunca asumir `apt`.
5. **Alcance: kit autocontenido en pendrive.**
6. **Licencia: la clave privada NUNCA viaja al pendrive ni al servidor.** Sale el fingerprint, entra la licencia. Se asume acceso a la PC/laptop del proveedor el día de la instalación.

## Hallazgo habilitante: el fingerprint es público y calculable en bash

`GET /licencia/estado` es anónimo y está en la allowlist de `BloqueoLicenciaMiddleware` → siempre responde 200 con `{"activada":bool,"codigoMaquina":"A3F2-..."}`, incluso sin licencia. La API arranca viva pero bloqueada (423 en casi todo).

Fingerprint Linux = SHA-256 de `/etc/machine-id` (fallback `/var/lib/dbus/machine-id`), hex MAYÚSCULAS agrupado de a 4 con guiones. Ver `FingerprintMaquinaLinux.cs` y `FingerprintMaquinaBase.cs:11-33`.

Consecuencia de diseño: se puede calcular en bash ANTES de instalar nada, así que `00-preflight.sh` imprime el fingerprint apenas llegás y la licencia viaja en paralelo mientras seguís instalando. Cero tiempo muerto.

NO es estable ante reinstalación del SO (systemd regenera `machine-id`) → si reinstalan, hay que re-emitir licencia.

## Estructura del kit

```
stockapp-kit-<version>/
  LEEME.md
  00-preflight.sh    ← diagnostica, NO toca nada, imprime fingerprint
  01-bootstrap.sh    ← Docker + Postgres + .env + firewall
  02-instalar.sh     ← wrapper de install.sh (install.sh NO se modifica)
  03-verificar.sh    ← healthcheck end-to-end
  04-licencia.sh     ← fingerprint / activar
  servidor/          ← copia fiel de deploy/
  offline/  docker/*.deb, pgclient/*.deb, postgres-16-alpine.tar, stockapp-api-<v>-linux-x64.tar.gz
  clientes/ GestionMunicipal-win-Setup.exe, LEEME-clientes.md
```

Lo arma `deploy/armar-kit.sh <version>` en la máquina del proveedor, con internet.

**Flujo del día:** `00-preflight` (→ fingerprint) → `01-bootstrap` → `02-instalar` → `04-licencia activar` → `03-verificar` → instalar clientes + Configurador. El paso de licencia va ANTES de tocar los clientes.

## Reglas transversales de los scripts

`set -euo pipefail`, exit 0/1, todo lo impreso va también a `/var/log/stockapp-kit-<fecha>.log`, y TODOS idempotentes.

## Contratos

### `00-preflight.sh` (read-only)

| Verifica | Detalle |
|---|---|
| SO + versión | vs. la fijada en el kit |
| Arquitectura | x86_64 |
| systemd | presente |
| Docker | instalado / corriendo |
| Cliente Postgres | `postgresql-client-16` |
| Herramientas | `curl` |
| Puertos | 5080 y 5433 libres |
| Disco | >10GB en `/opt`, `/var/lib`, `/var/backups` |
| RAM | >2GB |
| Machine id | `/etc/machine-id` legible |
| Instalación previa | detecta `stockapp-api` ya instalado |
| Red | IP y si es DHCP o estática (DHCP = amarillo grande) |
| Internet saliente | informativo, no bloqueante |

Cierra imprimiendo el CÓDIGO DE MÁQUINA en un bloque destacado.

### `01-bootstrap.sh` (root)

1. Docker desde `offline/docker/*.deb` o `apt` si hay internet confirmado; verifica `docker info`.
2. `postgresql-client-16` + `curl` desde `offline/pgclient/` — esto es lo que hace que `install.sh` no toque la red después (`install.sh` solo llama a `apt` si esos paquetes faltan, ver `install.sh:241-258`).
3. `docker load < offline/postgres-16-alpine.tar`.
4. Compose a `/opt/stockapp/`, `docker compose up -d`, espera con `wait-for-postgres.sh`.
5. Genera `/etc/stockapp/.env` con permisos 600: `JWT_SECRET` y `POSTGRES_PASSWORD` con `openssl rand -base64 48`, pide admin user+password validando los mismos requisitos que la API (mín. 8, letra+número), clave pública del kit, `API_BIND=0.0.0.0`, `API_PORT=5080`.
6. Firewall.

**Guardia clave:** si ya existe `/etc/stockapp/.env` NO lo pisa. Regenerarlo con el volumen de Postgres ya creado deja la API sin conectar, y Npgsql no arrastra la connection string a las excepciones, así que el error no lo dice.

### `02-instalar.sh`

Wrapper que resuelve paths y llama `servidor/install.sh <tarball> /etc/stockapp/.env`. Verifica que `01` haya corrido. `install.sh` NO se toca.

### `03-verificar.sh` — 8 chequeos

1. `systemctl is-active`.
2. `systemctl cat stockapp-api | grep ASPNETCORE_URLS` ← el ÚNICO que distingue antes/después; un curl OK no prueba nada porque la API vieja ya escuchaba.
3. curl loopback a `/licencia/estado`.
4. curl a `<ip-lan>:5080/licencia/estado` ← prueba el bind real, único que detecta `API_BIND` en loopback.
5. `__EFMigrationsHistory` vs. migraciones del tarball.
6. `POST /auth/login` con el admin.
7. `GET /backups/salud`.
8. Licencia activada.

Cierra con la URL exacta para el Configurador y el recordatorio en rojo de copiarse `/etc/stockapp/.env` al pendrive.

### `04-licencia.sh`

`fingerprint` (lo lee de la API o lo calcula) y `activar <archivo>` (`POST /licencia/activar`, confirma con `GET /licencia/estado`, traduce errores: "emitida para otra máquina" → "¿reinstalaron el SO?").

## Los dos temas espinosos

**La IP: el script NO la toca.** Deliberado — tocar netplan en una red desconocida es cómo te quedás sin conectividad, y la política de IPs es del área de sistemas del municipio. El preflight detecta DHCP y grita; el LEEME documenta reserva DHCP por MAC en el router (preferible: lo hace sistemas, es reversible, no toca el servidor) o IP estática en netplan. RIESGO PRINCIPAL DE TODO EL DESPLIEGUE: si el servidor cambia de IP los clientes pierden conexión y hay que pasar el Configurador por cada PC a mano.

**El firewall pregunta antes de activarse.** `ufw` con 5080/tcp abierto solo desde la subred LAN detectada y confirmada. Postgres queda en `127.0.0.1:5433`, nunca expuesto. Si `ufw` está inactivo, el script pregunta antes de activarlo y mete la regla de SSH primero.

## Riesgo operativo mayor que cualquier script

Los backups van al mismo disco que la base; muere el disco, se van los dos, y no hay acceso al servidor. La solución ya está construida (`GET /backups` + descarga desde el desktop admin, verificados): falta convertirlo en PROCEDIMIENTO escrito con frecuencia definida. Lo mismo con el webhook de alerta: si el firewall bloquea la salida, el dead-man's-switch no existe; el reemplazo en LAN es `GET /backups/salud` visible en el desktop admin, y alguien tiene que mirarlo.

## `deploy/deploy-vps.sh` (orquestador local)

```
deploy/deploy-vps.sh <version> [--dry-run] [--sin-backup] [--con-soporte]
```

Pasos:
1. Pre-vuelo de migraciones por SSH — consulta de duplicados case-insensitive contra `stockapp-pg`; la migración del guardián hace `RAISE EXCEPTION` a propósito, `MigrateAsync()` no tiene try/catch y corre antes de `app.Run()`, y tras 5 fallos systemd deja la unit en `failed`.
2. Backup con la versión vieja viva + `scp` de bajada — el backup vive DENTRO de la API, si la API no levanta no hay backup.
3. `publish-api.sh`.
4. `scp` del tarball, y de `install.sh`/`.service` solo si cambiaron (`git diff`).
5. `ssh` + `install.sh`.
6. Verificación remota.

`--dry-run` corre solo el paso 1. SSH del VPS en puerto 34377 — va en variable, no hardcodeado. El default hace backup; existe `--sin-backup` porque el usuario prefiere no hacerlo, pero un script que por omisión deja sin punto de retorno está mal diseñado.

## Arreglos al código existente

1. **Configurador.** Hoy prueba con `GET /`, que da 423 sin licencia → advertencia amarilla falsa con todo sano. El fix NO es aceptar el 423: es probar contra **`GET /licencia/estado`** (allowlist, siempre 200, shape verificable, y de yapa permite mostrar "conecta pero la licencia no está activada"). Es el mismo fix que ya aplicó `install.sh` por el mismo motivo (comentario `install.sh:457-463`). Archivo: `tools/StockApp.Configurador/ProbadorConexion.cs`. TDD + verificación por mutación (revertir el fix debe poner el test en rojo).
2. **Test de paridad del fingerprint bash ↔ C#.** Correr el snippet bash y comparar con `FingerprintMaquinaLinux.CodigoAgrupado` sobre el mismo input. Bordes: el `\n` final de `/etc/machine-id`, mayúsculas del hex, agrupado de a 4. Sin esto, el preflight puede dar un fingerprint inútil y se descubre en el municipio.
3. **`deploy/publish-licencias-cli.sh`.** Publicar la CLI self-contained (hoy necesita SDK .NET 10; el csproj no tiene RID ni SelfContained).

## Estrategia de prueba

Un kit que nunca se probó en una máquina limpia no es un kit. VM descartable con snapshot (multipass/VirtualBox, Ubuntu de la versión fijada): correr de punta a punta en VM virgen, restaurar, repetir.

**Verificación POR MUTACIÓN:**

| Mutación | Resultado esperado |
|---|---|
| Sacar `API_BIND` | paso 4 de `03-verificar.sh` en rojo |
| Apagar `stockapp-pg` antes de instalar | `01-bootstrap.sh` no sigue |
| Licencia de otra máquina | mensaje correcto de `04-licencia.sh` |
| Correr `02` sin `01` | aborta claro |
| Correr todo dos veces | idempotente, sin daño |
| Puerto 5080 ocupado | `00-preflight.sh` en rojo antes de tocar nada |

Si alguna queda en verde, el script no detecta nada.

## Entregas

- **E1**: kit LAN completo + arreglo del Configurador + paridad del fingerprint (bloquea la instalación real).
- **E2**: `deploy-vps.sh` con pre-vuelo y backup (sirve ya para el deploy pendiente de la migración del guardián).
- **E3**: procedimientos escritos — bajada periódica de backups, monitoreo de `/backups/salud`.
