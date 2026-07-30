# Despliegue de StockApp.Api en un VPS Linux

Runbook completo, desde "VPS recién accedido" hasta "desktop conectado y andando".
Asume el entorno relevado para este despliegue:

- Ubuntu 24.04 LTS, `docker compose` v2 disponible, **.NET NO instalado** (no hace falta:
  publicamos self-contained).
- **SSH en el puerto 34377**, no en el 22.
- El VPS ya corre **"pinar"** (otro proyecto, en Docker: nginx en `0.0.0.0:80`/`:443`, web,
  api, postgres, redis). **Este runbook nunca lo toca.**
- **No hay firewall activo** (`ufw` sin uso). Ver la sección "Firewall" al final —
  léela ANTES de tocar `ufw`, aunque sea en otro momento.

Arquitectura elegida (no se re-discute acá, ver `.superpowers/sdd/deploy-vps.md` para el
razonamiento completo):

1. La API corre por **systemd**, no en contenedor (containerizarla cambiaría
   `/etc/machine-id` en cada recreación y rompería la licencia).
2. Publish **self-contained** `linux-x64` (el VPS no tiene .NET).
3. API bindeada a **`127.0.0.1:5080`**. El acceso es por **túnel SSH**, nunca por puerto
   público. No se abre ningún puerto, no se toca `ufw`, no se toca el nginx de pinar.
4. Postgres propio en contenedor `stockapp-pg`, bindeado a **`127.0.0.1:5433`**.
5. Usuario de sistema `stockapp` con **HOME real en `/var/lib/stockapp`**.
6. `postgresql-client-16` instalado en el host (matchea la versión del contenedor).

---

## 0. Prerrequisitos y verificación previa

En tu máquina de trabajo (NO en el VPS):

- .NET 10 SDK (para `dotnet publish`).
- Acceso SSH al VPS: `ssh -p 34377 usuario@<ip-del-vps>`.
- El repo clonado, en la rama que vayas a desplegar.

En el VPS, antes de tocar nada, verificá que no vas a pisar "pinar":

```bash
ssh -p 34377 usuario@<ip-del-vps>
docker ps                      # confirmá que ves los contenedores de pinar (nginx, web, api, postgres, redis)
sudo ss -ltnp | grep -E ':80|:443|:5432|:5433|:5080'   # confirmá qué puertos están realmente ocupados
```

Esperado: `80` y `443` ocupados por el nginx de pinar; `5432`/`5433`/`5080` libres (el
Postgres de pinar no mapea puerto al host, así que ni siquiera debería aparecer ahí).
Si `5433` o `5080` YA están ocupados por algo, pará y averiguá qué es antes de seguir —
no asumas que es seguro reusarlos.

---

## 1. Generar el par de claves de licencia

**Esto se hace en tu máquina de trabajo, NUNCA en el VPS.** La clave privada firma las
licencias que activan cada instalación — si se filtra, cualquiera puede emitir licencias
válidas para cualquier máquina.

```bash
dotnet run --project tools/StockApp.Licencias.Cli -- generar-claves --salida ~/stockapp-claves
```

Esto imprime:

```
Clave privada escrita en: /home/vos/stockapp-claves/clave-privada.pem
GUARDALA FUERA DEL REPO. No la compartas ni la commitees.

Clave pública (pegar en OpcionesLicencia.ClavePublicaBase64Default):
<base64...>
```

**Reglas duras:**

- `clave-privada.pem` **NUNCA** va al servidor, **NUNCA** al repo (`.gitignore` ya bloquea
  `*.pem` y `clave-privada*`, pero la responsabilidad es tuya: no la copies a ningún lado
  compartido). Guardala en un gestor de secretos o un backup cifrado personal.
- La clave **pública** (el base64 impreso) es la que va en `deploy/.env` →
  `LICENCIA_CLAVE_PUBLICA_BASE64`. Esa sí puede viajar — es la mitad que solo verifica
  firmas, no las emite.

Si ya generaste el par en una entrega anterior (Inc 7 Fase B), **reusá la misma clave
privada** — no generes un par nuevo por despliegue, o vas a tener que reemitir licencias
para todas las instalaciones existentes.

---

## 2. Levantar Postgres y crear la base

En tu máquina, preparás el `.env` real (no se commitea):

```bash
cp deploy/.env.example deploy/.env
# Editá deploy/.env: POSTGRES_PASSWORD, JWT_SECRET, BOOTSTRAP_PASSWORD y
# LICENCIA_CLAVE_PUBLICA_BASE64 (la clave pública del paso 1). Generá los secretos con:
#   openssl rand -base64 24   # POSTGRES_PASSWORD
#   openssl rand -base64 48   # JWT_SECRET
```

Copiá `deploy/.env` y `deploy/docker-compose.postgres.yml` al VPS (por ejemplo a
`~/stockapp-deploy/`):

```bash
scp -P 34377 deploy/.env deploy/docker-compose.postgres.yml usuario@<ip-del-vps>:~/stockapp-deploy/
```

En el VPS:

```bash
cd ~/stockapp-deploy
docker compose --env-file .env -f docker-compose.postgres.yml up -d
docker compose --env-file .env -f docker-compose.postgres.yml ps   # esperar "healthy"
```

El contenedor `stockapp-pg` crea la base `POSTGRES_DB` automáticamente al primer arranque
(comportamiento estándar de la imagen `postgres:16-alpine`) — no hace falta un paso manual
de `CREATE DATABASE`. Las tablas las crea la propia API al arrancar (migración automática,
ver Program.cs) — tampoco hace falta correr migraciones a mano.

Verificación:

```bash
docker exec -e PGPASSWORD=<POSTGRES_PASSWORD> stockapp-pg \
  psql -U <POSTGRES_USER> -d <POSTGRES_DB> -c '\conninfo'
```

---

## 3. Publicar, copiar e instalar la API

En tu máquina:

```bash
deploy/publish-api.sh                 # genera deploy/dist/stockapp-api-<version>-linux-x64.tar.gz
```

El comando anterior imprime la ruta EXACTA del tarball generado (última línea,
`[publish-api] OK: ...`). Usá siempre ese nombre exacto de acá en adelante — **nunca un
glob** como `stockapp-api-*-linux-x64.tar.gz`: `deploy/dist/` normalmente tiene más de un
tarball (versiones previas, guardadas a propósito para rollback — ver el final de esta
sección), y un glob ahí expande a más de un argumento. `install.sh` ahora exige
exactamente 2 argumentos y rechaza correr si no los recibe, así que ese escenario ya no
pasa desapercibido — pero de todos modos no dependas de que el script te salve, escribí el
nombre exacto:

```bash
TARBALL="stockapp-api-<version>-linux-x64.tar.gz"   # el que imprimió el comando anterior
scp -P 34377 \
  "deploy/dist/${TARBALL}" \
  deploy/install.sh deploy/stockapp-api.service deploy/wait-for-postgres.sh \
  usuario@<ip-del-vps>:~/stockapp-deploy/
```

(`deploy/.env` ya lo copiaste en el paso 2 — `install.sh` lo vuelve a leer, no hace falta
copiarlo de nuevo salvo que lo hayas editado.)

En el VPS:

```bash
cd ~/stockapp-deploy
sudo ./install.sh "${TARBALL}" .env
```

`install.sh` es idempotente: crea el usuario `stockapp` (si no existe), instala
`postgresql-client-16` y `curl` (si faltan), respalda cualquier instalación previa en
`/var/backups/stockapp-api/<timestamp>/`, extrae el release en `/opt/stockapp-api`, genera
`/etc/stockapp-api/api.env` (600, secretos) a partir de tu `.env`, instala la unit de
systemd con el puerto correcto, y arranca el servicio — verificando al final que responde
en `127.0.0.1:5080`.

Si termina con "OK: StockApp.Api responde en 127.0.0.1:5080", la API está arriba pero
**bloqueada (423)** hasta que actives la licencia (paso siguiente) — es el comportamiento
esperado, no un error.

---

## 4. Activar la licencia

Con la API arriba pero bloqueada, necesitás el código de máquina del VPS. Abrí un túnel
SSH temporal (ver paso 5 para el comando completo) o, más simple, consultalo directo desde
el VPS:

```bash
curl -s http://127.0.0.1:5080/licencia/estado
# {"activada":false,"codigoMaquina":"A3F2-9B41-..."}
```

En tu máquina, con la clave privada del paso 1, emitís la licencia para ESE código de
máquina:

```bash
dotnet run --project tools/StockApp.Licencias.Cli -- emitir-licencia \
  --clave ~/stockapp-claves/clave-privada.pem \
  --cliente "Municipio de Carmelo" \
  --maquina A3F2-9B41-...
```

Esto imprime el string de licencia (una línea). Activala contra el VPS — por túnel SSH
(ver paso 5) o, si estás parado en el VPS, directo:

```bash
curl -s -X POST http://127.0.0.1:5080/licencia/activar \
  -H "Content-Type: application/json" \
  -d '{"licencia":"<el string que imprimió emitir-licencia>"}'
# {"activada":true,"codigoMaquina":"A3F2-9B41-..."}
```

Con `activada:true`, la API deja de devolver 423 en el resto de los endpoints. El resto de
la activación (login, primer uso) se puede hacer desde el desktop una vez conectado.

---

## 5. Conectar el desktop por túnel SSH

El acceso productivo es SIEMPRE por túnel — la API nunca escucha en una interfaz pública.

Desde la máquina del puesto de trabajo (Windows/Linux con el cliente OpenSSH):

```bash
ssh -p 34377 -N -L 5080:127.0.0.1:5080 usuario@<ip-del-vps>
```

Dejá esa terminal abierta (o corré el comando como servicio/tarea en segundo plano — en
Windows, `autossh` o una tarea programada con este mismo comando). Mientras el túnel esté
vivo, `localhost:5080` en tu PC es literalmente `127.0.0.1:5080` en el VPS.

En el `appsettings.json` junto al ejecutable del desktop:

```json
{ "Api": { "BaseUrl": "http://localhost:5080" } }
```

Abrí el desktop. Con la licencia ya activada (paso 4), debería llegar directo a la
pantalla de login. Si activaste la licencia por curl salteando este paso, también podés
activarla acá: con la licencia sin activar, el desktop muestra la pantalla de bloqueo con
un campo para pegar el string de licencia.

Login con el Admin de bootstrap (`BOOTSTRAP_ADMIN_USER`/`BOOTSTRAP_PASSWORD` de tu `.env`).
**Cambiá esa contraseña** desde el desktop apenas entres — ese valor solo sirve para el
primer arranque.

---

## 6. Verificación post-instalación

Con el túnel abierto (paso 5), desde tu máquina:

```bash
# Healthcheck básico
curl -s http://localhost:5080/ ; echo
# {"status":"ok","service":"StockApp.Api"}

# Login (confirma que la BD migró y el bootstrap sembró el Admin)
curl -s -X POST http://localhost:5080/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"<BOOTSTRAP_ADMIN_USER>","contrasena":"<BOOTSTRAP_PASSWORD>"}'
# Copiar "token" -> <TOKEN_ADMIN>

# Logs: confirma que Serilog puede escribir en el HOME de stockapp
curl -s http://localhost:5080/logs -H "Authorization: Bearer <TOKEN_ADMIN>"
# Debe listar al menos el archivo de log del día (o un array vacío si recién arrancó y
# todavía no se emitió ningún WARNING/ERROR -- el mínimo nivel de archivo es Warning).

# Backups: confirma que BackupProgramadoService corre (cada 12h fijas; recién instalado
# puede no haber corrido todavía la primera vez)
curl -s http://localhost:5080/backups/salud -H "Authorization: Bearer <TOKEN_ADMIN>"
```

Del lado del VPS, para confirmar que el primer backup corrió (podés esperar hasta 12h, o
reiniciar el servicio para forzar el arranque inmediato de `BackupProgramadoService`):

```bash
sudo systemctl restart stockapp-api
sleep 15
curl -s http://127.0.0.1:5080/backups/salud -H "Authorization: Bearer <TOKEN_ADMIN>"
# "ultimoExitoEn" debería tener una fecha reciente, "vencido" en false
ls -la /var/lib/stockapp/.local/share/StockApp/backups/
```

---

## 7. Troubleshooting

### Backups, licencia o logs "desaparecen" entre reinicios, o nunca se generan

Causa posible (poco intuitiva, verificada durante la preparación de este runbook):
`Environment.SpecialFolder.LocalApplicationData` en .NET/Linux solo resuelve a
`$HOME/.local/share` si ese directorio **ya existe y es legible en el momento en que
arranca el proceso** — si no existe, .NET devuelve un string vacío en vez de crearlo, y
`UserDataPathProvider` termina resolviendo un path relativo contra el `WorkingDirectory`
del servicio en lugar de uno absoluto bajo `/var/lib/stockapp`. `install.sh` ya crea
`/var/lib/stockapp/.local/share` explícitamente para evitar esto (`useradd
--create-home` por sí solo NO alcanza — crea `/var/lib/stockapp` pero no esa subcarpeta).
Si por algún motivo ese directorio se borró o nunca se creó (por ejemplo, alguien re-creó
el usuario `stockapp` a mano, sin pasar por `install.sh`):

```bash
sudo -u stockapp test -d /var/lib/stockapp/.local/share && echo "OK" || echo "FALTA"
# Si falta:
sudo mkdir -p /var/lib/stockapp/.local/share
sudo chown -R stockapp:stockapp /var/lib/stockapp/.local
sudo systemctl restart stockapp-api
```

### Arranque colgado, el servicio queda "activo" pero nunca responde

Causa: Postgres no estaba listo cuando la API intentó migrar (`MigrateAsync()` corre ANTES
de `app.Run()` en `Program.cs` — sin el `ExecStartPre` de `wait-for-postgres.sh`, esto
cuelga en silencio, sin loguear nada).

```bash
sudo journalctl -u stockapp-api -n 100 --no-pager
```

- Si ves líneas de `[wait-for-postgres]` reintentando y fallando: Postgres no está arriba.
  Verificá `docker compose --env-file .env -f docker-compose.postgres.yml ps` — si no
  está "healthy", revisá `docker logs stockapp-pg`.
- Si `wait-for-postgres` pasó OK pero el servicio sigue sin responder: puede ser una
  migración larga (raro) o un problema real de conexión (`ConnectionStrings__Default` mal
  armado en `/etc/stockapp-api/api.env` — reinstalar con `install.sh` regenera ese archivo
  desde `deploy/.env`).

### El script de instalación falló pero el servicio parece estar andando

Antes de restaurar cualquier backup (ver "Rollback" más abajo), confirmá que el servicio
está realmente roto y no fue un falso negativo del script:

```bash
sudo systemctl status stockapp-api --no-pager
curl -s http://127.0.0.1:5080/licencia/estado ; echo
```

Si `systemctl status` muestra `active (running)` y el `curl` devuelve un JSON con
`codigoMaquina` (no importa si `activada` es `true` o `false`), la API está sana — el fallo
fue del script (por ejemplo, un timeout corto si la primera migración tardó más de lo
esperado), no del servicio. Revisá `sudo journalctl -u stockapp-api -n 100 --no-pager` para
entender la causa antes de tocar nada más. **No corras el Rollback B**
(`rm -rf /opt/stockapp-api/*`) sobre una instalación sana — perdés el release que sí
funciona.

### La API responde pero todo da `423 Locked`

Licencia no activada, o activada para otro fingerprint. Ver paso 4. Confirmá el código de
máquina actual:

```bash
curl -s http://127.0.0.1:5080/licencia/estado
```

Si `codigoMaquina` cambió respecto a la licencia que emitiste (por ejemplo, reinstalaste el
SO del VPS), tenés que volver a emitir una licencia para el código nuevo.

### Los backups fallan (`pg_dump` ausente o con error)

```bash
curl -s http://127.0.0.1:5080/backups -H "Authorization: Bearer <TOKEN_ADMIN>"
# mirar el campo de error de la última corrida fallida
which pg_dump   # debería listar /usr/bin/pg_dump (postgresql-client-16)
pg_dump --version
```

Si `pg_dump` no está: `sudo apt-get install postgresql-client-16` (o volvé a correr
`install.sh`, que lo instala si falta). Si está pero la versión no matchea la del
contenedor (`postgres:16-alpine`), reinstalá `postgresql-client-16` — no mezcles con
`postgresql-client-<otra-versión>`.

### Los logs no se escriben (o `/logs` devuelve vacío siempre)

Casi siempre permisos: `$HOME/.local/share/StockApp/logs` (con `HOME=/var/lib/stockapp`)
no es escribible por el usuario `stockapp`.

```bash
sudo -u stockapp test -w /var/lib/stockapp/.local/share/StockApp/logs && echo "OK, escribible" || echo "SIN PERMISO"
ls -la /var/lib/stockapp/.local/share/StockApp/
```

Si la ruta no existe o los permisos están mal, algo modificó `/var/lib/stockapp` por fuera
de `install.sh` (por ejemplo, un `chown` manual). Arreglo:

```bash
sudo chown -R stockapp:stockapp /var/lib/stockapp
sudo systemctl restart stockapp-api
```

Recordá: el archivo de log solo recibe eventos de nivel `Warning` o superior (ver
`Program.cs`) — que esté vacío en un arranque limpio sin errores es normal, no un síntoma
de este problema.

---

## 8. Actualizar a una versión nueva

```bash
# En tu máquina
deploy/publish-api.sh 1.5.0          # o el número/tag que corresponda
scp -P 34377 deploy/dist/stockapp-api-1.5.0-linux-x64.tar.gz usuario@<ip-del-vps>:~/stockapp-deploy/

# En el VPS — mismo comando que la instalación inicial, install.sh detecta que ya hay
# una instalación y la actualiza (respaldando la anterior automáticamente)
cd ~/stockapp-deploy
sudo ./install.sh stockapp-api-1.5.0-linux-x64.tar.gz .env
```

`install.sh` reinicia el servicio al final y espera a que vuelva a responder. La licencia
activada persiste (vive en `/var/lib/stockapp/.local/share/StockApp/licencia.lic`, fuera
del directorio de la release) — no hace falta reactivarla en una actualización.

**Guardá cada tarball que publiques** (`deploy/dist/*.tar.gz`, localmente — no se
commitean) — es la forma más simple y confiable de hacer rollback (ver abajo).

## Rollback

Dos formas, de más a menos preferida:

**A) Reinstalar la versión anterior** (recomendado — es exactamente el mismo camino que
una actualización, solo que "hacia atrás"):

```bash
scp -P 34377 deploy/dist/stockapp-api-<version-anterior>-linux-x64.tar.gz usuario@<ip-del-vps>:~/stockapp-deploy/
ssh -p 34377 usuario@<ip-del-vps>
cd ~/stockapp-deploy
sudo ./install.sh stockapp-api-<version-anterior>-linux-x64.tar.gz .env
```

**B) Restaurar el backup automático que hizo `install.sh` antes de la última actualización**
(útil si no conservaste el tarball anterior):

```bash
ls /var/backups/stockapp-api/                      # elegí el timestamp correcto
sudo systemctl stop stockapp-api
sudo rm -rf /opt/stockapp-api/*
sudo cp -a /var/backups/stockapp-api/<timestamp>/. /opt/stockapp-api/
sudo chown -R stockapp:stockapp /opt/stockapp-api
sudo systemctl start stockapp-api
```

Ninguna de las dos formas toca la base de datos ni la licencia — un rollback de la API no
revierte migraciones. Si la versión nueva agregó una migración de esquema incompatible con
la anterior, un rollback de binario solo no alcanza (fuera de alcance de este runbook:
evaluar caso por caso si eso llega a pasar).

---

## Firewall — advertencia importante

**Esta instalación NO toca `ufw` ni ningún firewall.** El VPS queda tal como estaba
relevado: sin firewall activo. Eso es intencional acá — el único "perímetro" real de
StockApp.Api es que escucha exclusivamente en `127.0.0.1:5080` (nunca `0.0.0.0`), así que
no hay puerto nuevo que proteger.

**Si en algún momento futuro se activa `ufw` en este VPS** (por cualquier motivo, incluso
uno ajeno a StockApp), hacelo en este orden exacto, ANTES de poner `ufw enable`:

```bash
sudo ufw allow 34377/tcp     # SSH -- este VPS NO usa el 22. Sin esto, PERDÉS EL ACCESO AL VPS.
sudo ufw allow 80/tcp        # pinar (nginx)
sudo ufw allow 443/tcp       # pinar (nginx)
sudo ufw enable
```

No hace falta ninguna regla para `5080` ni `5433` — ambos están bindeados a `127.0.0.1`,
un firewall de `ufw` (que filtra tráfico de red externo) ni siquiera los ve. Abrir esos
puertos en `ufw` no haría nada por sí solo (seguirían inaccesibles desde afuera porque el
bind es a loopback) — pero tampoco hace falta intentarlo.

Si te salteás el primer `allow` (34377) antes de `ufw enable`, y no tenés una consola de
emergencia (KVM/VNC del proveedor del VPS), **te quedás afuera del servidor** — y de paso,
sin el `allow 80/443`, tirás la producción de "pinar" con el mismo error. Verificá las
reglas con `sudo ufw status verbose` antes de confirmar `enable`, no después.
