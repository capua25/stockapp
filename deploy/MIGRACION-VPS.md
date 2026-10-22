# Migración de VPS: runbook

Dos comandos hacen el trabajo. Este documento dice **cuándo** correr cada uno, **qué mirar** y **qué
hacer con cada código de salida**. El detalle de cada paso vive en el código (el encabezado de
`deploy/preparar-vps-nuevo.sh` y el de `deploy/migrar-vps.sh`) y el diseño, en
`docs/superpowers/specs/2026-10-20-migracion-vps-automatizada-design.md`. El runbook manual anterior
(P1 a P10, E2, C0 a C10, V1 a V10) queda en el historial de git.

**Estado:** el ensayo de punta a punta contra los servidores reales y el corte están **pendientes**:
necesitan el VPS nuevo, el dominio y la fecha. El único ensayo hecho es E1 (sección 10).

## 1. Qué es esto

- `deploy/preparar-vps-nuevo.sh` deja el VPS nuevo listo: Docker, usuario `stockapp`, firewall, TLS,
  `.env` del servidor, Postgres vacío, primer deploy y feed de actualizaciones. Es idempotente:
  re-correrlo continúa donde quedó.
- `deploy/migrar-vps.sh` hace todo lo demás, según el modo: `--ensayo`, el corte (sin flags),
  `--continuar`, `--revertir`, `--desde-backup` y `--post-corte`.
- **Todos los comandos se corren desde la raíz del repo y en una terminal de verdad**: las palabras
  de confirmación (`CONGELAR`, `REVERTIR`, `RESTAURAR`, `BORRAR`) se leen de la terminal, y sin terminal
  el script sale con 3.
- Solo una corrida a la vez: los dos scripts toman un lock en `~/stockapp-backups-vps/.migracion.lock`
  (si está tomado, salen con 3).

Reglas que valen en TODO el documento:

- El VPS viejo es **compartido con otra aplicación**. Sobre él solo corren los scripts de este runbook,
  que operan por nombre sobre `stockapp-api`, `stockapp-pg` y `/var/lib/stockapp`. Nada global de
  Docker, de systemd ni de certbot, y el proxy de la otra app no se toca.
- `deploy/deploy-vps.sh`: **NUNCA** se corre contra el VPS viejo. Sin `--sin-backup` deja el backup en el
  directorio temporal del host, legible por los demás usuarios del servidor compartido. Contra el VPS
  nuevo solo se usa con `--sin-backup`, y solo en el caso de la sección 4 (cargar overrides).
- Los dumps de producción contienen todo, incluidos los hashes de contraseñas. Viven en
  `~/stockapp-backups-vps/` (700, fuera del repo) y nunca se commitean.
- Con dos VPS en juego, **mirá siempre qué archivo estás cargando**:

| Archivo | Dónde | Para qué | Cómo se elige |
|---|---|---|---|
| `deploy/.env` | máquina del operador | apunta al VPS **viejo** (y al de testing en el ensayo) | es el default de los scripts |
| `deploy/.env.vps-nuevo` | máquina del operador | apunta al VPS **nuevo** (`deploy/.env*` está ignorado por git) | `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo <script>` |
| `~/stockapp-deploy/.env` | servidor nuevo (600) | configura el servicio real: lo lee `install.sh` y `docker compose` | lo genera `preparar-vps-nuevo.sh` en el servidor |

El `.env` del operador se carga con `source` **después** del entorno: si define `VPS_HOST`, **pisa**
al `VPS_HOST` que exportaste. No alcanza con exportar variables para cambiar de VPS: hay que usar
`STOCKAPP_ENV_LOCAL`. Si la variable apunta a un archivo que no existe, los scripts abortan antes de
abrir ninguna conexión. `preparar-vps-nuevo.sh` exige `STOCKAPP_ENV_LOCAL` y se niega (9) si resuelve
al mismo servidor que `deploy/.env`.

Marcadores usados en este documento: `<dominio-nuevo>`, `<ip-nueva>`, `<puerto-nuevo>` (SSH del VPS
nuevo), `<usuario>` (usuario SSH del VPS nuevo), `<fecha>` (AAAAMMDD) y `<ts>` (el timestamp UTC que
lleva el nombre de la corrida). Ningún valor real vive en el repo.

## 2. Prerrequisitos de Martin

Ningún agente escribe estos archivos: son tuyos. Solo nombres de variables, nunca valores en el repo.

- [ ] **VPS nuevo, dedicado.** Ubuntu 24.04 LTS, usuario SSH con clave y `sudo` sin contraseña.
  Preparar se niega (9) si hay contenedores ajenos a StockApp o el puerto 80 ocupado.

```bash
ssh -p <puerto-nuevo> -o BatchMode=yes <usuario>@<ip-nueva> 'sudo -n true && echo OK'
```

Esperado: `OK`. Si pide contraseña o falla, arreglá el `sudoers` o la clave antes de seguir.
- [ ] **Firewall del proveedor.** Si el panel del proveedor tiene un firewall propio, abrí ahí el puerto
  SSH, el 80 (HTTP-01 de Let's Encrypt, al emitir y al renovar) y el 8080 (la API). Preparar maneja el
  `ufw` del servidor, no el del panel.
- [ ] **DNS.** Registro `A` de `<dominio-nuevo>` → `<ip-nueva>`, TTL 300 s, creado **días antes**.

```bash
dig +short <dominio-nuevo>
```

Esperado: exactamente `<ip-nueva>`. Preparar lo comprueba antes de pedir el certificado (sale 3 si no
coincide) para no gastar intentos de Let's Encrypt.
- [ ] **`deploy/.env`** (el VPS viejo; lo usan el ensayo y el corte). Variables: `VPS_HOST`, `VPS_USER`,
  `VPS_SSH_PORT` (sin default a propósito: si faltan, los scripts abortan), `API_SCHEME`, `API_HOSTNAME`,
  `API_PORT`, `LICENCIA_CLAVE_PUBLICA_BASE64` (preparar la copia al servidor nuevo; solo la lee) y
  `VPS_ADMIN_USER` + `VPS_ADMIN_PASSWORD`: un usuario **real** de los datos, con el permiso
  `GestionarDiagnostico` (V5 y V8 hacen login con él y piden `/backups`). `VPS_ADMIN_USER` es
  obligatorio y no cae a `BOOTSTRAP_ADMIN_USER`. La contraseña puede ir en el `.env`, en el entorno del
  comando puntual (`VPS_ADMIN_PASSWORD=… deploy/migrar-vps.sh`, sin `export`) o, si no está en
  ninguno, se pide por terminal sin eco. Ojo, y no es lo mismo en los dos scripts: en `migrar-vps.sh`
  **gana el entorno** sobre el `.env`; en `deploy/bajar-backup-vps.sh` es al revés, el `.env` **pisa**
  al entorno.
- [ ] **`deploy/.env.vps-nuevo`** completo, con `chmod 600`: `VPS_HOST` (la IP nueva), `VPS_USER`,
  `VPS_SSH_PORT`, `API_SCHEME=https`, `API_HOSTNAME=<dominio-nuevo>` y `API_PORT=8080`. El resto del
  `.env` del servidor (secretos nuevos, `Licencia__Habilitado=false`, rutas del certificado) lo genera
  preparar en el servidor.
- [ ] **`releases/win`** con la versión 0.5.3 publicada y el `Setup.exe` de la **misma** versión
  (`build/pack-win.ps1`, ver `build/README-empaquetado.md`). Preparar sale 3 si falta la versión.
- [ ] **Los valores de los overrides.** Sus **nombres** los lista el primer `--ensayo` (sección 4); los
  valores están en el `api.env` del VPS viejo y los cargás vos, a mano.

## 3. Preparar el VPS nuevo

Preparar no toca el VPS viejo. Primera corrida:

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3
```

Esperado: exit 0 y `preparar: el VPS quedó listo.`, con los pasos 1 a 8 y el 10 en `OK`; sin
`--probar-renovacion`, el paso 9 sale `OMITIDO`. Después de
la salida hay una lista de lo que queda **pendiente para vos**: los overrides, la renovación del
certificado y el `Setup.exe`. Si falla un paso, mirá la tabla de códigos de la sección 6 y re-corré: lo
hecho queda hecho.

Segunda corrida, para comprobar la idempotencia (los pasos 1 a 8 tienen que dar `YA ESTABA`,
el paso 10 da `OK` y el paso 9 sigue `OMITIDO`):

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3
```

Para revisar un VPS preparado semanas atrás, sin aplicar nada (solo lectura; cada paso dice `HECHO` o
`PENDIENTE`):

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3 --verificar
```

Probar la renovación del certificado, **una vez y a mano**. Corre `certbot renew --force-renewal` de
verdad (`--dry-run` no ejecuta los hooks, así que no probaría el hook), y verifica que la API reinicie:

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3 --probar-renovacion
```

Gasta cupo de Let's Encrypt: si la verificación posterior falla, **no** repitas el comando a ciegas
(cada corrida es otra renovación forzada); mirá `sudo journalctl -u stockapp-api -n 50 --no-pager` en el
VPS nuevo primero. Una vez probada, el script lo anota en el servidor y no vuelve a hacerlo.

Qué hay que saber de lo que hace:

- **Firewall.** Habilita `ufw` con el SSH primero y arma antes una reversa automática (`stockapp-ufw-reversa`,
  120 s) que lo deshace si el re-chequeo de acceso con una conexión nueva no llega. Nunca resetea ni
  borra reglas. No ve el firewall del panel del proveedor.
- **Certificado.** HTTP-01 `--standalone` por el puerto 80, emitido con `--register-unsafely-without-email`:
  una cuenta ACME nueva no puede emitir en modo no interactivo sin ese flag. Consecuencia: la cuenta no
  tiene contacto de recuperación. Se agrega después, sin reemitir, con
  `sudo certbot update_account -m <mail>` en el VPS nuevo. Si el certificado ya existe con otro
  `renew_hook`, lo re-registra con `certbot reconfigure`, que hace un dry-run contra staging de Let's
  Encrypt. Si el proveedor no deja abrir el puerto 80, este camino no sirve tal cual: DNS-01 necesita
  el plugin de tu proveedor DNS o un TXT manual, y la renovación deja de ser automática.
- **`.env` del servidor.** Se genera **en el servidor** (`~/stockapp-deploy/.env`, 600), con secretos
  nuevos que nunca pasan por tu máquina, `LICENCIA_CLAVE_PUBLICA_BASE64` copiada de `deploy/.env` y
  `Licencia__Habilitado=false`. Si ya existe, **no se reescribe jamás**: si le falta una clave o un
  valor fijo no coincide, el paso falla y te dice solo los nombres. `BOOTSTRAP_PASSWORD` es aleatoria y
  solo sirve hasta el primer restore; hasta el primer ensayo la API queda expuesta en el 8080 con un
  admin sembrado, con el rate limit del login en 10.
- **Primer deploy.** Solo si la unit no existe. Con la API instalada, preparar **nunca** vuelve a
  desplegar: después del corte este VPS es producción, y un deploy sin backup no se hace por accidente.

## 4. Ensayo

**4.1. Ensayo automático.** Detiene la API del viejo (testing) alrededor de un minuto y la vuelve a
arrancar sola: avisá a quien lo use. No pide confirmación.

```bash
deploy/migrar-vps.sh --ensayo
```

Esperado: exit 0, resultado GO técnico, el viejo `active`. Al final imprime la **tabla de tiempos** (para
pegar en la sección 10) y los **nombres de los overrides** que faltan en el `.env` del servidor nuevo
(salen de `overrides-api-env.txt` del volcado de la corrida). Los códigos de salida: sección 6.

**4.2. Cargar los overrides.** Con los nombres que imprimió el ensayo, agregá cada `NOMBRE=valor` (el
valor lo tomás del `api.env` del VPS viejo) al `~/stockapp-deploy/.env` del servidor nuevo. Eso no
alcanza: `install.sh` es quien copia esas claves a `/etc/stockapp-api/api.env`, así que hay que
reinstalar la misma versión:

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh 0.5.3 --sin-backup
```

Verificá cada override sin imprimir su valor (en el VPS nuevo):

```bash
sudo grep -c '^<NOMBRE-DEL-OVERRIDE>=' /etc/stockapp-api/api.env
```

Esperado: `1`. No hace falta repetir el `--ensayo` para comprobarlo: el pre-vuelo (el del `--ensayo` y el
del corte) lee los nombres de `/etc/stockapp-api/api.env` además de los del `.env`, nunca los valores, y
sale 3 si algún override del viejo falta en el `.env` o está en el `.env` pero no en el `api.env` (pide
reinstalar con el comando de arriba). Si una de las dos claves que preparar ya escribe
(`Licencia__Habilitado`, `RateLimiting__Login__PermitLimit`) figura entre los overrides, no le pongas el
valor del viejo: los valores fijos de preparar no se tocan.

**4.3. Tiempos.** Pegá la tabla del resumen en la sección 10. El corte real usa esos tiempos para
dimensionar la ventana.

**4.4. Smoke de la GUI (V9).** Lo hace un agente con el toolkit `scripts/gui-verificacion/`, sobre los
datos del ensayo: el desktop 0.5.3 contra `https://<dominio-nuevo>:8080`, login, recorrer las grillas
principales (productos, movimientos, gastos, tareas, documentos y auditoría) y abrir un adjunto. Junta
capturas y **las mira**: `capturar.sh` no es fail-closed, y con WSLg caído captura el escritorio de
Windows con exit 0. Resultado: OK o FALLA, con evidencia.

**4.5. Ensayo del corte completo + `--revertir`, antes del día D.** Decidido por Martin: el día D no es la
primera vez que se congela el viejo. Contra el VPS viejo real (testing), con el nuevo ya preparado y
ensayado:

```bash
deploy/migrar-vps.sh
deploy/migrar-vps.sh --revertir
```

El primero pide `CONGELAR`; el segundo, `REVERTIR`. Es el mismo código del día D: el viejo queda sin API
(congelado) mientras dura el corte. Esperado: el corte termina en GO técnico y `--revertir` deja el
viejo `active`, con los mismos contenedores que antes, y el nuevo detenido y deshabilitado. **Antes de
reintentar** el corte (o un `--ensayo`) hay que levantar la API nueva, porque es la que sirve el feed y el
pre-vuelo lo exige:

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh 0.5.3 --sin-backup
```

Después comprobá el nuevo con `preparar-vps-nuevo.sh --verificar` (sección 6, "Después de un `--revertir`").

**Qué mirar la primera vez** (lo que los fakes del arnés local no pueden probar; spec 8.3). Es la primera
vez que estos scripts hablan con servidores reales:

- [ ] `certbot reconfigure` hace un dry-run contra staging de Let's Encrypt: si el certificado ya existía con
  otro hook y ese dry-run falla por red, el paso 4 de preparar falla; no es un problema del certificado.
- [ ] El `daemon-reload` antes del `start` de la unit en `--revertir`: el `start` no debe avisar que la unit
  cambió en disco (`systemctl status stockapp-api` en el viejo, sin ese aviso).
- [ ] Que systemd borre el timer transitorio `stockapp-ufw-reversa` después de desarmarlo:
  `systemctl list-timers --all` en el VPS nuevo no lo lista.
- [ ] El SHA256 y el `Size` del feed de Velopack (`releases.win.json`): el pre-vuelo de preparar y V7 los
  comprueban contra el `.nupkg` servido; si el feed y el archivo no coinciden, mirá cómo se publicó.
- [ ] El texto real de certbot sin email: la primera emisión no puede quedar esperando un dato por teclado.
- [ ] El `docker ps` real del VPS compartido: V10 (viejo `masked`/`inactive` e `IGUAL`) compara los
  contenedores de la otra app también, y el arnés usa un `docker` de mentira.
- [ ] Un banner o aviso que ssh escriba en el stderr del VPS viejo (MOTD, "Warning: Permanently added...")
  puede hacer que el `test -e` del pre-vuelo dé 8 y bloquee todo; si pasa, es el banner, no la unit.
- [ ] La rama de terminal de la confirmación: `CONGELAR` se lee de `/dev/tty`, y los fakes del arnés la
  reemplazan por stdin. La primera vez real es la que la ejercita; probala con el ensayo del corte (4.5).
- [ ] El reinstalar los overrides (4.2): `deploy-vps.sh 0.5.3 --sin-backup` contra el nuevo,
  con los datos del ensayo ya cargados, termina bien y el pre-vuelo pasa a ver los overrides en `api.env`. Nadie lo
  corrió contra un servidor real.
- [ ] Lo mismo después de un `--revertir`: el `deploy-vps.sh` que levanta la API nueva (enable + restart de
  `install.sh`) y un `preparar --verificar` posterior con exit 0.
- [ ] Desde tu máquina, **sin `-k`**: `curl -fsS https://<dominio-nuevo>:8080/` responde con el certificado
  válido para el dominio nuevo, y `ufw status` en el VPS nuevo muestra las tres reglas (SSH, 80 y 8080).

## 5. Día D

Antes de empezar, Martin confirma la ventana, avisa que testing deja de existir en el servidor viejo y
que **no se hacen deploys** al VPS viejo durante el corte. Las PCs del municipio se instalan en esta
misma ventana. Mirá qué `.env` carga cada comando: sin `STOCKAPP_ENV_LOCAL` es `deploy/.env`, o sea el
VPS **viejo**.

1. **El corte.**

```bash
deploy/migrar-vps.sh
```

   El pre-vuelo es de solo lectura (config, identidad de los dos servidores, estado del viejo,
   preparación del nuevo, overrides y la credencial de verificación, que se prueba contra la API vieja
   **antes** de congelar). Después imprime los dos hosts, la versión instalada en el nuevo, cuánto duró
   la indisponibilidad en el último ensayo y los comandos para volver atrás, y espera. Escribí exactamente
   `CONGELAR`: ahí empieza la indisponibilidad del testing del viejo, y cada línea muestra cuánto lleva.
   Cualquier otra cosa sale con 4 sin tocar nada.
2. **Anotá la ruta de la corrida** que imprime (`~/stockapp-backups-vps/corridas/corte-<ts>`): la pedís
   para `--continuar`, `--revertir` y `--post-corte`. El registro completo está en su `registro.log`.
3. Esperado: exit 0, **GO técnico**, con la tabla de pasos y tiempos, la de verificaciones y las fechas del
   corte y del fin del rollback. Quedan pendientes tres cosas que el script no hace.
4. **V9, el smoke de la GUI**, por un agente, como en 4.4. **La decisión de GO es de Martin.**
5. **Las PCs.** Se instalan con el `Setup.exe` 0.5.3 y el Configurador apunta a
   `https://<dominio-nuevo>:8080`. El `Setup.exe` no está firmado: Windows muestra el aviso de
   **SmartScreen** ("Windows protegió su PC"); se avisa de antemano y se sigue con "Más información" →
   "Ejecutar de todas formas". **La primera escritura real en el VPS nuevo es el punto de no retorno
   barato**: hasta ahí `--revertir` es limpio; después, lo único que queda es R-B (sección 7).
6. **Anotá las fechas** que imprimió el resumen: corte `____-__-__`, fin del rollback `____-__-__`
   (D + 14 días).

Entre el congelamiento y el final del paso 11, **no renueves certificados ni reinicies el VPS nuevo**: el
hook de TLS reinicia `stockapp-api`, y un reboot la arranca; con la base a medio restaurar, la API
escribiría sobre ella.

Si en el log ves `AVISO: fecha_utc ... dentro de la tolerancia de 300 s`, el reloj de tu máquina saltó
hacia atrás durante el volcado. Se acepta hasta 300 s y no es un error.

## 6. Si algo sale mal

Antes de cualquier otra cosa, **leé el resumen que imprimió el script**: dice en qué estado quedó el VPS
viejo (congelado o sirviendo), qué hizo cada paso y el comando exacto para seguir o para volver atrás. No
repitas un comando a ciegas, y guardá esa salida junto con el `registro.log` de la corrida.

### `preparar-vps-nuevo.sh`

| Código | Significado | Qué hacer |
|---|---|---|
| 0 | el VPS quedó listo (o ya lo estaba) | Nada. Para revisar un VPS ya preparado: `--verificar` (solo lectura). |
| 2 | uso / argumentos | Corregir el comando (la forma exacta está en la sección 3). |
| 3 | precondición (config, `sudo -n`, versión del SO, DNS, falta `releases/win`, lock): no se tocó nada | No se tocó nada. Leer el mensaje, arreglar la configuración, el DNS, el sistema operativo o `releases/win` (`build/pack-win.ps1`) y re-correr. |
| 5 | falló un paso: lo hecho queda hecho y re-correr continúa | Re-correr el mismo comando: lo hecho da `YA ESTABA` y continúa en el paso que falló. Si repite en el mismo paso, leer su detalle antes de insistir. |
| 6 | el re-chequeo de acceso después de `ufw enable` falló: la reversa automática deshace el firewall en N segundos; esperar y re-correr | No toques nada: la reversa automática (`stockapp-ufw-reversa`) deshace el firewall en 120 s. Esperá más de 2 minutos, comprobá que entrás por SSH y re-corré. Si el puerto de `VPS_SSH_PORT` no es el de sshd, corregilo antes. |
| 8 | no se pudo consultar (ssh 255, salida ilegible) | Ssh caído o salida ilegible: **no es** "no hay". Comprobá el acceso (`sudo -n true`) y re-corré; no insistas a ciegas. |
| 9 | alcance: el destino no parece un VPS dedicado (contenedores ajenos, puerto 80 ocupado) o es el VPS viejo | No sigas. Revisá que `deploy/.env.vps-nuevo` apunte al VPS nuevo y dedicado: hay contenedores ajenos, el puerto 80 está ocupado o es el VPS viejo. |

### `migrar-vps.sh`

| Código | Significado | Qué hacer |
|---|---|---|
| 0 | GO técnico: todas las V automáticas OK. Quedan V9 y la decisión de Martin | Seguí con V9, la decisión de GO y las PCs (sección 5). Si fue `--ensayo`: sección 4. |
| 2 | uso / argumentos | Corregir el comando. |
| 3 | precondición del pre-vuelo (config, identidad, credencial, corrida sin terminar, lock): no se tocó nada | No se tocó nada. Leer el mensaje: config, machine-id, credencial rechazada, overrides que faltan (sección 4.2), lock tomado o una corrida sin terminar (`--continuar <corrida>` o `--revertir`). |
| 4 | el operador no confirmó: no se tocó nada | No escribiste `CONGELAR`. No se tocó nada: re-correr cuando estés listo. |
| 5 | falló un paso antes de congelar el viejo, que sigue intacto y sirviendo. Re-correr arranca una corrida nueva | El viejo sigue intacto y sirviendo (el resumen lo confirma). Corregir la causa y re-correr: arranca una corrida nueva. |
| 6 | falló un paso después de congelar el viejo, que quedó congelado. El resumen dice el estado exacto y la salida: --continuar <corrida> o --revertir | El viejo quedó congelado. Corregir la causa y `deploy/migrar-vps.sh --continuar <corrida>`, o volver atrás con `deploy/migrar-vps.sh --revertir`. Ver "Seguir o volver atrás". |
| 7 | NO-GO: falló el arranque de la API nueva (la corrida queda abierta: --continuar la retoma en el paso 11) o falló una V (resultado=NO-GO: --continuar repite la verificación). La API nueva queda como esté para diagnosticar (las PCs no están instaladas: nadie escribe). Se imprime --revertir | El resumen imprime `--revertir`. Arreglar y `--continuar <corrida>` repite la verificación, o `--revertir`. La API nueva queda como esté, para diagnosticar. |
| 8 | no se pudo consultar | "No pude consultar" nunca es "cero" ni "vacío": investigá (ssh, docker, systemctl) antes de reintentar. Si el viejo ya estaba congelado, `--revertir`. |
| 9 | alcance: la lista de contenedores del viejo cambió, o los dos .env son el mismo servidor | Alcance: no se ofrece `--continuar` hasta que un humano mire. Otro proceso tocó contenedores del viejo, o los dos `.env` apuntan al mismo servidor. |

### Seguir o volver atrás

- **`deploy/migrar-vps.sh --continuar <corrida>`** retoma una corrida que falló **después** de congelar el
  viejo, en el primer paso que no terminó (solo entre el 6 y el 12). El paso 6 vuelve a volcar en una
  carpeta nueva. Si el restore salió con 6 o 7 (la base quedó restaurada pero no verifica), solo sigue con
  `--continuar <corrida> --reintentar-restauracion`: lo decide un humano, y repite `--recrear-base` con otro
  dump de seguridad. Una corrida que nunca congeló el viejo se cierra sola y hay que empezar otra. Tras
  un NO-GO arreglado, repite la verificación y el resumen.
- **`deploy/migrar-vps.sh --revertir`** (sin argumento toma la última corrida de corte; con
  `~/stockapp-backups-vps/corridas/corte-<ts>`, esa) devuelve el servicio al VPS viejo: pide `REVERTIR`,
  detiene **primero** la API nueva, y en el viejo desenmascara, devuelve la unit, hace `daemon-reload` y
  arranca. Se niega (3) si `LogsAuditoria` del nuevo creció desde la verificación: hubo PCs trabajando, ya
  no es la vuelta atrás limpia sino R-B (sección 7). `--aun-con-escrituras` muestra el delta y sigue a la
  confirmación, sabiendo que lo escrito en el nuevo **no** vuelve al viejo. Es idempotente: re-correrlo da
  `YA ESTABA` en cada paso. Códigos propios: 0 revertido; 2 uso; 3 precondición o escrituras reales; 4 sin
  confirmar; 6 falló un paso de la vuelta (re-correr); 8 no pude consultar, o la unit no se puede devolver
  sin pisar otra; 9 otros servidores que los de la corrida, o el `docker ps` del viejo cambió (la vuelta
  atrás ya se hizo; el resumen separa los contenedores de StockApp de los ajenos).
- **VPS nuevo inalcanzable.** `--revertir` sale 8 sin tocar el viejo, porque no puede garantizar que la
  API nueva esté detenida (con dos APIs vivas, lo escrito en la nueva se perdería). Apagá el VPS nuevo
  **desde el panel del proveedor** y devolvé el viejo con los comandos de abajo.
- **Después de un `--revertir`, antes de reintentar.** El `--revertir` deja la API nueva **detenida y
  deshabilitada**, y es esa API la que sirve el feed. Por eso, sin hacer nada más, `preparar-vps-nuevo.sh
  --verificar` sale 5 (paso 8 PENDIENTE: el feed no responde) y el pre-vuelo del corte siguiente aborta 3
  nombrando el feed, antes de pedir ninguna confirmación. Nada la vuelve a levantar solo (el `preparar`
  completo no vuelve a desplegar si la unit existe). Se levanta con un deploy, que reinstala la misma
  versión sin tocar la base (`install.sh` hace `enable` y `restart`):

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/deploy-vps.sh 0.5.3 --sin-backup
```

  Recién entonces comprobá que el nuevo está listo, con `--verificar` (solo lectura):

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3 --verificar
```

  Esperado: exit 0, los pasos 1 a 8 en `HECHO`. Solo en el ensayo previo al día D: ese deploy es seguro
  porque el nuevo todavía no es producción.
- **A mano**, solo si `--revertir` no puede correr (por ejemplo, tu máquina murió a mitad del corte). Son los
  comandos que el corte imprime antes de la confirmación, en este orden, en el VPS **viejo** (y antes,
  `sudo systemctl stop stockapp-api` en el nuevo):

```bash
sudo systemctl unmask stockapp-api
sudo mv /etc/systemd/system/stockapp-api.service.congelado /etc/systemd/system/stockapp-api.service
sudo systemctl daemon-reload
sudo systemctl start stockapp-api
systemctl is-active stockapp-api
```

  Esperado: `active`. Un `unmask` solo, sin devolver la unit, no sirve: la unit sigue movida.

## 7. R-B: después de la primera escritura real, a mano

- [ ] **R-B.** Corregir hacia adelante es lo preferido. Solo si el VPS nuevo resulta irrecuperable (**último
  recurso**) se hace la **migración inversa con las mismas piezas**:
  1. `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/volcado-final-vps.sh --salida ~/stockapp-backups-vps/inversa-<fecha>/`
     (congela el nuevo).
  2. Copiar al VPS viejo el volcado, su manifiesto y `restaurar-backup.sh` (como en C4, con el puerto y el usuario del `.env` del operador viejo).
     El viejo sigue detenido y enmascarado, que es lo que el script exige (API inactiva).
  3. `restaurar-backup.sh … --recrear-base stockapp` en el viejo. El dump de seguridad que toma
     `--recrear-base` conserva lo que tenía el viejo.
  4. Recién ahí, los comandos de R-A completos (`unmask`, devolver la unit, `daemon-reload`, `start`) y repuntar las PCs con el Configurador.

C4 = paso 8 de `migrar-vps.sh`; R-A es `--revertir` (sección 6). `restaurar-backup.sh` y
`volcado-final-vps.sh` son los bloques que `migrar-vps.sh` llama por vos; acá, por ser un último recurso
que ningún modo automatiza, se usan a mano.

## 8. Desastre: el VPS murió

El VPS de producción no se puede congelar ni volcar. Se recupera con **el último dump que tengas en tu
máquina** (el que deja `deploy/bajar-backup-vps.sh` mientras el servidor vive, o el volcado de una
corrida anterior), sobre un VPS nuevo o reinstalado:

1. Preparar el VPS (sección 3, con el dominio y la IP que corresponda en `deploy/.env.vps-nuevo`):

```bash
STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3
```

2. Restaurar el dump:

```bash
deploy/migrar-vps.sh --desde-backup <dump> --env-nuevo deploy/.env.vps-nuevo
```

   Con `--manifiesto <m>` también se comparan los conteos; sin él se verifica la estructura. Antes de
   tocar nada comprueba el dump (legible, mínimo 1024 bytes, `pg_restore --list`; su `.sha256` si hay uno
   al lado) y pide `RESTAURAR`, con la fecha del dump y el aviso de que **todo lo posterior a esa fecha
   se pierde**. No se contacta al VPS viejo: `--env-viejo` (default `deploy/.env`) es un archivo LOCAL de
   tu máquina, del que salen `VPS_ADMIN_USER`/`VPS_ADMIN_PASSWORD` para V5 y V8 y la clave de licencias; sin
   ese archivo o sin `VPS_ADMIN_USER` sale 2. Si falla, la corrida se cierra y se vuelve a correr
   entera: no hay `--continuar` ni `--revertir`.

**El límite, dicho en voz alta:** un desastre se recupera hasta el último dump que esté fuera del
servidor muerto, y hoy ninguno lo está de forma automática. Por decisión de Martin,
el backup off-site programado es el próximo proyecto y no forma parte de esta migración. Mientras tanto, la bajada
periódica es el Procedimiento 1 de `deploy/PROCEDIMIENTOS.md`.

## 9. D+14: cerrar

El VPS viejo queda **detenido y enmascarado durante 14 días** (rollback). No se borra nada: ni el volumen
`stockapp_pgdata`, ni `/var/lib/stockapp`, ni la unit. Pasado el plazo (la fecha la imprimió el resumen
del corte):

```bash
deploy/migrar-vps.sh --post-corte ~/stockapp-backups-vps/corridas/corte-<ts>
```

Exige que la corrida tenga `resultado=GO` y 14 días desde el congelamiento (antes del plazo, solo con
`--antes-de-plazo` y la palabra `BORRAR`). **No toca el VPS viejo.** En orden:

1. Comprueba que `LogsAuditoria` del VPS nuevo sea ≥ al del manifiesto del corte; si no puede
   consultarlo, se detiene.
2. **ARCHIVA, no borra:** mueve el dump final del corte y su manifiesto a
   `~/stockapp-backups-vps/corte-<ts>/` (700), con el sha256 antes y después, y copia ahí el estado y el
   `registro.log` de la corrida. Es el "dump archivado fuera del servidor", y va primero: si falla, no
   sigue con nada más. Los volcados contienen hashes de contraseñas: ese directorio no se commitea ni se
   sube a ningún lado.
3. Ofrece borrar, **uno por uno**, mostrando la ruta exacta y el tamaño, y pidiendo `BORRAR` cada vez:
   las corridas de ensayo terminadas, lo que dejó el runbook manual anterior y, en el servidor nuevo,
   los directorios de `~/stockapp-deploy/migracion/`. Todo lo demás se conserva y lo dice: lo que se
   conserva incluye `migracion-<fecha>/` (puede tener el dump de un corte manual sin archivar),
   `corte-*`, `inversa-*` y la corrida del corte, que nunca se borra.
4. **Imprime, sin ejecutar,** la referencia para desmantelar el VPS viejo. Desmantelarlo es una decisión de
   Martin, siempre acotada por nombre a StockApp (nunca nada global): `docker compose … down` **sin**
   `-v`; `docker volume rm stockapp_pgdata` solo con el dump del corte archivado y `LogsAuditoria` del
   nuevo ≥ al del manifiesto; `systemctl disable` solo aplica si antes se hizo `unmask`.

Re-correrlo da `YA ESTABA` en el archivo. Códigos: 0 hecho; 2 uso; 3 precondición (sin GO, antes del
plazo, `LogsAuditoria` menor que el manifiesto, lock); 4 sin la palabra `BORRAR` del plazo; 5 falló el
archivo o un borrado (lo hecho queda hecho; re-correr); 8 no pude consultar; 9 el VPS nuevo no es el de
la corrida. Ambiente de testing después del corte: decisión pendiente de Martin (spec §5.8).

## 10. Tiempos y E1

Los llena E1 (lo corre un agente); el `--ensayo` imprime la tabla de su corrida lista para pegar y
completa la columna del VPS nuevo. El corte real usa esos tiempos para dimensionar la ventana.

| Paso | E1 (local) | Ensayo `--ensayo` (VPS nuevo) | Corte real |
|---|---|---|---|
| Bajar el backup real (`bajar-backup-vps.sh`) | 13 s (dump de 95 KB; el `--id` del historial, 11 s) | n/a | n/a |
| `ensayo-local.sh preparar` | 5 s | n/a | n/a |
| Volcado (paso 6 de `migrar-vps.sh`; en E1, `ensayo-local.sh volcar`) | 2 s | _pendiente_ | _pendiente_ |
| Restauración (paso 9 de `migrar-vps.sh`; en E1, `ensayo-local.sh restaurar`) | 14 s sobre la base vacía; 5 s con `--recrear-base` | _pendiente_ | _pendiente_ |
| Arranque de la API contra la base restaurada (paso 11) | 28 s hasta `GET /` OK (incluye restore y build de `dotnet run` con un `HOME` vacío) | _pendiente_ | _pendiente_ |
| Verificación (paso 12, V1 a V10) | menos de 10 s (V4, V5, V8 y `/updates`; `--verificar-post-arranque`, 0,4 s) | _pendiente_ | _pendiente_ |
| Ventana total con el VPS viejo sin API (pasos 6 a 13) | n/a | _pendiente_ | _pendiente_ |

**Resultado de E1 (2026-10-20): OK.** Backup real de testing (31 tablas, 150 filas, 25 migraciones,
última `20260910225922_AgregaIndiceFuncionalNombreCatalogos`): `preparar`, `volcar` y `restaurar` con
exit 0, conteos idénticos al manifiesto, secuencias y catálogos OK; segundo `restaurar` sin
`--recrear-base` → 3, con `--recrear-base` → 0 y dump de seguridad. API local: `GET /` OK, sin
excepciones de migración, V4 `activada: true`, V5 login real con token, V8 historial servido (200) y
`POST /backups` → 202 con una corrida `Exitosa` en 1 s, `/updates` sirve el feed,
`--verificar-post-arranque` exit 0 (`CorridasBackup` 11 → 11: el catch-up y el `POST` sumaron dos
corridas y la retención borró dos). Limpieza: cero contenedores, volúmenes y redes `stockapp-ensayo-*`,
`docker ps` igual al del inicio. **El ensayo contra los servidores reales (`--ensayo`) y el corte quedan
PENDIENTES**: necesitan el VPS nuevo, el dominio y la fecha.

