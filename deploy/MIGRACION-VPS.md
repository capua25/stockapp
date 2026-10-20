# Migración de los datos de producción al VPS nuevo

Runbook operativo: de "VPS nuevo en blanco" a "el municipio trabajando sobre el servidor nuevo
con todos sus datos". Diseño y razones: `docs/superpowers/specs/2026-10-20-migracion-vps-design.md`.
Estilo de `deploy/DEPLOY.md`: cada paso tiene su casilla, el comando exacto, el resultado
esperado y qué hacer si no coincide.

**Estado:** E1 (ensayo local) lo corre un agente y deja sus tiempos abajo. E2 (ensayo general en
el VPS nuevo) y el corte quedan **pendientes**: necesitan el VPS nuevo, el dominio y la fecha.

Marcadores usados en este documento: `<dominio-nuevo>`, `<ip-nueva>`, `<puerto-nuevo>` (SSH del
VPS nuevo), `<usuario>` (usuario SSH del VPS nuevo), `<fecha>` (AAAAMMDD) y `<ts>` (el timestamp
UTC que lleva el nombre del volcado). Ningún valor real vive en el repo.

## Reglas que valen en TODO el documento

- El VPS viejo es **compartido con otra aplicación**. Sobre él solo se corren los scripts de
  este runbook, que operan por nombre sobre `stockapp-api`, `stockapp-pg` y `/var/lib/stockapp`.
  Nada global de Docker, de systemd ni de certbot, y el proxy de la otra app no se toca.
- **`deploy/deploy-vps.sh` sin `--sin-backup` deja el backup en el `/tmp` del host** (un lugar
  legible por los demás usuarios del servidor compartido). Por eso, en la migración, **NUNCA se
  corre `deploy-vps.sh` contra el VPS viejo**. Contra el VPS nuevo se usa siempre con
  `--sin-backup` (en E2 y en C7 no hay nada que respaldar).
- Los dumps de producción contienen todo, incluidos los hashes de contraseñas. Viven en
  `~/stockapp-backups-vps/` (700, fuera del repo) y nunca se commitean.
- Cada comando de abajo que apunta a un servidor lo decide el `.env` del operador. Con dos VPS en
  juego, **mirá siempre qué archivo estás cargando** (ver la tabla siguiente).

### Los tres archivos `.env` (no son el mismo)

| Archivo | Dónde | Para qué | Cómo se elige |
|---|---|---|---|
| `deploy/.env` | máquina del operador | apunta al VPS **viejo** (y al de testing en el ensayo) | es el default de los scripts |
| `deploy/.env.vps-nuevo` | máquina del operador | apunta al VPS **nuevo** (`deploy/.env*` está ignorado por git) | `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo <script>` |
| `~/stockapp-deploy/.env` | servidor nuevo (600) | configura el servicio real: lo lee `install.sh` y `docker compose` | es el que usa `restaurar-backup.sh` por defecto |

El `.env` del operador se carga con `source` **después** del entorno: si define `VPS_HOST`, **pisa**
al `VPS_HOST` que exportaste. No alcanza con exportar variables para cambiar de VPS: hay que usar
`STOCKAPP_ENV_LOCAL`. Si la variable apunta a un archivo que no existe, los scripts abortan antes
de abrir ninguna conexión.

## Lo que Martin tiene que cargar

Ningún agente escribe estos archivos: son tuyos. Solo nombres de variables, nunca valores en el repo.

- **En `deploy/.env` (el existente; lo usan el ensayo E1 y el corte contra el VPS viejo):**
  - `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT` (sin default a propósito: si faltan, los scripts abortan);
  - `API_SCHEME`, `API_HOSTNAME`, `API_PORT` (para que los curl remotos hablen con el esquema y el
    puerto reales de ese VPS);
  - `VPS_ADMIN_PASSWORD` (y `VPS_ADMIN_USER` si no es el de `BOOTSTRAP_ADMIN_USER`) de un usuario
    con el permiso `GestionarDiagnostico`. Alternativa: un `BOOTSTRAP_PASSWORD` que **todavía** sea
    válido, pero en una instalación viva casi seguro ya se cambió.
  - La contraseña puede ir en el `.env` **o** como variable de entorno del comando puntual
    (`VPS_ADMIN_PASSWORD=… deploy/bajar-backup-vps.sh …`, sin `export`). Ojo: si el `.env` define la
    clave, **pisa** a la del entorno.
- **En `deploy/.env.vps-nuevo` (nuevo, ver P7):** `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`,
  `POSTGRES_USER`, `POSTGRES_DB`, `API_SCHEME`, `API_HOSTNAME`, `API_PORT` y, si hace falta,
  `VPS_ADMIN_USER` / `VPS_ADMIN_PASSWORD`.
- **En `~/stockapp-deploy/.env` del servidor nuevo (ver P6):** `POSTGRES_USER`, `POSTGRES_PASSWORD`,
  `POSTGRES_DB`, `JWT_SECRET`, `BOOTSTRAP_ADMIN_USER`, `BOOTSTRAP_PASSWORD`,
  `LICENCIA_CLAVE_PUBLICA_BASE64`, `API_BIND`, `API_SCHEME`, `API_PORT`, `API_HOSTNAME`,
  `Kestrel__Certificates__Default__Path`, `Kestrel__Certificates__Default__KeyPath`,
  `Licencia__Habilitado` y `RateLimiting__Login__PermitLimit`, más los overrides que liste
  `overrides-api-env.txt`.

## Tiempos

Los llena E1 (lo corre un agente) y E2 los completa. El corte real usa los de E2 para dimensionar
la ventana.

| Paso | E1 (local) | E2 (VPS nuevo) | Corte real |
|---|---|---|---|
| Bajar el backup real (`bajar-backup-vps.sh`) | 13 s (dump de 95 KB; el `--id` del historial, 11 s) | n/a | n/a |
| `ensayo-local.sh preparar` | 5 s | n/a | n/a |
| Volcado (`ensayo-local.sh volcar` / `volcado-final-vps.sh`, C3) | 2 s | _pendiente_ | _pendiente_ |
| `restaurar-backup.sh` (C5) | 14 s sobre la base vacía; 5 s con `--recrear-base` | _pendiente_ | _pendiente_ |
| Arranque de la API contra la base restaurada (C7) | 28 s hasta `GET /` OK (incluye restore y build de `dotnet run` con un `HOME` vacío) | _pendiente_ | _pendiente_ |
| Verificación (C8, V1 a V10) | menos de 10 s (V4, V5, V8 y `/updates`; `--verificar-post-arranque`, 0,4 s) | _pendiente_ | _pendiente_ |
| Ventana total con el VPS viejo sin API (C3 a C9) | n/a | _pendiente_ | _pendiente_ |

**Resultado de E1 (2026-10-20): OK.** Backup real de testing (31 tablas, 150 filas, 25 migraciones,
última `20260910225922_AgregaIndiceFuncionalNombreCatalogos`): `preparar`, `volcar` y `restaurar` con
exit 0, conteos idénticos al manifiesto, secuencias y catálogos OK; segundo `restaurar` sin
`--recrear-base` → 3, con `--recrear-base` → 0 y dump de seguridad. API local: `GET /` OK, sin
excepciones de migración, V4 `activada: true`, V5 login real con token, V8 historial servido (200) y
`POST /backups` → 202 con una corrida `Exitosa` en 1 s, `/updates` sirve el feed,
`--verificar-post-arranque` exit 0 (`CorridasBackup` 11 → 11: el catch-up y el `POST` sumaron dos
corridas y la retención borró dos). Limpieza: cero contenedores, volúmenes y redes `stockapp-ensayo-*`,
`docker ps` igual al del inicio. **E2 y el corte quedan PENDIENTES**: necesitan el VPS nuevo, el
dominio y la fecha.

---

# Preparación (días antes)

Nada de esto toca el VPS viejo.

### P1. VPS nuevo

- [ ] **P1.** Ubuntu 24.04 LTS (`install.sh` instala `postgresql-client-16` por apt; en 22.04 no está).
  Usuario SSH con clave, `sudo` sin contraseña y, desde P4, miembro del grupo `docker`.

```bash
ssh -p <puerto-nuevo> -o BatchMode=yes <usuario>@<ip-nueva> 'sudo -n true && echo OK'
```

Esperado: `OK`. Si pide contraseña o falla: arreglar el `sudoers` o la clave antes de seguir; todos
los scripts asumen `sudo -n`. Anotá el puerto SSH: va en `VPS_SSH_PORT`.

### P2. DNS

- [ ] **P2.** Registro `A` de `<dominio-nuevo>` → `<ip-nueva>`, TTL 300 s, creado **días antes**.

```bash
dig +short <dominio-nuevo>
```

Esperado: exactamente `<ip-nueva>`. Si no: esperar la propagación o revisar el panel DNS; sin esto
Let's Encrypt (P5) no puede emitir.

### P3. Firewall (`ufw`) del VPS nuevo

- [ ] **P3.** El SSH se habilita **antes** de `ufw enable`, o te quedás afuera.

```bash
sudo ufw allow <puerto-nuevo>/tcp
sudo ufw allow 80/tcp      # HTTP-01 de Let's Encrypt, al emitir y al renovar
sudo ufw allow 8080/tcp    # la API
sudo ufw enable
sudo ufw status numbered
```

Esperado: las tres reglas (y sus pares IPv6). Es el modo "expuesto" de `DEPLOY.md` (sección
"Firewall"), con `API_BIND=0.0.0.0`.

### P4. Docker

- [ ] **P4.** No se usa `deploy/kit/01-bootstrap.sh`: es el bootstrap del kit LAN (interactivo, sin TLS,
  pensado para trabajar offline). El VPS sigue el layout de `DEPLOY.md` sección 2:
  compose y `.env` en `~/stockapp-deploy/`.

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker <usuario>
mkdir -p ~/stockapp-deploy
```

Cerrá la sesión SSH y volvé a entrar (el grupo recién se aplica en la sesión nueva). Verificación:
`docker ps` sin `sudo` → tabla vacía, sin error de permisos.

### P5. TLS (Let's Encrypt, HTTP-01 `--standalone`)

- [ ] **P5.a** Instalar el deploy-hook versionado (`deploy/tls/stockapp-tls-deploy-hook.sh`, ya viene en
  el repo; no hace falta traerlo de ningún servidor). Desde tu checkout:

```bash
ssh -p <puerto-nuevo> <usuario>@<ip-nueva> 'mkdir -p ~/stockapp-deploy/deploy/tls'
scp -P <puerto-nuevo> deploy/tls/stockapp-tls-deploy-hook.sh <usuario>@<ip-nueva>:~/stockapp-deploy/deploy/tls/
# en el servidor nuevo:
cd ~/stockapp-deploy
sudo install -m 0755 deploy/tls/stockapp-tls-deploy-hook.sh /usr/local/sbin/
```

Esperado: `ls -l /usr/local/sbin/stockapp-tls-deploy-hook.sh` → `-rwxr-xr-x root root`.

- [ ] **P5.b** Crear el usuario `stockapp` **antes** de emitir: el hook hace `chown stockapp:stockapp`.
  Es el mismo comando que `install.sh`, que después lo detecta existente y no lo recrea.

```bash
sudo useradd --system --create-home --home-dir /var/lib/stockapp --shell /usr/sbin/nologin stockapp
id stockapp
```

Orden obligatorio: con `API_SCHEME=https`, `install.sh` aborta si los PEM no existen.

- [ ] **P5.c** Emitir el certificado y registrar el hook (queda como `renew_hook` de **este** certificado):

```bash
sudo apt-get install -y certbot
sudo certbot certonly --standalone -d <dominio-nuevo> --deploy-hook /usr/local/sbin/stockapp-tls-deploy-hook.sh
sudo ls -l /var/lib/stockapp/tls/
```

Esperado: `fullchain.pem` y `privkey.pem`, ambos `stockapp stockapp` con modo `-rw-------`. En esta
primera emisión la API todavía no existe: el hook copia los PEM y su `systemctl restart stockapp-api`
falla (la unit no está instalada), así que certbot puede avisar que el hook devolvió error. Es
esperado; lo que importa es que los PEM estén copiados. Si no están: el certificado quedó emitido en
`/etc/letsencrypt/live/<dominio-nuevo>/`; corré el hook a mano con
`sudo RENEWED_LINEAGE=/etc/letsencrypt/live/<dominio-nuevo> /usr/local/sbin/stockapp-tls-deploy-hook.sh`
(sale con 2 si falta algún PEM, sin copiar ni reiniciar nada).

- [ ] **P5.d** (se hace **después de E2**, cuando la API ya existe) Probar la renovación **de verdad**.
  `certbot renew --dry-run` **no ejecuta los hooks**, así que no prueba nada del hook:

```bash
sudo certbot renew --force-renewal --cert-name <dominio-nuevo>
systemctl is-active stockapp-api
```

Esperado: `active`, y `sudo journalctl -u stockapp-api -n 20 --no-pager` muestra un arranque nuevo.
Gasta cupo de emisión: no repetirlo sin necesidad. Alternativa DNS-01 (si el proveedor no deja abrir
el 80, o para emitir antes de que el DNS apunte a la IP nueva): requiere plugin del proveedor DNS o un
TXT manual, y la renovación manual no es automática; por eso queda solo como alternativa.

### P6. `.env` del servidor nuevo (`~/stockapp-deploy/.env`, 600)

- [ ] **P6.** A partir de `deploy/.env.example`. En el servidor:

```bash
install -m 600 /dev/null ~/stockapp-deploy/.env
# generá los secretos NUEVOS (no se reutilizan los del VPS viejo):
openssl rand -base64 24    # POSTGRES_PASSWORD
openssl rand -base64 48    # JWT_SECRET
```

Contenido (editá `~/stockapp-deploy/.env` con tu editor):

| Variable | Valor |
|---|---|
| `POSTGRES_USER` / `POSTGRES_DB` | `stockapp` / `stockapp` |
| `POSTGRES_PASSWORD`, `JWT_SECRET` | **nuevos** (los comandos de arriba) |
| `BOOTSTRAP_ADMIN_USER`, `BOOTSTRAP_PASSWORD` | los exige `install.sh`; con la base restaurada se **ignoran** (el seed no hace nada si ya hay usuarios) |
| `LICENCIA_CLAVE_PUBLICA_BASE64` | el **mismo** valor que `deploy/.env`: el reset de admin firmado sigue usando esa clave, y con ella la clave privada de `~/stockapp-claves` sigue sirviendo |
| `API_BIND`, `API_SCHEME`, `API_PORT`, `API_HOSTNAME` | `0.0.0.0`, `https`, `8080`, `<dominio-nuevo>` |
| `Kestrel__Certificates__Default__Path` / `…__KeyPath` | `/var/lib/stockapp/tls/fullchain.pem` / `/var/lib/stockapp/tls/privkey.pem` |
| `Licencia__Habilitado` | `false` (el licenciamiento queda apagado en el VPS nuevo) |
| `RateLimiting__Login__PermitLimit` | `10` (lo recomendado para el modo expuesto) |
| overrides | los **nombres** que liste `overrides-api-env.txt` del volcado (ver C3): se recrean a mano, con su valor |

Esperado: `stat -c '%a' ~/stockapp-deploy/.env` → `600`, y ningún valor que empiece con `CAMBIAR`.

### P7. `.env` del operador para el VPS nuevo (`deploy/.env.vps-nuevo`)

- [ ] **P7.** En tu máquina, a partir de la lista de "Lo que Martin tiene que cargar". `chmod 600`.

```bash
( set -a; source deploy/.env.vps-nuevo; set +a
  ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" 'sudo -n true && echo OK' )
```

Esperado: `OK`. Si dice que falta una variable o responde el VPS equivocado, el archivo está mal.

### P8. Postgres del VPS nuevo

- [ ] **P8.** Copiar el compose y el `.env` del servidor (P6) y levantar el contenedor:

```bash
scp -P <puerto-nuevo> deploy/docker-compose.postgres.yml <usuario>@<ip-nueva>:~/stockapp-deploy/
# en el servidor nuevo:
cd ~/stockapp-deploy
docker compose --env-file .env -f docker-compose.postgres.yml up -d
docker compose --env-file .env -f docker-compose.postgres.yml ps
docker exec stockapp-pg psql -U stockapp -d stockapp -c '\dt'
```

Esperado: `healthy`, y `Did not find any relations.` (la imagen crea `POSTGRES_DB` vacía). Con tablas ya
creadas: algo arrancó la API contra esta base; no sigas, revisalo.

### P9. Ensayo general

- [ ] **P9.** Correr **E2** (más abajo). Deja la API 0.5.3 instalada, TLS andando, el feed 0.5.3 publicado
  y el hook probado (P5.d).

### P10. Instalador de las PCs

- [ ] **P10.** El `Setup.exe` de la **misma** 0.5.3 que se publica en el feed (`build/pack-win.ps1`, ver
  `build/README-empaquetado.md`). Las PCs se instalan **a la vez** que la migración, con el
  Configurador apuntando a `https://<dominio-nuevo>:8080`. El `Setup.exe` no está firmado:
  Windows va a mostrar el aviso de **SmartScreen** ("Windows protegió su PC"); se avisa de antemano
  a quien instale y se sigue con "Más información" → "Ejecutar de todas formas".

---

# Ensayos

### E1. Ensayo local con un backup real (solo lectura sobre el VPS actual)

Lo corre un agente; **no detiene** el servidor actual: solo lee (login, `GET /backups` y la descarga
del contenido por el stdout de ssh). El VPS viejo se simula en tu máquina con contenedores propios
`stockapp-ensayo-*`. Nunca se toca el `stockapp-pg` de desarrollo.

- [ ] **E1.1** Carpeta de trabajo, fuera del repo y fuera de `/mnt`:

```bash
TRABAJO=~/stockapp-backups-vps/ensayo-$(date -u +%Y%m%d)
mkdir -p -m 700 "$TRABAJO"
docker ps --format '{{.Names}}' > "$TRABAJO/docker-ps-inicio.txt"
```

Esa foto se toma con la máquina quieta: justo después de un `dotnet test` suele quedar unos segundos
un `testcontainers-ryuk-*` que desaparece solo, y su ausencia posterior da `exit 9` en `volcar` o
`limpiar` (pasó en el primer intento de E1).

- [ ] **E1.2** Backup real, solo lectura (la tabla de corridas se imprime antes de elegir):

```bash
STOCKAPP_ENV_LOCAL=deploy/.env deploy/bajar-backup-vps.sh --salida "$TRABAJO/real"
STOCKAPP_ENV_LOCAL=deploy/.env deploy/bajar-backup-vps.sh --salida "$TRABAJO/historial" --id <id-anterior>
```

Esperado: exit 0 y un `backup_*.dump` ≥ 1024 bytes. Exit 3 por login rechazado: la credencial de
"Lo que Martin tiene que cargar" no sirve; el agente se detiene y te avisa.

- [ ] **E1.3** Volcado y restauración sobre contenedores descartables:

```bash
deploy/ensayo/ensayo-local.sh preparar "$TRABAJO/real/backup_<…>.dump" --trabajo "$TRABAJO/e1" --historial "$TRABAJO/historial/backup_<…>.dump"
deploy/ensayo/ensayo-local.sh volcar --trabajo "$TRABAJO/e1"
deploy/ensayo/ensayo-local.sh restaurar --trabajo "$TRABAJO/e1"
```

Esperado: los tres con exit 0, `estado/stockapp-api.habilitado` = `masked` y conteos idénticos al
manifiesto. Un segundo `restaurar` sin `--recrear-base` sale con 3 (base no vacía); con
`--recrear-base` sale con 0 y deja un `pre-recrear-stockapp-*.dump` en `$TRABAJO/e1/seguridad`.

- [ ] **E1.4** API 0.5.3 local contra la base restaurada (puerto 5199), historial extraído (C6 local):

```bash
mkdir -p "$TRABAJO/e1/home-api/.local/share/StockApp" "$TRABAJO/e1/updates"
printf '{"Assets":[]}\n' > "$TRABAJO/e1/updates/releases.ensayo.json"
tar -C "$TRABAJO/e1/home-api/.local/share/StockApp" -xpf "$TRABAJO/e1/salida/backups-historial.tar"
HOME="$TRABAJO/e1/home-api" ASPNETCORE_URLS=http://127.0.0.1:5199 \
ConnectionStrings__Default="Host=127.0.0.1;Port=5434;Database=stockapp;Username=stockapp;Password=$(cat "$TRABAJO/e1/pg-password")" \
Licencia__Habilitado=false Jwt__Secret="$(openssl rand -base64 48)" Bootstrap__AdminUser=ensayo Bootstrap__Password=Ensayo12345 Actualizaciones__Directorio="$TRABAJO/e1/updates" \
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --no-launch-profile > "$TRABAJO/e1/api.log" 2>&1 &
echo $! > "$TRABAJO/e1/api.pid"
curl -fsS http://127.0.0.1:5199/
curl -fsS http://127.0.0.1:5199/updates/releases.ensayo.json
```

Sin `Actualizaciones__Directorio` la API usa `/var/lib/stockapp/updates`, que en tu máquina no se
puede crear: `/updates` queda en 404 y no prueba nada. Con el `HOME` descartable, `dotnet run` baja
de nuevo todos los paquetes NuGet a `$TRABAJO/e1/home-api` (unos 330 MB, y el arranque tarda más):
se borran con `$TRABAJO` en E1.7. Los logs de la API van a
`$TRABAJO/e1/home-api/.local/share/StockApp/logs/`, no a `api.log`.

Esperado (en menos de 120 s): `{"status":"ok","service":"StockApp.Api"}` y `api.log` sin excepciones de
migración.

- [ ] **E1.5** V4 a V8 contra esa API local, y `--verificar-post-arranque`:

```bash
deploy/restaurar-backup.sh "$TRABAJO/e1/salida/volcado-<ts>.dump" --manifiesto "$TRABAJO/e1/salida/volcado-<ts>.manifiesto" \
  --contenedor stockapp-ensayo-pg --base stockapp --usuario stockapp --verificar-post-arranque
```

Esperado: exit 0 (`CorridasBackup` puede crecer; `LogsAuditoria` ≥ manifiesto).

- [ ] **E1.6** Limpieza verificada. La API se mata **por PID exacto** y se confirma:

```bash
kill "$(cat "$TRABAJO/e1/api.pid")"; ps -p "$(cat "$TRABAJO/e1/api.pid")"
deploy/ensayo/ensayo-local.sh limpiar --trabajo "$TRABAJO/e1"
docker ps --format '{{.Names}}' | diff - "$TRABAJO/docker-ps-inicio.txt" && echo IGUAL
rm -f "$TRABAJO/e1/token"
```

Esperado: `ps` sin el proceso, `limpiar` con exit 0 y `IGUAL`. **Antes** del `kill`, anotá los hijos
con `ps --ppid "$(cat "$TRABAJO/e1/api.pid")" -o pid=,comm=`: además de `StockApp.Api`, `dotnet run`
deja dos nodos de MSBuild (`dotnet`, con `/nodeReuse:true`) que sobreviven al padre. Matalos a todos
por esos PID exactos y confirmá con `ps -p <pid1>,<pid2>,…` que no queda ninguno.

**Exit 9 y falsos rojos.** `volcar` y `limpiar` comparan la lista de `docker ps` del principio con la
del final y la igualdad es estricta a propósito. Si **otro proceso** crea o borra contenedores mientras
corren (otro `dotnet test`, un compose levantado en paralelo), la comparación da `exit 9` aunque el
script no haya tocado nada ajeno. No es un fallo del script: repetí con la máquina quieta. Por la
misma razón, los tests de Kit (`EnsayoCompleto_*`) se corren solos, no en paralelo con otra suite que
use Docker.

- [ ] **E1.7** **Borrar los datos reales del ensayo.** `$TRABAJO` queda con una COPIA de producción
  (dumps, historial, `pg-password`, dumps de seguridad); `limpiar` no la borra a propósito. Primero
  confirmá qué hay y cuánto pesa, y que los dumps que querés conservar ya están archivados en otro lado:

```bash
ls -la "$TRABAJO"; du -sh "$TRABAJO"
```

Solo si es lo que esperabas, y con la ruta escrita a mano (nunca con una variable vacía en el medio):

```bash
rm -rf <trabajo>
```

Donde `<trabajo>` es la ruta literal que te mostró el `ls`. Esperado: `ls <trabajo>` → `No such file or
directory`.

### E2. Ensayo general en el VPS nuevo

Se hace después de P1 a P8. Es la secuencia completa del corte (C3 a C8) con estas diferencias, y **se
cronometra cada paso** (tabla "Tiempos"):

- [ ] **E2.1** `volcado-final-vps.sh --ensayo` contra el VPS viejo (usa `deploy/.env`). **Detiene testing
  alrededor de un minuto**: el `trap` vuelve a arrancar la API al salir, pero solo si estaba activa al
  empezar. Avisá a quien use testing.

```bash
unset STOCKAPP_ENV_LOCAL
deploy/volcado-final-vps.sh --salida ~/stockapp-backups-vps/ensayo-e2-<fecha>/ --ensayo
```

Esperado: exit 0, y después `systemctl is-active stockapp-api` en el viejo → `active` (no enmascara).
- [ ] **E2.2** C4 y C5 (copiar y restaurar). La base del VPS nuevo está vacía, así que alcanza sin `--recrear-base`.
- [ ] **E2.3** C6 (historial).
- [ ] **E2.4** En lugar de C7: el **primer** deploy contra el VPS nuevo. Su paso 2 necesita una API viva con
  login y en un VPS en blanco no la hay, por eso `--sin-backup`:

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh 0.5.3 --sin-backup
```

El marcador del último deploy es **por host** (`deploy/dist/.ultimo-deploy-vps-commit-<host>`): el
primer deploy a un host nuevo siempre copia `install.sh`. El marcador viejo sin sufijo se ignora.
- [ ] **E2.5** El feed arranca con la 0.5.3:
`STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo build/publicar-release.sh 0.5.3`
- [ ] **E2.6** P5.d (`certbot renew --force-renewal --cert-name <dominio-nuevo>`) y verificar que la API
  reinicia y sigue sirviendo con el certificado nuevo.
- [ ] **E2.7** V1 a V9 completos, incluido el smoke de la GUI.
- [ ] **E2.8** Anotar los tiempos. El VPS nuevo queda así hasta el corte, que repite C1 a C8 con `--recrear-base`.

---

# Corte (día D)

Los tiempos salen de E2. **Antes de cada comando de abajo, comprobá qué `.env` carga**: sin
`STOCKAPP_ENV_LOCAL` es `deploy/.env`, o sea el VPS **viejo**.

### C0. Confirmación

- [ ] **C0.** Martin confirma la ventana, avisa que testing deja de existir en el servidor viejo y que
  **no se hacen deploys** al VPS viejo durante el corte (y, recordatorio, `deploy-vps.sh` nunca se corre
  contra él). Las PCs del municipio se instalan en esta misma ventana.

### C1. VPS nuevo: detener la API que dejó E2

- [ ] **C1.**

```bash
sudo systemctl stop stockapp-api
systemctl is-active stockapp-api
```

Esperado: `inactive`. Si dice otra cosa, no sigas: una API viva escribiría sobre la base que vas a recrear.

### C2. VPS nuevo: limpiar los restos del ensayo

- [ ] **C2.** Solo los `backup_*.dump` y `*.tmp` de la carpeta de backups, por patrón y dentro de esa ruta.
  Por qué: `ReconciliarDumpHuerfanosAsync` adoptaría como corridas válidas los dumps del ensayo,
  que no tienen fila en la base restaurada. El feed (`/var/lib/stockapp/updates/`) y los certificados no se tocan.

```bash
sudo find /var/lib/stockapp/.local/share/StockApp/backups -maxdepth 1 -type f \( -name 'backup_*.dump' -o -name '*.tmp' \) -print
# revisá la lista; si es lo esperado, el mismo comando con -delete en lugar de -print:
sudo find /var/lib/stockapp/.local/share/StockApp/backups -maxdepth 1 -type f \( -name 'backup_*.dump' -o -name '*.tmp' \) -delete
```

Esperado: la segunda corrida no imprime nada al repetir la primera.

### C3. Máquina del operador, contra el VPS viejo: congelar y volcar

- [ ] **C3.** Congela la API, vuelca dentro del contenedor, arma el manifiesto y trae todo verificado.
  **Sin `--ensayo`**: esta vez la API vieja queda detenida y enmascarada.

```bash
unset STOCKAPP_ENV_LOCAL     # el default es deploy/.env = el VPS VIEJO
deploy/volcado-final-vps.sh --salida ~/stockapp-backups-vps/migracion-<fecha>/
```

Qué hace, en orden: relevamiento de solo lectura → `systemctl stop stockapp-api` → verifica que la
unit sea `/etc/systemd/system/stockapp-api.service` (`FragmentPath`) → la mueve a
`stockapp-api.service.congelado` → `systemctl daemon-reload` → `systemctl -q mask stockapp-api` (el
`systemctl mask stockapp-api` a secas falla sobre una unit regular: "already exists") →
`systemctl is-enabled stockapp-api` tiene que dar `masked` → sin conexiones a la base → `pg_dump`
dentro del contenedor → manifiesto → traída por el stdout de ssh con sha256 verificado → historial de
backups → nombres de los overrides de `api.env`. **Ningún dump toca el `/tmp` del host.**

Esperado: exit 0 y, en `<salida>`, en modo 600: `volcado-<ts>.dump`, `volcado-<ts>.manifiesto`,
`backups-historial.tar` y `overrides-api-env.txt` (solo nombres: son los que hay que recrear a mano en
el `.env` del servidor nuevo, P6).

| Exit | Significado | Qué hacer |
|---|---|---|
| 2 | uso / argumentos | corregir el comando |
| 3 | precondición (config, sudo, API sigue activa tras el stop, conexiones abiertas) | leer el mensaje; si ya se detuvo la API, ver R-A |
| 4 | la traída no es válida (sha ≠, tamaño chico, tar ilegible); el archivo local se borró | repetir C3 (con la unit ya enmascarada no se mueve nada más) |
| 8 | no se pudo consultar (ssh/systemctl/docker/psql); también la unit no está donde `install.sh` la deja | no insistir a ciegas: investigar |
| 9 | `docker ps` final ≠ inicial | otro proceso tocó contenedores; revisar que no fue el script |

**El script imprime en qué estado dejó la API y los comandos exactos para volver atrás (R-A)** en
cada fallo posterior al `stop` y en el final feliz: guardá esa salida.

### C4. Copiar al VPS nuevo

- [ ] **C4.**

```bash
ssh -p <puerto-nuevo> <usuario>@<ip-nueva> 'mkdir -p -m 700 ~/stockapp-deploy/migracion'
scp -P <puerto-nuevo> ~/stockapp-backups-vps/migracion-<fecha>/volcado-<ts>.dump \
  ~/stockapp-backups-vps/migracion-<fecha>/volcado-<ts>.manifiesto \
  ~/stockapp-backups-vps/migracion-<fecha>/backups-historial.tar \
  deploy/restaurar-backup.sh <usuario>@<ip-nueva>:~/stockapp-deploy/migracion/
ssh -p <puerto-nuevo> <usuario>@<ip-nueva> 'cd ~/stockapp-deploy/migracion && chmod 600 volcado-* backups-historial.tar && chmod 755 restaurar-backup.sh'
```

Esperado: sin errores. `restaurar-backup.sh` es autocontenido (se copia solo, sin el repo) y verifica
el sha256 y el tamaño contra el manifiesto **antes** de tocar nada.

### C5. VPS nuevo: restaurar

- [ ] **C5.** La base trae los datos del ensayo, así que `--recrear-base` toma primero un dump de
  seguridad. Con la API detenida (C1):

```bash
cd ~/stockapp-deploy
./migracion/restaurar-backup.sh migracion/volcado-<ts>.dump --manifiesto migracion/volcado-<ts>.manifiesto \
  --recrear-base stockapp --dir-seguridad ~/stockapp-deploy/migracion
```

Esperado: exit 0, el resumen con conteos idénticos al manifiesto, secuencias OK, sin duplicados, y
"API NO arrancada". El script **nunca** arranca la API.

| Exit | Qué pasó | Qué hacer |
|---|---|---|
| 3 | precondición: API activa, contenedor fuera de patrón, base no vacía sin `--recrear-base`, conexiones abiertas | corregir y repetir |
| 4 | dump inválido (sha, `--list`, tablas ≠ manifiesto) | volver a C3/C4: se trajo mal |
| 5 | falló `pg_restore`: la base quedó **vacía** | repetir tal cual |
| 6 / 7 | conteos, migración, secuencias o duplicados: la base **quedó restaurada** | no arrancar la API; investigar; repetir con `--recrear-base` (guarda otro dump de seguridad) |
| 8 | no se pudo consultar | investigar, no asumir "vacío" |
| 9 | falló el dump de seguridad: **no se borró nada** | investigar antes de reintentar |

### C6. VPS nuevo: historial de backups

- [ ] **C6.**

```bash
sudo tar -C /var/lib/stockapp/.local/share/StockApp -xpf ~/stockapp-deploy/migracion/backups-historial.tar
sudo chown -R stockapp:stockapp /var/lib/stockapp/.local/share/StockApp/backups
```

Esperado: archivos 640 y carpeta 750 de `stockapp:stockapp`. Con `-p` se conservan los mtime, así que
`ReconciliarDumpHuerfanosAsync` no los trata como recién creados (y de todos modos tienen fila en `CorridasBackup`).

### C7. VPS nuevo: arrancar la API

- [ ] **C7.** E2 ya instaló la 0.5.3:

```bash
sudo systemctl start stockapp-api
sudo journalctl -u stockapp-api -n 50 --no-pager
```

Si desde E2 se instaló otra versión, en su lugar:
`STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh 0.5.3 --sin-backup` (su paso 1 repite el
pre-vuelo sobre la base restaurada). La API migra hacia adelante si el binario es más nuevo que el dump.
Esperado: `Application started`, sin excepciones de migración.

### C8. Verificación

- [ ] **C8.** V1 a V10, punto por punto (sección siguiente). Si alguno falla, **no se pasa a C9**.

### C9. Go

- [ ] **C9.** Las PCs se instalan con el `Setup.exe` 0.5.3 (aviso de SmartScreen: ver P10) y el
  Configurador apunta a `https://<dominio-nuevo>:8080`. **La primera escritura real en el VPS nuevo es el
  punto de no retorno barato**: hasta ahí R-A es limpio.

### C10. Cierre

- [ ] **C10.** Anotar acá la fecha del corte y la fecha de fin del rollback (D + 14 días):
  corte `____-__-__`, fin del rollback `____-__-__`.

---

# Verificación (C8)

Cada punto con su comando. Un resultado vacío, o un error de conexión, es "no se pudo verificar", no "OK".

- [ ] **V1.** `restaurar-backup.sh` salió con 0 en C5: sha OK, `--list` OK, conteos **idénticos** al manifiesto,
  secuencias OK, sin duplicados.
- [ ] **V2.** La API está viva, sin errores de arranque y migrada al menos hasta donde estaba el manifiesto:

```bash
systemctl is-active stockapp-api
sudo journalctl -u stockapp-api --since '10 min ago' --no-pager | grep -ciE 'error|exception|fail'
docker exec stockapp-pg psql -U stockapp -d stockapp -At -c 'SELECT max("MigrationId") FROM "__EFMigrationsHistory"'
```

Esperado: `active`, un conteo `0` (el `grep` sale con 1 cuando no encuentra nada: es lo normal) y una
`MigrationId` ≥ la `ultima_migracion` del manifiesto.
- [ ] **V3.** Desde la máquina del operador, **sin `-k`**:
`curl -fsS https://<dominio-nuevo>:8080/` → `{"status":"ok","service":"StockApp.Api"}`, con certificado válido para el dominio nuevo.
- [ ] **V4.** `curl -fsS https://<dominio-nuevo>:8080/licencia/estado` → `"activada":true` (`Licencia__Habilitado=false`).
- [ ] **V5.** Login con un usuario **real** de los datos migrados. La contraseña se escribe en el momento
  (no se guarda, no pasa por argv):

```bash
read -rs -p 'Contraseña: ' PW; echo
jq -n --arg u '<usuario-real>' --arg p "$PW" '{nombreUsuario:$u,contrasena:$p}' \
  | curl -fsS -X POST https://<dominio-nuevo>:8080/auth/login -H 'Content-Type: application/json' --data @- \
  | jq -r .token > ~/stockapp-backups-vps/token; chmod 600 ~/stockapp-backups-vps/token; unset PW
```

Esperado: el archivo del token no está vacío. Al terminar: `rm -f ~/stockapp-backups-vps/token`.
- [ ] **V6.** Conteos **después** del arranque contra el manifiesto: ninguna tabla puede tener menos filas, solo
  pueden crecer `LogsAuditoria` y `__EFMigrationsHistory`, y `CorridasBackup` puede variar (la retención de
  backups borra filas al arrancar; se informa el delta). `LogsAuditoria` tiene que ser ≥ el manifiesto:

```bash
cd ~/stockapp-deploy
./migracion/restaurar-backup.sh --verificar-post-arranque --manifiesto migracion/volcado-<ts>.manifiesto
```

Esperado: exit 0. Es de solo lectura.
- [ ] **V7.** El feed: `curl -fsS https://<dominio-nuevo>:8080/updates/releases.win.json | grep -c 0.5.3` → ≥ 1, y
  `curl -sI https://<dominio-nuevo>:8080/updates/<FileName-del-full>` → `200` con `Content-Length` igual al `Size` del feed.
- [ ] **V8.** Backups (con el token de V5):

```bash
printf 'header = "Authorization: Bearer %s"\n' "$(cat ~/stockapp-backups-vps/token)" \
  | curl -fsS -K - https://<dominio-nuevo>:8080/backups
printf 'header = "Authorization: Bearer %s"\n' "$(cat ~/stockapp-backups-vps/token)" \
  | curl -fsS -K - -o /dev/null -w '%{http_code}\n' https://<dominio-nuevo>:8080/backups/<id-viejo>/contenido
printf 'header = "Authorization: Bearer %s"\n' "$(cat ~/stockapp-backups-vps/token)" \
  | curl -fsS -K - -X POST https://<dominio-nuevo>:8080/backups
```

Esperado: lista con las corridas viejas, `200` en el contenido de una vieja (C6) y, en menos de 60 s, una
corrida nueva `Exitosa` en `GET /backups` (prueba que `pg_dump` anda con la contraseña nueva; agrega una
fila a `CorridasBackup`, que V6 ya acepta).
- [ ] **V9.** Smoke de la GUI **de solo lectura**, hecho por un agente con el toolkit `scripts/gui-verificacion/`:
  el desktop 0.5.3 apunta al dominio nuevo, login, recorrer las grillas principales (productos, movimientos,
  gastos, tareas, documentos y auditoría) y abrir un adjunto. Junta capturas y **las mira**: `capturar.sh` no
  es fail-closed y con WSLg caído captura el escritorio de Windows con exit 0. Resultado: OK o FALLA, con evidencia.
- [ ] **V10.** El VPS viejo quedó congelado y no se tocó nada ajeno:

```bash
unset STOCKAPP_ENV_LOCAL
( set -a; source deploy/.env; set +a
  ssh -p "$VPS_SSH_PORT" -o BatchMode=yes "${VPS_USER}@${VPS_HOST}" \
    'systemctl is-enabled stockapp-api; systemctl is-active stockapp-api; docker ps --format "{{.Names}}"' )
```

Esperado: `masked`, `inactive` y los **mismos contenedores** que el relevamiento de C3 (los de la otra app incluidos).

---

# Rollback

### R-A. Antes de C9, sin escrituras reales en el VPS nuevo

- [ ] **R-A.** En el VPS **viejo**, en este orden (son los comandos que `volcado-final-vps.sh` imprime; los datos
  del viejo están intactos porque el volcado es solo de lectura):

```bash
sudo systemctl unmask stockapp-api
sudo mv /etc/systemd/system/stockapp-api.service.congelado /etc/systemd/system/stockapp-api.service
sudo systemctl daemon-reload
sudo systemctl start stockapp-api
systemctl is-active stockapp-api
```

Esperado: `active`. Un `unmask` solo, sin devolver la unit, no sirve: la unit sigue movida. Si el script
falló antes de mover la unit, imprime solo lo que corresponde a su estado. El VPS nuevo queda como
estaba, para reintentar otro día.

### R-B. Después de C9

- [ ] **R-B.** Corregir hacia adelante es lo preferido. Solo si el VPS nuevo resulta irrecuperable (**último
  recurso**) se hace la **migración inversa con las mismas piezas**:
  1. `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/volcado-final-vps.sh --salida ~/stockapp-backups-vps/inversa-<fecha>/`
     (congela el nuevo).
  2. Copiar al VPS viejo el volcado, su manifiesto y `restaurar-backup.sh` (como en C4, con el puerto y el usuario del `.env` del operador viejo).
     El viejo sigue detenido y enmascarado, que es lo que el script exige (API inactiva).
  3. `restaurar-backup.sh … --recrear-base stockapp` en el viejo. El dump de seguridad que toma
     `--recrear-base` conserva lo que tenía el viejo.
  4. Recién ahí, los comandos de R-A completos (`unmask`, devolver la unit, `daemon-reload`, `start`) y repuntar las PCs con el Configurador.

---

# Post-corte

- El VPS viejo queda **detenido y enmascarado durante 14 días** (rollback). No se borra nada: ni el volumen
  `stockapp_pgdata`, ni `/var/lib/stockapp`, ni la unit.
- Al día 14, **desmantelarlo es una decisión de Martin**. Solo como referencia, siempre acotado por nombre a
  StockApp (nunca nada global; certbot del viejo solo se toca en la línea del certificado de StockApp):
  - `docker compose --env-file .env -f docker-compose.postgres.yml down` **sin** `-v`;
  - `docker volume rm stockapp_pgdata`, solo después de confirmar que `LogsAuditoria` del VPS nuevo es ≥ al del
    manifiesto **y** de tener un dump archivado fuera del servidor;
  - `systemctl disable` solo aplica si antes se hizo `unmask`: enmascarada, la unit ya no arranca.
- Ambiente de testing después del corte: decisión pendiente de Martin (spec §5.8).
