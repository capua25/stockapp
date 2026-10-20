# StockApp — Migración de VPS automatizada en dos scripts

**Fecha:** 2026-10-20
**Estado:** diseño para revisión de Martin. Las decisiones marcadas **(decidido por Martin)** ya están tomadas; las marcadas **(decisión del asistente)** son propuestas para que Martin las confirme o corrija antes de planificar.
**Alcance:** dos orquestadores nuevos en `deploy/` (`preparar-vps-nuevo.sh` y `migrar-vps.sh`), un cambio chico en `deploy/deploy-vps.sh` (§6.6), la extensión del arnés de ensayo local (`deploy/ensayo/ensayo-local.sh` y sus shims), sus tests, y la reescritura de `deploy/MIGRACION-VPS.md` como runbook corto.
**Fuera de alcance:** reescribir los bloques que ya existen y están probados (`restaurar-backup.sh`, `volcado-final-vps.sh`, `bajar-backup-vps.sh`, `publicar-release.sh`, el hook de TLS), backups fuera del servidor (off-site), multi-tenant, el ambiente de testing posterior al corte y desmantelar el VPS viejo.
**Relación con la spec anterior:** `docs/superpowers/specs/2026-10-20-migracion-vps-design.md` sigue siendo la autoridad sobre **qué** se migra, los anti-objetivos A1 a A7, los dos scripts que diseñó y las decisiones de §5.1. Esta spec reemplaza **cómo** se ejecuta: el runbook manual pasa a dos comandos.

## 1. Intención y porqué

La migración al VPS nuevo ya tiene sus piezas: `restaurar-backup.sh`, `volcado-final-vps.sh`, `bajar-backup-vps.sh`, el hook de TLS versionado y un ensayo local (E1) que salió bien con un backup real. Lo que las encadena es `deploy/MIGRACION-VPS.md`, un runbook de unas 760 líneas con P1 a P10, E1, E2, C0 a C10, V1 a V10, R-A, R-B y el post-corte, todo para copiar y pegar a mano.

**Decidido por Martin (2026-10-20): el runbook manual se reemplaza por dos scripts.**
- `deploy/preparar-vps-nuevo.sh` deja un VPS en blanco listo para recibir los datos (reemplaza P3 a P8, el primer deploy y la publicación del feed).
- `deploy/migrar-vps.sh` hace el ensayo, el corte, la vuelta atrás y la restauración desde un backup (reemplaza C1 a C8 y E2).

**Por qué (decidido por Martin):**

1. **Todos los defectos que encontró la review final estaban en prosa que nunca se ejecutó.** No había ningún bug en los scripts. Lo que se rompía era el texto del runbook:
   - restaurar en silencio el volcado de E2 en lugar del corte, que perdía todo lo escrito entre E2 y el corte (MUST-FIX 1);
   - un `tar -C` sobre un directorio que en un VPS en blanco no existe (MUST-FIX 2);
   - la contraseña de un usuario real en el argv de `jq` (MUST-FIX 3);
   - un comando de recuperación de `PROCEDIMIENTOS.md` que sale con 2 tal como está escrito (MUST-FIX 4);
   - en los "fix-now": R-A sin detener primero la API nueva, V10 comparando contra una lista que nadie guardaba, y un post-corte que borraba la única copia local del dump final.

   Cada arreglo agregó más prosa, y la prosa solo se puede custodiar con tests de sincronía que verifican que el texto **existe**, no que el comando **funcione**. Un script, en cambio, se prueba con TDD y con mutación quitando reglas, que es lo que ya cubre a los bloques.
2. **Reutilización para recuperación ante desastres.** Si un VPS muere, el camino es VPS nuevo + `preparar-vps-nuevo.sh` + el último backup disponible + `migrar-vps.sh --desde-backup`. Hoy ese camino no existe salvo en la prosa de `PROCEDIMIENTOS.md`.
3. **Servidores futuros.** El multi-tenant va a necesitar preparar servidores más de una vez. Un procedimiento que vive en un script se repite igual; uno que vive en un runbook se repite con errores.

**Nota 2026-10-20 (supersede):** la spec anterior, §3, lista como no-objetivo "Automatizar el corte entero en un solo comando (decisión del asistente)". **Esta spec lo deja sin efecto, por decisión de Martin.** Los puntos donde un humano tiene que mirar el resultado (conteos, login, smoke de la GUI) no desaparecen: los conteos y el login pasan a ser verificaciones automáticas y fail-closed, el smoke de la GUI sigue siendo de un agente y **la decisión de GO sigue siendo humana** (§3).

## 2. Objetivos

- **O1.** Un VPS Ubuntu 24.04 en blanco queda listo para recibir los datos con **un comando** (`preparar-vps-nuevo.sh`): Docker, usuario y directorios, firewall, TLS, `.env` del servidor, Postgres, la API instalada y el feed publicado. Correrlo dos veces da el mismo resultado: la segunda corrida informa "ya estaba" en cada paso.
- **O2.** El corte es **un comando** (`migrar-vps.sh`) con **una sola confirmación**, justo antes de congelar el VPS viejo. Todo lo demás es automático y termina en un resumen GO / NO-GO con tiempos.
- **O3.** El ensayo general (`migrar-vps.sh --ensayo`) corre **exactamente la misma secuencia** que el corte. La diferencia es que el VPS viejo vuelve solo a su estado anterior.
- **O4.** La vuelta atrás R-A es **un comando** (`migrar-vps.sh --revertir`).
- **O5.** Si el proceso se corta a mitad de camino, volver a correr el script retoma desde donde quedó o dice exactamente qué hacer. Nunca repite a ciegas un paso destructivo.
- **O6.** Recuperación ante desastres con las mismas piezas: `migrar-vps.sh --desde-backup <dump>` sobre un VPS preparado.
- **O7.** Los dos orquestadores corren de punta a punta en la máquina del operador, contra el arnés de ensayo local (con los límites de §8.2), y sus guardianes se prueban por mutación.

## 3. No-objetivos

- **Automatizar lo que sigue siendo manual (decidido por Martin):**
  - el registro DNS en el proveedor;
  - instalar las PCs (Configurador y aviso de SmartScreen);
  - el smoke de la GUI (V9), que lo hace un agente con `scripts/gui-verificacion/`;
  - la decisión de GO;
  - los borrados del día D+14, uno por uno y con confirmación.
- **Reescribir los bloques existentes.** Los orquestadores los llaman como subprocesos y confían en sus códigos de salida (§6.2). El único cambio a un bloque es el de `deploy-vps.sh` (§6.6), porque sin él el primer deploy sobre una base vacía no funciona.
- **R-B (migración inversa después del GO).** Sigue siendo manual y de último recurso, con las mismas piezas, como en la spec anterior. Automatizarla obligaría a hacer simétrico el congelamiento del VPS viejo compartido, y eso no se justifica para un camino que se espera no usar nunca.
- **Parametrizar nombres para varias instancias** (unit, contenedor, `/var/lib/stockapp`). Es trabajo del multi-tenant. Los orquestadores no cablean nada que lo impida, pero tampoco lo resuelven.
- **Backups off-site.** Siguen siendo un problema aparte (spec anterior, §3). El modo `--desde-backup` hace explícito que la recuperación ante desastres depende de ellos (R8).
- **Desmantelar el VPS viejo.** `--post-corte` solo **imprime** los comandos de referencia y nunca los ejecuta.

## 4. Anti-objetivos (lo que NO puede pasar)

**A1 a A7 de la spec anterior siguen vigentes sin cambios:** no perder datos, no borrar auditoría, que nadie escriba en el viejo después del dump, no tocar pinar, no restaurar encima de una base migrada sin `--recrear-base`, no dejar un dump a medio restaurar y no dejar dumps en lugares legibles por otros. Los orquestadores los heredan porque llaman a los mismos bloques. Además:

- **A8. Quedarse sin acceso SSH al VPS nuevo.** El firewall se habilita solo después de permitir el puerto SSH, con una reversa automática armada y un re-chequeo con una **conexión nueva** (§6.4, paso 3).
- **A9. Apuntar al VPS equivocado.**
  - `preparar-vps-nuevo.sh`, que instala paquetes, toca el firewall y despliega, nunca puede correr contra el VPS viejo compartido.
  - `migrar-vps.sh` nunca puede correr con los dos `.env` apuntando al mismo servidor.
  - Se comprueba con la identidad real de cada host (`/etc/machine-id`), no con el nombre que figura en el `.env` (§6.3.4).
- **A10. Regenerar los secretos sobre un volumen ya inicializado.** La imagen de Postgres usa `POSTGRES_PASSWORD` solo en la primera inicialización del volumen. Un `.env` regenerado deja la API sin poder conectarse. Si el `.env` del servidor ya existe, **nunca se reescribe**.
- **A11. Restaurar un volcado que no es el de esta corrida.** Es el MUST-FIX 1 de la review, ahora como invariante de código: el orquestador conoce el `<ts>` que acaba de producir y no restaura ningún otro (§6.5.4, paso 7).
- **A12. Un congelamiento desatendido.** El corte nunca congela el VPS viejo sin una confirmación escrita por un humano en una terminal. Un corte sin terminal se niega a correr. La única excepción son los hosts `.invalid` del arnés (§6.3.6).
- **A13. Un "retomar" ciego.** Un reintento nunca repite un paso cuyo resultado no puede verificar. Retoma solo desde estados observables y, si no puede saber dónde quedó, se detiene y lo dice.
- **A14. Correr `deploy-vps.sh` contra el VPS viejo.** `migrar-vps.sh` no lo llama nunca. `preparar-vps-nuevo.sh` lo llama solo después de los chequeos de identidad de A9.

## 5. Restricciones

- **El VPS viejo es compartido con pinar (decidido por Martin).**
  - Sobre el viejo, `migrar-vps.sh` solo corre lecturas acotadas por nombre (`systemctl is-active/is-enabled/show stockapp-api`, `docker ps --format`, `docker exec stockapp-pg`), `volcado-final-vps.sh` y los comandos de R-A.
  - Nada global de Docker, systemd, certbot ni firewall. El proxy de la otra app no se toca.
  - `migrar-vps.sh` entra en `MigracionVpsSincroniaTests.ScriptsNuevos`, con todos sus tokens prohibidos.
- **El VPS nuevo es dedicado.** `preparar-vps-nuevo.sh` sí instala paquetes y maneja `ufw` y `certbot`, porque es su trabajo. Por eso queda **exento** de la lista de tokens prohibidos, pero con un guardián propio: se niega a operar si el destino tiene contenedores ajenos o el puerto 80 ocupado, que son señales de que no es un VPS dedicado (§6.4, paso 0).
- **Secretos.**
  - Nunca viajan en argv, ni quedan en archivos de la máquina del operador, ni aparecen en logs.
  - `POSTGRES_PASSWORD`, `JWT_SECRET` y `BOOTSTRAP_PASSWORD` se generan **en el servidor** y nunca pasan por la máquina del operador (decidido por Martin).
  - La contraseña del usuario real que se usa para verificar se pide por terminal, vive solo en una variable del proceso y viaja por stdin, igual que en `bajar-backup-vps.sh`. El token queda en una variable, no en un archivo: en eso se corrige el V5 del runbook actual, que lo guardaba en `~/stockapp-backups-vps/token`.
- **Nada pasa por un `/tmp` compartido.** Los dumps viven en el `/tmp` del contenedor, en el directorio de la corrida (700) o en el directorio de la migración del servidor (700). En la máquina del operador, todo temporal se crea dentro del directorio de la corrida. Así desaparece también el `/tmp/sha-corte-<fecha>.txt` del post-corte actual.
- **La auditoría no se borra nunca.** Todo lo destructivo pasa por `restaurar-backup.sh --recrear-base`, que toma antes un dump de seguridad. `--post-corte` nunca borra el archivo del corte.
- **Bash:** `set -euo pipefail`, el gotcha de `grep` en un pipe y la regla de que "no pude consultar" nunca equivale a "vacío" ni a "cero". El exit 255 de ssh es siempre "no pude consultar".
- **TDD con mutación quitando reglas.** Los contenedores y procesos de los tests tienen nombre o PID propio, se eliminan por nombre o PID exacto y se verifica con `docker ps`/`ps` al terminar. Ningún test toca el `stockapp-pg` de desarrollo.
- **No se compila por rutina.** El arnés publica la API una vez por corrida de tests (§8.2): es necesario, no rutina.
- **Ubuntu 24.04** en el VPS nuevo (spec anterior, §5.1, decisión 10).

## 6. Diseño

### 6.1 Decisiones

**De Martin (2026-10-20):**
1. Dos scripts, `preparar-vps-nuevo.sh` y `migrar-vps.sh`, reemplazan el runbook manual.
2. `preparar-vps-nuevo.sh` corre por ssh contra el VPS nuevo (`STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo`) y hace, en este orden:
   - Docker y compose;
   - el usuario `stockapp` y sus directorios (incluido `.local/share/StockApp`);
   - el firewall, con el SSH primero y un re-chequeo de acceso;
   - `certbot --standalone` y la instalación del hook, con el dominio que sale del `.env`;
   - el `.env` del servidor, generado **en el servidor** (`LICENCIA_CLAVE_PUBLICA_BASE64` copiada del `deploy/.env` viejo, `Licencia__Habilitado=false` y los valores fijos del runbook);
   - Postgres levantado y verificado **vacío**;
   - el primer deploy con `deploy-vps.sh --sin-backup`;
   - la publicación del feed 0.5.3.
3. `migrar-vps.sh` tiene tres modos:
   - `--ensayo`: el VPS viejo vuelve solo a su estado anterior;
   - corte: **una** confirmación justo antes de congelar el viejo, que es cuando empieza la indisponibilidad, y todo lo demás automático;
   - `--revertir`: R-A en un comando.
4. La secuencia del corte es la siguiente. El V9 queda en manos de un agente o se hace a mano.
   1. Detener la API nueva.
   2. Limpiar los restos del ensayo.
   3. Guardar la lista de contenedores del viejo.
   4. `volcado-final-vps.sh`.
   5. Transferir a un directorio de corrida nuevo.
   6. Restaurar con `--recrear-base`, controlando que `fecha_utc` sea la de esta corrida.
   7. Extraer el historial.
   8. Arrancar la API.
   9. Correr V1 a V8 y V10.
   10. Imprimir el resumen GO / NO-GO con tiempos.
5. Siguen siendo manuales: el DNS, las PCs, el smoke de la GUI, la decisión de GO y los borrados del día D+14.

**Del asistente:**
1. **Los bloques no se reescriben.** Se llaman como subprocesos, con su `STOCKAPP_ENV_LOCAL` explícito en cada llamada, y el contrato con ellos son **sus códigos de salida y los archivos que dejan**, nunca el texto que imprimen (§6.2).
2. **Cada corrida tiene un directorio propio** en la máquina del operador y otro en el servidor, con un archivo de estado sin secretos (§6.5.2). Una sola corrida sin terminar a la vez, protegida con un lock.
3. **Identidad por `/etc/machine-id`** (A9, §6.3.4).
4. **La confirmación del corte se hace escribiendo una palabra** (`CONGELAR`), no con `s/N`, y se lee de la terminal (§6.3.6).
5. **La credencial de verificación se valida contra el VPS viejo antes de la confirmación.** Un error al tipearla tiene que aparecer antes de la indisponibilidad, no en V5 con el viejo ya congelado (§6.5.3).
6. **El firewall se habilita con una reversa automática armada.** Hay un timer transitorio con nombre propio que deshace `ufw enable` si el re-chequeo con una conexión nueva no llega a desarmarlo (§6.4, paso 3).
7. **El primer deploy sobre una base vacía necesita un cambio en el pre-vuelo de `deploy-vps.sh`** (§6.6). Es el hecho del repo que contradice el brief, y se explica ahí.
8. **El modo de recuperación ante desastres es un modo de `migrar-vps.sh`** (`--desde-backup`), no un script aparte (§6.5.7).
9. **`--post-corte` existe, pero solo archiva** el dump del corte, sin destruir nada, y **pregunta ruta por ruta** antes de cada borrado. Nunca toca el VPS viejo (§6.5.8).
10. **`--revertir` pide una confirmación propia** y se niega si detecta escrituras reales en el VPS nuevo, porque en ese caso ya no es R-A sino R-B (§6.5.6).
11. **El arnés local simula los dos hosts con shims, no con un contenedor con sshd** (§8.2).
12. **`deploy/MIGRACION-VPS.md` pasa a ser un runbook corto** (§6.7). El detalle de P, E, C y V que hoy está en prosa se borra, porque queda en git y en el código.

### 6.2 Composición: los bloques y quién los llama

| Bloque (existente, probado) | Lo llama | Dónde corre | Qué se usa de su contrato |
|---|---|---|---|
| `deploy/deploy-vps.sh <v> --sin-backup` | `preparar` (primer deploy) | operador → VPS nuevo | exit 0 = deploy verificado (su paso 6). Cualquier otro valor = falló (hoy sale siempre con 1) |
| `build/publicar-release.sh <v>` | `preparar` | operador → VPS nuevo | exit 0 = feed publicado y verificado por HTTP (su paso 7) |
| `deploy/tls/stockapp-tls-deploy-hook.sh` | certbot, en el VPS nuevo | VPS nuevo | se instala con `install -m 0755`. Exit 2 = no copió nada |
| `deploy/volcado-final-vps.sh --salida <dir> [--ensayo]` | `migrar` | operador → VPS viejo | exit 0/2/3/4/8/9 (su tabla). **Archivos:** el único `volcado-*.dump` y `volcado-*.manifiesto` de un `<dir>` **nuevo y vacío**, más `backups-historial.tar` y `overrides-api-env.txt` |
| `deploy/restaurar-backup.sh … --recrear-base stockapp --dir-seguridad <corrida>/seguridad` | `migrar`, por ssh | VPS nuevo (se copia a la corrida: es autocontenido) | exit 0/2/3/4/5/6/7/8/9 (su tabla). Llega intacto a través de ssh; el 255 es de ssh y cuenta como "no pude consultar" |
| `deploy/restaurar-backup.sh --verificar-post-arranque --manifiesto <m>` | `migrar` (V6) | VPS nuevo | exit 0 = conteos posteriores al arranque aceptables |
| `deploy/bajar-backup-vps.sh` | el operador, a mano | operador → un VPS vivo | produce el dump que consume `--desde-backup` (§6.5.7). Los orquestadores no lo llaman |

**Reglas de composición (decisión del asistente):**
- Los bloques se ejecutan como procesos hijos, **nunca con `source`**. El entorno de cada llamada se arma explícitamente: `STOCKAPP_ENV_LOCAL=<archivo> <bloque> …`. El orquestador nunca exporta `STOCKAPP_ENV_LOCAL` ni `VPS_*` para toda la sesión. Es el mismo criterio que la regla F2 del ledger de la migración.
- **No se parsea la salida humana de un bloque.** Para saber qué volcado se produjo, el orquestador le pasa a `volcado-final-vps.sh` un `--salida` **recién creado y vacío** y exige que al terminar haya **exactamente un** `volcado-*.dump` y **exactamente un** `volcado-*.manifiesto`, con el mismo `<ts>`. Cualquier otra cosa es un error del orquestador (exit 8) y no se adivina.
- El orquestador **no reinterpreta** el código de un bloque más allá de su clase. Imprime `bloque <nombre> salió con <n>` junto con la fila correspondiente de la tabla del bloque, que se copia en el encabezado del orquestador y la custodia un test de sincronía (§8.1), y decide según §6.3.2.
- La salida del bloque se ve en vivo (con `tee` al log de la corrida), con sangría para distinguirla de las líneas del orquestador.

### 6.3 Convenciones comunes a los dos orquestadores

#### 6.3.1 Salida para el operador

- **Una línea por paso, con número, nombre, resultado y duración:**
  ```
  [14:02:11] ( 4/12) volcado del VPS viejo ........................ OK      41 s
  [14:02:52] ( 5/12) transferencia al VPS nuevo ................... OK       3 s
  [14:02:55] ( 6/12) restauración (--recrear-base) ................ FALLÓ   12 s  (restaurar-backup.sh: 6)
  ```
  Resultados posibles: `OK`, `YA ESTABA` (paso idempotente que no tuvo nada que hacer), `OMITIDO` (con el motivo) y `FALLÓ` (con el bloque y el código).
- **Reloj de indisponibilidad.** En el corte, desde el congelamiento, cada línea agrega `viejo sin API hace 3 min 12 s`.
- **Log de la corrida** (`<corrida>/registro.log`, 600): todo lo que se ve en pantalla. Ningún secreto llega a la salida (§8.1 lo prueba con los valores reales del arnés).
- **Resumen final** con el resultado, la tabla de pasos y tiempos, la tabla de V con su estado, lo que queda pendiente (V9, la decisión de GO, las PCs), las fechas del corte y del fin del rollback (D+14), y el comando exacto para volver atrás si corresponde.

#### 6.3.2 Códigos de salida (decisión del asistente; estables, documentados en el encabezado y fijados por tests)

| Código | `preparar-vps-nuevo.sh` | `migrar-vps.sh` |
|---|---|---|
| 0 | el VPS quedó listo (o ya lo estaba) | **GO técnico**: todas las V automáticas OK. Quedan V9 y la decisión de Martin |
| 2 | uso / argumentos | uso / argumentos |
| 3 | precondición (config, `sudo -n`, versión del SO, DNS, falta `releases/win`, lock): **no se tocó nada** | precondición del pre-vuelo (config, identidad, credencial, corrida sin terminar, lock): **no se tocó nada** |
| 4 | — | el operador no confirmó: **no se tocó nada** |
| 5 | falló un paso: lo hecho queda hecho y re-correr continúa | falló un paso **antes** de congelar el viejo, que sigue intacto y sirviendo. Re-correr arranca una corrida nueva |
| 6 | el re-chequeo de acceso después de `ufw enable` falló: la reversa automática deshace el firewall en N segundos; esperar y re-correr | falló un paso **después** de congelar el viejo, que quedó congelado. El resumen dice el estado exacto y la salida: `--continuar <corrida>` o `--revertir` |
| 7 | — | **NO-GO**: la secuencia terminó, pero falló una V. La API nueva queda arriba para diagnosticar (las PCs no están instaladas: nadie escribe). Se imprime `--revertir` |
| 8 | no se pudo consultar (ssh 255, salida ilegible) | no se pudo consultar |
| 9 | alcance: el destino no parece un VPS dedicado (contenedores ajenos, puerto 80 ocupado) o es el VPS viejo | alcance: la lista de contenedores del viejo cambió, o los dos `.env` son el mismo servidor |

#### 6.3.3 Lock y corridas sin terminar

- `flock` sobre `~/stockapp-backups-vps/.migracion.lock`, tomado al inicio por los dos orquestadores y por todos los modos. Se libera solo cuando termina el proceso, así que no hay limpieza que olvidar. Si el lock está tomado → exit 3.
- `migrar-vps.sh` en modo corte o `--desde-backup` busca corridas cuyo estado no tenga `resultado=` (corridas sin terminar). Si hay alguna, se niega a empezar otra (exit 3) e imprime las dos salidas: `--continuar <corrida>` o `--revertir`.

#### 6.3.4 Identidad de los hosts (A9, A14)

- `preparar-vps-nuevo.sh`:
  - **exige** que `STOCKAPP_ENV_LOCAL` esté definida y no resuelva a `deploy/.env` (el default, que es el viejo);
  - si `deploy/.env` existe y define `VPS_HOST`, exige que sea distinto;
  - en el destino, aborta con 9 si `docker ps -a --format '{{.Names}}'` muestra algún contenedor que no matchee `^stockapp`, o si el puerto 80 está escuchando antes de que el propio script lo use para certbot.
- `migrar-vps.sh` lee `/etc/machine-id` de los dos hosts (es una lectura) y exige que sean **distintos**. Los guarda en el estado de la corrida: un `--continuar` o un `--revertir` posteriores exigen los mismos valores. Un `.env` que cambió de servidor a mitad de una corrida es un error, no una novedad.

#### 6.3.5 Secretos

- Los dos orquestadores cargan el `.env` del operador con el mismo bloque `STOCKAPP_ENV_LOCAL` que los bloques, con exit 3 si falta configuración.
- Los valores de `LICENCIA_CLAVE_PUBLICA_BASE64` y del `.env` del servidor nunca se imprimen: solo los **nombres** de las claves y "presente" o "falta".
- Todo lo que viaja al servidor (el script remoto que genera el `.env`, la clave pública, la SQL) va por **stdin de ssh**, nunca en el comando. Es el mismo criterio que `ssh_vps_autenticado` (`deploy-vps.sh`) y el pre-vuelo.

#### 6.3.6 La confirmación (A12)

- Se lee de `/dev/tty` con un texto que dice qué va a pasar y cuánto duró la indisponibilidad en el último ensayo (sale del estado de esa corrida). Hay que escribir exactamente `CONGELAR` en el corte, `REVERTIR` en `--revertir` y `RESTAURAR` en `--desde-backup`. Cualquier otra cosa → exit 4.
- Sin terminal → exit 3: un corte no se lanza desatendido.
- **Único desvío, para tests:** si los dos `VPS_HOST` terminan en `.invalid` (los del arnés; el ssh falso rechaza cualquier otro), la palabra se puede leer de stdin. El mismo criterio vale para todos los parámetros que solo existen para el ensayo local (`--contenedor-*`, `--raiz-remota-*`, la inyección de fallos de §8.2): si un host no es `.invalid`, se rechazan con exit 2. Así, ningún desvío de test sirve contra un servidor real.

### 6.4 `deploy/preparar-vps-nuevo.sh`

**Uso:** `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version <v> [--env-viejo deploy/.env] [--verificar] [--probar-renovacion]`

- `--version`: obligatoria, sin default (0.5.3 en la migración). Es la misma para el deploy y para el feed.
- `--env-viejo`: de donde se lee **solo** `LICENCIA_CLAVE_PUBLICA_BASE64`, línea por línea y sin `source`, igual que `restaurar-backup.sh` lee su `.env`.
- `--verificar`: modo de **solo lectura**. Recorre los pasos, informa `HECHO` o `PENDIENTE` en cada uno y sale con 0 solo si todos están hechos. Es el motor de la idempotencia expuesto al operador: sirve también para revisar un VPS preparado semanas atrás.
- `--probar-renovacion`: corre el paso 9 (§6.4, tabla). Ver la pregunta abierta 2.

**Principio de idempotencia (decisión del asistente): manda el estado observado, no un marcador.** Cada paso tiene tres partes: *¿ya está?* (lectura) → *aplicar* → *re-verificar*. Si ya estaba, informa `YA ESTABA` y no aplica nada. El único marcador del servidor es para lo que no se puede observar: que la renovación del certificado ya se probó. Vive en `~/stockapp-deploy/.preparacion/renovacion-probada` y guarda la fecha. Un paso que falla deja hecho lo que hizo, y re-correr el script retoma, porque los pasos anteriores dan `YA ESTABA`.

| Paso | ¿Ya está? (lectura) | Aplicar | Re-verificar |
|---|---|---|---|
| 0. Pre-vuelo (local + lectura remota) | — | `.env` del operador (A9, §6.3.4); `VPS_*`, `API_SCHEME=https`, `API_HOSTNAME` y `API_PORT` presentes; ssh con `BatchMode` y `sudo -n true`; `/etc/os-release` con `VERSION_ID="24.04"`; el puerto de `VPS_SSH_PORT` escuchando (`ss -ltn`); `API_HOSTNAME` resuelve a la misma IP que `VPS_HOST`, para no quemar intentos de Let's Encrypt; `releases/win` tiene `<v>`, para que `publicar-release.sh` no falle al final; `LICENCIA_CLAVE_PUBLICA_BASE64` legible en `--env-viejo`, con formato base64 y sin `CAMBIAR` | nada que aplicar: si algo falla → 3 o 9 |
| 1. Docker | `docker compose version` como `VPS_USER`, sin sudo | `apt-get install -y docker.io docker-compose-v2` y `usermod -aG docker <usuario>` | una conexión ssh **nueva** (cada llamada abre una) corre `docker ps` sin sudo |
| 2. Usuario y directorios | `id stockapp`; los directorios con dueño y modo | `useradd --system --create-home --home-dir /var/lib/stockapp --shell /usr/sbin/nologin stockapp` (mismo comando que `install.sh`); `install -d -o stockapp -g stockapp -m 750` de `.local`, `.local/share` y `.local/share/StockApp`; `~/stockapp-deploy` y `~/stockapp-deploy/migracion` con 700 | `stat` de cada uno |
| 3. Firewall | `ufw status` activo con las tres reglas | ver "Firewall" abajo | ver abajo |
| 4. TLS | `/etc/letsencrypt/live/<dominio>/` existe, el certificado vence en más de 30 días, el `renew_hook` de `/etc/letsencrypt/renewal/<dominio>.conf` es el hook y los PEM de `/var/lib/stockapp/tls/` son `stockapp:stockapp 0600` | `apt-get install -y certbot`; `install -m 0755` del hook en `/usr/local/sbin/` (se copia por scp al staging del usuario); `certbot certonly --standalone -n --agree-tos -d <dominio> --deploy-hook …`. Ver "TLS" abajo | lo mismo que "¿ya está?" |
| 5. `.env` del servidor | `~/stockapp-deploy/.env` existe | se **genera en el servidor** (ver abajo) | todas las claves requeridas están presentes, ninguna empieza con `CAMBIAR`, el modo es 600 y los valores **no secretos** coinciden con el `.env` del operador (`API_SCHEME`, `API_HOSTNAME`, `API_PORT`, `POSTGRES_USER`, `POSTGRES_DB`) |
| 6. Postgres | `stockapp-pg` corriendo y `healthy` | scp de `deploy/docker-compose.postgres.yml` y `docker compose --env-file .env -f docker-compose.postgres.yml up -d`; espera acotada hasta `healthy` | ver "Base vacía" abajo |
| 7. Primer deploy | la unit `stockapp-api` existe | `STOCKAPP_ENV_LOCAL=<env-nuevo> deploy/deploy-vps.sh <v> --sin-backup` (necesita el cambio de §6.6) | el exit 0 del bloque, que ya verificó `/licencia/estado`, las migraciones y `is-active` |
| 8. Feed | `GET /updates/releases.win.json` ya sirve una versión ≥ `<v>` | `STOCKAPP_ENV_LOCAL=<env-nuevo> build/publicar-release.sh <v>` | el exit 0 del bloque (verifica por HTTP) |
| 9. Renovación (solo con `--probar-renovacion`, y una única vez) | el marcador de la renovación existe | `certbot renew --force-renewal --cert-name <dominio>` | `is-active` = `active`, y en el journal hay un arranque posterior a la renovación |
| 10. Verificación final (siempre) | — | — | desde la máquina del operador, **sin `-k`**: `GET https://<dominio>:<puerto>/` OK y `/licencia/estado` con `activada:true`; el feed sirve `<v>`; `ufw` está activo con las tres reglas; el certificado vence en más de 30 días; la unit está `enabled`; `docker ps` muestra solo `stockapp-pg` |

**Firewall (A8), en este orden y nada más:**
1. `ufw allow <VPS_SSH_PORT>/tcp`, `ufw allow 80/tcp` y `ufw allow <API_PORT>/tcp`. Son idempotentes: si la regla ya existe, `ufw` la saltea. **Nunca** se usa `ufw reset` ni se borran reglas.
2. Si `ufw` ya está activo, se termina acá, después de re-verificar las reglas.
3. **Se arma la reversa:** `systemd-run --unit=stockapp-ufw-reversa --on-active=120 /usr/sbin/ufw disable`, un timer transitorio con nombre propio.
4. `ufw --force enable`.
5. **Re-chequeo con una conexión ssh nueva** desde la máquina del operador, con `ConnectTimeout` corto y `BatchMode`, que corre `sudo -n true`.
6. Si el re-chequeo responde, se desarma la reversa con `systemctl stop stockapp-ufw-reversa.timer`, usando ese nombre exacto, y se verifica que el timer ya no exista.
7. Si no responde, exit 6: la reversa deshace el firewall en 120 s como máximo.

Por qué no alcanza con "permitir el SSH primero": si `VPS_SSH_PORT` no es el puerto en el que escucha sshd (un error de tipeo, o el `ssh.socket` de 24.04 escuchando en otro puerto), la regla correcta no existe y el `enable` deja afuera al operador para siempre. El paso 0 ya compara el puerto, y la reversa cubre lo que el paso 0 no puede prever. El compose publica Postgres solo en `127.0.0.1:5433`, así que el conocido bypass de `ufw` por Docker no expone nada. Un test fija que el compose siga en loopback (§8.1).

**TLS:**
- El usuario `stockapp` se crea en el paso 2, **antes** del certificado, porque el hook hace `chown`.
- En la primera emisión, la API todavía no existe: el hook copia los PEM y su `systemctl restart stockapp-api` falla, así que certbot puede devolver un código distinto de cero aunque el certificado se haya emitido. Por eso **el paso no mira el exit de certbot, mira el resultado**: certificado en `live/`, `renew_hook` registrado y PEM copiados con dueño y modo correctos.
- Si el certificado existe pero los PEM no, se corre el hook a mano (`RENEWED_LINEAGE=… /usr/local/sbin/stockapp-tls-deploy-hook.sh`), como describe hoy el runbook en P5.c.
- **Esa recuperación pasa de prosa a código.**

**`.env` del servidor (decidido por Martin, detalle del asistente).**
- Se arma con un script remoto que viaja por **stdin** de ssh y corre con `umask 077`. Escribe con `install -m 600 /dev/null` + append. `POSTGRES_PASSWORD` (`openssl rand -base64 24`), `JWT_SECRET` (`openssl rand -base64 48`) y `BOOTSTRAP_PASSWORD` se generan **ahí**.
- La clave pública viaja por el mismo stdin.
- Los valores fijos son los de la tabla P6 del runbook actual:
  - `POSTGRES_USER=stockapp` y `POSTGRES_DB=stockapp`;
  - `BOOTSTRAP_ADMIN_USER=admin`;
  - `API_BIND=0.0.0.0`, `API_SCHEME=https`, `API_PORT` y `API_HOSTNAME` (del `.env` del operador);
  - `Kestrel__Certificates__Default__Path` y `KeyPath` en `/var/lib/stockapp/tls/…`;
  - `Licencia__Habilitado=false`;
  - `RateLimiting__Login__PermitLimit=10`.
- **Si el archivo existe, no se toca** (A10). Si le falta una clave o un valor fijo no coincide, el paso falla, informa **solo los nombres** y le deja la corrección al operador. Un `.env` de servidor no se edita automáticamente.
- `BOOTSTRAP_PASSWORD` solo sirve hasta el primer restore, porque el seed no hace nada si ya hay usuarios. Mientras tanto queda en el `.env` del servidor (600), donde Martin puede leerla si hiciera falta.
- **Los overrides** (claves que el `api.env` del viejo tiene fuera de lo que escribe `install.sh`) **no** los pone este script: sus valores pueden ser secretos del viejo, y la lista de nombres sale recién del volcado. Ver §6.5.3.

**Base vacía (decidido por Martin) y el primer deploy.**
- El paso 6 verifica que la base esté **vacía** (ninguna relación fuera de los schemas del sistema; mismo criterio que `restaurar-backup.sh`) **solo la primera vez**: si la unit `stockapp-api` todavía no existe.
- Con la API ya desplegada, la base deja de estar vacía y está bien: el primer arranque migró el esquema y sembró el admin. En ese caso el paso informa las tablas, las filas de `LogsAuditoria` y de `Usuarios`, y sigue sin tocar nada.
- Si la unit no existe y la base **no** está vacía, algo arrancó contra esta base sin pasar por el script: exit 5, con el detalle. No se borra nada.
- El paso 7 se corre **solo si la unit no existe**. Con la API instalada, preparar **nunca** vuelve a desplegar, porque después del corte este VPS es producción y un `deploy-vps.sh --sin-backup` sería un deploy sin backup. Los deploys posteriores se hacen con `deploy-vps.sh` sin `--sin-backup`, como siempre.

**Resumen final:** cada paso con `OK`, `YA ESTABA` u `OMITIDO`, y lo que queda **pendiente para Martin**:
- los nombres de los overrides que hay que cargar a mano en el `.env` del servidor, que se conocen después del primer `--ensayo`;
- la renovación, si nunca se probó;
- el `Setup.exe` de la misma `<v>`.

### 6.5 `deploy/migrar-vps.sh`

#### 6.5.1 Modos y uso

```
deploy/migrar-vps.sh --ensayo                      [--env-viejo deploy/.env] [--env-nuevo deploy/.env.vps-nuevo]
deploy/migrar-vps.sh                               [--env-viejo …] [--env-nuevo …]        # el corte
deploy/migrar-vps.sh --continuar <corrida>
deploy/migrar-vps.sh --revertir [<corrida>]                                               # R-A
deploy/migrar-vps.sh --desde-backup <dump> [--manifiesto <m>] [--env-nuevo …]             # desastre
deploy/migrar-vps.sh --post-corte <corrida-del-corte>                                     # D+14
```

- Los defaults de `--env-viejo` y `--env-nuevo` son los nombres de la tabla de los tres `.env` del runbook actual. Que dos archivos distintos apunten al mismo servidor lo detecta §6.3.4.
- Si `--revertir` no recibe una corrida, toma la última de modo corte cuyo estado registra el congelamiento.
- El usuario para V5 y V8 sale de `VPS_ADMIN_USER` del `--env-viejo`, que es el mismo usuario real que viaja en los datos. La contraseña sale de `VPS_ADMIN_PASSWORD` si está en el entorno o en el `.env`. Si no, se pide por terminal (`read -rs`) en el pre-vuelo. Tiene que ser un usuario con `GestionarDiagnostico`, por `/backups`.

#### 6.5.2 La corrida y su estado (decisión del asistente)

- **Local:** `~/stockapp-backups-vps/corridas/<modo>-<AAAAMMDDTHHMMSSZ>/` (700), fuera del repo y fuera de `/mnt`, con el mismo rechazo que `ensayo-local.sh`. Contiene:
  - `estado`, con `clave=valor`, sin secretos y escrito de forma atómica (temporal en el mismo directorio + `mv`);
  - `registro.log`;
  - `volcado/`, el `--salida` vacío que recibe `volcado-final-vps.sh`;
  - `contenedores-viejo.txt`.
- **Servidor nuevo:** `~/stockapp-deploy/migracion/<modo>-<ts>/` (700), con el volcado, el manifiesto, el tar del historial, la copia de `restaurar-backup.sh` y `seguridad/`, que es el `--dir-seguridad` de `--recrear-base`.
- **Claves del estado:**
  - `modo`, `inicio_utc`, `env_viejo`, `env_nuevo` y `machine_id_viejo`/`machine_id_nuevo`;
  - `paso_<n>=ok <utc> <segundos>` para cada paso;
  - `volcado=<nombre>`, `sha256=<…>` y `dir_remoto=<…>`;
  - `congelado_utc` y `api_nueva_activa_utc`;
  - `logs_auditoria_post_verificacion=<n>` (§6.5.6);
  - `resultado=GO|NO-GO|FALLO-<n>` y `fin_rollback=<fecha>`.

  `resultado` es lo último que se escribe, y una corrida sin esa clave está sin terminar (§6.3.3).

#### 6.5.3 Pre-vuelo (solo lectura; si falla, exit 3/8/9 sin tocar nada)

1. Los dos `.env` del operador, los `VPS_*` y la identidad (§6.3.4). Se compara contra el estado de la última corrida de ensayo, si existe.
2. **Viejo:**
   - `sudo -n true`;
   - `stockapp-api` `active`, y `FragmentPath` igual a `/etc/systemd/system/stockapp-api.service`: así se sabe **antes** de la indisponibilidad que el congelamiento va a poder mover la unit;
   - que no exista un `.congelado`;
   - `stockapp-pg` corriendo;
   - espacio en disco suficiente para el dump dentro del contenedor (`pg_database_size` contra `df` del volumen de Docker).
3. **Nuevo:**
   - `preparar-vps-nuevo.sh --verificar` da todo `HECHO` (se llama el bloque; los pasos 9 y 10 se informan, no bloquean);
   - espacio en disco para el dump, la copia de seguridad y el historial.
4. **Overrides.** Si hay un ensayo anterior, su `overrides-api-env.txt` lista nombres. Cada uno tiene que estar presente en el `.env` del servidor nuevo (se leen solo los nombres de las claves). Si falta alguno → exit 3. En el primer `--ensayo` no hay lista: se informa al final y el corte ya la va a tener.
5. **Credencial.** Login contra la API **vieja**, por ssh al loopback, con la contraseña por stdin, igual que `bajar-backup-vps.sh`. El login no escribe en la base (verificado en el ledger de la migración, `AuthEndpoints.cs:22-51`). Un 401 da exit 3 **antes** de la confirmación. En `--desde-backup` no hay viejo, y la credencial se valida recién en V5.
6. La credencial tiene que tener permiso para `GET /backups`, que es de solo lectura: 403 → exit 3.

#### 6.5.4 Secuencia (modo corte; `--ensayo` en §6.5.5)

| # | Paso | Detalle | Si falla |
|---|---|---|---|
| 1 | Pre-vuelo | §6.5.3 | 3/8/9, nada tocado |
| 2 | **Confirmación** | §6.3.6, palabra `CONGELAR`. Muestra los hosts (nombre + machine-id abreviado), el `<v>` instalado en el nuevo, la indisponibilidad del último ensayo y los comandos de R-A, que también quedan en el log antes de tocar nada | 4, nada tocado |
| 3 | Detener la API nueva | `sudo systemctl stop stockapp-api` en el nuevo; `is-active` tiene que dar `inactive` (vacío = no pude consultar) | 5 |
| 4 | Limpiar restos del ensayo | en el nuevo: borra **uno por uno, por patrón y con `-maxdepth 1`** los `backup_*.dump` y `*.tmp` de `/var/lib/stockapp/.local/share/StockApp/backups/` (C2 del runbook, mismo porqué: `ReconciliarDumpHuerfanosAsync`). Aparta en `migracion/legado-<ts>/` todo `volcado-*` o `backups-historial.tar` que esté suelto en la raíz de `migracion/`, porque un E2 manual anterior pudo dejarlo ahí. Las corridas del orquestador ya viven cada una en su directorio, así que no se mueven. Nada de `rm -rf` sobre variables | 5 |
| 5 | Lista de contenedores del viejo | `docker ps --format '{{.Names}}'` ordenada en `contenedores-viejo.txt`; si está vacía o el ssh falla → 8 | 8 |
| 6 | **Congelar y volcar** | `STOCKAPP_ENV_LOCAL=<env-viejo> deploy/volcado-final-vps.sh --salida <corrida>/volcado`. **Acá empieza el reloj de indisponibilidad**: se registra `congelado_utc` antes de llamar al bloque, porque su paso 2 detiene la API en los primeros segundos | ver nota ① |
| 7 | **Identificar el volcado de esta corrida (A11)** | en `<corrida>/volcado/` hay exactamente un `.dump` y un `.manifiesto` con el mismo `<ts>`; el manifiesto dice `dump=<ese nombre>`, su `sha256=` es el del archivo local (se recalcula) y su `fecha_utc=` es **posterior** a `inicio_utc` de esta corrida. Se guardan `volcado` y `sha256` en el estado. Desde acá, **ningún paso nombra otro volcado** | 6 |
| 8 | Transferir | `mkdir -m 700` del directorio remoto **nuevo** (falla si ya existe); scp del dump, el manifiesto, el tar y `restaurar-backup.sh`; `chmod 600` a los datos y 755 al script; `sha256sum` remoto igual al del estado | 6 |
| 9 | Restaurar | ssh: `<dir>/restaurar-backup.sh <dir>/<volcado> --manifiesto <dir>/<manifiesto> --recrear-base stockapp --dir-seguridad <dir>/seguridad`. La base siempre tiene algo (como mínimo, lo que sembró el primer deploy de preparar), así que `--recrear-base` va **siempre** y su dump de seguridad también (A2) | ver nota ② |
| 10 | Historial | `sudo tar -C /var/lib/stockapp/.local/share/StockApp -xpf <dir>/backups-historial.tar` y `chown -R stockapp:stockapp …/backups`. El directorio existe desde el paso 2 de preparar, así que el MUST-FIX 2 desaparece por construcción | 6 |
| 11 | Arrancar la API nueva | `sudo systemctl start stockapp-api`; espera acotada (120 s, reloj monotónico) hasta que `is-active` dé `active` **y** `GET /` responda OK por loopback (con `--resolve`, como `deploy-vps.sh`). Registra `api_nueva_activa_utc` | 7 (la API puede haber quedado en `failed`: el resumen trae las últimas líneas del journal) |
| 12 | Verificación | V1 a V8 y V10 (§6.5.5). Registra `logs_auditoria_post_verificacion` | 7 |
| 13 | Resumen | §6.3.1. `resultado=GO` o `NO-GO`, `fin_rollback = fecha del corte + 14 días`. Recuerda lo pendiente: **V9 (agente), la decisión de GO de Martin, las PCs** | — |

**① Fallo del volcado.** El bloque ya imprime el estado de la API vieja y los comandos de R-A. El orquestador agrega su lectura según el código del bloque:
- **3 o 8 antes del stop:** el bloque no detuvo nada (el orquestador lo confirma con `is-active` del viejo) → exit 5.
- **Cualquier fallo después del stop:** exit 6. El resumen dice: "el viejo quedó detenido (y enmascarado, si el bloque llegó hasta ahí); para seguir, `--continuar`, que **vuelve a correr el volcado** en una carpeta nueva (con la unit ya enmascarada no se mueve nada más, y es lo que el bloque documenta para un reintento); para volver atrás, `--revertir`".
- **9 (alcance):** exit 9 y **no** se ofrece `--continuar` hasta que un humano mire.

**② Fallo de la restauración.** El orquestador sigue la tabla de `restaurar-backup.sh`:
- **5:** la base quedó vacía, y `--continuar` reintenta el paso tal cual.
- **3 o 4:** precondición o dump inválido → exit 6, y `--continuar` repite desde la transferencia (paso 8), a un directorio remoto nuevo.
- **6 o 7:** la base quedó restaurada pero no verifica → exit 6 **sin** `--continuar` automático. Lo decide un humano: investiga y después elige entre `--continuar --reintentar-restauracion` (vuelve a `--recrear-base`, con otro dump de seguridad) y `--revertir`.
- **8 o 9:** exit 8, investigar.

En ningún caso se arranca la API (la regla de la spec anterior: el conteo se compara **antes** del arranque).

**`--continuar <corrida>` (A13).**
- Relee el estado y comprueba la identidad de los hosts.
- Retoma en el primer paso que no tenga `ok`, **solo si es uno de los retomables**: 6 a 12, según las reglas de ① y ②.
- Si el último paso registrado es anterior al 6, el viejo nunca se congeló, así que no hay nada que retomar: lo dice y sugiere una corrida nueva.
- Si todos los pasos están `ok`, solo repite la verificación (paso 12) y el resumen. Es la forma de volver a correr las V después de un NO-GO arreglado.

#### 6.5.5 Verificación automática y `--ensayo`

| V | Cómo (automático) | Bloquea el GO |
|---|---|---|
| V1 | `restaurar-backup.sh` salió con 0 en el paso 9 | sí |
| V2 | `is-active` = `active`; la `max("MigrationId")` es ≥ la `ultima_migracion` del manifiesto; la cantidad de líneas con error en el journal desde `api_nueva_activa_utc` se **informa** y no bloquea, porque el patrón tiene falsos positivos (deuda T10 #6 de la review) | sí (las dos primeras) |
| V3 | `curl -fsS https://<dominio>:<puerto>/` **desde la máquina del operador, sin `-k`**: valida el certificado contra el dominio nuevo | sí |
| V4 | `/licencia/estado` → `"activada":true` | sí |
| V5 | login real desde la máquina del operador, con la contraseña por stdin (`PW="$PW" jq … env.PW \| curl --data @-`) y el token en una variable | sí |
| V6 | `restaurar-backup.sh --verificar-post-arranque --manifiesto …` por ssh | sí |
| V7 | `releases.win.json` contiene `<v>`; `curl -sI` del `.nupkg` full da 200 con `Content-Length` igual al `Size` | sí |
| V8 | `GET /backups` lista corridas del historial; `GET /backups/{id}/contenido` de **la más vieja** da 200; `POST /backups` termina en una corrida `Exitosa` en menos de 60 s (con el token por stdin, `curl -K -`) | sí |
| V9 | **no se automatiza**: el resumen lo lista como `PENDIENTE (agente con scripts/gui-verificacion)` | — (lo decide Martin) |
| V10 | viejo: `is-enabled` = `masked`, `is-active` = `inactive`, y `docker ps` igual a `contenedores-viejo.txt` | sí |

**`--ensayo` (decidido por Martin, detalle del asistente)** corre la **misma** tabla de §6.5.4, con estas diferencias:
- **Sin confirmación** (paso 2 omitido): el ensayo detiene testing alrededor de un minuto y el resumen lo avisa.
- **Paso 6:** `volcado-final-vps.sh --ensayo`, que no enmascara y vuelve a arrancar la API vieja si estaba activa.
- **V10 invertido:** el viejo tiene que quedar `active` y no `masked`, con el mismo `docker ps`. Así se prueba que el ensayo lo dejó como estaba.
- Al final, el VPS nuevo queda con los datos del ensayo y la API arriba, igual que dejaba E2. El corte los reemplaza con `--recrear-base`.
- El resumen imprime la **tabla de tiempos** lista para pegar en el runbook y la lista de overrides (nombres) que falta cargar en el `.env` del servidor nuevo.

El ensayo es también el ensayo del orquestador: lo que corre el día D es exactamente el mismo código, con un solo `if` en el paso 6 y en V10.

#### 6.5.6 `--revertir` (R-A)

1. Pre-vuelo: identidad contra la corrida y `sudo -n` en los dos hosts.
2. **Guardia de escrituras reales.**
   - Lee `LogsAuditoria` del nuevo y lo compara con `logs_auditoria_post_verificacion` del estado. Si creció, hubo escrituras posteriores a la verificación, o sea, PCs trabajando: ya no es R-A sino R-B. En ese caso se niega (exit 3) y apunta a la sección R-B del runbook, salvo que se pase `--aun-con-escrituras`, que muestra el delta y exige la palabra.
   - Si la corrida no llegó a verificar, no hay valor de referencia y se informa el conteo.
3. Confirmación con la palabra `REVERTIR`.
4. **Nuevo:** `systemctl stop stockapp-api`, y `is-active` tiene que dar `inactive`. Va **primero**, por el fix-now de la review: con dos APIs vivas, las escrituras en la nueva se perderían.
5. **Viejo**, en este orden, cada uno con su chequeo de "ya está":
   1. `unmask` (si `is-enabled` da `masked`);
   2. `mv …service.congelado …service` (si el `.congelado` existe y el original no);
   3. `daemon-reload`;
   4. `start`.
   - Si **los dos** archivos de la unit existen, no se pisa nada: exit 8.
6. **Verificación:**
   - en el viejo, `is-active` = `active` y `docker ps` igual a `contenedores-viejo.txt`;
   - en el nuevo, `inactive`.
   - El estado registra `resultado=REVERTIDO`.

Es idempotente: re-correrlo da `YA ESTABA` en cada paso. Funciona también para una corrida que murió a mitad del volcado, porque cada sub-paso mira el estado real.

#### 6.5.7 `--desde-backup <dump> [--manifiesto <m>]` (recuperación ante desastres; decisión del asistente)

**Por qué es un modo y no un script aparte.** La mitad "VPS nuevo" de la migración (pasos 3, 4, 8 a 12) es exactamente lo que hace falta en un desastre. Un script aparte duplicaría esa mitad y se probaría por separado. Como modo, comparte código, tests y arnés.

**Por qué no llama a `bajar-backup-vps.sh`.** En un desastre, el VPS de origen está muerto, y `bajar-backup-vps.sh` necesita la API viva del servidor del que baja. Ese script sigue siendo la forma de **juntar** dumps mientras el servidor vive, y `--desde-backup` consume lo que el operador tenga.

**Secuencia:**
- Pre-vuelo: solo el nuevo. El dump tiene que ser legible, ≥ 1024 bytes y pasar `pg_restore --list` local. De ese listado sale la fecha (`Archive created at`), que se muestra. Si hay un `.sha256` al lado, como deja `bajar-backup-vps.sh`, se verifica.
- Confirmación con la palabra `RESTAURAR`. El texto dice "todo lo posterior a <fecha del dump> se pierde".
- Pasos 3, 4, 8, 9 (sin manifiesto, `restaurar-backup.sh` verifica la estructura y no los conteos; si se da `--manifiesto`, también los conteos), 11 y 12.
- V1 reducido, V2 a V5, V7 y V8 (sin historial, `GET /backups` puede listar corridas cuyo archivo no existe: se informa y no bloquea). V6 solo con manifiesto. Sin V10.

**Límite que hay que decir en voz alta:** el desastre se recupera hasta el último dump que esté **fuera** del servidor muerto. Hoy, ninguno lo está de forma automática (R8, pregunta abierta 3).

#### 6.5.8 `--post-corte <corrida-del-corte>` (decisión del asistente)

1. Exige `resultado=GO` y que hayan pasado 14 días desde `congelado_utc`. Antes de eso se niega, salvo con `--antes-de-plazo`, que exige la palabra `BORRAR`.
2. Verifica que `LogsAuditoria` del nuevo sea ≥ al manifiesto (A2) y, si no puede consultarlo, se detiene.
3. **Archiva sin destruir nada:** mueve el `.dump` y el `.manifiesto` del corte a `~/stockapp-backups-vps/corte-<fecha>/` (700), con el sha256 calculado antes y verificado después. Este paso va primero y, si falla, no sigue.
4. Lista los candidatos a borrar, cada uno con su ruta exacta y tamaño:
   - las corridas locales de ensayo y lo que quede de la del corte;
   - en el nuevo, `~/stockapp-deploy/migracion/<corridas>` y sus `seguridad/`.

   Para **cada uno**, pregunta y exige la palabra `BORRAR`. El borrado es por ruta exacta. **Nunca** borra el archivo del corte.
5. **Imprime**, solo como referencia, los comandos acotados para desmantelar el VPS viejo (los del post-corte del runbook actual). No los ejecuta: desmantelar el viejo es una decisión de Martin.

### 6.6 Cambio en `deploy/deploy-vps.sh` (el hecho que contradice el brief)

**El hecho (verificado leyendo el código, sin ejecutarlo).** El paso 1 de `deploy-vps.sh`, el pre-vuelo de catálogos, corre `SELECT … FROM "Categorias" …` por cada uno de los 7 catálogos y **aborta con exit 1 si la consulta deja algo en stderr** (`deploy-vps.sh:296-327`). Ese chequeo es correcto: "no pude consultar" no es "sin duplicados".

Sobre la base **vacía** que deja el paso 6 de preparar, la tabla no existe, psql escribe `relation "Categorias" does not exist` en stderr y el deploy aborta, aunque se pase `--sin-backup`. Por eso el runbook actual hace el restore (E2.2) **antes** del primer deploy (E2.4). El brief pide lo contrario: el primer deploy dentro de preparar, sobre la base verificada vacía.

**Opciones:**

| Opción | A favor | En contra |
|---|---|---|
| **a) El pre-vuelo reconoce "la tabla no existe" (recomendada, decisión del asistente)** | cambio chico, con tests y mutación, en un bloque que ya tiene tests; preparar queda desacoplado del VPS viejo (sirve para el desastre y para servidores futuros) | toca un script que hoy funciona |
| b) El primer deploy pasa a `migrar --ensayo`, después del restore, como en el runbook actual | no toca `deploy-vps.sh` | preparar no termina con la API ni el feed andando, la renovación no se puede probar hasta el ensayo y `migrar` tendría que llamar a `deploy-vps.sh`, que es justo lo que A14 quiere evitar |
| c) preparar instala sin `deploy-vps.sh` (`publish-api.sh` + scp + `install.sh`) | no toca `deploy-vps.sh` | duplica los pasos 3 a 6 de un bloque probado |

**Con (a):**
- Antes de consultar cada catálogo, el pre-vuelo pregunta `SELECT to_regclass('"<tabla>"') IS NULL`, por stdin igual que la SQL actual. Es el mismo criterio que la regla F4 del ledger de la migración.
- Si la tabla **no existe**, informa `"<tabla>": no existe (base sin migrar): nada que verificar` y sigue. Si existe, consulta como hoy.
- Un fallo de **esa** consulta sigue abortando con 1: "no existe" es un resultado válido, distinto de "no pude consultar".
- Con la base sin migrar, el paso 6 (`COUNT(*) FROM "__EFMigrationsHistory"`) no se ve afectado, porque corre después de `install.sh`, cuando la API ya migró.

**Tests:**
- una base con el esquema real y duplicados sigue abortando;
- una base vacía pasa el pre-vuelo;
- un `docker exec` que falla sigue abortando.

**Mutación:** si se saca la rama "no existe", el test de la base vacía queda en rojo. Si se cambia la rama por un `|| true`, queda en rojo el test de la consulta que falla.

### 6.7 Qué pasa con `deploy/MIGRACION-VPS.md`

**Se reescribe como un runbook corto (decisión del asistente).** Hay que borrar la prosa que pasa a ser código, no dejarla "por las dudas": dos fuentes de verdad son el defecto que esta spec arregla. El detalle actual queda en git.

1. **Qué es esto**, las reglas de siempre (VPS viejo compartido, dónde viven los dumps) y los tres `.env`.
2. **Prerrequisitos de Martin:**
   - el VPS creado con Ubuntu 24.04, un usuario con clave, `sudo -n` y el puerto SSH;
   - el registro `A` del dominio nuevo, TTL 300, días antes;
   - `deploy/.env.vps-nuevo`;
   - `releases/win` con la versión y el `Setup.exe` de la misma versión;
   - la credencial del usuario real de verificación;
   - los valores de los overrides.
3. **Preparar:** `STOCKAPP_ENV_LOCAL=deploy/.env.vps-nuevo deploy/preparar-vps-nuevo.sh --version 0.5.3`, una segunda vez (todo `YA ESTABA`), `--probar-renovacion` una vez, y qué hacer con cada código de salida.
4. **Ensayo:** `deploy/migrar-vps.sh --ensayo`, cargar los overrides que liste y pegar la tabla de tiempos. Después, el smoke de la GUI (V9) por un agente sobre los datos del ensayo.
5. **Día D:**
   - `deploy/migrar-vps.sh` y escribir `CONGELAR`;
   - V9 por un agente;
   - **GO de Martin**;
   - instalar las PCs (SmartScreen);
   - anotar las fechas que imprimió el resumen.
6. **Si algo sale mal:** la tabla de códigos de §6.3.2 con su acción, `--continuar` y `--revertir`.
7. **R-B**, manual y de último recurso: queda el texto actual.
8. **Desastre:** `preparar` + `--desde-backup`, con el límite de R8 dicho en una línea.
9. **D+14:** `deploy/migrar-vps.sh --post-corte <corrida>`.
10. **Tiempos** (tabla) y **E1** (el resultado ya registrado, como historia).

`MigracionVpsSincroniaTests` tiene hoy guardianes sobre el **texto** del runbook (`Runbook_ReflejaLosHallazgosDeLasReviews` y afines). Se reescriben con el runbook nuevo. Cada hallazgo de la review que hoy custodia un `InlineData` de prosa pasa a custodiarse con un test del orquestador (§8.1, tabla de mutación). El test de "sin hosts ni IPs cableados" se mantiene sobre el runbook y los scripts nuevos. `PROCEDIMIENTOS.md` (recuperación) pasa a apuntar a `--desde-backup`.

### 6.8 El día D, de un vistazo

```
operador:   migrar-vps.sh ─ pre-vuelo (lectura) ─ [CONGELAR] ─┬─────────────────────────────────────────── resumen GO/NO-GO
VPS nuevo:                                                     stop API ─ limpiar        transferir ─ restaurar ─ historial ─ start ─ V1–V8
VPS viejo:                      (lecturas, login)              lista ctrs ─ volcado-final (stop+mask+dump+manifiesto) ─────────────── V10
                                                               └──────── indisponibilidad de testing: desde el stop hasta el resumen ───────┘
después:    V9 (agente) ─ GO de Martin ─ PCs ─ … D+14: --post-corte
```

La indisponibilidad es la del ambiente de **testing** del viejo: el municipio se instala en la misma ventana y nunca apuntó al viejo (spec anterior, §1).

## 7. Qué queda a criterio del implementador

- Si las funciones comunes (líneas de progreso, tiempos, estado, lock, carga de `.env`, confirmación) van en un `deploy/lib/orquestacion.sh` que los dos orquestadores cargan con `source`, o duplicadas con un test de sincronía. `restaurar-backup.sh` sigue siendo autocontenido, porque se copia solo al servidor.
- El formato exacto de las líneas de progreso y del resumen, siempre que tengan número de paso, resultado, duración y, en el corte, el reloj de indisponibilidad.
- Los nombres de las claves del archivo de estado, siempre que no tenga secretos, se escriba de forma atómica y `resultado=` sea lo último.
- El tiempo de la reversa del firewall (120 s propuesto) y los tiempos de espera acotados (`healthy`, arranque y V8).
- Si `preparar --verificar` y el paso 10 comparten el mismo código.
- Cómo se copia el hook al servidor (scp al staging, o por stdin + `install`), siempre que no quede ningún archivo en un `/tmp` y el modo final sea 0755 root.
- Si `--post-corte` también ofrece borrar `~/stockapp-backups-vps/contenedores-*` y similares que haya dejado el runbook manual.
- La redacción del runbook nuevo, siempre que cada sección tenga el comando exacto y la tabla de qué hacer según el código de salida.

## 8. Verificación

### 8.1 Tests unitarios con stubs por `PATH` (en `tests/StockApp.Infrastructure.Tests/Kit/`)

`PrepararVpsNuevoTests.cs` y `MigrarVpsTests.cs`, con el patrón de `DeployVpsTests`/`VolcadoFinalVpsTests`: `ssh` y `scp` falsos al frente del `PATH` que registran un **rastro** ordenado.

Además, con **un host simulado con estado**: el `ssh` falso ejecuta el comando remoto contra shims de `apt-get`, `ufw`, `certbot`, `useradd`, `systemd-run`, `systemctl` y `docker`. Cada shim guarda su estado en archivos (reglas de ufw, timer armado o no, certificado emitido, unit instalada) y es **fiel en lo que importa**, como el shim de `systemctl` del ensayo. Algunos ejemplos:
- `ufw enable` con la regla del puerto SSH ausente deja al ssh falso rechazando conexiones nuevas;
- el hook en la primera emisión falla en el `restart`, pero copia los PEM.

**Los bloques no se reemplazan con una costura en el script de producción.** El test copia el orquestador a un árbol temporal con la forma del repo, con bloques falsos que registran sus argumentos y su entorno (`STOCKAPP_ENV_LOCAL`) y salen con el código que pide el test. Los bloques reales tienen sus propios tests.

Casos mínimos de `preparar`:
- dos corridas seguidas: la segunda da todo `YA ESTABA`, ninguna acción en el rastro y exit 0;
- `.env` existente → no se reescribe (el rastro no tiene escritura y el archivo queda igual byte a byte);
- `.env` existente con un valor fijo distinto → falla nombrando solo la clave;
- orden: `ufw allow <ssh>` antes que `ufw --force enable`; la reversa armada antes del `enable` y desarmada solo después de que una conexión **nueva** respondió;
- re-chequeo fallido → exit 6, con la reversa todavía armada;
- `STOCKAPP_ENV_LOCAL` ausente, o igual a `deploy/.env` → 3 sin ssh;
- contenedor ajeno o puerto 80 ocupado → 9 sin ninguna acción;
- DNS que no coincide → 3 antes de certbot;
- certbot que sale ≠0 pero copió los PEM → OK;
- certbot que emitió pero no copió → corre el hook a mano;
- base no vacía sin unit → 5 sin borrar nada;
- con la unit ya instalada, **nunca** aparece `deploy-vps.sh` en el rastro;
- los secretos generados (el host simulado los deja en su `.env`, y el test los lee) **no aparecen** en el rastro, en el argv de ningún proceso registrado ni en el log.

Casos mínimos de `migrar`:
- la confirmación es lo **único** entre el pre-vuelo y el primer comando que modifica algo, y con una palabra distinta → 4 sin modificaciones en el rastro;
- sin terminal, con hosts que no son `.invalid` → 3;
- en el rastro, `volcado` antes que `restaurar`, y `restaurar` antes que `start`;
- `restaurar` con 6 o 7 → no hay `start` en el rastro y sale con 6;
- **dos volcados en la carpeta** (el test planta uno extra), o un manifiesto con `fecha_utc` anterior al inicio, o con un `dump=` distinto → 6 sin restaurar (A11);
- machine-id iguales → 9;
- `--continuar` después de un fallo en cada paso retomable retoma en ese paso, y un `--continuar` de una corrida que no llegó a congelar se niega;
- `--revertir` → `stop` en el nuevo **antes** de cualquier comando en el viejo, y re-correrlo da `YA ESTABA`;
- `--revertir` con `LogsAuditoria` mayor que el registrado → 3;
- `--ensayo` → no hay confirmación, el volcado recibe `--ensayo` y V10 exige `active`;
- `deploy-vps.sh` **nunca** aparece en el rastro de `migrar`;
- todo comando dirigido al host viejo matchea la lista blanca de §5;
- ningún `/tmp/` fuera de `docker exec`.

**Guardianes de sincronía:**
- la tabla de códigos de cada bloque copiada en el encabezado del orquestador es igual a la del bloque;
- `docker-compose.postgres.yml` publica solo en `127.0.0.1`;
- `migrar-vps.sh` está en `ScriptsNuevos`, con todos los tokens prohibidos;
- `preparar-vps-nuevo.sh` está exento de `ufw`, `certbot` y `apt-get` y de nada más (por ejemplo, sigue prohibido `ufw reset`).

**Mutación (quitando, nunca agregando, y restaurando).** Cada fila tiene que dejar al menos un test en rojo:

| Se quita | Rojo esperado |
|---|---|
| la confirmación | el test de "palabra distinta → 4" |
| el control de volcado único / `fecha_utc` / `dump=` | los tres casos de A11 |
| la comparación de machine-id | machine-id iguales → 9 |
| "no arrancar si restaurar ≠ 0" | `restaurar` con 6 → hay `start` |
| el `stop` del nuevo en `--revertir` | orden de `--revertir` |
| la guardia de escrituras de `--revertir` | `LogsAuditoria` mayor → 3 |
| `--ensayo` pasado al volcado | V10 del ensayo, el viejo queda `masked` |
| "no reescribir `.env`" | el `.env` existente cambia |
| `ufw allow <ssh>` | el host simulado rechaza la conexión nueva → 6 |
| la reversa del firewall | el test del re-chequeo fallido (el timer no está armado) |
| "no desplegar con la unit instalada" | `deploy-vps.sh` en el rastro de la segunda corrida |
| el chequeo de contenedores ajenos | contenedor ajeno → 9 |
| la rama "no existe" del pre-vuelo de `deploy-vps.sh` | base vacía (§6.6) |

### 8.2 Arnés local de punta a punta (`deploy/ensayo/ensayo-local.sh`, extendido)

**Decisión del asistente: shims, no un contenedor con sshd.**
- **Contra el contenedor con sshd:** un contenedor con sshd que sea creíble como VPS necesitaría systemd (para la unit) y Docker adentro (para `stockapp-pg`), o sea `--privileged`, y aun así `ufw` y `certbot` no serían fieles. No hay un Let's Encrypt contra el que emitir, y las reglas de netfilter de un contenedor no son las del host.
- **A favor de los shims:** el enfoque de shims ya demostró su valor en E1. Sus guardas impiden tocar un host real (solo `.invalid`) o el `/etc` real (raíz verificada).
- **Lo que queda fuera:** una VM queda descartada por la misma razón por la que Martin bloqueó la Fase 5 del kit.

**Qué se extiende:**
- **Dos hosts simulados.** El `ssh` falso enruta por nombre: `vps-viejo.ensayo.invalid` y `vps-nuevo.ensayo.invalid`. Cada uno tiene su raíz (`<trabajo>/hosts/<host>/`), su estado de `systemctl` y su `HOME` remoto. El shim de `systemctl` generaliza su guarda: la raíz tiene que ser una de las de `<trabajo>/hosts/`, nunca `/`.
- **Postgres real.** Contenedores `stockapp-ensayo-origen-pg-<s>` (el viejo) y `stockapp-ensayo-pg-<s>` (el nuevo), como hoy.
- **API real en el host nuevo.** El `start` del shim de `systemctl`, en el host nuevo, lanza la API **publicada** (un `dotnet publish` por corrida del arnés) con el entorno del `api.env` simulado. Guarda el PID, y `stop` la mata por PID exacto y verifica con `ps`. Las V se corren contra una API de verdad, no contra un stub (la regla de "un stub que miente hace teatro").
- **TLS:** en el arnés es `http`. V3 sin `-k` y el certificado se prueban solo en el VPS real. Es una brecha declarada.
- **Subcomandos nuevos:**
  - `preparar-migracion`: arma el viejo como hoy, y el nuevo **en el estado que deja `preparar-vps-nuevo.sh`**. Es decir: base migrada por un primer arranque de la API (sembró el admin, así que `--recrear-base` es obligatorio como en la realidad), feed mínimo, unit y directorios.
  - `migrar <args>`: corre `migrar-vps.sh` real contra los dos hosts, con `--contenedor-*` y `--raiz-remota-*` (solo válidos con hosts `.invalid`, §6.3.6) y la palabra de confirmación por stdin.
  - `limpiar` borra todo por nombre o PID exacto.
- **Inyección de fallos solo en el arnés:** `STOCKAPP_ENSAYO_FALLAR_EN=<paso>` se acepta solo con hosts `.invalid`. Con eso se prueba `--continuar` y `--revertir` desde cada punto de corte.

**Casos de punta a punta**, en la colección serializada de `EnsayoLocalTests` (F9 del ledger: `docker ps` estricto):
- `--ensayo` → GO, con el viejo `active`;
- corte → GO, con el viejo `masked`;
- `--revertir` después del corte → el viejo `active` y el nuevo `inactive`;
- corte con fallo inyectado en los pasos 7, 9 y 11 → `--continuar` → GO;
- `--desde-backup` con el dump de una corrida anterior → GO reducido;
- `--post-corte --antes-de-plazo` con las palabras por stdin → archivo con sha verificado y borrados solo de lo confirmado.

**`preparar-vps-nuevo.sh` no corre de punta a punta en el arnés.** Lo que el arnés no puede simular con fidelidad (apt, ufw, certbot, `install.sh` como root) está cubierto por los tests de §8.1 con el host con estado. **Su prueba de punta a punta real es la primera corrida contra el VPS nuevo en blanco, donde no hay nada que perder (§8.3).** Es una desviación declarada del brief: hoy no hay una forma fiel de correr local lo que instala paquetes y maneja el firewall de un host.

### 8.3 Verificación real (el GATE orgánico, lo corre un agente salvo donde se dice)

1. `preparar-vps-nuevo.sh` contra el VPS nuevo en blanco → exit 0. **Segunda corrida** → todo `YA ESTABA`. `--verificar` → todo `HECHO`. Desde la máquina del operador: `curl` sin `-k` OK y `ufw status` con las tres reglas. `--probar-renovacion` una vez.
2. `migrar-vps.sh --ensayo` contra el viejo real y el nuevo → GO. Después, el viejo `active`. Se anotan los tiempos (reemplaza E2).
3. V9 (smoke de la GUI) por un agente sobre los datos del ensayo, mirando las capturas, porque `capturar.sh` no es fail-closed.
4. (Pregunta abierta 4) un **ensayo del corte completo + `--revertir`** sobre el viejo real, antes del día D.
5. El día D: el corte, con Martin en la terminal para escribir `CONGELAR`.

## 9. Riesgos

- **R1. Los shims divergen del sistema real** (`ufw`, `certbot`, el comportamiento de systemd). Mitigaciones:
  - shims "fieles en lo que importa", con el comportamiento real verificado y citado en su encabezado, como ya hace el shim de `systemctl`;
  - la primera corrida real es sobre un VPS en blanco;
  - preparar mira el **resultado** y no el exit de las herramientas (certbot).
- **R2. Quedarse sin SSH** (A8). Mitigaciones: el paso 0 compara el puerto, la regla del SSH va primero, la reversa automática está armada y el re-chequeo usa una conexión nueva. Lo que queda: un firewall del proveedor, fuera del VPS, no lo ve este script.
- **R3. Límites de Let's Encrypt.** El DNS se verifica antes de certbot (los fallos de validación tienen su propio límite). Un certificado vigente no se re-emite, y `--force-renewal` se corre una sola vez, con marcador. Hay un límite de emisiones duplicadas por semana.
- **R4. Un camino del orquestador que nunca se ejecutó** (los mismos defectos de prosa, ahora en un `if`). Mitigación: la inyección de fallos del arnés en cada paso retomable, y la mutación de §8.1.
- **R5. La máquina del operador se muere a mitad del corte.** El estado y el log están en su disco. Mitigación: los comandos de R-A son fijos y quedan impresos **antes** de la confirmación. `--revertir` mira el estado real y no necesita el log, aunque necesita la corrida para la identidad: sin ella, los comandos impresos de R-A sirven a mano.
- **R6. Error al tipear la credencial.** Se valida contra la API vieja **antes** de la confirmación (§6.5.3).
- **R7. Acoplamiento a la salida humana de un bloque.** Prohibido por §6.2. El contrato son los códigos y los archivos, y lo fija el test de sincronía de las tablas de códigos.
- **R8. La recuperación ante desastres es tan buena como el último dump que está fuera del servidor.** Hoy los backups viven en el mismo VPS, y no hay off-site automático (spec anterior, R9). `--desde-backup` no lo resuelve: lo deja a la vista.
- **R9. El primer deploy deja un admin sembrado con una contraseña que nadie conoce, y la API expuesta en el 8080 hasta el primer ensayo.** Es una contraseña aleatoria de servidor, en un `.env` 600, con rate limit de login a 10. El primer `--ensayo` la reemplaza por los usuarios reales.
- **R10. El cambio de §6.6 afloja el pre-vuelo de `deploy-vps.sh`.** Lo acota que "no existe" sea un resultado de una consulta exitosa (`to_regclass`). Un fallo de consulta sigue abortando, y los dos caminos tienen test y mutación.
- **R11. Scripts grandes.** Dos orquestadores con varios modos en bash. Mitigación: la secuencia es una tabla de pasos con funciones chicas, la librería común de §7 y tests por paso.

## 10. Preguntas abiertas que solo Martin puede responder

1. **§6.6:** ¿aceptás el cambio chico al pre-vuelo de `deploy-vps.sh` (opción a)? Es lo que permite que el primer deploy viva en `preparar-vps-nuevo.sh`, como pediste. Si no, la alternativa es (b): el primer deploy pasa a `migrar --ensayo`, y preparar no termina con la API andando.
2. **Renovación del certificado:** ¿`preparar` la prueba **una sola vez automáticamente** (gasta una emisión del cupo semanal, con marcador para no repetir) o **solo con `--probar-renovacion`**, como propone esta spec?
3. **Recuperación ante desastres:** ¿alcanza con "el último dump que el operador tenga a mano" (por ejemplo, de un `bajar-backup-vps.sh` corrido cada tanto), o querés que la próxima pieza sea un backup off-site programado? Esta spec no lo incluye.
4. **¿Ensayamos el corte completo + `--revertir` sobre el VPS viejo** (testing) antes del día D? Es el único ensayo real de la vuelta atrás. Cuesta unos minutos de testing caído, con la unit enmascarada y después restaurada.
