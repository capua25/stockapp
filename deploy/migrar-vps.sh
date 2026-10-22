#!/usr/bin/env bash
# =======================================================================================
# deploy/migrar-vps.sh -- migra StockApp del VPS viejo al VPS nuevo con un corte controlado.
# Corre en la MÁQUINA DEL OPERADOR y opera los dos servidores por ssh.
# =======================================================================================
#
# Uso:
#   deploy/migrar-vps.sh [--ensayo] [--env-viejo <archivo>] [--env-nuevo <archivo>]
#   deploy/migrar-vps.sh --continuar <corrida> [--reintentar-restauracion]
#   deploy/migrar-vps.sh --revertir [<corrida>] [--aun-con-escrituras]
#   deploy/migrar-vps.sh --desde-backup <dump> [--manifiesto <m>] [--env-nuevo <archivo>]
#   deploy/migrar-vps.sh --post-corte <corrida-del-corte> [--antes-de-plazo]
#
#   (sin --ensayo)  El corte. Pre-vuelo de SOLO LECTURA (config, identidad de los hosts, estado del
#                   viejo, preparación del nuevo, overrides, credencial) y, recién después, una
#                   confirmación escrita: hay que teclear exactamente CONGELAR en una terminal.
#   --ensayo        La misma secuencia sin la confirmación (paso 2 omitido) y sin congelar el viejo
#                   de forma permanente: el volcado corre con --ensayo, que no enmascara y vuelve a
#                   arrancar la API vieja, y V10 exige el viejo active y no masked. El viejo (testing)
#                   queda sin API mientras dura el volcado. El resumen trae la tabla de tiempos para el
#                   runbook y los overrides que faltan en el .env del servidor nuevo.
#   --continuar     Retoma una corrida de corte o de ensayo que falló DESPUÉS de congelar. Relee su
#                   estado (los .env salen de ahí: no se aceptan --env-*), exige los mismos machine-id,
#                   toma el lock, quita resultado= y retoma en el primer paso que no está hecho, solo
#                   entre el 6 y el 12. El paso 6 vuelve a volcar en una carpeta nueva (volcado-<n>/);
#                   el 9 se reintenta tal cual si restaurar salió con 5, repite desde el 8 (directorio
#                   remoto nuevo) con 3 o 4, y con 6 o 7 solo con --reintentar-restauracion (lo decide
#                   un humano). Con todos los pasos hechos, o después de un NO-GO, repite la
#                   verificación (12) y el resumen (13). Una corrida que no llegó a congelar se cierra
#                   con resultado=FALLO-ABANDONADA y hay que empezar una nueva; una terminada (con
#                   resultado=, salvo NO-GO) no se toca. En el corte, el viejo tiene que seguir
#                   congelado: si alguien lo volvió a arrancar, se niega (3).
#   --revertir      La vuelta atrás (R-A) en un comando: el VPS viejo vuelve a servir. Sin <corrida>, la
#                   última de corte que registra el congelamiento; con ella, también una de ensayo (así se
#                   cierra un ensayo abierto). Los .env, contenedores y raíces salen del estado de la corrida
#                   y se exigen los mismos machine-id. En orden: sudo -n en los dos; la guardia de escrituras
#                   (LogsAuditoria del nuevo contra logs_auditoria_post_verificacion: si creció hubo PCs
#                   trabajando, ya no es R-A sino R-B, y sale 3 salvo --aun-con-escrituras, que muestra el
#                   delta y sigue a la confirmación); la palabra REVERTIR; la API NUEVA detenida PRIMERO; en
#                   el viejo, cada uno solo si hace falta según lo que se OBSERVA: unmask (si está masked), mv
#                   del .congelado a su lugar (si existe y la unit no), daemon-reload y start. Si existen los
#                   dos archivos de la unit no se pisa ninguno (8). Verifica el viejo active, atendiendo por
#                   loopback y con el docker ps de antes del corte, y el nuevo detenido; escribe
#                   resultado=REVERTIDO en la corrida revertida y en la propia (revertir-<ts>, con su log).
#                   Es idempotente: re-correrlo da YA ESTABA en cada paso y no modifica nada. Códigos: 0
#                   revertido, 2 uso, 3 precondición o escrituras reales (R-B), 4 sin confirmar, 6 falló un
#                   paso de la vuelta (re-correr), 8 no pude consultar o la unit no se puede devolver sin
#                   pisar, 9 otros servidores que los de la corrida o el docker ps del viejo cambió (en ese
#                   caso la vuelta atrás ya se hizo y la corrida queda REVERTIDO: se informa separando los
#                   contenedores stockapp* de los ajenos). La API nueva queda detenida Y deshabilitada
#                   (ella sirve el feed que exige el pre-vuelo: antes de reintentar hay que levantarla con
#                   deploy/deploy-vps.sh <v> --sin-backup contra el nuevo, que hace enable y restart; R56); el resultado previo de la corrida
#                   revertida queda en resultado_previo. Con el VPS nuevo inalcanzable sale 8 sin tocar el
#                   viejo: hay que apagar el nuevo desde el panel del proveedor.
#   --desde-backup  Recuperación ante desastres: el VPS viejo murió o no se puede congelar, y el VPS nuevo
#                   (ya preparado) recibe un backup que el operador tiene en esta máquina (el que deja
#                   bajar-backup-vps.sh, o el volcado de una corrida anterior). Al VPS viejo no se le corre
#                   nada: ni ssh ni volcado. Pre-vuelo, antes de tocar nada: el dump legible, de al
#                   menos 1024 bytes y aceptado por pg_restore --list local (de ese listado sale la fecha,
#                   "Archive created at"); si hay un <dump>.sha256 al lado, como el de bajar-backup-vps.sh,
#                   tiene que coincidir; con --manifiesto, el manifiesto tiene que nombrar ese dump y su
#                   sha256. Después, solo el nuevo (preparar-vps-nuevo.sh --verificar y el espacio, con el
#                   tamaño del dump como cota inferior) y la palabra RESTAURAR, con el aviso de que todo lo
#                   posterior a la fecha del dump se pierde. Pasos 3, 4, 8, 9 (sin manifiesto restaurar
#                   verifica la estructura y no los conteos), 11 y 12; el 5, 6, 7 y 10 son del viejo o de su
#                   volcado y quedan OMITIDO. Verificación: V1 a V5, V7 y V8 (sin historial: la descarga de
#                   una corrida vieja se informa y no bloquea; el backup nuevo sí), V6 solo con manifiesto y
#                   sin V10. El usuario real de V5 y V8 es VPS_ADMIN_USER del --env-viejo, que es el .env
#                   LOCAL de siempre y existe aunque el VPS viejo esté muerto (de él se leen solo esa clave y
#                   VPS_ADMIN_PASSWORD, si no viene del entorno o de la terminal; nunca el host), y
#                   preparar --verificar lee de él la clave de licencias. Sin ese archivo o sin
#                   VPS_ADMIN_USER sale 2. Una corrida que falla se cierra
#                   (resultado=FALLO-<n>) y se vuelve a correr entera: no hay --continuar ni --revertir (el
#                   paso 9 recrea la base con un dump de seguridad de lo que tenga). Códigos: los de la tabla,
#                   sin el 9 de contenedores (no hay viejo); 6 es "falló un paso desde la transferencia".
#   --post-corte    D+14, con una corrida de corte con resultado=GO y 14 días desde congelado_utc (antes,
#                   solo con --antes-de-plazo y la palabra BORRAR). No toca el VPS viejo. En orden:
#                   LogsAuditoria del nuevo >= el del manifiesto del corte (si no puede consultarlo, se
#                   detiene); MUEVE el dump y el manifiesto del corte a ~/stockapp-backups-vps/corte-<ts>/
#                   (700), con el sha256 antes y después (si falla, no sigue), y copia ahí el estado y el
#                   registro.log de la corrida (con lo que este modo borra) y el sha256 del dump y del
#                   manifiesto. Después ofrece borrar, uno por uno, con su ruta, su tamaño y la palabra
#                   BORRAR cada vez, por LISTA BLANCA: los directorios de ~/stockapp-deploy/migracion/ del
#                   nuevo (si ya no existe, lo dice y sigue), las corridas de ensayo terminadas y lo que deja
#                   el runbook manual (ensayo-<fecha>/, ensayo-e2-<fecha>/, contenedores-viejo-<fecha>.txt,
#                   v10-viejo.txt, token). NUNCA ofrece migracion-<fecha>/, corte-* (de ningún
#                   formato), inversa-*, una corrida de corte (tampoco esta) ni lo que no sea de la
#                   migración: se lista como "se conserva". Por último IMPRIME (no ejecuta) la referencia para
#                   desmantelar el viejo. Re-correrlo da YA ESTABA en el archivo. Códigos: 0 hecho, 2 uso, 3
#                   precondición (sin GO, antes del plazo, LogsAuditoria menor que el manifiesto, lock), 4 sin
#                   la palabra BORRAR del plazo, 5 falló el archivo o un borrado (lo hecho queda hecho;
#                   re-correr), 8 no pude consultar, 9 el VPS nuevo no es el de la corrida.
#   --env-viejo     .env del operador para el VPS viejo. Default: deploy/.env.
#                   Además de los VPS_*: VPS_ADMIN_USER (obligatorio; no cae a BOOTSTRAP_ADMIN_USER)
#                   y VPS_ADMIN_PASSWORD. La contraseña sale del entorno, si no del archivo y, si
#                   no, se pide por terminal sin eco. Es un usuario real con GestionarDiagnostico.
#   --env-nuevo     .env del operador para el VPS nuevo. Default: deploy/.env.vps-nuevo.
#
# Solo para el arnés de ensayo local. Se rechazan con exit 2 si algún VPS_HOST no termina en
# .invalid, igual que la variable STOCKAPP_ENSAYO_FALLAR_EN: ningún desvío de prueba sirve contra un
# servidor real.
#   --contenedor-viejo <n>   --contenedor-nuevo <n>     (default stockapp-pg; ^stockapp[a-z0-9-]*$)
#   --raiz-remota-viejo <d>  --raiz-remota-nuevo <d>    (prefijo de las rutas absolutas remotas)
#   --sin-verificar-preparacion                         (no llama a preparar-vps-nuevo.sh --verificar)
#   STOCKAPP_ENSAYO_FALLAR_EN=<n>  (3 a 11) antes de correr el paso <n>, la primera vez en la corrida
#                   (clave fallo_inyectado_<n>), lo marca FALLÓ con la clase que le corresponde: 3 a 6
#                   antes de congelar -> 5; 9 como restaurar 5 -> 6; 11 -> 7; el resto -> 6.
#
# ⚠️  EL VPS VIEJO ES COMPARTIDO con otra aplicación. Sobre él, este script solo corre lecturas
# acotadas POR NOMBRE a StockApp: systemctl is-active / is-enabled / show de stockapp-api,
# docker ps --format, docker exec del contenedor de Postgres de StockApp, cat de /etc/machine-id,
# test -e de la unit y el curl de login y de GET /backups por loopback (y, solo en --revertir, GET /);
# más los bloques (volcado-final-vps.sh) y, solo en --revertir, los cuatro comandos de vuelta atrás. Nada global de Docker, de systemd ni
# de firewall, y nada del proxy de la otra aplicación. Ningún dump ni archivo temporal pasa por el
# /tmp del host.
#
# Secretos: nunca en argv, en archivos de esta máquina ni en el log. La contraseña de verificación
# vive en una variable del proceso y viaja por el stdin de ssh (el cuerpo del login) hacia curl; el
# token, igual, solo en una variable y por el stdin de curl -K -.
#
# Pre-vuelo (paso 1) y confirmación (paso 2):
#   - Identidad (A9): /etc/machine-id de los dos hosts, leído por ssh ANTES de crear la corrida.
#     Iguales -> 9. Ilegible -> 8. Se compara además contra el último ensayo, si existe.
#   - Viejo: sudo -n, stockapp-api active con FragmentPath igual a la unit que deja install.sh (así
#     se sabe ANTES de la indisponibilidad que el congelamiento va a poder moverla), sin .congelado,
#     el contenedor de Postgres corriendo y espacio para el dump (pg_database_size contra el df del
#     contenedor; nunca el df del directorio de volúmenes de Docker, que pide root).
#   - Nuevo: preparar-vps-nuevo.sh --verificar con la versión que sirve el feed del nuevo (un bloque,
#     como proceso hijo, con el .env del nuevo), espacio para el dump y la copia de seguridad, y los
#     overrides del último ensayo presentes en el .env del servidor (solo NOMBRES).
#   - Credencial: login contra la API vieja por ssh al loopback y GET /backups (solo lectura).
#   - Una corrida sin terminar (con estado y sin resultado=) impide empezar otra de corte.
#   - Hasta la confirmación no se modifica nada. Un fallo del pre-vuelo cierra la corrida con
#     resultado=FALLO-<n>, salvo que ya hubiera congelado el viejo.
#
# Secuencia del corte (pasos 3 a 13). Contrato del estado: cada paso hecho deja
# paso_<n>=ok <utc> <segundos>, y un paso que este modo no tiene (el 2 en el ensayo) deja
# paso_<n>=omitido <utc> <segundos>. --continuar cuenta los dos como hechos y nada más: cualquier
# otro valor de un paso_<n> es "no pude interpretar" (8) y no se retoma a ciegas (A13).
#    3  detiene la API NUEVA (primero: con dos APIs vivas, lo escrito en la nueva se perdería) y
#       exige que is-active ya no diga active.
#    4  en el nuevo, borra uno por uno, por patrón y sin bajar de nivel, los backup_*.dump y *.tmp
#       que dejó el ensayo, y aparta en migracion/legado-<ts>/ lo suelto de un ensayo manual.
#    5  guarda la lista de contenedores del viejo (contenedores-viejo.txt) y cómo está habilitada
#       la unit.
#    6  registra congelado_utc y recién después llama a volcado-final-vps.sh con un --salida nuevo:
#       desde ahí corre el reloj de indisponibilidad. Si el bloque falla y el viejo sigue sirviendo
#       como antes (is-active active, la misma habilitación), sale con 5; si no, con 6 (9 si es
#       alcance). Si el bloque dice OK, el viejo TIENE que estar inactive y masked.
#    7  A11: en el --salida hay exactamente un volcado y un manifiesto con el mismo <ts>, y el
#       manifiesto dice dump=<ese nombre>, el sha256 del archivo local y una fecha_utc no anterior
#       al inicio de la corrida. Desde acá ningún paso nombra otro volcado.
#    8  directorio remoto NUEVO (mkdir sin -p), scp, chmod y sha256sum remoto igual al del estado.
#    9  restaurar-backup.sh --recrear-base por ssh. Con un código distinto de 0 NO se arranca la API.
#   10  extrae el historial de backups y le da dueño stockapp.
#   11  habilita (enable) y arranca la API nueva, y espera (120 s, reloj monotónico) active y GET / por
#       loopback.
#   12  V1-V8 y V10; anota LogsAuditoria para --revertir.
#   13  resumen: GO (0) o NO-GO (7), fin_rollback = congelado + 14 días y el comando de vuelta atrás.
# Después del congelamiento, todo fallo deja la corrida abierta (sin resultado=) y dice cómo seguir
# (--continuar <corrida>) o volver atrás (--revertir), con el estado OBSERVADO de la API nueva. Un
# exit 7 del arranque (paso 11) también la deja abierta; el NO-GO de la verificación la cierra con
# resultado=NO-GO, que --continuar vuelve a abrir para repetir la verificación.
#
# Códigos de salida (estables):
#   0  GO técnico: todas las V automáticas OK. Quedan V9 y la decisión de Martin
#   2  uso / argumentos
#   3  precondición del pre-vuelo (config, identidad, credencial, corrida sin terminar, lock): no se tocó nada
#   4  el operador no confirmó: no se tocó nada
#   5  falló un paso antes de congelar el viejo, que sigue intacto y sirviendo. Re-correr arranca una corrida nueva
#   6  falló un paso después de congelar el viejo, que quedó congelado. El resumen dice el estado exacto y la salida: --continuar <corrida> o --revertir
#   7  NO-GO: falló el arranque de la API nueva (la corrida queda abierta: --continuar la retoma en el paso 11) o falló una V (resultado=NO-GO: --continuar repite la verificación). La API nueva queda como esté para diagnosticar (las PCs no están instaladas: nadie escribe). Se imprime --revertir
#   8  no se pudo consultar
#   9  alcance: la lista de contenedores del viejo cambió, o los dos .env son el mismo servidor
#
# Tablas de códigos de los bloques que este script llama (copiadas sin cambios; un test de
# sincronía las compara con las de cada bloque):
# >>> CODIGOS deploy/volcado-final-vps.sh
# Códigos de salida (estables):
#   0  OK
#   2  uso / argumentos inválidos
#   3  precondición: config faltante o inválida, sin sudo -n, contenedor fuera de patrón o
#      detenido, la API sigue activa tras el stop, conexiones abiertas a la base
#   4  traída inválida: sha local ≠ remoto, tamaño < 1024 o la transferencia falló (el archivo
#      local se borra), o el historial de backups no es un tar legible
#   8  no se pudo consultar (ssh/systemctl/docker/psql con error o salida ilegible): "no pude
#      consultar" nunca es "cero" ni "vacío"; también si --ensayo no pudo rearrancar la API, o
#      si la unit no está donde la deja install.sh (o ya existe un .congelado): no se toca
#   9  alcance: la lista de contenedores al final no es la del principio
# <<< CODIGOS deploy/volcado-final-vps.sh
# >>> CODIGOS deploy/restaurar-backup.sh
# Códigos de salida (estables):
#   0  OK
#   2  uso/argumentos/manifiesto inválidos
#   3  precondición: API activa, contenedor fuera de patrón/ausente/detenido, base no vacía,
#      conexiones abiertas al DROP DATABASE de --recrear-base
#   4  dump inválido: sha256/tamaño ≠ manifiesto, pg_restore --list falla, tablas ≠ manifiesto
#   5  falló pg_restore: la base quedó VACÍA (transacción única), se puede reintentar tal cual
#   6  falló la verificación de conteos, última migración o secuencias (la base QUEDA restaurada)
#   7  duplicados en catálogos con Nombre único (la base QUEDA restaurada)
#   8  no se pudo consultar (docker/psql con error): "no pude consultar" nunca es "vacío"
#   9  falló el dump de seguridad de --recrear-base: NO se borró nada
# <<< CODIGOS deploy/restaurar-backup.sh
# =======================================================================================
set -euo pipefail

# Secretos (R34): una variable que el shell del operador ya exportaba conserva el atributo export
# al asignarla (también si se la declara `local` dentro de una función), y el secreto llegaría al
# entorno de ssh, de curl y de los bloques. Se borran ANTES de cargar la lib y de asignarlas: así
# nacen sin exportar. Son todas las que llegan a contener la contraseña, el cuerpo del login o un
# token (también la salida de ssh y de curl, donde viaja la respuesta del login).
unset PW TOKEN cuerpo listado entrada CURL_CUERPO ORQ_SALIDA ORQ_STDERR

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
# shellcheck source=lib/orquestacion.sh
source "$REPO_ROOT/deploy/lib/orquestacion.sh"

PASOS_TOTAL=13

# El trap EXIT de la lib borra los temporales privados del operador (_orq_tmp_limpiar): poner otro
# trap lo reemplazaría y dejaría en /tmp el stderr de ssh. Este lo ENCADENA. Además cierra una corrida
# que falló antes de tocar el viejo: sin un resultado=, una corrida con estado cuenta como "sin
# terminar" y bloquearía todos los cortes siguientes. Con congelado_utc la decisión (--continuar o
# --revertir) es de un humano y la corrida se deja abierta, salvo con 5: el 5 dice "antes de
# congelar" (el volcado falló antes del stop y el viejo sigue sirviendo, lo que se confirmó con
# is-active), así que no hay nada que retomar aunque congelado_utc ya esté escrito (R31).
_migrar_salir() {
    local rc=$? resultado_rc=0 congelado_rc=0 archivo
    # Solo quien tiene el lock toca el estado: un --continuar que perdió el lock no puede cerrar la
    # corrida que otro proceso está corriendo.
    if [[ "$rc" -ne 0 && -n "${ORQ_CORRIDA:-}" && "$LOCK_TOMADO" -eq 1 ]]; then
        archivo="$ORQ_CORRIDA/estado"
        if [[ -f "$archivo" ]]; then
            orq_estado_de "$archivo" resultado >/dev/null 2>&1 || resultado_rc=$?
            orq_estado_de "$archivo" congelado_utc >/dev/null 2>&1 || congelado_rc=$?
            if [[ "$resultado_rc" -eq 1 && ( "$congelado_rc" -eq 1 || "$rc" -eq 5 ) ]]; then
                orq_estado_poner resultado "FALLO-$rc" >/dev/null 2>&1 || :
            fi
        fi
    fi
    _orq_tmp_limpiar
}
LOCK_TOMADO=0
trap '_migrar_salir' EXIT

uso() {
    sed -n '/^# Uso:/,/^#   STOCKAPP_ENSAYO_FALLAR_EN=/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
}

# ---------------------------------------------------------------------------------------
# Salida y abortos
# ---------------------------------------------------------------------------------------

PASO_ABIERTO=0
PASO_T0=0
# Después del congelamiento (paso 6) todo fallo deja el viejo congelado: abortar imprime el estado y
# las salidas. DETALLE_FALLO es lo que va entre paréntesis en la línea del paso que falló (el bloque
# y su código); SALIDA_SUGERIDA elige qué salidas se ofrecen (ver resumen_de_fallo).
CONGELADO=0
SIN_AYUDA_DE_REVERTIR=0
DETALLE_FALLO=""
SALIDA_SUGERIDA="continuar"
ESTADO_VIEJO_TXT=""

abrir_paso() {
    orq_paso "$1" "$PASOS_TOTAL" "$2"
    PASO_ABIERTO=1
    PASO_T0="$(orq_reloj)"
}

# cerrar_paso <n> <OK|YA ESTABA|OMITIDO> [detalle] [marca]: línea del paso y su clave en el estado.
cerrar_paso() {
    local n="$1" resultado="$2" detalle="${3-}" marca="${4:-ok}" duracion
    duracion=$(($(orq_reloj) - PASO_T0))
    PASO_ABIERTO=0
    orq_fin_paso "$resultado" "$detalle"
    orq_estado_poner "paso_$n" "$marca $(date -u +%Y-%m-%dT%H:%M:%SZ) $duracion"
}

# abortar <codigo> <mensaje>: cierra el paso abierto como FALLÓ y sale. Con el viejo ya congelado
# (y salvo el 5, que es "antes de congelar"), antes de salir dice en qué estado quedó y cómo seguir.
abortar() {
    local codigo="$1"
    shift
    echo "migrar: $*" >&2
    if [[ "$PASO_ABIERTO" -eq 1 ]]; then
        PASO_ABIERTO=0
        orq_fin_paso FALLÓ "${DETALLE_FALLO:-exit $codigo}"
    fi
    if [[ "$CONGELADO" -eq 1 && "$codigo" -ne 5 ]]; then
        resumen_de_fallo "$codigo"
    fi
    if [[ "$MODO" == "revertir" && "$codigo" -ne 2 && "$codigo" -ne 4 && "$SIN_AYUDA_DE_REVERTIR" -eq 0 ]]; then
        ayuda_de_revertir "$codigo"
    fi
    if [[ "$MODO" == "desde-backup" && "$codigo" -ge 5 && -n "${ORQ_CORRIDA:-}" ]]; then
        ayuda_desde_backup "$codigo"
    fi
    exit "$codigo"
}

# ayuda_desde_backup <codigo>: no hay viejo que revertir ni corrida que retomar. El trap la cierra con
# resultado=FALLO-<n>, y la salida es volver a correr --desde-backup entero.
ayuda_desde_backup() {
    {
        echo
        echo "== La recuperación desde el backup no terminó (exit $1) =="
        echo "Corrida: $ORQ_CORRIDA (queda cerrada con resultado=FALLO-$1)"
        estado_nuevo_observado
        echo "Una corrida de --desde-backup no se retoma: se vuelve a correr entera, y su paso 9 recrea la base"
        echo "del nuevo con un dump de seguridad de lo que tenga. Investigá la causa (arriba) y después:"
        echo "    deploy/migrar-vps.sh --desde-backup ${BACKUP}${MANIFIESTO_RUTA:+ --manifiesto $MANIFIESTO_RUTA} --env-nuevo $ENV_NUEVO"
    } >&2
}

# resumen_de_fallo <codigo>: el viejo quedó congelado y la corrida queda abierta (sin resultado=).
# Las salidas según SALIDA_SUGERIDA:
#   continuar                 --continuar <corrida> o --revertir
#   reintentar-restauracion   la base quedó restaurada pero no verifica: lo decide un humano
#   investigar                no se pudo consultar o el código no se puede clasificar
#   revisar                   alcance (9): NO se ofrece --continuar hasta que un humano mire
#   solo-revertir             la unit quedó movida sin enmascarar: retomar no avanzaría (R39)
resumen_de_fallo() {
    local codigo="$1" que="el corte"
    [[ "$MODO" == "ensayo" ]] && que="el ensayo"
    {
        echo
        echo "== ${que^} no terminó (exit $codigo) =="
        echo "Corrida: $ORQ_CORRIDA (queda abierta: sin resultado)"
        if [[ -n "$ESTADO_VIEJO_TXT" ]]; then
            echo "VPS viejo: $ESTADO_VIEJO_TXT"
        elif [[ "$MODO" == "ensayo" ]]; then
            echo "VPS viejo (testing): $(estado_observado VIEJO)"
        else
            echo "VPS viejo: quedó congelado: stockapp-api detenida (y enmascarada, si el volcado llegó hasta ahí). Nadie escribe en la base vieja."
        fi
        estado_nuevo_observado
        case "$SALIDA_SUGERIDA" in
            reintentar-restauracion)
                echo "Sin retomar automático: la base del nuevo QUEDÓ restaurada pero no verifica. Investigá y después elegí:"
                echo "    deploy/migrar-vps.sh --continuar $ORQ_CORRIDA --reintentar-restauracion   (vuelve a --recrear-base, con otro dump de seguridad)"
                ;;
            investigar)
                echo "Investigá la causa (arriba) y después elegí:"
                echo "    deploy/migrar-vps.sh --continuar $ORQ_CORRIDA"
                ;;
            revisar)
                echo "Es un problema de alcance: un humano tiene que mirar antes de seguir. No se ofrece retomar."
                ;;
            solo-revertir)
                echo "No se ofrece retomar: $MOTIVO_SOLO_REVERTIR"
                ;;
            *)
                echo "Para seguir:"
                echo "    deploy/migrar-vps.sh --continuar $ORQ_CORRIDA"
                ;;
        esac
        echo "Para volver atrás (el viejo vuelve a servir):"
        echo "    deploy/migrar-vps.sh --revertir $ORQ_CORRIDA"
    } >&2
}

# estado_observado <PREFIJO>: lo que dice is-active (y, en el viejo, is-enabled) de stockapp-api, sin
# abortar: se usa DENTRO de abortar, donde un "no pude consultar" se informa y no se vuelve a abortar.
estado_observado() {
    local palabra rc
    orq_ssh "$1" "systemctl is-active stockapp-api"
    palabra="$ORQ_SALIDA" rc="$ORQ_RC"
    if [[ ( "$rc" -ne 0 && "$rc" -ne 3 ) || ! "$palabra" =~ ^[a-z-]+$ ]]; then
        printf 'no pude consultar el estado de stockapp-api (salió con %s)' "$rc"
        return 0
    fi
    if [[ "$1" == "VIEJO" ]]; then
        orq_ssh VIEJO "systemctl is-enabled stockapp-api"
        if [[ ( "$ORQ_RC" -eq 0 || "$ORQ_RC" -eq 1 ) && "$ORQ_SALIDA" =~ ^[a-z-]+$ ]]; then
            printf "stockapp-api dice '%s' y su unit '%s'" "$palabra" "$ORQ_SALIDA"
            return 0
        fi
    fi
    printf "stockapp-api dice '%s'" "$palabra"
}

# estado_nuevo_observado: la línea del VPS nuevo del resumen de fallo, con lo que se OBSERVA (R37).
estado_nuevo_observado() {
    local rc
    orq_ssh NUEVO "systemctl is-active stockapp-api"
    rc="$ORQ_RC"
    if [[ "$rc" -ne 0 && "$rc" -ne 3 ]]; then
        echo "VPS nuevo: no pude consultar el estado de stockapp-api (salió con $rc): no la arranques a mano hasta saber cómo quedó."
        return 0
    fi
    case "$ORQ_SALIDA" in
        inactive) echo "VPS nuevo: la API nueva está detenida (inactive); no la arranques a mano." ;;
        failed) echo "VPS nuevo: stockapp-api quedó en failed (no atiende); el journal está arriba. No la arranques a mano." ;;
        active) echo "VPS nuevo: stockapp-api está ARRIBA (active), pero esto no es un GO: no instales las PCs." ;;
        *) echo "VPS nuevo: stockapp-api dice '${ORQ_SALIDA}'." ;;
    esac
}

# volcar_stderr: lo que dijo ssh o el comando remoto, con sangría. Solo el stderr, nunca la salida:
# la de un login lleva el token.
volcar_stderr() {
    if [[ -n "$ORQ_STDERR" ]]; then
        printf '%s\n' "$ORQ_STDERR" | sed 's/^/    | /' >&2
    fi
}

# consulta_ok <qué>: tras orq_ssh, un código distinto de cero (el 255 de ssh incluido) es "no pude
# consultar", nunca "vacío" ni "cero".
consulta_ok() {
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar 8 "no pude consultar $1 (salió con $ORQ_RC). Esto NO es 'vacío' ni 'cero'."
    fi
}

contiene_linea() {
    local texto="$1" buscado="$2" linea
    while IFS= read -r linea; do
        if [[ "$linea" == "$buscado" ]]; then
            return 0
        fi
    done <<< "$texto"
    return 1
}

ahora_utc() {
    date -u +%Y-%m-%dT%H:%M:%SZ
}

# ---------------------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------------------

MODO="corte"
ENV_VIEJO="$REPO_ROOT/deploy/.env"
ENV_NUEVO="$REPO_ROOT/deploy/.env.vps-nuevo"
CONTENEDOR_VIEJO="stockapp-pg"
CONTENEDOR_NUEVO="stockapp-pg"
RAIZ_VIEJO=""
RAIZ_NUEVO=""
SIN_VERIFICAR_PREPARACION=0
ARNES_USADO=()
declare -A ARNES_DADO=()
CONTINUAR=""
REINTENTAR_RESTAURACION=0
ENV_EXPLICITO=0
REVERTIR=0
REVERTIR_CORRIDA=""
AUN_CON_ESCRITURAS=0
ENV_VIEJO_DADO=0
DESDE_BACKUP=""
MANIFIESTO_DADO=""
POST_CORTE=""
ANTES_DE_PLAZO=0
# La corrida cuyo estado se relee (--continuar, --revertir o --post-corte).
OBJETIVO=""

valor_de() {
    if [[ $# -lt 2 || -z "$2" ]]; then
        echo "migrar: $1 necesita un valor" >&2
        uso
        exit 2
    fi
}

valor_contenedor() {
    valor_de "$@"
    if [[ ! "$2" =~ ^stockapp[a-z0-9-]*$ ]]; then
        echo "migrar: $1 '$2' no matchea ^stockapp[a-z0-9-]*\$" >&2
        exit 2
    fi
}

valor_raiz() {
    valor_de "$@"
    if [[ ! "$2" =~ ^/[A-Za-z0-9._/-]+$ || "$2" == *..* ]]; then
        echo "migrar: $1 '$2' tiene que ser una ruta absoluta sin '..'" >&2
        exit 2
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --ensayo)
            MODO="ensayo"
            shift
            ;;
        --continuar)
            valor_de "$@"
            CONTINUAR="$2"
            shift 2
            ;;
        --reintentar-restauracion)
            REINTENTAR_RESTAURACION=1
            shift
            ;;
        --revertir)
            # La corrida es opcional: sin ella, la última de corte que registra el congelamiento.
            REVERTIR=1
            if [[ $# -ge 2 && "$2" != -* ]]; then
                REVERTIR_CORRIDA="$2"
                shift 2
            else
                shift
            fi
            ;;
        --aun-con-escrituras)
            AUN_CON_ESCRITURAS=1
            shift
            ;;
        --env-viejo)
            valor_de "$@"
            ENV_VIEJO="$2"
            ENV_EXPLICITO=1
            ENV_VIEJO_DADO=1
            shift 2
            ;;
        --desde-backup)
            valor_de "$@"
            DESDE_BACKUP="$2"
            shift 2
            ;;
        --manifiesto)
            valor_de "$@"
            MANIFIESTO_DADO="$2"
            shift 2
            ;;
        --post-corte)
            valor_de "$@"
            POST_CORTE="$2"
            shift 2
            ;;
        --antes-de-plazo)
            ANTES_DE_PLAZO=1
            shift
            ;;
        --env-nuevo)
            valor_de "$@"
            ENV_NUEVO="$2"
            ENV_EXPLICITO=1
            shift 2
            ;;
        --contenedor-viejo)
            valor_contenedor "$@"
            CONTENEDOR_VIEJO="$2"
            ARNES_USADO+=("$1")
            ARNES_DADO[$1]=1
            shift 2
            ;;
        --contenedor-nuevo)
            valor_contenedor "$@"
            CONTENEDOR_NUEVO="$2"
            ARNES_USADO+=("$1")
            ARNES_DADO[$1]=1
            shift 2
            ;;
        --raiz-remota-viejo)
            valor_raiz "$@"
            RAIZ_VIEJO="$2"
            ARNES_USADO+=("$1")
            ARNES_DADO[$1]=1
            shift 2
            ;;
        --raiz-remota-nuevo)
            valor_raiz "$@"
            RAIZ_NUEVO="$2"
            ARNES_USADO+=("$1")
            ARNES_DADO[$1]=1
            shift 2
            ;;
        --sin-verificar-preparacion)
            SIN_VERIFICAR_PREPARACION=1
            ARNES_USADO+=("$1")
            shift
            ;;
        -h | --help)
            uso
            exit 0
            ;;
        *)
            echo "migrar: argumento desconocido '$1'" >&2
            uso
            exit 2
            ;;
    esac
done

# ruta_de_corrida_valida <ruta> [<modos>]: la ruta absoluta de una corrida de uno de <modos> (una
# alternativa de regex; por defecto corte|ensayo) bajo las corridas de este HOME, tal como la imprime migrar.
ruta_de_corrida_valida() {
    local modos="${2:-corte|ensayo}"
    [[ -n "${HOME:-}" && "${1%/*}" == "$HOME/stockapp-backups-vps/corridas" &&
        "${1##*/}" =~ ^($modos)-[0-9]{8}T[0-9]{6}Z$ ]]
}

# Un solo modo por invocación: el corte (sin opción), --ensayo, --continuar, --revertir, --desde-backup
# o --post-corte.
modos_pedidos=0
[[ "$MODO" == "ensayo" ]] && modos_pedidos=$((modos_pedidos + 1))
[[ -n "$CONTINUAR" ]] && modos_pedidos=$((modos_pedidos + 1))
[[ "$REVERTIR" -eq 1 ]] && modos_pedidos=$((modos_pedidos + 1))
[[ -n "$DESDE_BACKUP" ]] && modos_pedidos=$((modos_pedidos + 1))
[[ -n "$POST_CORTE" ]] && modos_pedidos=$((modos_pedidos + 1))
if [[ "$modos_pedidos" -gt 1 ]]; then
    echo "migrar: --ensayo, --continuar, --revertir, --desde-backup y --post-corte son modos distintos: uno por vez." >&2
    exit 2
fi
if [[ -n "$MANIFIESTO_DADO" && -z "$DESDE_BACKUP" ]]; then
    echo "migrar: --manifiesto solo existe con --desde-backup <dump>." >&2
    exit 2
fi
if [[ "$ANTES_DE_PLAZO" -eq 1 && -z "$POST_CORTE" ]]; then
    echo "migrar: --antes-de-plazo solo existe con --post-corte <corrida>." >&2
    exit 2
fi

if [[ -n "$DESDE_BACKUP" ]]; then
    # No hay VPS viejo: puede estar muerto, y nada de esta corrida lo nombra.
    # --env-viejo SÍ: es un archivo local del operador (de ahí salen la credencial de V5/V8 y la clave de
    # licencias), y nunca se usa para conectarse al viejo.
    if [[ -n "${ARNES_DADO[--contenedor-viejo]-}" || -n "${ARNES_DADO[--raiz-remota-viejo]-}" ||
        "$REINTENTAR_RESTAURACION" -eq 1 || "$AUN_CON_ESCRITURAS" -eq 1 ]]; then
        echo "migrar: --desde-backup no acepta --contenedor-viejo, --raiz-remota-viejo, --reintentar-restauracion ni --aun-con-escrituras: no se opera el VPS viejo, y una corrida que falla se vuelve a correr entera." >&2
        exit 2
    fi
    MODO="desde-backup"
fi

if [[ -n "$POST_CORTE" ]]; then
    # El .env del nuevo, los contenedores y las raíces salen del estado de la corrida del corte.
    if [[ "$ENV_EXPLICITO" -eq 1 || "${#ARNES_USADO[@]}" -gt 0 || "$REINTENTAR_RESTAURACION" -eq 1 || "$AUN_CON_ESCRITURAS" -eq 1 ]]; then
        echo "migrar: --post-corte solo acepta --antes-de-plazo: el servidor nuevo, los contenedores y las raíces salen del estado de la corrida del corte." >&2
        exit 2
    fi
    POST_CORTE="${POST_CORTE%/}"
    if ! ruta_de_corrida_valida "$POST_CORTE" corte; then
        echo "migrar: --post-corte necesita la ruta de una corrida de corte, tal como la imprimió migrar (~/stockapp-backups-vps/corridas/corte-<ts>): '$POST_CORTE'." >&2
        exit 2
    fi
    MODO="post-corte"
fi

if [[ "$REVERTIR" -eq 1 ]]; then
    # Como --continuar: los .env, los contenedores y las raíces salen del estado de la corrida.
    if [[ "$ENV_EXPLICITO" -eq 1 || "$MODO" != "corte" || -n "$CONTINUAR" || "$REINTENTAR_RESTAURACION" -eq 1 ]]; then
        echo "migrar: --revertir no acepta --env-viejo, --env-nuevo, --ensayo, --continuar ni --reintentar-restauracion: los servidores salen del estado de la corrida." >&2
        exit 2
    fi
    REVERTIR_CORRIDA="${REVERTIR_CORRIDA%/}"
    if [[ -n "$REVERTIR_CORRIDA" ]] && ! ruta_de_corrida_valida "$REVERTIR_CORRIDA"; then
        echo "migrar: --revertir necesita la ruta de una corrida de corte o de ensayo, tal como la imprimió migrar (~/stockapp-backups-vps/corridas/<corte|ensayo>-<ts>): '$REVERTIR_CORRIDA'." >&2
        exit 2
    fi
elif [[ "$AUN_CON_ESCRITURAS" -eq 1 ]]; then
    echo "migrar: --aun-con-escrituras solo existe con --revertir." >&2
    exit 2
fi

if [[ -n "$CONTINUAR" ]]; then
    # Los .env y el modo salen del estado de la corrida: otra cosa sería retomar contra otros servidores.
    if [[ "$ENV_EXPLICITO" -eq 1 || "$MODO" != "corte" ]]; then
        echo "migrar: --continuar no acepta --env-viejo, --env-nuevo ni --ensayo: salen del estado de la corrida." >&2
        exit 2
    fi
    # Solo una corrida de corte, de ensayo o de --desde-backup (que no se retoma: solo se cierra), con su
    # ruta absoluta bajo las corridas de este HOME.
    CONTINUAR="${CONTINUAR%/}"
    if ! ruta_de_corrida_valida "$CONTINUAR" 'corte|ensayo|desde-backup'; then
        echo "migrar: --continuar necesita la ruta de una corrida de corte, de ensayo o de --desde-backup (esta última solo se cierra), tal como la imprimió migrar (~/stockapp-backups-vps/corridas/<corte|ensayo|desde-backup>-<ts>): '$CONTINUAR'." >&2
        exit 2
    fi
elif [[ "$REINTENTAR_RESTAURACION" -eq 1 ]]; then
    echo "migrar: --reintentar-restauracion solo existe con --continuar <corrida>." >&2
    exit 2
fi

# ---------------------------------------------------------------------------------------
# Configuración (fase A: nada de esto crea una corrida ni escribe nada)
# ---------------------------------------------------------------------------------------

# La contraseña de verificación: el entorno gana sobre el archivo. Se copia a una variable que NO
# se exporta y se saca del entorno, para que ningún proceso hijo (ssh, los bloques) la herede.
PW="${VPS_ADMIN_PASSWORD-}"
unset VPS_ADMIN_PASSWORD

CLAVES_COMUNES=(VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT POSTGRES_USER POSTGRES_DB)

# exigir <PREFIJO> <archivo> <CLAVE>...: cada clave tiene que traer un valor.
exigir() {
    local p="$1" archivo="$2" clave nombre
    shift 2
    for clave in "$@"; do
        nombre="${p}_${clave}"
        if [[ -z "${!nombre:-}" ]]; then
            abortar 3 "falta $clave en $archivo."
        fi
    done
}

validar_destino() {
    local p="$1" archivo="$2" nombre_host="${1}_VPS_HOST" nombre_usuario="${1}_VPS_USER" nombre_puerto="${1}_VPS_SSH_PORT"
    local nombre_esquema="${1}_API_SCHEME" nombre_apihost="${1}_API_HOSTNAME" nombre_apipuerto="${1}_API_PORT"
    local nombre_pguser="${1}_POSTGRES_USER" nombre_pgdb="${1}_POSTGRES_DB"
    if [[ ! "${!nombre_host}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
        abortar 3 "VPS_HOST en $archivo no parece un nombre ni una IP."
    fi
    if [[ ! "${!nombre_usuario}" =~ ^[a-z_][a-z0-9_-]*$ ]]; then
        abortar 3 "VPS_USER en $archivo no parece un usuario de Linux."
    fi
    if [[ ! "${!nombre_puerto}" =~ ^[0-9]+$ ]] || (("${!nombre_puerto}" < 1 || "${!nombre_puerto}" > 65535)); then
        abortar 3 "VPS_SSH_PORT en $archivo no es un puerto."
    fi
    if [[ "${!nombre_esquema}" != "http" && "${!nombre_esquema}" != "https" ]]; then
        abortar 3 "API_SCHEME en $archivo tiene que ser exactamente http o https."
    fi
    if [[ -n "${!nombre_apihost:-}" && ! "${!nombre_apihost}" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]; then
        abortar 3 "API_HOSTNAME en $archivo no parece un nombre de host."
    fi
    if [[ ! "${!nombre_apipuerto}" =~ ^[0-9]+$ ]] || (("${!nombre_apipuerto}" < 1 || "${!nombre_apipuerto}" > 65535)); then
        abortar 3 "API_PORT en $archivo no es un puerto."
    fi
    if [[ ! "${!nombre_pguser}" =~ ^[a-z_][a-z0-9_]*$ || ! "${!nombre_pgdb}" =~ ^[a-z_][a-z0-9_]*$ ]]; then
        abortar 3 "POSTGRES_USER o POSTGRES_DB en $archivo no matchean ^[a-z_][a-z0-9_]*\$."
    fi
}

# solo_nuevo: 0 en los modos que no tienen VPS viejo (--desde-backup) o no lo tocan (--post-corte): su
# .env ni se lee.
solo_nuevo() {
    [[ "$MODO" == "desde-backup" || "$MODO" == "post-corte" ]]
}

# El usuario real de V5 y V8 y el .env de donde salen él y su contraseña: el del viejo (es el mismo
# usuario que viaja en los datos) o, sin viejo, el del nuevo.
USUARIO_VERIFICACION=""
ENV_CREDENCIAL=""

# cargar_config_solo_nuevo: lo mismo que cargar_config, sin conectarse nunca al VPS viejo. En
# --desde-backup el usuario real de V5/V8 y su contraseña salen del --env-viejo (spec 6.5.1), que es un
# archivo LOCAL del operador y existe aunque el VPS viejo esté muerto: de él se leen SOLO esas dos claves
# (nunca VPS_HOST), y preparar-vps-nuevo.sh --verificar lee de él la clave de licencias.
cargar_config_solo_nuevo() {
    orq_leer_env "$ENV_NUEVO" NUEVO "${CLAVES_COMUNES[@]}"
    NUEVO_POSTGRES_USER="${NUEVO_POSTGRES_USER:-stockapp}"
    NUEVO_POSTGRES_DB="${NUEVO_POSTGRES_DB:-stockapp}"
    exigir NUEVO "$ENV_NUEVO" VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT
    if [[ "$MODO" == "desde-backup" ]]; then
        ENV_VIEJO="$(realpath -m -- "$ENV_VIEJO")"
        if [[ ! -f "$ENV_VIEJO" || ! -r "$ENV_VIEJO" ]]; then
            abortar 2 "--desde-backup necesita --env-viejo (el .env LOCAL de siempre; no se contacta al VPS viejo): de ahí salen VPS_ADMIN_USER y VPS_ADMIN_PASSWORD para V5/V8 y la clave de licencias para preparar-vps-nuevo.sh --verificar. No puedo leer $ENV_VIEJO."
        fi
        orq_leer_env "$ENV_VIEJO" VIEJO VPS_ADMIN_USER VPS_ADMIN_PASSWORD
        if [[ -z "${VIEJO_VPS_ADMIN_USER:-}" ]]; then
            abortar 2 "falta VPS_ADMIN_USER en $ENV_VIEJO (--env-viejo): es el usuario real con el que V5 y V8 verifican la API nueva."
        fi
        if [[ -z "$PW" && -n "${VIEJO_VPS_ADMIN_PASSWORD-}" ]]; then
            PW="$VIEJO_VPS_ADMIN_PASSWORD"
        fi
        unset VIEJO_VPS_ADMIN_PASSWORD
        USUARIO_VERIFICACION="$VIEJO_VPS_ADMIN_USER"
        ENV_CREDENCIAL="$ENV_VIEJO"
    fi

    if [[ -n "${STOCKAPP_ENSAYO_FALLAR_EN-}" ]]; then
        ARNES_USADO+=("STOCKAPP_ENSAYO_FALLAR_EN")
        if [[ ! "$STOCKAPP_ENSAYO_FALLAR_EN" =~ ^([3-9]|1[01])$ ]]; then
            abortar 2 "STOCKAPP_ENSAYO_FALLAR_EN='$STOCKAPP_ENSAYO_FALLAR_EN': solo se inyectan los pasos 3 a 11."
        fi
    fi
    if orq_hosts_de_ensayo "$NUEVO_VPS_HOST"; then
        ORQ_ENSAYO=1
    elif [[ "${#ARNES_USADO[@]}" -gt 0 ]]; then
        abortar 2 "${ARNES_USADO[*]} solo existen para el ensayo local (hosts .invalid) y acá VPS_HOST es un servidor real."
    fi
    validar_destino NUEVO "$ENV_NUEVO"
    if [[ "$ORQ_ENSAYO" -ne 1 && "$NUEVO_API_SCHEME" != "https" ]]; then
        abortar 3 "API_SCHEME en $ENV_NUEVO tiene que ser https: el VPS nuevo sirve con TLS y la verificación V3 lo comprueba."
    fi
}

cargar_config() {
    if [[ -z "${HOME:-}" ]]; then
        abortar 3 "HOME no está definido: no sé dónde guardar la corrida."
    fi
    ENV_NUEVO="$(realpath -m -- "$ENV_NUEVO")"
    if solo_nuevo; then
        cargar_config_solo_nuevo
        cargar_urls_del_nuevo
        return 0
    fi
    ENV_VIEJO="$(realpath -m -- "$ENV_VIEJO")"
    if [[ "$ENV_VIEJO" == "$ENV_NUEVO" ]]; then
        abortar 9 "--env-viejo y --env-nuevo son el mismo archivo ($ENV_VIEJO): es el mismo servidor."
    fi

    orq_leer_env "$ENV_VIEJO" VIEJO "${CLAVES_COMUNES[@]}" VPS_ADMIN_USER VPS_ADMIN_PASSWORD
    orq_leer_env "$ENV_NUEVO" NUEVO "${CLAVES_COMUNES[@]}"
    if [[ -z "$PW" && -n "${VIEJO_VPS_ADMIN_PASSWORD-}" ]]; then
        PW="$VIEJO_VPS_ADMIN_PASSWORD"
    fi
    unset VIEJO_VPS_ADMIN_PASSWORD
    USUARIO_VERIFICACION="${VIEJO_VPS_ADMIN_USER-}"
    ENV_CREDENCIAL="$ENV_VIEJO"

    # Defaults del viejo (los mismos que usan bajar-backup-vps.sh y volcado-final-vps.sh). El nuevo
    # no tiene defaults: sin configuración explícita no se apunta a ningún servidor.
    VIEJO_API_SCHEME="${VIEJO_API_SCHEME:-http}"
    VIEJO_API_PORT="${VIEJO_API_PORT:-8080}"
    VIEJO_POSTGRES_USER="${VIEJO_POSTGRES_USER:-stockapp}"
    VIEJO_POSTGRES_DB="${VIEJO_POSTGRES_DB:-stockapp}"
    NUEVO_POSTGRES_USER="${NUEVO_POSTGRES_USER:-stockapp}"
    NUEVO_POSTGRES_DB="${NUEVO_POSTGRES_DB:-stockapp}"

    exigir VIEJO "$ENV_VIEJO" VPS_HOST VPS_USER VPS_SSH_PORT VPS_ADMIN_USER
    exigir NUEVO "$ENV_NUEVO" VPS_HOST VPS_USER VPS_SSH_PORT API_SCHEME API_HOSTNAME API_PORT
    if [[ "$VIEJO_API_SCHEME" == "https" ]]; then
        exigir VIEJO "$ENV_VIEJO" API_HOSTNAME
    fi

    # Los parámetros de arnés y la inyección de fallos solo existen para el ensayo local: si algún
    # host no es .invalid se rechazan. Y solo con TODOS los hosts .invalid la palabra de
    # confirmación puede leerse de stdin (ORQ_ENSAYO, que solo se pone acá, nunca el entorno).
    if [[ -n "${STOCKAPP_ENSAYO_FALLAR_EN-}" ]]; then
        ARNES_USADO+=("STOCKAPP_ENSAYO_FALLAR_EN")
        if [[ ! "$STOCKAPP_ENSAYO_FALLAR_EN" =~ ^([3-9]|1[01])$ ]]; then
            abortar 2 "STOCKAPP_ENSAYO_FALLAR_EN='$STOCKAPP_ENSAYO_FALLAR_EN': solo se inyectan los pasos 3 a 11."
        fi
    fi
    if orq_hosts_de_ensayo "$VIEJO_VPS_HOST" "$NUEVO_VPS_HOST"; then
        ORQ_ENSAYO=1
    elif [[ "${#ARNES_USADO[@]}" -gt 0 ]]; then
        abortar 2 "${ARNES_USADO[*]} solo existen para el ensayo local (hosts .invalid) y acá algún VPS_HOST es un servidor real."
    fi

    validar_destino VIEJO "$ENV_VIEJO"
    validar_destino NUEVO "$ENV_NUEVO"
    if [[ "$ORQ_ENSAYO" -ne 1 && "$NUEVO_API_SCHEME" != "https" ]]; then
        abortar 3 "API_SCHEME en $ENV_NUEVO tiene que ser https: el VPS nuevo sirve con TLS y la verificación V3 lo comprueba."
    fi

    # A9: dos .env que apuntan al mismo servidor. La identidad real (machine-id) se comprueba después.
    if [[ "${VIEJO_VPS_HOST,,}" == "${NUEVO_VPS_HOST,,}" ]]; then
        abortar 9 "los dos .env apuntan al mismo VPS_HOST ($VIEJO_VPS_HOST): migrar nunca corre con los dos .env en el mismo servidor."
    fi

    # URL de la API vieja para el login y GET /backups, desde el propio servidor (loopback). Con
    # https, --resolve fuerza el loopback validando el certificado real (nunca -k).
    if [[ "$VIEJO_API_SCHEME" == "https" ]]; then
        URL_VIEJO="https://${VIEJO_API_HOSTNAME}:${VIEJO_API_PORT}"
        RESOLVE_VIEJO="--resolve ${VIEJO_API_HOSTNAME}:${VIEJO_API_PORT}:127.0.0.1 "
    else
        URL_VIEJO="http://127.0.0.1:${VIEJO_API_PORT}"
        RESOLVE_VIEJO=""
    fi
    cargar_urls_del_nuevo
}

cargar_urls_del_nuevo() {
    # La API nueva: la URL pública (V3 a V8, desde esta máquina y validando el certificado) y la de
    # loopback desde el propio servidor nuevo (el arranque del paso 11), con el mismo criterio.
    URL_NUEVO="${NUEVO_API_SCHEME}://${NUEVO_API_HOSTNAME}:${NUEVO_API_PORT}"
    if [[ "$NUEVO_API_SCHEME" == "https" ]]; then
        URL_NUEVO_LOCAL="$URL_NUEVO"
        RESOLVE_NUEVO="--resolve ${NUEVO_API_HOSTNAME}:${NUEVO_API_PORT}:127.0.0.1 "
    else
        URL_NUEVO_LOCAL="http://127.0.0.1:${NUEVO_API_PORT}"
        RESOLVE_NUEVO=""
    fi
    DIR_STOCKAPP_NUEVO="${RAIZ_NUEVO}/var/lib/stockapp/.local/share/StockApp"
}

exigir_terminal() {
    # A12: el corte nunca congela sin una confirmación escrita por un humano en una terminal. Lo mismo
    # para RESTAURAR (--desde-backup) y para cada BORRAR de --post-corte.
    if [[ ( "$MODO" == "corte" || "$MODO" == "desde-backup" || "$MODO" == "post-corte" ) && "$ORQ_ENSAYO" -ne 1 ]] &&
        ! { : </dev/tty; } 2>/dev/null; then
        abortar 3 "no hay una terminal para confirmar: esta operación no se lanza desatendida. No se tocó nada."
    fi
}

# ---------------------------------------------------------------------------------------
# Identidad de los hosts (fase A: lecturas por ssh, antes de crear la corrida)
# ---------------------------------------------------------------------------------------

MACHINE_ID_VIEJO=""
MACHINE_ID_NUEVO=""

# leer_machine_id <VIEJO|NUEVO> <raiz-remota> <variable-destino>
leer_machine_id() {
    local p="$1" raiz="$2" destino="$3" nombre_host="${1}_VPS_HOST"
    orq_ssh "$p" "cat ${raiz}/etc/machine-id"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar 8 "no pude leer /etc/machine-id de ${!nombre_host} (salió con $ORQ_RC)."
    fi
    if [[ ! "$ORQ_SALIDA" =~ ^[0-9a-f]{32}$ ]]; then
        abortar 8 "el machine-id de ${!nombre_host} no tiene el formato esperado (32 hexadecimales): no puedo interpretarlo."
    fi
    printf -v "$destino" '%s' "$ORQ_SALIDA"
}

# ensayos_previos: las corridas de ensayo anteriores, la más vieja primero.
ensayos_previos() {
    local base="$HOME/stockapp-backups-vps/corridas" dir
    if [[ ! -d "$base" ]]; then
        return 0
    fi
    for dir in "$base"/ensayo-*/; do
        if [[ -d "$dir" ]]; then
            printf '%s\n' "${dir%/}"
        fi
    done
}

# ensayo_previo_con <clave>... -> ENSAYO_PREVIO = el último ensayo cuyo estado tiene todas las claves.
ENSAYO_PREVIO=""
ensayo_previo_con() {
    local dir clave rc falta
    ENSAYO_PREVIO=""
    while IFS= read -r dir; do
        [[ -n "$dir" && -f "$dir/estado" ]] || continue
        falta=0
        for clave in "$@"; do
            rc=0
            orq_estado_de "$dir/estado" "$clave" >/dev/null 2>&1 || rc=$?
            if [[ "$rc" -ne 0 ]]; then
                falta=1
            fi
        done
        if [[ "$falta" -eq 0 ]]; then
            ENSAYO_PREVIO="$dir"
        fi
    done < <(ensayos_previos)
}

verificar_identidad() {
    local previo_viejo previo_nuevo
    if solo_nuevo; then
        # Un solo servidor: su identidad queda en el estado (y --post-corte la compara con la del corte).
        leer_machine_id NUEVO "$RAIZ_NUEVO" MACHINE_ID_NUEVO
        return 0
    fi
    leer_machine_id VIEJO "$RAIZ_VIEJO" MACHINE_ID_VIEJO
    leer_machine_id NUEVO "$RAIZ_NUEVO" MACHINE_ID_NUEVO
    # A9: los dos hosts tienen que ser servidores distintos; el nombre del .env no prueba nada.
    if [[ "$MACHINE_ID_VIEJO" == "$MACHINE_ID_NUEVO" ]]; then
        abortar 9 "los dos hosts tienen el mismo /etc/machine-id: son el mismo servidor. No se tocó nada."
    fi
    # Un corte contra otros servidores que los del último ensayo no es el que se ensayó. (Al continuar,
    # la referencia es el estado de la propia corrida.)
    if [[ "$MODO" == "corte" && -z "$CONTINUAR" ]]; then
        ensayo_previo_con machine_id_viejo machine_id_nuevo
        if [[ -n "$ENSAYO_PREVIO" ]]; then
            previo_viejo="$(orq_estado_de "$ENSAYO_PREVIO/estado" machine_id_viejo)"
            previo_nuevo="$(orq_estado_de "$ENSAYO_PREVIO/estado" machine_id_nuevo)"
            if [[ "$previo_viejo" != "$MACHINE_ID_VIEJO" || "$previo_nuevo" != "$MACHINE_ID_NUEVO" ]]; then
                abortar 3 "el último ensayo ($ENSAYO_PREVIO) se hizo contra otros servidores que los de estos .env: repetí el ensayo contra este par antes del corte."
            fi
        fi
    fi
}

# ---------------------------------------------------------------------------------------
# Corridas sin terminar (spec 6.3.3)
# ---------------------------------------------------------------------------------------

# Una corrida cuenta como sin terminar solo si tiene archivo de estado Y no tiene resultado=: quien
# pierde el lock deja una corrida sin estado, y contarla bloquearía todos los cortes. Las de
# --revertir no cuentan: revertir es idempotente y se re-corre.
verificar_corridas_sin_terminar() {
    local base="$HOME/stockapp-backups-vps/corridas" dir rc sin_terminar=()
    for dir in "$base"/*/; do
        dir="${dir%/}"
        [[ -d "$dir" && -f "$dir/estado" ]] || continue
        case "${dir##*/}" in
            revertir-*) continue ;;
        esac
        rc=0
        orq_estado_de "$dir/estado" resultado >/dev/null 2>&1 || rc=$?
        # 0 = terminada. 1 = sin resultado. 2 = no se pudo leer: tampoco se puede decir que terminó.
        if [[ "$rc" -ne 0 ]]; then
            sin_terminar+=("$dir")
        fi
    done
    if [[ "${#sin_terminar[@]}" -gt 0 ]]; then
        {
            echo "migrar: hay corridas sin terminar; no se empieza otra:"
            for dir in "${sin_terminar[@]}"; do
                echo "  $dir"
                echo "    deploy/migrar-vps.sh --continuar $dir"
            done
            echo "  o, para una de corte o de ensayo, deshacer el congelamiento si lo hubo:"
            echo "    deploy/migrar-vps.sh --revertir"
            # R58: en un desastre la corrida abierta suele ser la de un servidor que ya no existe, y ahí
            # --continuar y --revertir salen 8 o 9 por el machine-id. La única salida es cerrarla a mano.
            if [[ "$MODO" == "desde-backup" ]]; then
                echo "  Si el servidor de esa corrida ya no existe (el viejo murió después de CONGELAR, o el nuevo se"
                echo "  reaprovisionó después de revertir a mano), --continuar y --revertir salen 8 o 9 por el machine-id."
                echo "  Cerrala a mano con la receta de deploy/MIGRACION-VPS.md, sección 6:"
                echo "  «Cerrar a mano una corrida cuyo servidor ya no existe»."
                echo "  Para cada corrida de arriba que esté en ese caso:"
                for dir in "${sin_terminar[@]}"; do
                    echo "    printf 'resultado=ABANDONADA-MANUAL\\n' >> $dir/estado"
                done
                echo "  Nunca si el VPS viejo sigue vivo y congelado: para eso es deploy/migrar-vps.sh --revertir."
            fi
        } >&2
        if [[ "$MODO" == "desde-backup" ]]; then
            abortar 3 "una corrida sin terminar tiene que retomarse (--continuar), revertirse (--revertir) o, si su servidor ya no existe, cerrarse a mano (deploy/MIGRACION-VPS.md, sección 6) antes de empezar otra. No se tocó nada."
        fi
        abortar 3 "una corrida sin terminar tiene que retomarse (--continuar) o revertirse (--revertir) antes de empezar otra. No se tocó nada."
    fi
}

# ---------------------------------------------------------------------------------------
# Pre-vuelo (solo lectura)
# ---------------------------------------------------------------------------------------

VERSION_NUEVO=""
TAMANO_BASE_VIEJO=0
TAMANO_BASE_NUEVO=0
TOKEN=""
CODIGO_HTTP=""
CURL_RC=0
CURL_CUERPO=""
CURL_ERR=""

# curl_operador <url>: GET desde esta máquina validando el certificado de verdad (nunca se lo desactiva). Deja
# CURL_RC, CURL_CUERPO y CURL_ERR; nunca sale. El stderr va a la corrida (700).
curl_operador() {
    _curl_operador 0 "" "$1"
}

# curl_operador_con <entrada> <opciones>... <url>: lo mismo, con <entrada> por el stdin de curl (el
# cuerpo del login con --data @-, o la configuración con el token con -K -): un secreto nunca va en
# el argv. La URL va siempre última.
curl_operador_con() {
    local entrada="$1"
    shift
    _curl_operador 1 "$entrada" "$@"
}

_curl_operador() {
    local tiene_entrada="$1" entrada="$2" err
    shift 2
    CURL_RC=0
    CURL_CUERPO=""
    CURL_ERR=""
    if ! orq_tmp_dir || ! err="$(mktemp "$ORQ_TMP_DIR/curl-stderr.XXXXXX")"; then
        CURL_RC=255
        CURL_ERR="no pude crear el temporal para el stderr de curl"
        return 0
    fi
    if [[ "$tiene_entrada" == 1 ]]; then
        CURL_CUERPO="$(curl -fsS --connect-timeout 10 --max-time 30 "$@" 9>&- 2>"$err" < <(printf '%s' "$entrada"))" || CURL_RC=$?
    else
        CURL_CUERPO="$(curl -fsS --connect-timeout 10 --max-time 30 "$@" 9>&- 2>"$err" </dev/null)" || CURL_RC=$?
    fi
    CURL_ERR="$(cat "$err" 2>/dev/null)" || CURL_ERR=""
    rm -f -- "$err"
}

# codigo_http: el código HTTP que curl -f deja en su stderr ("returned error: 401"), o vacío.
codigo_http() {
    CODIGO_HTTP=""
    if [[ "$ORQ_STDERR" =~ returned\ error:\ ([0-9]{3}) ]]; then
        CODIGO_HTTP="${BASH_REMATCH[1]}"
    fi
}

# tamano_base <PREFIJO> <contenedor> <docker> <usuario> <base> -> TAMANO_BASE (bytes)
TAMANO_BASE=0
tamano_base() {
    local p="$1" contenedor="$2" docker="$3" usuario="$4" base="$5"
    orq_ssh "$p" "${docker}exec -i ${contenedor} psql -U ${usuario} -d ${base} -tA -v ON_ERROR_STOP=1" \
        "SELECT pg_database_size(current_database());"
    consulta_ok "el tamaño de la base ($p)"
    if [[ ! "$ORQ_SALIDA" =~ ^[0-9]{1,18}$ ]]; then
        abortar 8 "no pude interpretar el tamaño de la base ($p): la respuesta no es un número."
    fi
    TAMANO_BASE="$ORQ_SALIDA"
}

# espacio_libre <PREFIJO> <comando-df> -> LIBRE_BYTES (columna Available de df -P, en bloques de 1 KiB)
LIBRE_BYTES=0
espacio_libre() {
    local p="$1" comando="$2" linea libre rc=0
    orq_ssh "$p" "$comando"
    consulta_ok "el espacio en disco ($p)"
    linea="$(printf '%s\n' "$ORQ_SALIDA" | sed -n '2p')"
    read -r _ _ _ libre _ <<< "$linea" || rc=$?
    if [[ ! "${libre:-}" =~ ^[0-9]{1,15}$ ]]; then
        abortar 8 "no pude interpretar el df del $p: ${linea:-(vacío)}"
    fi
    LIBRE_BYTES=$((libre * 1024))
}

prevuelo_viejo() {
    local unit="${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service"

    orq_ssh VIEJO "sudo -n true"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        consulta_ok "el acceso por ssh al VPS viejo"
    elif [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar 3 "'sudo -n true' falló en el VPS viejo: $VIEJO_VPS_USER necesita sudo sin contraseña."
    fi

    orq_ssh VIEJO "systemctl is-active stockapp-api"
    case "$ORQ_RC" in
        0 | 3) ;;
        *) consulta_ok "el estado de stockapp-api en el VPS viejo" ;;
    esac
    case "$ORQ_SALIDA" in
        active)
            if [[ "$ORQ_RC" -ne 0 ]]; then
                abortar 8 "stockapp-api dice active pero el código de systemctl no coincide: no puedo interpretarlo."
            fi
            ;;
        inactive | failed | activating | deactivating | reloading)
            abortar 3 "stockapp-api en el VPS viejo no está active (dice '$ORQ_SALIDA'): no hay nada que migrar sin una API sirviendo."
            ;;
        *) abortar 8 "no pude interpretar 'systemctl is-active stockapp-api' del VPS viejo." ;;
    esac

    orq_ssh VIEJO "systemctl show -p FragmentPath stockapp-api"
    consulta_ok "la ruta de la unit de stockapp-api en el VPS viejo"
    if [[ ! "$ORQ_SALIDA" =~ ^FragmentPath=.*$ ]]; then
        abortar 8 "no pude interpretar 'systemctl show -p FragmentPath': ${ORQ_SALIDA:-(vacío)}"
    fi
    if [[ "$ORQ_SALIDA" != "FragmentPath=$unit" ]]; then
        abortar 3 "la unit stockapp-api del VPS viejo no es '$unit' (FragmentPath dice '${ORQ_SALIDA#FragmentPath=}'): el congelamiento no podría moverla. Se sabe ahora, antes de la indisponibilidad."
    fi

    orq_ssh VIEJO "test -e ${unit}.congelado"
    if [[ "$ORQ_RC" -eq 0 ]]; then
        abortar 3 "ya existe ${unit}.congelado en el VPS viejo: un congelamiento anterior sin deshacer. Revisalo (--revertir) antes de empezar."
    elif [[ "$ORQ_RC" -ne 1 || -n "$ORQ_STDERR" ]]; then
        consulta_ok "si existe ${unit}.congelado"
        abortar 8 "no pude consultar si existe ${unit}.congelado."
    fi

    orq_ssh VIEJO "docker ps --format '{{.Names}}'"
    consulta_ok "la lista de contenedores del VPS viejo"
    if [[ -z "$ORQ_SALIDA" ]]; then
        abortar 8 "la lista de contenedores del VPS viejo vino vacía: no puedo interpretarla."
    fi
    if ! contiene_linea "$ORQ_SALIDA" "$CONTENEDOR_VIEJO"; then
        abortar 3 "el contenedor $CONTENEDOR_VIEJO no está corriendo en el VPS viejo."
    fi

    tamano_base VIEJO "$CONTENEDOR_VIEJO" "docker " "$VIEJO_POSTGRES_USER" "$VIEJO_POSTGRES_DB"
    TAMANO_BASE_VIEJO="$TAMANO_BASE"
    espacio_libre VIEJO "docker exec ${CONTENEDOR_VIEJO} df -P /var/lib/postgresql/data"
    if [[ "$LIBRE_BYTES" -lt "$TAMANO_BASE_VIEJO" ]]; then
        abortar 3 "no alcanza el espacio en disco del contenedor del VPS viejo: la base pesa $TAMANO_BASE_VIEJO bytes y quedan $LIBRE_BYTES para el dump."
    fi

    echo "  - viejo: stockapp-api active, unit en su lugar, sin .congelado, $CONTENEDOR_VIEJO corriendo, base de $TAMANO_BASE_VIEJO bytes con espacio para el dump"
}

# ayuda_feed_ausente: qué hacer cuando el nuevo no sirve el feed (R56). El caso normal es el reintento
# después de un --revertir, que deja la API nueva detenida y deshabilitada (R44); preparar-vps-nuevo.sh no
# la levanta si la unit existe, así que mandar solo a preparar deja al operador en un bucle.
ayuda_feed_ausente() {
    printf '%s' "Lo sirve la API nueva: si venís de un --revertir, la API nueva está detenida y deshabilitada. Levantala (reinstalá la misma versión, sin tocar la base) con STOCKAPP_ENV_LOCAL=${ENV_NUEVO} deploy/deploy-vps.sh <v> --sin-backup contra el nuevo y comprobá con preparar-vps-nuevo.sh --verificar (deploy/MIGRACION-VPS.md, sección 6, «Después de un --revertir»). Si el nuevo nunca terminó de prepararse, terminá preparar-vps-nuevo.sh. No se tocó nada."
}

# leer_version_del_feed: la mayor versión Full que sirve el feed del VPS nuevo -> VERSION_NUEVO.
leer_version_del_feed() {
    local url="${NUEVO_API_SCHEME}://${NUEVO_API_HOSTNAME}:${NUEVO_API_PORT}/updates/releases.win.json" mayor rc=0
    curl_operador "$url"
    case "$CURL_RC" in
        0) ;;
        22 | 7)
            if [[ "$SIN_VERIFICAR_PREPARACION" -eq 1 ]]; then
                VERSION_NUEVO="desconocida"
                return 0
            fi
            abortar 3 "el VPS nuevo no sirve el feed de actualizaciones ($url). $(ayuda_feed_ausente)"
            ;;
        *)
            if [[ "$SIN_VERIFICAR_PREPARACION" -eq 1 ]]; then
                VERSION_NUEVO="desconocida"
                return 0
            fi
            abortar 8 "no pude consultar el feed del VPS nuevo (curl salió con $CURL_RC). ${CURL_ERR}"
            ;;
    esac
    mayor="$(printf '%s' "$CURL_CUERPO" | jq -r '.Assets[] | select(.Type == "Full") | .Version' | sort -V | tail -n 1)" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        abortar 8 "el feed que sirve el VPS nuevo no es un releases.win.json válido."
    fi
    if [[ -z "$mayor" ]]; then
        abortar 3 "el feed del VPS nuevo no tiene ninguna versión Full. $(ayuda_feed_ausente)"
    fi
    if [[ ! "$mayor" =~ ^[0-9]+(\.[0-9]+){1,3}([-+][0-9A-Za-z.-]+)?$ ]]; then
        abortar 8 "la versión que sirve el feed del VPS nuevo ('$mayor') no tiene forma de versión."
    fi
    VERSION_NUEVO="$mayor"
}

prevuelo_nuevo() {
    leer_version_del_feed
    # El feed lo sirve la API nueva, y entre el paso 3 (la detiene) y el 11 (la arranca) no contesta:
    # --continuar no puede volver a leerlo. La versión queda en el estado de la corrida.
    if [[ -n "${ORQ_CORRIDA:-}" && -d "${ORQ_CORRIDA}" && "$VERSION_NUEVO" != "desconocida" ]]; then
        orq_estado_poner version_nuevo "$VERSION_NUEVO"
    fi

    if [[ "$SIN_VERIFICAR_PREPARACION" -eq 1 ]]; then
        echo "  - preparación del nuevo: OMITIDO (arnés)"
    else
        echo "  - preparación del nuevo (preparar-vps-nuevo.sh --verificar, versión $VERSION_NUEVO):"
        # También en --desde-backup: el --env-viejo es local y trae la clave pública de licencias.
        orq_bloque deploy/preparar-vps-nuevo.sh "$ENV_NUEVO" --version "$VERSION_NUEVO" --env-viejo "$ENV_VIEJO" --verificar
        echo "  bloque preparar-vps-nuevo.sh salió con $ORQ_RC_BLOQUE"
        case "$ORQ_RC_BLOQUE" in
            0) ;;
            8) abortar 8 "preparar-vps-nuevo.sh --verificar no pudo consultar el VPS nuevo." ;;
            9) abortar 9 "preparar-vps-nuevo.sh --verificar dice que el destino no es un VPS dedicado (alcance)." ;;
            *) abortar 3 "el VPS nuevo no está listo (preparar-vps-nuevo.sh --verificar salió con $ORQ_RC_BLOQUE): correr preparar-vps-nuevo.sh antes del corte." ;;
        esac
    fi

    tamano_base NUEVO "$CONTENEDOR_NUEVO" "sudo -n docker " "$NUEVO_POSTGRES_USER" "$NUEVO_POSTGRES_DB"
    TAMANO_BASE_NUEVO="$TAMANO_BASE"
    # El volumen de Postgres recibe la base restaurada; el directorio de la migración recibe el dump,
    # el historial y la copia de seguridad de la base que se reemplaza.
    espacio_libre NUEVO "sudo -n docker exec ${CONTENEDOR_NUEVO} df -P /var/lib/postgresql/data"
    if [[ "$LIBRE_BYTES" -lt "$TAMANO_BASE_VIEJO" ]]; then
        abortar 3 "no alcanza el espacio en disco del volumen de Postgres del VPS nuevo: la base vieja pesa $TAMANO_BASE_VIEJO bytes y quedan $LIBRE_BYTES."
    fi
    espacio_libre NUEVO 'df -P "$HOME/stockapp-deploy/migracion"'
    if [[ "$LIBRE_BYTES" -lt $((TAMANO_BASE_VIEJO + TAMANO_BASE_NUEVO)) ]]; then
        abortar 3 "no alcanza el espacio en disco del VPS nuevo para el dump y la copia de seguridad: hacen falta $((TAMANO_BASE_VIEJO + TAMANO_BASE_NUEVO)) bytes y quedan $LIBRE_BYTES."
    fi
    echo "  - nuevo: feed en la versión $VERSION_NUEVO, base de $TAMANO_BASE_NUEVO bytes, espacio para el dump y la copia de seguridad"
}

# comparar_overrides <archivo>: los nombres de <archivo> que faltan en el .env del servidor nuevo
# (OVERRIDES_FALTAN) y los que están en el .env pero no en /etc/stockapp-api/api.env (OVERRIDES_SIN_INSTALAR:
# solo install.sh copia el uno al otro y este script nunca lo corre, así que un override solo en el .env no
# llega a la API; R57). Además OVERRIDES_CANTIDAD. Sale 0, 3 si una línea no es un nombre de variable, u 8
# si no pudo leer las claves (ORQ_RC dice con qué salió). Solo los NOMBRES cruzan la red: los dos sed
# imprimen lo que está antes del "=" y nada más.
OVERRIDES_FALTAN=""
OVERRIDES_SIN_INSTALAR=""
OVERRIDES_CANTIDAD=0
comparar_overrides() {
    local archivo="$1" nombre comando claves_env claves_api
    OVERRIDES_FALTAN=""
    OVERRIDES_SIN_INSTALAR=""
    OVERRIDES_CANTIDAD=0
    comando='sed -n '\''s/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p'\'' "$HOME/stockapp-deploy/.env"'
    orq_ssh NUEVO "$comando"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        return 8
    fi
    claves_env="$ORQ_SALIDA"
    comando='sudo -n sed -n '\''s/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p'\'' "'"${RAIZ_NUEVO}"'/etc/stockapp-api/api.env"'
    orq_ssh NUEVO "$comando"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        return 8
    fi
    claves_api="$ORQ_SALIDA"
    while IFS= read -r nombre || [[ -n "$nombre" ]]; do
        [[ -n "$nombre" ]] || continue
        if [[ ! "$nombre" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
            return 3
        fi
        OVERRIDES_CANTIDAD=$((OVERRIDES_CANTIDAD + 1))
        if ! contiene_linea "$claves_env" "$nombre"; then
            OVERRIDES_FALTAN+="${OVERRIDES_FALTAN:+, }$nombre"
        elif ! contiene_linea "$claves_api" "$nombre"; then
            OVERRIDES_SIN_INSTALAR+="${OVERRIDES_SIN_INSTALAR:+, }$nombre"
        fi
    done < "$archivo"
}

prevuelo_overrides() {
    local archivo="" dir sub rc=0 version_reinstalar
    while IFS= read -r dir; do
        [[ -n "$dir" ]] || continue
        # La carpeta del volcado del ensayo: volcado/ o la del último reintento (volcado_dir).
        sub="$(orq_estado_de "$dir/estado" volcado_dir 2>/dev/null)" || sub="volcado"
        if [[ "$sub" =~ ^volcado(-[0-9]+)?$ && -f "$dir/$sub/overrides-api-env.txt" ]]; then
            archivo="$dir/$sub/overrides-api-env.txt"
        fi
    done < <(ensayos_previos)
    if [[ -z "$archivo" ]]; then
        echo "  - overrides: no hay un ensayo anterior con lista de overrides; el primer ensayo la deja al final."
        return 0
    fi
    comparar_overrides "$archivo" || rc=$?
    case "$rc" in
        0) ;;
        3) abortar 3 "$archivo tiene una línea que no es un nombre de variable." ;;
        *) consulta_ok "las claves del .env del VPS nuevo" ;;
    esac
    if [[ -n "$OVERRIDES_FALTAN" ]]; then
        abortar 3 "faltan en el .env del servidor nuevo (~/stockapp-deploy/.env) los overrides del VPS viejo: $OVERRIDES_FALTAN. Cargalos a mano (solo se muestran los nombres)."
    fi
    if [[ -n "$OVERRIDES_SIN_INSTALAR" ]]; then
        version_reinstalar="<v>"
        if [[ -n "$VERSION_NUEVO" && "$VERSION_NUEVO" != "desconocida" ]]; then
            version_reinstalar="$VERSION_NUEVO"
        fi
        abortar 3 "los overrides del VPS viejo $OVERRIDES_SIN_INSTALAR están en ~/stockapp-deploy/.env del servidor nuevo pero no en /etc/stockapp-api/api.env, así que la API no los tiene (este script nunca corre install.sh): reinstalá con deploy/deploy-vps.sh ${version_reinstalar} --sin-backup contra el VPS nuevo (STOCKAPP_ENV_LOCAL=<.env del nuevo>)."
    fi
    echo "  - overrides: los $OVERRIDES_CANTIDAD del último ensayo están en el .env del servidor nuevo y en su api.env"
}

# obtener_contrasena: la de VPS_ADMIN_PASSWORD (entorno o .env del viejo) o, si no, se pide: por
# terminal sin eco o, en el arnés, de stdin ANTES de la palabra de confirmación.
obtener_contrasena() {
    local rc=0
    if [[ -n "$PW" ]]; then
        return 0
    fi
    if [[ "$ORQ_ENSAYO" -eq 1 ]]; then
        IFS= read -r PW || rc=$?
    elif { : </dev/tty; } 2>/dev/null; then
        printf 'Contraseña de %s (no se muestra): ' "$USUARIO_VERIFICACION" >/dev/tty
        IFS= read -rs PW </dev/tty || rc=$?
        printf '\n' >/dev/tty
    else
        abortar 3 "falta VPS_ADMIN_PASSWORD (entorno o $ENV_CREDENCIAL) y no hay una terminal para pedirla."
    fi
    if [[ -z "$PW" ]]; then
        abortar 3 "falta VPS_ADMIN_PASSWORD (entorno, $ENV_CREDENCIAL o terminal): no puedo validar la credencial."
    fi
}

# prevuelo_credencial: login contra la API VIEJA por ssh al loopback (la contraseña va en el cuerpo,
# por stdin, nunca en el comando) y GET /backups (solo lectura). El login no escribe en la base.
# Mismo patrón que bajar-backup-vps.sh: curl -f sale 22 ante un 4xx/5xx y el código HTTP viene en su
# stderr; solo 401/403 son una credencial rechazada, cualquier otra cosa es "no pude consultar".
prevuelo_credencial() {
    local cuerpo rc=0 listado cantidad
    obtener_contrasena

    cuerpo="$(PW="$PW" jq -nc --arg u "$VIEJO_VPS_ADMIN_USER" '{nombreUsuario: $u, contrasena: env.PW}')" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        abortar 8 "no pude armar el cuerpo del login."
    fi
    orq_ssh VIEJO "curl -fsS ${RESOLVE_VIEJO}-X POST ${URL_VIEJO}/auth/login -H 'Content-Type: application/json' --data @-" "$cuerpo"
    cuerpo=""
    case "$ORQ_RC" in
        0) ;;
        22)
            codigo_http
            volcar_stderr
            if [[ "$CODIGO_HTTP" == "401" || "$CODIGO_HTTP" == "403" ]]; then
                abortar 3 "la API del VPS viejo rechazó el login de '$VIEJO_VPS_ADMIN_USER' (HTTP $CODIGO_HTTP). Revisá VPS_ADMIN_USER y VPS_ADMIN_PASSWORD. No se tocó nada."
            fi
            abortar 8 "el login falló con ${CODIGO_HTTP:-un código HTTP que no pude leer}: no es un problema de credenciales."
            ;;
        *)
            volcar_stderr
            abortar 8 "no pude hacer el login en la API del VPS viejo (salió con $ORQ_RC)."
            ;;
    esac
    rc=0
    TOKEN="$(printf '%s' "$ORQ_SALIDA" | jq -r '.token // empty')" || rc=$?
    ORQ_SALIDA=""
    if [[ "$rc" -ne 0 || -z "$TOKEN" ]]; then
        abortar 8 "la respuesta del login no trae un token legible."
    fi

    # El token viaja solo por el stdin de curl -K -, nunca en el comando.
    listado="$(printf 'header = "Authorization: Bearer %s"' "$TOKEN")"
    TOKEN=""
    orq_ssh VIEJO "curl -fsS ${RESOLVE_VIEJO}${URL_VIEJO}/backups -K -" "$listado"
    listado=""
    case "$ORQ_RC" in
        0) ;;
        22)
            codigo_http
            volcar_stderr
            if [[ "$CODIGO_HTTP" == "403" ]]; then
                abortar 3 "'$VIEJO_VPS_ADMIN_USER' no tiene permiso para GET /backups (GestionarDiagnostico): hace falta un usuario que lo tenga para verificar el corte. No se tocó nada."
            fi
            abortar 8 "GET /backups falló con ${CODIGO_HTTP:-un código HTTP que no pude leer}."
            ;;
        *)
            volcar_stderr
            abortar 8 "no pude consultar GET /backups en la API del VPS viejo (salió con $ORQ_RC)."
            ;;
    esac
    rc=0
    cantidad="$(printf '%s' "$ORQ_SALIDA" | jq -e 'if type == "array" then length else error("no es una lista") end')" || rc=$?
    ORQ_SALIDA=""
    if [[ "$rc" -ne 0 || ! "$cantidad" =~ ^[0-9]+$ ]]; then
        abortar 8 "GET /backups no devolvió una lista JSON legible."
    fi
    echo "  - credencial: login de '$VIEJO_VPS_ADMIN_USER' y GET /backups OK ($cantidad corridas)"
}

prevuelo() {
    prevuelo_viejo
    prevuelo_nuevo
    prevuelo_overrides
    prevuelo_credencial
}

# ---------------------------------------------------------------------------------------
# --desde-backup: el backup del operador (spec 6.5.7)
# ---------------------------------------------------------------------------------------

BACKUP=""
BACKUP_BYTES=0
FECHA_BACKUP=""
SHA_HERMANO_TXT=""
MANIFIESTO_RUTA=""
MANIFIESTO_TXT=""
NOMBRE_SEGURO='^[A-Za-z0-9][A-Za-z0-9._-]*$'

# validar_backup: fase A, solo en esta máquina (ningún servidor, ninguna corrida). El dump tiene que ser
# legible, de al menos 1024 bytes y aceptado por pg_restore --list, que además da la fecha del dump. Un
# pg_restore --list que pasa NO prueba que el dump esté completo (un -Fc truncado lo pasa): por eso el
# .sha256 hermano, si existe, y el manifiesto, si se da, tienen que coincidir con el sha256 del archivo.
validar_backup() {
    local nombre listado rc=0 lineas linea hash archivo_sha nombre_sha dump_m="" sha_m="" migr_m="" n_dump=0 n_sha=0 n_migr=0
    BACKUP="$(realpath -m -- "$DESDE_BACKUP")"
    nombre="${BACKUP##*/}"
    if [[ ! "$nombre" =~ $NOMBRE_SEGURO ]]; then
        abortar 2 "el nombre del dump ('$nombre') tiene caracteres que no viajan seguros en un comando remoto: renombralo a letras, números, '.', '_' y '-'."
    fi
    if [[ ! -e "$BACKUP" ]]; then
        abortar 3 "el dump $BACKUP no existe. No se tocó nada."
    fi
    if [[ ! -f "$BACKUP" || ! -r "$BACKUP" ]]; then
        abortar 3 "el dump $BACKUP no es un archivo que se pueda leer. No se tocó nada."
    fi
    BACKUP_BYTES="$(stat -c %s -- "$BACKUP")" || abortar 3 "no pude leer el tamaño de $BACKUP."
    if [[ "$BACKUP_BYTES" -lt 1024 ]]; then
        abortar 3 "el dump $BACKUP pesa $BACKUP_BYTES bytes (< 1024): no es un dump de StockApp. No se tocó nada."
    fi
    if ! command -v pg_restore >/dev/null 2>&1; then
        abortar 3 "falta 'pg_restore' en esta máquina: hace falta para comprobar el dump antes de restaurarlo."
    fi
    listado="$(pg_restore --list "$BACKUP" </dev/null 9>&-)" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        abortar 3 "pg_restore --list no puede leer $BACKUP (salió con $rc): no es un dump -Fc válido. No se tocó nada."
    fi
    # Sin pipe (un head que corta antes haría fallar la tubería bajo pipefail): la línea está al principio.
    FECHA_BACKUP=""
    while IFS= read -r linea; do
        if [[ "$linea" =~ ^\;\ Archive\ created\ at\ (.*[^ ])\ *$ ]]; then
            FECHA_BACKUP="${BASH_REMATCH[1]}"
            break
        fi
    done <<< "$listado"
    listado=""
    if [[ -z "$FECHA_BACKUP" || "$FECHA_BACKUP" == *$'\r'* ]]; then
        abortar 8 "pg_restore --list de $BACKUP no trae la línea 'Archive created at': no puedo decir de cuándo es el dump."
    fi
    if ! SHA_VOLCADO="$(sha256sum -- "$BACKUP")"; then
        abortar 3 "no pude calcular el sha256 de $BACKUP."
    fi
    SHA_VOLCADO="${SHA_VOLCADO%% *}"
    VOLCADO="$nombre"

    # El .sha256 que deja bajar-backup-vps.sh al lado del dump: una línea de sha256sum con ese nombre.
    archivo_sha="$BACKUP.sha256"
    if [[ -e "$archivo_sha" ]]; then
        lineas="$(cat -- "$archivo_sha" 2>/dev/null)" || abortar 3 "no pude leer $archivo_sha."
        linea="${lineas%%$'\n'*}"
        if [[ "$lineas" != "$linea" || ! "$linea" =~ ^([0-9a-f]{64})\ [\ *](.+)$ ]]; then
            abortar 3 "$archivo_sha no es una línea de sha256sum ('<sha256>  <archivo>'): no puedo verificar el dump. No se tocó nada."
        fi
        hash="${BASH_REMATCH[1]}"
        nombre_sha="${BASH_REMATCH[2]}"
        if [[ "$nombre_sha" != "$nombre" ]]; then
            abortar 3 "$archivo_sha es de '$nombre_sha', no de $nombre. No se tocó nada."
        fi
        if [[ "$hash" != "$SHA_VOLCADO" ]]; then
            abortar 3 "el sha256 de $BACKUP ($SHA_VOLCADO) no es el de $archivo_sha ($hash): el dump cambió o está incompleto. No se tocó nada."
        fi
        SHA_HERMANO_TXT="${nombre}.sha256 verificado"
    else
        SHA_HERMANO_TXT="sin .sha256 al lado: el sha256 no se compara con nada (pg_restore --list no prueba que el dump esté completo)"
    fi

    # El manifiesto, si se da: el de ESE dump (dump= y sha256=). Con él restaurar verifica los conteos.
    if [[ -n "$MANIFIESTO_DADO" ]]; then
        MANIFIESTO_RUTA="$(realpath -m -- "$MANIFIESTO_DADO")"
        MANIFIESTO="${MANIFIESTO_RUTA##*/}"
        if [[ ! "$MANIFIESTO" =~ $NOMBRE_SEGURO || "$MANIFIESTO" == "$nombre" ]]; then
            abortar 2 "el nombre del manifiesto ('$MANIFIESTO') tiene caracteres que no viajan seguros en un comando remoto (o es el del dump)."
        fi
        if [[ ! -f "$MANIFIESTO_RUTA" || ! -r "$MANIFIESTO_RUTA" ]]; then
            abortar 3 "el manifiesto $MANIFIESTO_RUTA no existe o no se puede leer. No se tocó nada."
        fi
        while IFS= read -r linea || [[ -n "$linea" ]]; do
            [[ "$linea" == "[conteos]" ]] && break
            case "$linea" in
                dump=*) dump_m="${linea#dump=}"; n_dump=$((n_dump + 1)) ;;
                sha256=*) sha_m="${linea#sha256=}"; n_sha=$((n_sha + 1)) ;;
                ultima_migracion=*) migr_m="${linea#ultima_migracion=}"; n_migr=$((n_migr + 1)) ;;
            esac
        done < "$MANIFIESTO_RUTA" || abortar 3 "no pude leer el manifiesto $MANIFIESTO_RUTA."
        if [[ "$n_dump" -ne 1 || "$n_sha" -ne 1 || "$n_migr" -ne 1 || ! "$migr_m" =~ ^[0-9]{14}_[A-Za-z0-9_]+$ ]]; then
            abortar 3 "el manifiesto $MANIFIESTO tiene que traer exactamente una vez dump=, sha256= y una ultima_migracion= legible."
        fi
        if [[ "$dump_m" != "$nombre" ]]; then
            abortar 3 "el manifiesto $MANIFIESTO es de dump=$dump_m, no de $nombre. No se tocó nada."
        fi
        if [[ "$sha_m" != "$SHA_VOLCADO" ]]; then
            abortar 3 "el manifiesto $MANIFIESTO dice sha256=$sha_m y el dump tiene $SHA_VOLCADO: no es el mismo dump. No se tocó nada."
        fi
        ULTIMA_MIGRACION="$migr_m"
        MANIFIESTO_TXT="manifiesto $MANIFIESTO (dump= y sha256= coinciden; restaurar verifica también los conteos)"
    else
        MANIFIESTO=""
        MANIFIESTO_TXT="sin manifiesto: restaurar verifica la estructura y no los conteos; V6 no corre"
    fi
}

# R59: en --desde-backup los backup_*.dump y *.tmp del destino no son restos de un ensayo: pueden ser las
# copias más recientes y buenas (producción viva con la base corrupta). El pre-vuelo los lista (solo
# lectura), la confirmación RESTAURAR los nombra y el paso 4 los aparta en backups/legado-<ts>/: nunca se
# borran. La API los busca sin bajar de nivel (GetFiles), así que legado-<ts>/ no la confunde.
BACKUPS_DESTINO=""
listar_backups_destino() {
    local dir="${DIR_STOCKAPP_NUEVO}/backups"
    BACKUPS_DESTINO=""
    orq_ssh NUEVO "sudo -n test -d ${dir}"
    if [[ "$ORQ_RC" -eq 1 && -z "$ORQ_STDERR" ]]; then
        return 0
    fi
    if [[ "$ORQ_RC" -ne 0 || -n "$ORQ_STDERR" ]]; then
        consulta_ok "si existe ${dir} en el VPS nuevo"
        abortar 8 "no pude consultar si existe ${dir} en el VPS nuevo."
    fi
    orq_ssh NUEVO "sudo -n find ${dir} -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print"
    consulta_ok "los backups propios de ${dir} en el VPS nuevo"
    BACKUPS_DESTINO="$ORQ_SALIDA"
}

# cantidad_backups_destino -> por stdout, cuántos listó listar_backups_destino.
cantidad_backups_destino() {
    if [[ -z "$BACKUPS_DESTINO" ]]; then
        echo 0
    else
        printf '%s\n' "$BACKUPS_DESTINO" | grep -c .
    fi
}

prevuelo_desde_backup() {
    echo "  - backup: $BACKUP ($BACKUP_BYTES bytes, sha256 ${SHA_VOLCADO:0:12}…, creado $FECHA_BACKUP)"
    echo "  - $SHA_HERMANO_TXT"
    echo "  - $MANIFIESTO_TXT"
    # Sin la base de origen, el tamaño del dump es la única medida: una cota inferior de lo que ocupa
    # restaurado.
    TAMANO_BASE_VIEJO="$BACKUP_BYTES"
    prevuelo_nuevo
    listar_backups_destino
    echo "  - backups propios del destino: $(cantidad_backups_destino) (el paso 4 los aparta, no los borra)"
    # La credencial no se valida acá (no hay API vieja): se pide ahora para no interrumpir después, y la
    # valida V5 contra la API nueva.
    obtener_contrasena
    echo "  - credencial: '$USUARIO_VERIFICACION' (se valida en V5, contra la API nueva)"
}

confirmacion_desde_backup() {
    local texto propios n
    n="$(cantidad_backups_destino)"
    if [[ "$n" -eq 0 ]]; then
        propios="En ${DIR_STOCKAPP_NUEVO}/backups del nuevo no hay backups propios (backup_*.dump, *.tmp) que apartar."
    else
        propios="En ${DIR_STOCKAPP_NUEVO}/backups del nuevo hay ${n} backups propios. NO se borran: se apartan en ${DIR_STOCKAPP_NUEVO}/backups/legado-<ts>/, donde la API no los ve:
$(printf '%s\n' "$BACKUPS_DESTINO" | sed 's#^.*/#    - #')"
    fi
    texto="$(
        cat <<EOF

== Confirmación ==
Vas a RESTAURAR un backup en el VPS nuevo (recuperación ante desastres; el VPS viejo no se toca).
  backup: ${BACKUP} (creado ${FECHA_BACKUP}, sha256 ${SHA_VOLCADO:0:12}…)
  nuevo: ${NUEVO_VPS_HOST} (machine-id ${MACHINE_ID_NUEVO:0:8}): versión instalada ${VERSION_NUEVO}; su base se reemplaza (con copia de seguridad).
  ${propios}
Atención: todo lo posterior a ${FECHA_BACKUP} se pierde (lo que se escribió después de ese backup no está en él).
Para continuar escribí exactamente RESTAURAR y Enter. Cualquier otra cosa cancela sin tocar nada:
EOF
    )"
    orq_confirmar RESTAURAR "$texto"
}

# ---------------------------------------------------------------------------------------
# Confirmación (A12)
# ---------------------------------------------------------------------------------------

# indisponibilidad_del_ultimo_ensayo: "N min M s" del último ensayo que registró el reloj, o por qué no hay dato.
indisponibilidad_del_ultimo_ensayo() {
    local desde hasta a b segundos
    ensayo_previo_con congelado_utc api_nueva_activa_utc
    if [[ -z "$ENSAYO_PREVIO" ]]; then
        printf 'sin ensayo previo con el reloj de indisponibilidad'
        return 0
    fi
    desde="$(orq_estado_de "$ENSAYO_PREVIO/estado" congelado_utc)"
    hasta="$(orq_estado_de "$ENSAYO_PREVIO/estado" api_nueva_activa_utc)"
    if ! a="$(date -u -d "$desde" +%s 2>/dev/null)" || ! b="$(date -u -d "$hasta" +%s 2>/dev/null)" || [[ "$b" -lt "$a" ]]; then
        printf 'sin dato legible en %s' "$ENSAYO_PREVIO"
        return 0
    fi
    segundos=$((b - a))
    printf '%d min %d s (%s)' $((segundos / 60)) $((segundos % 60)) "${ENSAYO_PREVIO##*/}"
}

# comandos_de_vuelta_atras: los cuatro comandos de R-A, en el VPS viejo, tal como los dejan los bloques.
comandos_de_vuelta_atras() {
    cat <<EOF
    sudo systemctl unmask stockapp-api
    sudo mv ${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service.congelado ${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service
    sudo systemctl daemon-reload
    sudo systemctl start stockapp-api
EOF
}

confirmacion() {
    local texto
    texto="$(
        cat <<EOF

== Confirmación ==
Vas a CONGELAR el VPS viejo y llevar sus datos al VPS nuevo.
  viejo: ${VIEJO_VPS_HOST} (machine-id ${MACHINE_ID_VIEJO:0:8}): stockapp-api se detiene y su unit se enmascara.
  nuevo: ${NUEVO_VPS_HOST} (machine-id ${MACHINE_ID_NUEVO:0:8}): versión instalada ${VERSION_NUEVO}; su base se reemplaza (con copia de seguridad).
Desde el congelamiento hasta que la API nueva responda, nadie puede usar el sistema.
  Indisponibilidad del último ensayo: $(indisponibilidad_del_ultimo_ensayo)
Para volver atrás en el VPS viejo (R-A), estos son los cuatro comandos; el mismo efecto tiene deploy/migrar-vps.sh --revertir:
$(comandos_de_vuelta_atras)
Para continuar escribí exactamente CONGELAR y Enter. Cualquier otra cosa cancela sin tocar nada:
EOF
    )"
    orq_confirmar CONGELAR "$texto"
}

# ---------------------------------------------------------------------------------------
# Secuencia del corte (spec 6.5.4): pasos 3 a 13
# ---------------------------------------------------------------------------------------

PASO_NOMBRES=(
    ""
    "pre-vuelo"
    "confirmación"
    "detener la API nueva"
    "limpiar restos del ensayo"
    "lista de contenedores del VPS viejo"
    "congelar y volcar el VPS viejo"
    "identificar el volcado de esta corrida"
    "transferencia al VPS nuevo"
    "restauración (--recrear-base)"
    "historial de backups"
    "arrancar la API nueva"
    "verificación (V1-V8, V10)"
    "resumen"
)
# El contrato de paso_<n> en el estado (ver el encabezado): ok u omitido, la hora UTC y los segundos.
FORMA_DE_PASO='^(ok|omitido) [0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z [0-9]+$'
ARRANQUE_ESPERA=120
FECHA_TOLERANCIA=300
BACKUP_ESPERA=60

DIR_STOCKAPP_NUEVO=""
D_REL=""
VOLCADO_DIR="volcado"
VOLCADO=""
MANIFIESTO=""
SHA_VOLCADO=""
ULTIMA_MIGRACION=""
HABILITACION_VIEJO=""
RESTAURAR_RC=""

# omitir_paso <n> <motivo>: un paso que este modo no tiene (paso_<n>=omitido, ver el contrato).
omitir_paso() {
    abrir_paso "$1" "${PASO_NOMBRES[$1]}"
    cerrar_paso "$1" OMITIDO "$2" omitido
}

paso() {
    abrir_paso "$1" "${PASO_NOMBRES[$1]}"
    DETALLE_FALLO=""
    SALIDA_SUGERIDA="continuar"
    inyectar_fallo "$1"
}

# inyectar_fallo <n>: solo arnés (STOCKAPP_ENSAYO_FALLAR_EN, rechazada con hosts reales). Antes de
# correr el paso <n>, la primera vez en la corrida, lo marca FALLÓ con la clase que le corresponde.
inyectar_fallo() {
    local n="$1" rc=0
    if [[ "${STOCKAPP_ENSAYO_FALLAR_EN-}" != "$n" ]]; then
        return 0
    fi
    orq_estado_leer "fallo_inyectado_$n" >/dev/null 2>&1 || rc=$?
    case "$rc" in
        0) return 0 ;;
        1) ;;
        *) abortar 8 "no pude leer fallo_inyectado_$n del estado de la corrida." ;;
    esac
    orq_estado_poner "fallo_inyectado_$n" 1
    DETALLE_FALLO="fallo inyectado (arnés)"
    case "$n" in
        3 | 4 | 5 | 6) abortar 5 "fallo inyectado en el paso $n (arnés), antes de congelar el viejo." ;;
        9)
            orq_estado_poner restaurar_salio 5
            abortar 6 "fallo inyectado en el paso 9 (arnés), como restaurar-backup.sh con 5: la base del nuevo quedó VACÍA. --continuar reintenta la restauración tal cual."
            ;;
        11) abortar 7 "fallo inyectado en el paso 11 (arnés): la API nueva no arrancó." ;;
        *) abortar 6 "fallo inyectado en el paso $n (arnés)." ;;
    esac
}

# reloj_desde_congelado: arranca el reloj de indisponibilidad (ORQ_CONGELADO_SEG, en el reloj
# monotónico de la lib) desde congelado_utc del estado. Al continuar, el congelamiento ocurrió en otro
# proceso: se descuenta lo que pasó desde entonces según el reloj de pared, y nunca queda negativo.
reloj_desde_congelado() {
    local congelado e ahora transcurrido inicio
    congelado="$(orq_estado_leer congelado_utc)" || return 0
    if ! e="$(date -u -d "$congelado" +%s 2>/dev/null)"; then
        return 0
    fi
    ahora="$(date -u +%s)"
    transcurrido=$((ahora - e))
    if [[ "$transcurrido" -lt 0 ]]; then
        transcurrido=0
    fi
    inicio=$(($(orq_reloj) - transcurrido))
    if [[ "$inicio" -lt 0 ]]; then
        inicio=0
    fi
    ORQ_CONGELADO_SEG="$inicio"
}

# falla_remota <codigo> <qué>: tras orq_ssh. El 255 de ssh es "no pude consultar" (8); cualquier otro
# código distinto de cero sale con <codigo>.
falla_remota() {
    local codigo="$1"
    shift
    if [[ "$ORQ_RC" -eq 255 ]]; then
        volcar_stderr
        abortar 8 "no pude ejecutar $* (ssh salió con 255). Esto NO es 'vacío' ni 'cero'."
    fi
    if [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        abortar "$codigo" "$* falló (salió con $ORQ_RC)."
    fi
}

# rr <archivo>: la ruta, entre comillas, de <archivo> en el directorio remoto de esta corrida. $HOME
# se expande en el servidor: el directorio es el de la migración del usuario de deploy.
rr() {
    printf '"$HOME/%s/%s"' "$D_REL" "$1"
}

# fila_de_codigo <bloque> <n>: la fila de <n> en la tabla del bloque copiada en este encabezado.
fila_de_codigo() {
    local fila
    fila="$(awk -v ini="# >>> CODIGOS $1" -v fin="# <<< CODIGOS $1" -v n="$2" '
        $0 == ini { dentro = 1; next }
        $0 == fin { dentro = 0 }
        dentro && index($0, "#   " n "  ") == 1 { fila = 1; print; next }
        dentro && fila && index($0, "#      ") == 1 { print; next }
        { fila = 0 }' "${BASH_SOURCE[0]}")" || fila=""
    if [[ -n "$fila" ]]; then
        printf '%s\n' "$fila" | sed 's/^#/   /'
    else
        echo "    (código fuera de la tabla del bloque: no se adivina qué significa)"
    fi
}

# informar_bloque <bloque> <n>: "bloque <nombre> salió con <n>" y su fila de la tabla (spec 6.2).
informar_bloque() {
    echo "  bloque ${1##*/} salió con $2"
    if [[ "$2" -ne 0 ]]; then
        fila_de_codigo "$1" "$2"
    fi
}

# estado_api <PREFIJO> -> ESTADO_API (la palabra de is-active). rc 0 y 3 son respuestas; lo demás,
# y una palabra que no es de systemd, es "no pude consultar".
ESTADO_API=""
estado_api() {
    orq_ssh "$1" "systemctl is-active stockapp-api"
    case "$ORQ_RC" in
        0 | 3) ;;
        *) consulta_ok "el estado de stockapp-api ($1)" ;;
    esac
    case "$ORQ_SALIDA" in
        active | inactive | failed | activating | deactivating | reloading) ESTADO_API="$ORQ_SALIDA" ;;
        *) abortar 8 "no pude interpretar 'systemctl is-active stockapp-api' ($1): '${ORQ_SALIDA}'." ;;
    esac
}

# habilitacion_viejo -> HABILITACION (la palabra de is-enabled del viejo; 0 y 1 son respuestas).
HABILITACION=""
habilitacion_viejo() {
    orq_ssh VIEJO "systemctl is-enabled stockapp-api"
    case "$ORQ_RC" in
        0 | 1) ;;
        *) consulta_ok "la habilitación de stockapp-api en el VPS viejo" ;;
    esac
    if [[ ! "$ORQ_SALIDA" =~ ^[a-z-]+$ ]]; then
        abortar 8 "no pude interpretar 'systemctl is-enabled stockapp-api' del VPS viejo: '${ORQ_SALIDA}'."
    fi
    HABILITACION="$ORQ_SALIDA"
}

journal_nuevo() {
    orq_ssh NUEVO "sudo -n journalctl -u stockapp-api -n 30 --no-pager"
    if [[ "$ORQ_RC" -eq 0 && -n "$ORQ_SALIDA" ]]; then
        echo "  últimas líneas del journal de stockapp-api en el VPS nuevo:"
        printf '%s\n' "$ORQ_SALIDA" | sed 's/^/    | /'
    else
        echo "  (no pude leer el journal de stockapp-api en el VPS nuevo: salió con $ORQ_RC)"
    fi
}

corte_paso3() {
    paso 3
    orq_ssh NUEVO "sudo -n systemctl stop stockapp-api"
    falla_remota 5 "'systemctl stop stockapp-api' en el VPS nuevo"
    estado_api NUEVO
    # failed también es "no corre": ningún proceso de la API nueva puede escribir en la base.
    if [[ "$ESTADO_API" != "inactive" && "$ESTADO_API" != "failed" ]]; then
        abortar 5 "tras el stop, stockapp-api del VPS nuevo dice '$ESTADO_API' (se esperaba inactive). El viejo no se tocó."
    fi
    cerrar_paso 3 OK
}

# apartar_backups_destino <dir>: R59. Mueve (mv -n, nunca un borrado) los backup_*.dump y *.tmp de <dir> a
# <dir>/legado-<ts>/ (700). Sale 0 si apartó alguno, 1 si no había ninguno.
apartar_backups_destino() {
    local dir="$1" ts legado
    orq_ssh NUEVO "sudo -n find ${dir} -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print"
    consulta_ok "los backups propios de ${dir} en el VPS nuevo"
    if [[ -z "$ORQ_SALIDA" ]]; then
        echo "  - ${dir}: no hay backups propios que apartar"
        return 1
    fi
    ts="$(date -u +%Y%m%dT%H%M%SZ)"
    legado="${dir}/legado-${ts}"
    orq_ssh NUEVO "sudo -n mkdir -m 700 ${legado}"
    falla_remota 5 "crear ${legado} en el VPS nuevo"
    orq_ssh NUEVO "sudo -n find ${dir} -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print -exec mv -n -t ${legado} -- {} +"
    falla_remota 5 "apartar los backups propios del destino en ${legado}"
    printf '%s\n' "$ORQ_SALIDA" | sed "s#^.*/#  - apartado en ${legado}/: #"
    orq_ssh NUEVO "sudo -n find ${dir} -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print"
    consulta_ok "los backups propios de ${dir} en el VPS nuevo"
    if [[ -n "$ORQ_SALIDA" ]]; then
        abortar 5 "después de apartarlos siguen sueltos en ${dir}: $(printf '%s' "$ORQ_SALIDA" | tr '\n' ' ')"
    fi
    return 0
}

corte_paso4() {
    local hubo=0 dir="${DIR_STOCKAPP_NUEVO}/backups" lista ts legado
    paso 4
    # Los backup_*.dump y *.tmp del ensayo: ReconciliarDumpHuerfanosAsync los tomaría como propios.
    # Uno por uno, por patrón y sin bajar de nivel; nunca un borrado recursivo.
    orq_ssh NUEVO "sudo -n test -d ${dir}"
    if [[ "$ORQ_RC" -eq 0 && -z "$ORQ_STDERR" && "$MODO" == "desde-backup" ]]; then
        if apartar_backups_destino "$dir"; then
            hubo=1
        fi
    elif [[ "$ORQ_RC" -eq 0 && -z "$ORQ_STDERR" ]]; then
        # Corte y ensayo (spec 6.5.4, paso 4): en el nuevo recién preparado solo hay backups del ensayo, que
        # ReconciliarDumpHuerfanosAsync tomaría como propios. Se borran; en --desde-backup no (R59).
        orq_ssh NUEVO "sudo -n find ${dir} -maxdepth 1 -type f \\( -name 'backup_*.dump' -o -name '*.tmp' \\) -print -delete"
        falla_remota 5 "el borrado de los backups del ensayo en el VPS nuevo"
        if [[ -n "$ORQ_SALIDA" ]]; then
            hubo=1
            printf '%s\n' "$ORQ_SALIDA" | sed 's/^/  - borrado: /'
        fi
    elif [[ "$ORQ_RC" -eq 1 && -z "$ORQ_STDERR" ]]; then
        echo "  - ${dir} no existe todavía: no hay backups del ensayo que borrar"
    else
        consulta_ok "si existe ${dir} en el VPS nuevo"
        abortar 8 "no pude consultar si existe ${dir} en el VPS nuevo."
    fi

    # Lo que un ensayo manual anterior pudo dejar suelto en la raíz de migracion/ se aparta (nunca se
    # borra). Las corridas del orquestador viven cada una en su directorio y no se mueven.
    lista='find "$HOME/stockapp-deploy/migracion" -maxdepth 1 -type f \( -name '\''volcado-*'\'' -o -name '\''backups-historial.tar'\'' \) -print'
    orq_ssh NUEVO "$lista"
    consulta_ok "los restos sueltos de ~/stockapp-deploy/migracion en el VPS nuevo"
    if [[ -n "$ORQ_SALIDA" ]]; then
        hubo=1
        ts="$(date -u +%Y%m%dT%H%M%SZ)"
        legado="\"\$HOME/stockapp-deploy/migracion/legado-${ts}\""
        orq_ssh NUEVO "mkdir -m 700 ${legado} && ${lista% -print} -exec mv -n -t ${legado} -- {} +"
        falla_remota 5 "apartar los restos sueltos en ~/stockapp-deploy/migracion/legado-${ts}"
        orq_ssh NUEVO "$lista"
        consulta_ok "los restos sueltos de ~/stockapp-deploy/migracion en el VPS nuevo"
        if [[ -n "$ORQ_SALIDA" ]]; then
            abortar 5 "después de apartarlos siguen sueltos en ~/stockapp-deploy/migracion: $(printf '%s' "$ORQ_SALIDA" | tr '\n' ' ')"
        fi
        echo "  - restos sueltos apartados en ~/stockapp-deploy/migracion/legado-${ts}"
    fi

    if [[ "$hubo" -eq 1 ]]; then
        cerrar_paso 4 OK
    else
        cerrar_paso 4 "YA ESTABA"
    fi
}

corte_paso5() {
    local archivo="$ORQ_CORRIDA/contenedores-viejo.txt"
    paso 5
    orq_ssh VIEJO "docker ps --format '{{.Names}}'"
    consulta_ok "la lista de contenedores del VPS viejo"
    if [[ -z "$ORQ_SALIDA" ]]; then
        abortar 8 "la lista de contenedores del VPS viejo vino vacía: no puedo interpretarla."
    fi
    ( umask 077; printf '%s\n' "$ORQ_SALIDA" | LC_ALL=C sort > "$archivo" ) || abortar 8 "no pude escribir $archivo."
    # Cómo estaba habilitada la unit ANTES de congelar: un volcado que falla se clasifica como "antes
    # del stop" solo si el viejo sigue exactamente así (nota 1 de spec 6.5.4).
    habilitacion_viejo
    HABILITACION_VIEJO="$HABILITACION"
    orq_estado_poner habilitacion_viejo_inicial "$HABILITACION_VIEJO"
    cerrar_paso 5 OK
}

# unit_movida_sin_enmascarar: 0 si la unit del viejo está en .congelado pero is-enabled no dice masked
# (el volcado murió entre el mv y el mask). En ese estado el volcado se niega a correr otra vez (su
# exit 8, ".congelado ya existe") y --continuar desde el 6 no avanzaría nunca: la salida es
# --revertir. No aborta: lo que no se puede consultar es 1 y lo decide el resto.
MOTIVO_SOLO_REVERTIR="la unit de stockapp-api del VPS viejo quedó movida a .congelado pero no enmascarada: retomar la corrida volvería a correr el volcado, que con un .congelado ya existente se niega (exit 8), y no avanzaría nunca. La salida es --revertir, que devuelve la unit a su lugar y vuelve a arrancar el viejo; después, una corrida nueva."
unit_movida_sin_enmascarar() {
    orq_ssh VIEJO "test -e ${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service.congelado"
    if [[ "$ORQ_RC" -ne 0 ]]; then
        return 1
    fi
    orq_ssh VIEJO "systemctl is-enabled stockapp-api"
    [[ "$ORQ_SALIDA" != "masked" ]]
}

# viejo_intacto: 0 si el viejo sigue sirviendo como antes del bloque (active y con la habilitación
# del paso 5, sin enmascarar). Lo que no se puede consultar sale con 8: no se adivina.
viejo_intacto() {
    estado_api VIEJO
    habilitacion_viejo
    ESTADO_VIEJO_TXT="stockapp-api dice '$ESTADO_API' y su unit '$HABILITACION'."
    [[ "$ESTADO_API" == "active" && "$HABILITACION" == "$HABILITACION_VIEJO" && "$HABILITACION" != "masked" ]]
}

corte_paso6() {
    local salida args=() rc n=2 congelado_rc=0
    paso 6
    # Un --salida recién creado y vacío: lo que quede ahí es de esta corrida (A11, paso 7). Si un intento
    # anterior (--continuar) ya usó volcado/, el siguiente va a volcado-2/, volcado-3/...
    VOLCADO_DIR="volcado"
    while [[ -e "$ORQ_CORRIDA/$VOLCADO_DIR" ]]; do
        VOLCADO_DIR="volcado-$n"
        n=$((n + 1))
    done
    salida="$ORQ_CORRIDA/$VOLCADO_DIR"
    if ! mkdir -m 700 -- "$salida" 2>/dev/null; then
        abortar 8 "no pude crear $salida vacío (¿ya existe?): el volcado de esta corrida tiene que caer en un directorio nuevo."
    fi
    orq_estado_poner volcado_dir "$VOLCADO_DIR"
    # El ensayo nunca congela de forma permanente: el bloque no enmascara y vuelve a arrancar la API.
    if [[ "$MODO" == "ensayo" ]]; then
        args+=(--ensayo)
    fi
    if [[ "$CONTENEDOR_VIEJO" != "stockapp-pg" ]]; then
        args+=(--contenedor "$CONTENEDOR_VIEJO")
    fi
    if [[ -n "$RAIZ_VIEJO" ]]; then
        args+=(--raiz-remota "$RAIZ_VIEJO")
    fi

    # Acá empieza el reloj de indisponibilidad, ANTES de llamar al bloque: su paso 2 detiene la API
    # vieja en los primeros segundos. Al reintentar (--continuar) el viejo ya estaba congelado desde el
    # primer intento: congelado_utc no se pisa.
    orq_estado_leer congelado_utc >/dev/null 2>&1 || congelado_rc=$?
    case "$congelado_rc" in
        0) reloj_desde_congelado ;;
        1)
            orq_estado_poner congelado_utc "$(ahora_utc)"
            ORQ_CONGELADO_SEG="$(orq_reloj)"
            ;;
        *) abortar 8 "no pude leer congelado_utc del estado de la corrida." ;;
    esac
    CONGELADO=1

    orq_bloque deploy/volcado-final-vps.sh "$ENV_VIEJO" --salida "$salida" "${args[@]}"
    rc="$ORQ_RC_BLOQUE"
    informar_bloque deploy/volcado-final-vps.sh "$rc"
    DETALLE_FALLO="volcado-final-vps.sh: $rc"
    if [[ "$rc" -ne 0 ]]; then
        orq_estado_poner volcado_salio "$rc"
    fi
    case "$rc" in
        0) ;;
        9)
            SALIDA_SUGERIDA="revisar"
            ESTADO_VIEJO_TXT="detenido (y enmascarado, si el bloque llegó hasta ahí). La lista de contenedores del viejo cambió durante el volcado."
            abortar 9 "volcado-final-vps.sh salió con 9 (alcance): la lista de contenedores del VPS viejo cambió. Un humano tiene que mirar antes de seguir."
            ;;
        4)
            abortar 6 "volcado-final-vps.sh salió con 4 (traída inválida), después del stop. --continuar vuelve a correr el volcado en una carpeta nueva (con la unit ya enmascarada no se mueve nada más)."
            ;;
        *)
            # 3, 8 o 2: si el bloque no llegó a detener nada, el viejo sigue sirviendo (exit 5). Se
            # confirma mirando el viejo, no el código.
            if unit_movida_sin_enmascarar; then
                SALIDA_SUGERIDA="solo-revertir"
                ESTADO_VIEJO_TXT="detenido, con la unit movida a .congelado y no enmascarada."
                abortar 6 "volcado-final-vps.sh salió con $rc después del stop: $MOTIVO_SOLO_REVERTIR"
            fi
            if viejo_intacto; then
                echo "  - el VPS viejo sigue sirviendo: $ESTADO_VIEJO_TXT"
                abortar 5 "volcado-final-vps.sh salió con $rc antes del stop: el VPS viejo sigue intacto y sirviendo. Re-correr arranca una corrida nueva."
            fi
            abortar 6 "volcado-final-vps.sh salió con $rc después del stop. --continuar vuelve a correr el volcado en una carpeta nueva (con la unit ya enmascarada no se mueve nada más)."
            ;;
    esac

    if [[ "$MODO" == "ensayo" ]]; then
        # El viejo de testing vuelve a servir: el reloj de indisponibilidad se detiene acá. Que haya
        # quedado como estaba lo verifica V10 (invertido).
        unset ORQ_CONGELADO_SEG
        cerrar_paso 6 OK
        return 0
    fi
    # El bloque dijo OK: el viejo TIENE que haber quedado congelado. Si sigue sirviendo, restaurar
    # este volcado perdería toda escritura posterior al dump.
    estado_api VIEJO
    habilitacion_viejo
    ESTADO_VIEJO_TXT="stockapp-api dice '$ESTADO_API' y su unit '$HABILITACION'."
    if [[ "$ESTADO_API" != "inactive" || "$HABILITACION" != "masked" ]]; then
        DETALLE_FALLO="el viejo no quedó congelado"
        abortar 6 "volcado-final-vps.sh salió con 0, pero el VPS viejo no quedó congelado ($ESTADO_VIEJO_TXT; se esperaba inactive y masked). No se transfiere ni se restaura nada."
    fi
    ESTADO_VIEJO_TXT=""
    cerrar_paso 6 OK
}

corte_paso7() {
    local dir="$ORQ_CORRIDA/$VOLCADO_DIR" dumps=() manifiestos=() ts_dump ts_man linea inicio
    local dump_m="" sha_m="" fecha_m="" migr_m="" n_dump=0 n_sha=0 n_fecha=0 n_migr=0 sha_local a b
    paso 7
    shopt -s nullglob
    dumps=("$dir"/volcado-*.dump)
    manifiestos=("$dir"/volcado-*.manifiesto)
    shopt -u nullglob
    if [[ "${#dumps[@]}" -ne 1 || "${#manifiestos[@]}" -ne 1 ]]; then
        abortar 6 "en $dir tiene que haber exactamente un volcado-*.dump y un volcado-*.manifiesto (hay ${#dumps[@]} y ${#manifiestos[@]}): no se adivina cuál es el de esta corrida (A11)."
    fi
    VOLCADO="${dumps[0]##*/}"
    MANIFIESTO="${manifiestos[0]##*/}"
    if [[ "$VOLCADO" =~ ^volcado-([0-9]{8}T[0-9]{6}Z)\.dump$ ]]; then ts_dump="${BASH_REMATCH[1]}"; else ts_dump=""; fi
    if [[ "$MANIFIESTO" =~ ^volcado-([0-9]{8}T[0-9]{6}Z)\.manifiesto$ ]]; then ts_man="${BASH_REMATCH[1]}"; else ts_man=""; fi
    if [[ -z "$ts_dump" || "$ts_dump" != "$ts_man" ]]; then
        abortar 6 "el volcado ($VOLCADO) y el manifiesto ($MANIFIESTO) no tienen el mismo <ts> (A11)."
    fi

    while IFS= read -r linea || [[ -n "$linea" ]]; do
        [[ "$linea" == "[conteos]" ]] && break
        case "$linea" in
            dump=*) dump_m="${linea#dump=}"; n_dump=$((n_dump + 1)) ;;
            sha256=*) sha_m="${linea#sha256=}"; n_sha=$((n_sha + 1)) ;;
            fecha_utc=*) fecha_m="${linea#fecha_utc=}"; n_fecha=$((n_fecha + 1)) ;;
            ultima_migracion=*) migr_m="${linea#ultima_migracion=}"; n_migr=$((n_migr + 1)) ;;
        esac
    done < "${manifiestos[0]}" || abortar 6 "no pude leer el manifiesto ${manifiestos[0]}."
    if [[ "$n_dump" -ne 1 || "$n_sha" -ne 1 || "$n_fecha" -ne 1 || "$n_migr" -ne 1 ]]; then
        abortar 6 "el manifiesto $MANIFIESTO tiene que traer exactamente una vez dump=, sha256=, fecha_utc= y ultima_migracion=."
    fi
    if [[ "$dump_m" != "$VOLCADO" ]]; then
        abortar 6 "el manifiesto dice dump=$dump_m, pero el volcado de esta corrida es $VOLCADO (A11)."
    fi
    if ! sha_local="$(sha256sum -- "${dumps[0]}")"; then
        abortar 6 "no pude calcular el sha256 de ${dumps[0]}."
    fi
    sha_local="${sha_local%% *}"
    if [[ ! "$sha_m" =~ ^[0-9a-f]{64}$ || "$sha_m" != "$sha_local" ]]; then
        abortar 6 "el sha256 del dump local ($sha_local) no es el del manifiesto (sha256=$sha_m) (A11)."
    fi
    inicio="$(orq_estado_leer inicio_utc)" || abortar 8 "no pude leer inicio_utc del estado de la corrida."
    if [[ ! "$fecha_m" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] ||
        ! a="$(date -u -d "$fecha_m" +%s 2>/dev/null)" || ! b="$(date -u -d "$inicio" +%s 2>/dev/null)"; then
        abortar 6 "no pude interpretar fecha_utc=$fecha_m del manifiesto (o inicio_utc=$inicio)."
    fi
    # fecha_utc e inicio_utc salen del MISMO reloj, el de esta máquina (volcado-final-vps.sh hace
    # date -u acá), y el reloj de pared puede saltar hacia atrás (el de WSL salta 11-14 s bajo carga):
    # se toleran FECHA_TOLERANCIA segundos antes del inicio, con un aviso (R35). Un volcado viejo no
    # puede llegar acá igual: --salida es un directorio nuevo y vacío de esta corrida.
    if [[ "$a" -lt $((b - FECHA_TOLERANCIA)) ]]; then
        abortar 6 "fecha_utc=$fecha_m del manifiesto es anterior al inicio de esta corrida ($inicio) en más de ${FECHA_TOLERANCIA} s: no es el volcado de esta corrida (A11)."
    fi
    if [[ "$a" -lt "$b" ]]; then
        echo "  AVISO: fecha_utc=$fecha_m del manifiesto es $((b - a)) s anterior al inicio de esta corrida ($inicio): dentro de la tolerancia de ${FECHA_TOLERANCIA} s (el reloj de esta máquina saltó para atrás). Se acepta: el --salida es nuevo y vacío, y el <ts>, dump= y sha256 coinciden."
    fi
    if [[ ! "$migr_m" =~ ^[0-9]{14}_[A-Za-z0-9_]+$ ]]; then
        abortar 6 "ultima_migracion=$migr_m del manifiesto no tiene la forma de una migración."
    fi
    if [[ ! -f "$dir/backups-historial.tar" ]]; then
        abortar 6 "falta $dir/backups-historial.tar: el volcado no trajo el historial de backups."
    fi
    SHA_VOLCADO="$sha_local"
    ULTIMA_MIGRACION="$migr_m"
    orq_estado_poner volcado "$VOLCADO"
    orq_estado_poner sha256 "$SHA_VOLCADO"
    echo "  - $VOLCADO (sha256 ${SHA_VOLCADO:0:12}…, fecha_utc $fecha_m, última migración $ULTIMA_MIGRACION)"
    cerrar_paso 7 OK
}

# scp_al_nuevo <dir-relativo-al-HOME-remoto> <archivo>...
scp_al_nuevo() {
    local destino="$1" err rc=0 salida_err
    shift
    if ! orq_tmp_dir || ! err="$(mktemp "$ORQ_TMP_DIR/scp-stderr.XXXXXX")"; then
        abortar 8 "no pude crear el temporal para el stderr de scp."
    fi
    scp -P "$NUEVO_VPS_SSH_PORT" -o BatchMode=yes -- "$@" "${NUEVO_VPS_USER}@${NUEVO_VPS_HOST}:${destino}/" \
        9>&- 2>"$err" </dev/null || rc=$?
    salida_err="$(cat "$err" 2>/dev/null)" || salida_err=""
    rm -f -- "$err"
    if [[ "$rc" -ne 0 ]]; then
        [[ -z "$salida_err" ]] || printf '%s\n' "$salida_err" | sed 's/^/    | /' >&2
        abortar 6 "la copia por scp al VPS nuevo falló (salió con $rc)."
    fi
}

corte_paso8() {
    local nombre="${ORQ_CORRIDA##*/}" dir="$ORQ_CORRIDA/$VOLCADO_DIR" sha_remoto rc=0 f
    paso 8
    # Si un intento anterior ya creó su directorio remoto (--continuar), este va a otro, también nuevo.
    orq_estado_leer dir_remoto >/dev/null 2>&1 || rc=$?
    case "$rc" in
        0) nombre+="-r$(date -u +%Y%m%dT%H%M%SZ)" ;;
        1) ;;
        *) abortar 8 "no pude leer dir_remoto del estado de la corrida." ;;
    esac
    D_REL="stockapp-deploy/migracion/$nombre"
    # Un directorio remoto NUEVO (mkdir sin -p): si ya existe, no se reutiliza ni se mezcla.
    orq_ssh NUEVO "mkdir -m 700 \"\$HOME/$D_REL\""
    falla_remota 6 "crear el directorio remoto ~/$D_REL (tiene que ser nuevo)"
    orq_estado_poner dir_remoto "~/$D_REL"
    # --desde-backup: el backup del operador (y su manifiesto, si lo hay); no hay historial de backups.
    local origen=("$dir/$VOLCADO" "$dir/$MANIFIESTO" "$dir/backups-historial.tar") datos
    if [[ "$MODO" == "desde-backup" ]]; then
        origen=("$BACKUP")
        [[ -z "$MANIFIESTO" ]] || origen+=("$MANIFIESTO_RUTA")
    fi
    datos="$(rr "$VOLCADO")"
    for f in "${origen[@]:1}"; do
        datos+=" $(rr "${f##*/}")"
    done
    scp_al_nuevo "$D_REL" "${origen[@]}" "$REPO_ROOT/deploy/restaurar-backup.sh"
    orq_ssh NUEVO "chmod 600 ${datos} && chmod 755 $(rr restaurar-backup.sh)"
    falla_remota 6 "el chmod de los archivos transferidos"
    orq_ssh NUEVO "sha256sum $(rr "$VOLCADO")"
    falla_remota 6 "el sha256sum remoto del volcado"
    sha_remoto="${ORQ_SALIDA%% *}"
    if [[ "$sha_remoto" != "$SHA_VOLCADO" ]]; then
        abortar 6 "el sha256 del volcado en el VPS nuevo (${sha_remoto:-ilegible}) no es el de esta corrida ($SHA_VOLCADO)."
    fi
    cerrar_paso 8 OK
}

# opcion_contenedor_nuevo: " --contenedor <n>" solo si no es el default (nunca se nombra stockapp-pg).
opcion_contenedor_nuevo() {
    if [[ "$CONTENEDOR_NUEVO" != "stockapp-pg" ]]; then
        printf ' --contenedor %s' "$CONTENEDOR_NUEVO"
    fi
}

# como_reintentar <texto-del-corte>: cómo se sigue tras un fallo; en --desde-backup no hay --continuar.
como_reintentar() {
    if [[ "$MODO" == "desde-backup" ]]; then
        printf '%s' "La corrida se cierra: volvé a correr --desde-backup (ver abajo)."
    else
        printf '%s' "$1"
    fi
}

mostrar_salida_remota() {
    if [[ -n "$ORQ_SALIDA" ]]; then
        printf '%s\n' "$ORQ_SALIDA" | sed 's/^/    │ /'
    fi
    if [[ -n "$ORQ_STDERR" ]]; then
        printf '%s\n' "$ORQ_STDERR" | sed 's/^/    │ /'
    fi
}

corte_paso9() {
    local cmd rc
    paso 9
    # La base del nuevo siempre tiene algo (como mínimo, lo que sembró el primer deploy): --recrear-base
    # va siempre, con su dump de seguridad (A2).
    cmd="$(rr restaurar-backup.sh) $(rr "$VOLCADO")$(opcion_contenedor_nuevo)"
    # Sin manifiesto (--desde-backup con un backup_*.dump), restaurar verifica la estructura y no los conteos.
    if [[ -n "$MANIFIESTO" ]]; then
        cmd+=" --manifiesto $(rr "$MANIFIESTO")"
    fi
    cmd+=" --recrear-base ${NUEVO_POSTGRES_DB} --dir-seguridad $(rr seguridad)"
    orq_ssh NUEVO "$cmd"
    rc="$ORQ_RC"
    mostrar_salida_remota
    if [[ "$rc" -eq 255 ]]; then
        SALIDA_SUGERIDA="investigar"
        abortar 8 "no pude ejecutar restaurar-backup.sh en el VPS nuevo (ssh salió con 255): no se sabe en qué quedó la base."
    fi
    informar_bloque deploy/restaurar-backup.sh "$rc"
    DETALLE_FALLO="restaurar-backup.sh: $rc"
    orq_estado_poner restaurar_salio "$rc"
    # En ningún caso se arranca la API: el conteo se compara ANTES del arranque.
    case "$rc" in
        0) RESTAURAR_RC=0 ;;
        5) abortar 6 "restaurar-backup.sh salió con 5: pg_restore falló y la base del nuevo quedó VACÍA. $(como_reintentar "--continuar reintenta la restauración tal cual.")" ;;
        3 | 4) abortar 6 "restaurar-backup.sh salió con $rc (precondición o dump inválido): $(como_reintentar "--continuar repite desde la transferencia (paso 8), a un directorio remoto nuevo.")" ;;
        6 | 7)
            SALIDA_SUGERIDA="reintentar-restauracion"
            abortar 6 "restaurar-backup.sh salió con $rc: la base del nuevo QUEDÓ restaurada pero no verifica. No se arranca la API."
            ;;
        *)
            SALIDA_SUGERIDA="investigar"
            abortar 8 "restaurar-backup.sh salió con $rc: no se pudo consultar, falló el dump de seguridad o los argumentos están mal armados. No se arranca la API."
            ;;
    esac
    cerrar_paso 9 OK
}

corte_paso10() {
    paso 10
    orq_ssh NUEVO "sudo -n tar -C ${DIR_STOCKAPP_NUEVO} -xpf $(rr backups-historial.tar)"
    falla_remota 6 "la extracción del historial de backups en el VPS nuevo"
    if [[ -n "$RAIZ_NUEVO" ]]; then
        # En el arnés no existe el usuario stockapp (el sudo simulado corre como el operador).
        echo "  - chown -R stockapp:stockapp del historial: OMITIDO (arnés)"
    else
        orq_ssh NUEVO "sudo -n chown -R stockapp:stockapp ${DIR_STOCKAPP_NUEVO}/backups"
        falla_remota 6 "el chown del historial de backups en el VPS nuevo"
    fi
    cerrar_paso 10 OK
}

corte_paso11() {
    local limite
    paso 11
    # R44: habilitada antes del start (un --revertir anterior la deja deshabilitada): si el VPS nuevo se
    # reinicia después del corte, la API vuelve a arrancar sola.
    orq_ssh NUEVO "sudo -n systemctl enable stockapp-api"
    falla_remota 7 "'systemctl enable stockapp-api' en el VPS nuevo"
    orq_ssh NUEVO "systemctl is-enabled stockapp-api"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        consulta_ok "la habilitación de stockapp-api en el VPS nuevo"
    fi
    if [[ "$ORQ_SALIDA" != "enabled" ]]; then
        abortar 7 "tras el enable, stockapp-api del VPS nuevo dice '${ORQ_SALIDA}' (se esperaba enabled): no se arranca."
    fi
    orq_ssh NUEVO "sudo -n systemctl start stockapp-api"
    if [[ "$ORQ_RC" -eq 255 ]]; then
        falla_remota 7 "'systemctl start stockapp-api' en el VPS nuevo"
    elif [[ "$ORQ_RC" -ne 0 ]]; then
        volcar_stderr
        journal_nuevo
        abortar 7 "'systemctl start stockapp-api' falló en el VPS nuevo (salió con $ORQ_RC)."
    fi
    # Espera acotada con reloj monotónico: active Y GET / por loopback.
    limite=$(($(orq_reloj) + ARRANQUE_ESPERA))
    while :; do
        estado_api NUEVO
        case "$ESTADO_API" in
            active)
                orq_ssh NUEVO "curl -fsS --max-time 10 -o /dev/null ${RESOLVE_NUEVO}${URL_NUEVO_LOCAL}/"
                if [[ "$ORQ_RC" -eq 0 ]]; then
                    break
                fi
                if [[ "$ORQ_RC" -eq 255 ]]; then
                    consulta_ok "la API nueva por loopback"
                fi
                ;;
            failed)
                journal_nuevo
                abortar 7 "la API nueva quedó en 'failed' después del start."
                ;;
        esac
        if [[ "$(orq_reloj)" -ge "$limite" ]]; then
            journal_nuevo
            abortar 7 "la API nueva no respondió en ${ARRANQUE_ESPERA} s (is-active: $ESTADO_API)."
        fi
        sleep 2
    done
    orq_estado_poner api_nueva_activa_utc "$(ahora_utc)"
    cerrar_paso 11 OK
}

# ---------------------------------------------------------------------------------------
# Verificación automática (spec 6.5.5)
# ---------------------------------------------------------------------------------------

declare -A V_ESTADO=()
declare -A V_DETALLE=()
V_ORDEN=(V1 V2 V3 V4 V5 V6 V7 V8 V10)
declare -A V_NOMBRE=(
    [V1]="restauración (restaurar-backup.sh salió con 0)"
    [V2]="API nueva active y migraciones al día"
    [V3]="GET / desde esta máquina, validando el TLS"
    [V4]="/licencia/estado con activada:true"
    [V5]="login real desde esta máquina"
    [V6]="conteos después del arranque"
    [V7]="feed con la versión y el .nupkg completo"
    [V8]="backups: historial, descarga y uno nuevo"
    [V10]="VPS viejo enmascarado, inactivo y sin cambios"
)

v_ok() {
    V_ESTADO[$1]="OK"
    V_DETALLE[$1]="${2-}"
}

v_falla() {
    V_ESTADO[$1]="FALLÓ"
    V_DETALLE[$1]="${2-}"
}

# primera_linea <texto>: para los detalles de un fallo de curl (nunca el cuerpo de una respuesta).
primera_linea() {
    printf '%s' "${1%%$'\n'*}"
}

# psql_nuevo <sql> -> ORQ_RC / ORQ_SALIDA, con la SQL por stdin.
psql_nuevo() {
    orq_ssh NUEVO "sudo -n docker exec -i ${CONTENEDOR_NUEVO} psql -U ${NUEVO_POSTGRES_USER} -d ${NUEVO_POSTGRES_DB} -tA -v ON_ERROR_STOP=1" "$1"
}

verificar_v1() {
    if [[ "$RESTAURAR_RC" == "0" ]]; then
        v_ok V1
    else
        v_falla V1 "restaurar-backup.sh salió con ${RESTAURAR_RC:-?}"
    fi
}

verificar_v2() {
    local real desde errores rc=0
    orq_ssh NUEVO "systemctl is-active stockapp-api"
    if [[ "$ORQ_SALIDA" != "active" || "$ORQ_RC" -ne 0 ]]; then
        v_falla V2 "is-active dice '${ORQ_SALIDA:-no pude consultar}'"
        return 0
    fi
    psql_nuevo 'SELECT max("MigrationId") FROM "__EFMigrationsHistory";'
    real="$ORQ_SALIDA"
    if [[ "$ORQ_RC" -ne 0 || ! "$real" =~ ^[0-9]{14}_[A-Za-z0-9_]+$ ]]; then
        v_falla V2 "no pude leer la última migración aplicada (salió con $ORQ_RC)"
        return 0
    fi
    if [[ -z "$ULTIMA_MIGRACION" ]]; then
        # --desde-backup sin manifiesto: no hay una última migración de referencia.
        real+=" (sin manifiesto: no hay última migración de referencia)"
    elif ! printf '%s\n%s\n' "$ULTIMA_MIGRACION" "$real" | LC_ALL=C sort -C; then
        v_falla V2 "la última migración aplicada ($real) es anterior a la del manifiesto ($ULTIMA_MIGRACION)"
        return 0
    fi
    # Las líneas con error en el journal se INFORMAN y no bloquean: el patrón tiene falsos positivos.
    desde="$(orq_estado_leer api_nueva_activa_utc)" || desde=""
    desde="${desde/T/ }"
    desde="${desde%Z} UTC"
    orq_ssh NUEVO "sudo -n journalctl -u stockapp-api --since '${desde}' --no-pager -o cat"
    if [[ "$ORQ_RC" -eq 0 ]]; then
        errores="$(printf '%s\n' "$ORQ_SALIDA" | grep -ciE 'error|fail|crit|exception')" || rc=$?
        if [[ "$rc" -gt 1 ]]; then
            errores="?"
        fi
        v_ok V2 "migración $real; $errores línea(s) con error en el journal desde el arranque (se informa, no bloquea)"
    else
        v_ok V2 "migración $real; no pude leer el journal (salió con $ORQ_RC; se informa, no bloquea)"
    fi
}

verificar_v3() {
    curl_operador "${URL_NUEVO}/"
    if [[ "$CURL_RC" -eq 0 ]]; then
        v_ok V3
    else
        v_falla V3 "curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
    fi
}

verificar_v4() {
    local activada rc=0
    curl_operador "${URL_NUEVO}/licencia/estado"
    if [[ "$CURL_RC" -ne 0 ]]; then
        v_falla V4 "curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
        return 0
    fi
    activada="$(printf '%s' "$CURL_CUERPO" | jq -r '.activada')" || rc=$?
    if [[ "$rc" -eq 0 && "$activada" == "true" ]]; then
        v_ok V4
    else
        v_falla V4 "activada: ${activada:-ilegible}"
    fi
}

verificar_v5() {
    local cuerpo rc=0
    TOKEN=""
    cuerpo="$(PW="$PW" jq -nc --arg u "$USUARIO_VERIFICACION" '{nombreUsuario: $u, contrasena: env.PW}')" || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        v_falla V5 "no pude armar el cuerpo del login"
        return 0
    fi
    curl_operador_con "$cuerpo" -X POST -H 'Content-Type: application/json' --data @- "${URL_NUEVO}/auth/login"
    cuerpo=""
    if [[ "$CURL_RC" -ne 0 ]]; then
        CURL_CUERPO=""
        v_falla V5 "login de '$USUARIO_VERIFICACION': curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
        return 0
    fi
    rc=0
    TOKEN="$(printf '%s' "$CURL_CUERPO" | jq -r '.token // empty')" || rc=$?
    CURL_CUERPO=""
    if [[ "$rc" -ne 0 || -z "$TOKEN" ]]; then
        TOKEN=""
        v_falla V5 "la respuesta del login no trae un token legible"
        return 0
    fi
    v_ok V5 "login de '$USUARIO_VERIFICACION'"
}

verificar_v6() {
    if [[ -z "$MANIFIESTO" ]]; then
        V_ESTADO[V6]="OMITIDO"
        V_DETALLE[V6]="sin manifiesto: no hay conteos con qué comparar"
        return 0
    fi
    orq_ssh NUEVO "$(rr restaurar-backup.sh) --verificar-post-arranque --manifiesto $(rr "$MANIFIESTO")$(opcion_contenedor_nuevo)"
    mostrar_salida_remota
    if [[ "$ORQ_RC" -eq 0 ]]; then
        v_ok V6
    else
        v_falla V6 "restaurar-backup.sh --verificar-post-arranque salió con $ORQ_RC"
    fi
}

verificar_v7() {
    local version="$VERSION_NUEVO" activo nombre tamano rc=0 estado largo
    curl_operador "${URL_NUEVO}/updates/releases.win.json"
    if [[ "$CURL_RC" -ne 0 ]]; then
        v_falla V7 "el feed: curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
        return 0
    fi
    if [[ "$version" == "desconocida" ]]; then
        version="$(printf '%s' "$CURL_CUERPO" | jq -r '.Assets[] | select(.Type == "Full") | .Version' | sort -V | tail -n 1)" || rc=$?
    fi
    activo="$(printf '%s' "$CURL_CUERPO" | jq -r --arg v "$version" '[.Assets[] | select(.Type == "Full" and .Version == $v)] | if length == 1 then "\(.[0].FileName)\t\(.[0].Size)" else empty end')" || rc=$?
    IFS=$'\t' read -r nombre tamano <<< "$activo" || :
    if [[ "$rc" -ne 0 || ! "${nombre:-}" =~ ^[A-Za-z0-9._+-]+\.nupkg$ || ! "${tamano:-}" =~ ^[0-9]+$ ]]; then
        v_falla V7 "el feed no tiene exactamente un paquete Full de la versión $version"
        return 0
    fi
    curl_operador_con "" -I "${URL_NUEVO}/updates/${nombre}"
    if [[ "$CURL_RC" -ne 0 ]]; then
        v_falla V7 "HEAD de $nombre: curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
        return 0
    fi
    estado="$(printf '%s\n' "$CURL_CUERPO" | tr -d '\r' | sed -n '1s/^HTTP\/[0-9.]* \([0-9]*\).*/\1/p')"
    largo="$(printf '%s\n' "$CURL_CUERPO" | tr -d '\r' | sed -n 's/^[Cc][Oo][Nn][Tt][Ee][Nn][Tt]-[Ll][Ee][Nn][Gg][Tt][Hh]: *\([0-9]*\)$/\1/p' | tail -n 1)"
    if [[ "$estado" != "200" || "$largo" != "$tamano" ]]; then
        v_falla V7 "$nombre: HTTP ${estado:-?} con Content-Length ${largo:-?} (el feed dice Size $tamano)"
        return 0
    fi
    v_ok V7 "versión $version, $nombre de $tamano bytes"
}

verificar_v8() {
    local listado rc=0 cantidad vieja maximo limite nuevas exitosas otras nuevo_resultado historial
    if [[ -z "$TOKEN" ]]; then
        v_falla V8 "sin token (falló el login de V5)"
        return 0
    fi
    # El token viaja solo por el stdin de curl -K -, nunca en el comando.
    listado="$(printf 'header = "Authorization: Bearer %s"' "$TOKEN")"
    TOKEN=""
    curl_operador_con "$listado" -K - "${URL_NUEVO}/backups"
    if [[ "$CURL_RC" -ne 0 ]]; then
        listado=""
        v_falla V8 "GET /backups: curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
        return 0
    fi
    cantidad="$(printf '%s' "$CURL_CUERPO" | jq -e 'if type == "array" then length else error("no es una lista") end')" || rc=$?
    if [[ "$rc" -ne 0 || ! "$cantidad" =~ ^[0-9]+$ ]] || [[ "$cantidad" -eq 0 && "$MODO" != "desde-backup" ]]; then
        listado=""
        v_falla V8 "GET /backups no lista corridas del historial"
        return 0
    fi
    vieja="$(printf '%s' "$CURL_CUERPO" | jq -r '[.[] | select(.resultado == "Exitosa")] | sort_by(.finalizadaEn) | .[0].id // empty')" || vieja=""
    maximo="$(printf '%s' "$CURL_CUERPO" | jq -r '[.[].id] | max // 0')" || maximo=""
    if [[ "$MODO" == "desde-backup" ]]; then
        # Sin historial (spec 6.5.7): GET /backups puede listar corridas cuyo archivo no existe en el
        # nuevo. La descarga de la más vieja se informa y no bloquea; el backup nuevo (POST) sí.
        historial="$cantidad corridas en el listado"
        if [[ "$vieja" =~ ^[0-9]+$ ]]; then
            curl_operador_con "$listado" -K - -o /dev/null "${URL_NUEVO}/backups/${vieja}/contenido"
            if [[ "$CURL_RC" -eq 0 ]]; then
                historial+=", descarga de la #$vieja OK"
            else
                historial+=", la descarga de la #$vieja falló (curl $CURL_RC: sin historial, se informa y no bloquea)"
            fi
        else
            historial+=", ninguna Exitosa para descargar (se informa y no bloquea)"
        fi
        if [[ ! "$maximo" =~ ^[0-9]+$ ]]; then
            listado=""
            v_falla V8 "GET /backups trae ids ilegibles"
            return 0
        fi
    elif [[ ! "$vieja" =~ ^[0-9]+$ || ! "$maximo" =~ ^[0-9]+$ ]]; then
        listado=""
        v_falla V8 "GET /backups no trae una corrida Exitosa del historial"
        return 0
    else
        curl_operador_con "$listado" -K - -o /dev/null "${URL_NUEVO}/backups/${vieja}/contenido"
        if [[ "$CURL_RC" -ne 0 ]]; then
            listado=""
            v_falla V8 "GET /backups/${vieja}/contenido (la más vieja): curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
            return 0
        fi
        historial="$cantidad corridas en el historial, descarga de la #$vieja"
    fi
    curl_operador_con "$listado" -K - -X POST "${URL_NUEVO}/backups"
    if [[ "$CURL_RC" -ne 0 ]]; then
        listado=""
        v_falla V8 "POST /backups: curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
        return 0
    fi
    # La corrida nueva es la de id mayor que el máximo de antes del POST (no se compara la hora de
    # esta máquina con la del servidor). Espera acotada con reloj monotónico.
    limite=$(($(orq_reloj) + BACKUP_ESPERA))
    while :; do
        curl_operador_con "$listado" -K - "${URL_NUEVO}/backups"
        if [[ "$CURL_RC" -ne 0 ]]; then
            listado=""
            v_falla V8 "GET /backups tras el POST: curl salió con $CURL_RC: $(primera_linea "$CURL_ERR")"
            return 0
        fi
        rc=0
        nuevas="$(printf '%s' "$CURL_CUERPO" | jq -r --argjson m "$maximo" '[.[] | select(.id > $m) | .resultado] | join(" ")')" || rc=$?
        if [[ "$rc" -ne 0 ]]; then
            listado=""
            v_falla V8 "GET /backups tras el POST no devolvió una lista legible"
            return 0
        fi
        exitosas=0
        otras=""
        for nuevo_resultado in $nuevas; do
            if [[ "$nuevo_resultado" == "Exitosa" ]]; then exitosas=1; else otras+="$nuevo_resultado "; fi
        done
        if [[ -n "$otras" ]]; then
            listado=""
            v_falla V8 "la corrida de POST /backups terminó ${otras% }"
            return 0
        fi
        if [[ "$exitosas" -eq 1 ]]; then
            break
        fi
        if [[ "$(orq_reloj)" -ge "$limite" ]]; then
            listado=""
            v_falla V8 "POST /backups no terminó en una corrida Exitosa en ${BACKUP_ESPERA} s"
            return 0
        fi
        sleep 2
    done
    listado=""
    v_ok V8 "$historial y una corrida nueva Exitosa"
}

verificar_v10() {
    if [[ "$MODO" == "ensayo" ]]; then
        verificar_v10_ensayo
        return 0
    fi
    orq_ssh VIEJO "systemctl is-enabled stockapp-api"
    if [[ "$ORQ_SALIDA" != "masked" || ( "$ORQ_RC" -ne 0 && "$ORQ_RC" -ne 1 ) ]]; then
        v_falla V10 "is-enabled dice '${ORQ_SALIDA:-no pude consultar}' (se esperaba masked)"
        return 0
    fi
    orq_ssh VIEJO "systemctl is-active stockapp-api"
    if [[ "$ORQ_SALIDA" != "inactive" || "$ORQ_RC" -ne 3 ]]; then
        v_falla V10 "is-active dice '${ORQ_SALIDA:-no pude consultar}' (se esperaba inactive)"
        return 0
    fi
    misma_lista_de_contenedores V10 || return 0
    v_ok V10
}

# misma_lista_de_contenedores <V>: docker ps del viejo igual a contenedores-viejo.txt (0) o v_falla (1).
misma_lista_de_contenedores() {
    local actual esperado
    orq_ssh VIEJO "docker ps --format '{{.Names}}'"
    if [[ "$ORQ_RC" -ne 0 || -z "$ORQ_SALIDA" ]]; then
        v_falla "$1" "no pude consultar la lista de contenedores (salió con $ORQ_RC)"
        return 1
    fi
    actual="$(printf '%s\n' "$ORQ_SALIDA" | LC_ALL=C sort)"
    esperado="$(cat "$ORQ_CORRIDA/contenedores-viejo.txt" 2>/dev/null)" || esperado=""
    if [[ "$actual" != "$esperado" ]]; then
        v_falla "$1" "la lista de contenedores cambió: antes '$(printf '%s' "$esperado" | tr '\n' ' ')', ahora '$(printf '%s' "$actual" | tr '\n' ' ')'"
        return 1
    fi
}

# V10 invertido del ensayo: el viejo (testing) quedó como estaba: active, con la misma habilitación
# que antes del volcado (nunca masked) y el mismo docker ps.
verificar_v10_ensayo() {
    orq_ssh VIEJO "systemctl is-enabled stockapp-api"
    if [[ ( "$ORQ_RC" -ne 0 && "$ORQ_RC" -ne 1 ) || ! "$ORQ_SALIDA" =~ ^[a-z-]+$ ]]; then
        v_falla V10 "no pude consultar is-enabled (salió con $ORQ_RC)"
        return 0
    fi
    if [[ "$ORQ_SALIDA" == "masked" || "$ORQ_SALIDA" != "$HABILITACION_VIEJO" ]]; then
        v_falla V10 "is-enabled dice '$ORQ_SALIDA' (antes del volcado decía '${HABILITACION_VIEJO:-?}'; el ensayo no enmascara)"
        return 0
    fi
    orq_ssh VIEJO "systemctl is-active stockapp-api"
    if [[ "$ORQ_SALIDA" != "active" || "$ORQ_RC" -ne 0 ]]; then
        v_falla V10 "is-active dice '${ORQ_SALIDA:-no pude consultar}' (se esperaba active: el ensayo lo deja como estaba)"
        return 0
    fi
    misma_lista_de_contenedores V10 || return 0
    v_ok V10 "active y '$HABILITACION_VIEJO', como antes del ensayo"
}

V_FALLIDAS=""
corte_paso12() {
    local v
    paso 12
    verificar_v1
    verificar_v2
    verificar_v3
    verificar_v4
    verificar_v5
    verificar_v6
    verificar_v7
    verificar_v8
    # --desde-backup: no hay VPS viejo, así que no hay V10.
    if [[ "$MODO" != "desde-backup" ]]; then
        verificar_v10
    fi
    TOKEN=""
    # La referencia de --revertir: si después de esto LogsAuditoria crece, hubo escrituras reales.
    psql_nuevo 'SELECT count(*) FROM "LogsAuditoria";'
    if [[ "$ORQ_RC" -eq 0 && "$ORQ_SALIDA" =~ ^[0-9]+$ ]]; then
        orq_estado_poner logs_auditoria_post_verificacion "$ORQ_SALIDA"
    else
        echo "  - no pude contar LogsAuditoria en el VPS nuevo (salió con $ORQ_RC): --revertir no va a tener valor de referencia."
    fi
    V_FALLIDAS=""
    for v in "${V_ORDEN[@]}"; do
        # OMITIDO (V6 sin manifiesto) no es un fallo: la spec la pide solo con manifiesto.
        if [[ "$v" == "V10" && "$MODO" == "desde-backup" ]] || [[ "${V_ESTADO[$v]:-}" == "OMITIDO" ]]; then
            continue
        fi
        if [[ "${V_ESTADO[$v]:-}" != "OK" ]]; then
            V_FALLIDAS+="${V_FALLIDAS:+, }$v"
        fi
    done
    if [[ -z "$V_FALLIDAS" ]]; then
        cerrar_paso 12 OK
    else
        PASO_ABIERTO=0
        orq_fin_paso FALLÓ "$V_FALLIDAS"
    fi
}

# tabla_de_tiempos: los pasos y la indisponibilidad del ensayo, en Markdown, para pegar en el runbook.
tabla_de_tiempos() {
    local n valor marca seg desde hasta a b
    echo "Tiempos del ensayo (para pegar en el runbook):"
    echo
    echo "| Paso | Resultado | Duración |"
    echo "|---|---|---|"
    for ((n = 1; n <= PASOS_TOTAL; n++)); do
        valor="$(orq_estado_leer "paso_$n" 2>/dev/null)" || valor=""
        marca="${valor%% *}"
        seg="${valor##* }"
        if [[ "$valor" =~ $FORMA_DE_PASO ]]; then
            printf '| (%2d/%d) %s | %s | %s s |\n' "$n" "$PASOS_TOTAL" "${PASO_NOMBRES[$n]}" "$marca" "$seg"
        else
            printf '| (%2d/%d) %s | — | — |\n' "$n" "$PASOS_TOTAL" "${PASO_NOMBRES[$n]}"
        fi
    done
    desde="$(orq_estado_leer congelado_utc 2>/dev/null)" || desde=""
    hasta="$(orq_estado_leer api_nueva_activa_utc 2>/dev/null)" || hasta=""
    if a="$(date -u -d "$desde" +%s 2>/dev/null)" && b="$(date -u -d "$hasta" +%s 2>/dev/null)" && [[ "$b" -ge "$a" ]]; then
        printf '| Indisponibilidad estimada del corte (congelamiento -> API nueva activa) | | %d min %d s |\n' $(((b - a) / 60)) $(((b - a) % 60))
    else
        echo "| Indisponibilidad estimada del corte (congelamiento -> API nueva activa) | | sin dato |"
    fi
    echo
}

# overrides_del_ensayo: los nombres de overrides-api-env.txt de este ensayo que faltan en el .env del
# servidor nuevo. Solo informa: el ensayo no se frena por esto (el corte sí, en el pre-vuelo).
overrides_del_ensayo() {
    local archivo="$ORQ_CORRIDA/$VOLCADO_DIR/overrides-api-env.txt" rc=0
    if [[ ! -f "$archivo" ]]; then
        echo "Overrides: el volcado no dejó overrides-api-env.txt: no hay lista."
        return 0
    fi
    comparar_overrides "$archivo" || rc=$?
    case "$rc" in
        0)
            if [[ -n "$OVERRIDES_FALTAN" ]]; then
                echo "Overrides que faltan en el .env del servidor nuevo: $OVERRIDES_FALTAN"
                echo "  (cargalos a mano en ~/stockapp-deploy/.env del nuevo y reinstalá con deploy/deploy-vps.sh <v> --sin-backup; el pre-vuelo del corte lo exige)"
            elif [[ -n "$OVERRIDES_SIN_INSTALAR" ]]; then
                echo "Overrides en ~/stockapp-deploy/.env pero no en /etc/stockapp-api/api.env: $OVERRIDES_SIN_INSTALAR"
                echo "  (la API no los tiene: reinstalá con deploy/deploy-vps.sh <v> --sin-backup contra el nuevo; el pre-vuelo del corte lo exige)"
            else
                echo "Overrides: los $OVERRIDES_CANTIDAD del viejo ya están en el .env y en el api.env del servidor nuevo."
            fi
            ;;
        3) echo "Overrides: $archivo tiene una línea que no es un nombre de variable." ;;
        *) echo "Overrides: no pude leer las claves del .env del servidor nuevo (salió con $ORQ_RC): revisalo antes del corte." ;;
    esac
}

corte_paso13() {
    local congelado fin e n valor resultado nombre detalle que="corte"
    paso 13
    [[ "$MODO" == "ensayo" ]] && que="ensayo"
    [[ "$MODO" == "desde-backup" ]] && que="--desde-backup"
    congelado="$(orq_estado_leer congelado_utc)" || congelado=""
    if [[ "$MODO" == "ensayo" || "$MODO" == "desde-backup" ]]; then
        # Sin viejo congelado no hay rollback que vencer.
        fin=""
    elif e="$(date -u -d "$congelado" +%s 2>/dev/null)"; then
        fin="$(date -u -d "@$((e + 14 * 86400))" +%Y-%m-%dT%H:%M:%SZ)"
        orq_estado_poner fin_rollback "$fin"
    else
        fin="(no pude calcularlo: congelado_utc ilegible)"
    fi
    if [[ -z "$V_FALLIDAS" ]]; then
        resultado="GO"
    else
        resultado="NO-GO"
    fi

    echo
    echo "== Resumen del $que =="
    if [[ "$resultado" == "GO" ]]; then
        echo "Resultado: GO técnico: todas las verificaciones automáticas dieron OK."
    else
        echo "Resultado: NO-GO: fallaron $V_FALLIDAS. La API nueva queda arriba para diagnosticar (las PCs no están instaladas: nadie escribe)."
    fi
    echo "Pasos:"
    for ((n = 1; n <= PASOS_TOTAL; n++)); do
        valor="$(orq_estado_leer "paso_$n" 2>/dev/null)" || valor="—"
        printf '  (%2d/%d) %-42s %s\n' "$n" "$PASOS_TOTAL" "${PASO_NOMBRES[$n]}" "$valor"
    done
    echo "Verificación:"
    for v in V1 V2 V3 V4 V5 V6 V7 V8 V9 V10; do
        if [[ "$v" == "V9" ]]; then
            printf '  %-4s %-46s %s\n' V9 "smoke de la GUI" "PENDIENTE (agente con scripts/gui-verificacion)"
            continue
        fi
        nombre="${V_NOMBRE[$v]}"
        if [[ "$v" == "V10" && "$MODO" == "ensayo" ]]; then
            nombre="VPS viejo active, sin enmascarar y sin cambios"
        fi
        if [[ "$v" == "V10" && "$MODO" == "desde-backup" ]]; then
            printf '  %-4s %-46s %s\n' V10 "$nombre" "OMITIDO  (no hay VPS viejo)"
            continue
        fi
        detalle="${V_DETALLE[$v]:-}"
        printf '  %-4s %-46s %s%s\n' "$v" "$nombre" "${V_ESTADO[$v]:-FALLÓ}" "${detalle:+  ($detalle)}"
    done
    if [[ "$MODO" == "ensayo" ]]; then
        echo "Aviso: el ensayo detiene testing mientras dura el volcado (desde $congelado); --ensayo lo vuelve a arrancar y V10 lo comprueba."
        echo "El VPS nuevo queda con los datos del ensayo y la API arriba; el corte los reemplaza con --recrear-base."
        echo
        tabla_de_tiempos
        overrides_del_ensayo
        echo "Pendiente:"
        echo "  - V9: el smoke de la GUI sobre los datos del ensayo, por un agente con scripts/gui-verificacion"
        echo "  - pegar la tabla de tiempos en el runbook y cargar los overrides que falten"
    elif [[ "$MODO" == "desde-backup" ]]; then
        echo "Backup restaurado: ${BACKUP} (creado ${FECHA_BACKUP}). Todo lo posterior a esa fecha se perdió."
        echo "Pendiente:"
        echo "  - V9: el smoke de la GUI, por un agente con scripts/gui-verificacion"
        echo "  - la decisión de GO de Martin"
        echo "  - apuntar las PCs al servidor nuevo"
    else
        echo "Pendiente:"
        echo "  - V9: el smoke de la GUI, por un agente con scripts/gui-verificacion"
        echo "  - la decisión de GO de Martin"
        echo "  - instalar las PCs"
        echo "Fechas:"
        echo "  - corte (congelamiento del viejo): $congelado"
        echo "  - fin del rollback (D+14):         $fin"
        echo "Para volver atrás (el viejo vuelve a servir y el nuevo se detiene):"
        echo "    deploy/migrar-vps.sh --revertir $ORQ_CORRIDA"
    fi

    cerrar_paso 13 OK
    # resultado es lo último que se escribe.
    orq_estado_poner resultado "$resultado"
    if [[ "$resultado" == "GO" ]]; then
        exit 0
    fi
    exit 7
}

# ---------------------------------------------------------------------------------------
# Principal
# ---------------------------------------------------------------------------------------

# ---------------------------------------------------------------------------------------
# --continuar <corrida> (spec 6.5.4, A13)
# ---------------------------------------------------------------------------------------

# leer_clave_continuar <clave> -> VALOR_CLAVE, del estado de la corrida OBJETIVO (la de --continuar o
# --revertir); sale 0 si está, 1 si no; un estado ilegible es 8.
VALOR_CLAVE=""
leer_clave_continuar() {
    local rc=0
    VALOR_CLAVE="$(orq_estado_de "$OBJETIVO/estado" "$1" 2>/dev/null)" || rc=$?
    case "$rc" in
        0 | 1) return "$rc" ;;
        *) abortar 8 "no pude leer el estado de $OBJETIVO." ;;
    esac
}

# paso_hecho <n>: 0 si paso_<n> es ok u omitido (el contrato), 1 si no está. Cualquier otro valor no
# se interpreta: no se retoma a ciegas (A13).
paso_hecho() {
    leer_clave_continuar "paso_$1" || return 1
    if [[ "$VALOR_CLAVE" =~ $FORMA_DE_PASO ]]; then
        return 0
    fi
    abortar 8 "paso_$1='$VALOR_CLAVE' en $CONTINUAR/estado no es 'ok|omitido <utc> <segundos>': no sé dónde quedó la corrida y no la retomo a ciegas."
}

# recargar_volcado: el volcado de la corrida (pasos 6 y 7 ya hechos) desde el estado, verificado contra
# los archivos locales: el mismo nombre, el mismo sha256 y el manifiesto que lo nombra.
# cargar_volcado_dir: la carpeta del último intento del paso 6, del estado y nunca por defecto: si el 6
# se reintentó, la del primer intento puede tener otro volcado (A11).
cargar_volcado_dir() {
    leer_clave_continuar volcado_dir || abortar 3 "el estado no tiene volcado_dir= aunque el paso 6 está hecho: no sé en qué carpeta quedó el volcado de esta corrida, y no lo adivino (A11)."
    if [[ ! "$VALOR_CLAVE" =~ ^volcado(-[0-9]+)?$ || ! -d "$ORQ_CORRIDA/$VALOR_CLAVE" ]]; then
        abortar 3 "volcado_dir=$VALOR_CLAVE del estado no es una carpeta de volcado de esta corrida."
    fi
    VOLCADO_DIR="$VALOR_CLAVE"
}

recargar_volcado() {
    local dir sha_local linea
    leer_clave_continuar volcado || abortar 3 "el estado no tiene volcado= aunque el paso 7 está hecho: no sé qué volcado restaurar."
    VOLCADO="$VALOR_CLAVE"
    leer_clave_continuar sha256 || abortar 3 "el estado no tiene sha256= aunque el paso 7 está hecho."
    SHA_VOLCADO="$VALOR_CLAVE"
    dir="$ORQ_CORRIDA/$VOLCADO_DIR"
    if [[ ! "$VOLCADO" =~ ^volcado-([0-9]{8}T[0-9]{6}Z)\.dump$ ]]; then
        abortar 3 "volcado=$VOLCADO del estado no tiene la forma que deja migrar."
    fi
    MANIFIESTO="volcado-${BASH_REMATCH[1]}.manifiesto"
    if ! sha_local="$(sha256sum -- "$dir/$VOLCADO" 2>/dev/null)" || [[ "${sha_local%% *}" != "$SHA_VOLCADO" ]]; then
        abortar 6 "el volcado de esta corrida ($dir/$VOLCADO) falta o no tiene el sha256 del estado: no se restaura otro (A11)."
    fi
    ULTIMA_MIGRACION=""
    while IFS= read -r linea || [[ -n "$linea" ]]; do
        [[ "$linea" == "[conteos]" ]] && break
        case "$linea" in
            ultima_migracion=*) ULTIMA_MIGRACION="${linea#ultima_migracion=}" ;;
        esac
    done < "$dir/$MANIFIESTO" || abortar 6 "no pude leer el manifiesto $dir/$MANIFIESTO."
    if ! grep -qxF -- "dump=$VOLCADO" "$dir/$MANIFIESTO" || [[ ! "$ULTIMA_MIGRACION" =~ ^[0-9]{14}_[A-Za-z0-9_]+$ ]]; then
        abortar 6 "el manifiesto $dir/$MANIFIESTO no nombra a $VOLCADO o no trae una ultima_migracion legible."
    fi
}

# viejo_sigue_congelado <desde>: en el corte, retomar exige que el viejo siga congelado. Si alguien lo
# volvió a arrancar, restaurar el volcado perdería lo escrito después del dump.
viejo_sigue_congelado() {
    local desde="$1"
    if [[ "$desde" -eq 6 ]] && unit_movida_sin_enmascarar; then
        abortar 3 "no se retoma: $MOTIVO_SOLO_REVERTIR No se tocó nada. deploy/migrar-vps.sh --revertir $ORQ_CORRIDA"
    fi
    estado_api VIEJO
    habilitacion_viejo
    if [[ "$desde" -eq 6 ]]; then
        # El volcado anterior pudo fallar antes de enmascarar: alcanza con que no esté sirviendo.
        if [[ "$ESTADO_API" != "active" ]]; then
            return 0
        fi
    elif [[ "$ESTADO_API" == "inactive" && "$HABILITACION" == "masked" ]]; then
        return 0
    fi
    abortar 3 "el VPS viejo ya no está congelado (stockapp-api dice '$ESTADO_API' y su unit '$HABILITACION'): alguien lo volvió a arrancar y restaurar ahora perdería lo escrito después del dump. No se tocó nada. Para cerrar esta corrida: deploy/migrar-vps.sh --revertir $ORQ_CORRIDA; después, una corrida nueva."
}

# desde_donde_retomar -> DESDE: el primer paso sin hacer, con las reglas de las notas 1 y 2.
DESDE=0
desde_donde_retomar() {
    local n primero=14 restaurar=""
    for ((n = 1; n <= PASOS_TOTAL; n++)); do
        if ! paso_hecho "$n"; then
            primero="$n"
            break
        fi
    done
    if [[ "$primero" -lt 6 ]]; then
        abortar 3 "la corrida registra el congelamiento pero no el paso $primero: no sé dónde quedó y no la retomo a ciegas. Mirá el estado del viejo y, si corresponde, deploy/migrar-vps.sh --revertir $ORQ_CORRIDA."
    fi
    if [[ "$primero" -ge 12 ]]; then
        # Todo hecho, o un NO-GO: se repite la verificación y el resumen.
        DESDE=12
        return 0
    fi
    DESDE="$primero"
    if [[ "$primero" -eq 9 ]]; then
        if leer_clave_continuar restaurar_salio; then
            restaurar="$VALOR_CLAVE"
        fi
        case "$restaurar" in
            3 | 4) DESDE=8 ;;
            6 | 7)
                if [[ "$REINTENTAR_RESTAURACION" -ne 1 ]]; then
                    abortar 3 "restaurar-backup.sh salió con $restaurar: la base del nuevo QUEDÓ restaurada pero no verifica, y eso lo decide un humano. Investigá y después elegí: deploy/migrar-vps.sh --continuar $ORQ_CORRIDA --reintentar-restauracion (vuelve a --recrear-base, con otro dump de seguridad) o deploy/migrar-vps.sh --revertir $ORQ_CORRIDA."
                fi
                ;;
        esac
        if [[ "$REINTENTAR_RESTAURACION" -eq 1 && "$restaurar" != 6 && "$restaurar" != 7 ]]; then
            abortar 3 "--reintentar-restauracion es para una restauración que salió con 6 o 7, y esta salió con '${restaurar:-?}': sin la opción, --continuar ya sabe cómo seguir."
        fi
    elif [[ "$REINTENTAR_RESTAURACION" -eq 1 ]]; then
        abortar 3 "--reintentar-restauracion es para una restauración que salió con 6 o 7, y esta corrida retoma en el paso $primero."
    fi
}

# cargar_parametros_de_arnes: contenedores y raíces remotas salen del estado de la corrida (R38). Uno
# pasado explícito con otro valor sale 2 sin tocar nada: cambiar de contenedor a mitad de una corrida
# es empezar otra. Un valor que no es el default cuenta como parámetro de arnés: con hosts reales,
# cargar_config lo rechaza.
cargar_parametros_de_arnes() {
    local clave opcion variable defecto
    for clave in contenedor_viejo contenedor_nuevo raiz_remota_viejo raiz_remota_nuevo; do
        case "$clave" in
            contenedor_viejo) opcion=--contenedor-viejo variable=CONTENEDOR_VIEJO defecto=stockapp-pg ;;
            contenedor_nuevo) opcion=--contenedor-nuevo variable=CONTENEDOR_NUEVO defecto=stockapp-pg ;;
            raiz_remota_viejo) opcion=--raiz-remota-viejo variable=RAIZ_VIEJO defecto="" ;;
            raiz_remota_nuevo) opcion=--raiz-remota-nuevo variable=RAIZ_NUEVO defecto="" ;;
        esac
        leer_clave_continuar "$clave" || abortar 3 "el estado de $OBJETIVO no dice $clave: no sé contra qué contenedor o raíz operar, y no lo adivino."
        if [[ -n "${ARNES_DADO[$opcion]-}" && "${!variable}" != "$VALOR_CLAVE" ]]; then
            abortar 2 "$opcion '${!variable}' no es el de la corrida ('$VALOR_CLAVE'): se opera con el contexto de su estado. No se tocó nada."
        fi
        case "$clave" in
            contenedor_*)
                if [[ ! "$VALOR_CLAVE" =~ ^stockapp[a-z0-9-]*$ ]]; then
                    abortar 3 "$clave='$VALOR_CLAVE' del estado no matchea ^stockapp[a-z0-9-]*\$."
                fi
                ;;
            raiz_remota_*)
                if [[ -n "$VALOR_CLAVE" && ( ! "$VALOR_CLAVE" =~ ^/[A-Za-z0-9._/-]+$ || "$VALOR_CLAVE" == *..* ) ]]; then
                    abortar 3 "$clave='$VALOR_CLAVE' del estado no es una ruta absoluta sin '..'."
                fi
                ;;
        esac
        printf -v "$variable" '%s' "$VALOR_CLAVE"
        if [[ "$VALOR_CLAVE" != "$defecto" && -z "${ARNES_DADO[$opcion]-}" ]]; then
            ARNES_USADO+=("$clave (del estado)")
        fi
    done
}

continuar() {
    local modo rc=0 n
    ORQ_CORRIDA=""
    OBJETIVO="$CONTINUAR"
    if [[ ! -d "$CONTINUAR" || ! -f "$CONTINUAR/estado" ]]; then
        abortar 3 "$CONTINUAR no es una corrida con estado: no hay nada que retomar."
    fi
    # Fase A, de solo lectura: el modo, los .env y la identidad de los hosts salen del estado.
    leer_clave_continuar modo || abortar 3 "el estado de $CONTINUAR no dice el modo."
    modo="$VALOR_CLAVE"
    if [[ ( "$modo" != corte && "$modo" != ensayo && "$modo" != desde-backup ) || "${CONTINUAR##*/}" != "$modo-"* ]]; then
        abortar 3 "el estado de $CONTINUAR dice modo=$modo: --continuar retoma corridas de corte o de ensayo."
    fi
    MODO="$modo"
    # R32 y R41: sin congelado_utc la corrida murió antes de congelar y no hay nada que retomar. Se cierra
    # ANTES de exigir el contexto (env_*, contenedores, machine-id): una corrida que murió entre modo= y
    # esas claves no tiene ese contexto, y abierta bloquearía todo corte nuevo (R24). No toca ningún servidor.
    leer_clave_continuar congelado_utc || rc=$?
    if [[ "$rc" -ne 0 ]]; then
        ORQ_CORRIDA="$CONTINUAR"
        orq_log_iniciar
        orq_lock
        LOCK_TOMADO=1
        if leer_clave_continuar resultado; then
            abortar 3 "la corrida ya terminó (resultado=$VALOR_CLAVE): no hay nada que retomar. No se tocó nada."
        fi
        orq_estado_poner resultado FALLO-ABANDONADA
        if [[ "$MODO" == "desde-backup" ]]; then
            # Nunca congela nada: no hay qué retomar. Su paso 9 recrea la base, así que se corre entera otra vez.
            abortar 3 "una corrida de --desde-backup no se retoma: quedó cerrada con resultado=FALLO-ABANDONADA. Volvé a correr deploy/migrar-vps.sh --desde-backup <dump> [--manifiesto <m>] (su restauración recrea la base del nuevo con un dump de seguridad de lo que tenga)."
        fi
        abortar 3 "la corrida no llegó a congelar el VPS viejo, que siguió sirviendo: no hay nada que retomar. Quedó cerrada con resultado=FALLO-ABANDONADA; empezá una corrida nueva."
    fi
    leer_clave_continuar env_viejo || abortar 3 "el estado de $CONTINUAR no dice env_viejo."
    ENV_VIEJO="$VALOR_CLAVE"
    leer_clave_continuar env_nuevo || abortar 3 "el estado de $CONTINUAR no dice env_nuevo."
    ENV_NUEVO="$VALOR_CLAVE"
    cargar_parametros_de_arnes
    cargar_config
    verificar_identidad
    leer_clave_continuar machine_id_viejo || VALOR_CLAVE=""
    if [[ "$VALOR_CLAVE" != "$MACHINE_ID_VIEJO" ]]; then
        abortar 9 "el VPS viejo de $ENV_VIEJO (machine-id ${MACHINE_ID_VIEJO:0:8}) no es el de la corrida (${VALOR_CLAVE:0:8}): un .env que cambió de servidor a mitad de una corrida es un error. No se tocó nada."
    fi
    leer_clave_continuar machine_id_nuevo || VALOR_CLAVE=""
    if [[ "$VALOR_CLAVE" != "$MACHINE_ID_NUEVO" ]]; then
        abortar 9 "el VPS nuevo de $ENV_NUEVO (machine-id ${MACHINE_ID_NUEVO:0:8}) no es el de la corrida (${VALOR_CLAVE:0:8}): un .env que cambió de servidor a mitad de una corrida es un error. No se tocó nada."
    fi

    # Fase B: el log de la corrida (se agrega al que ya tiene) y el lock. Recién con el lock se decide.
    ORQ_CORRIDA="$CONTINUAR"
    orq_log_iniciar
    orq_lock
    LOCK_TOMADO=1
    echo "migrar-vps: --continuar ($MODO)"
    echo "  corrida: $ORQ_CORRIDA"
    echo "  viejo: $VIEJO_VPS_HOST (machine-id ${MACHINE_ID_VIEJO:0:8})"
    echo "  nuevo: $NUEVO_VPS_HOST (machine-id ${MACHINE_ID_NUEVO:0:8})"

    if leer_clave_continuar resultado && [[ "$VALOR_CLAVE" != "NO-GO" ]]; then
        abortar 3 "la corrida ya terminó (resultado=$VALOR_CLAVE): no hay nada que retomar. No se tocó nada."
    fi
    # R4: desde acá la corrida vuelve a estar abierta (también si era un NO-GO) hasta su nuevo resultado.
    orq_estado_quitar resultado

    desde_donde_retomar
    leer_clave_continuar habilitacion_viejo_inicial || VALOR_CLAVE=""
    HABILITACION_VIEJO="$VALOR_CLAVE"
    if [[ "$MODO" == "corte" ]]; then
        viejo_sigue_congelado "$DESDE"
    fi
    obtener_contrasena
    # La versión sale del estado: con el viejo congelado la API nueva está detenida (la detiene el paso 3
    # y la arranca el 11) y no sirve el feed. Una corrida sin esa clave (anterior a este cambio) lo lee.
    if leer_clave_continuar version_nuevo && [[ -n "$VALOR_CLAVE" ]]; then
        VERSION_NUEVO="$VALOR_CLAVE"
    else
        leer_version_del_feed
    fi
    # Todo paso retomado lee su entrada del estado: desde el 7, la carpeta del volcado.
    if [[ "$DESDE" -ge 7 ]]; then
        cargar_volcado_dir
    fi
    if [[ "$DESDE" -ge 8 ]]; then
        recargar_volcado
    fi
    if [[ "$DESDE" -ge 9 ]]; then
        leer_clave_continuar dir_remoto || abortar 3 "el estado no tiene dir_remoto= aunque el paso 8 está hecho."
        D_REL="${VALOR_CLAVE#\~/}"
        if [[ ! "$D_REL" =~ ^stockapp-deploy/migracion/(corte|ensayo)-[0-9]{8}T[0-9]{6}Z(-r[0-9]{8}T[0-9]{6}Z)?$ ]]; then
            abortar 3 "dir_remoto=$VALOR_CLAVE del estado no tiene la forma que deja migrar."
        fi
    fi
    if [[ "$DESDE" -ge 10 ]]; then
        RESTAURAR_RC=0
    fi
    CONGELADO=1
    # El reloj de indisponibilidad sigue corriendo en el corte. En el ensayo el viejo ya volvió a servir
    # (salvo que se repita el volcado: el paso 6 lo arranca otra vez).
    if [[ "$MODO" == "corte" ]]; then
        reloj_desde_congelado
    fi
    echo "  retoma en el paso $DESDE (${PASO_NOMBRES[$DESDE]})"

    for ((n = DESDE; n <= 11; n++)); do
        "corte_paso$n"
    done
    corte_paso12
    corte_paso13
}

# ---------------------------------------------------------------------------------------
# --revertir [<corrida>] (spec 6.5.6, R-A)
# ---------------------------------------------------------------------------------------
# Deshace el congelamiento del VPS viejo y lo vuelve a arrancar, con la API nueva detenida PRIMERO. Cada
# sub-paso mira el estado OBSERVADO del viejo (enmascarada, dónde está la unit, is-active), no lo que dice
# la corrida: así funciona desde cualquier punto en que haya muerto el volcado, y re-correrlo no hace nada.

UNIT_VIEJO=""
UNIT_CONGELADA_VIEJO=""
HAY_UNIT=0
HAY_CONGELADA=0
DELTA_ESCRITURAS=""

# ultima_corrida_de_corte -> OBJETIVO: la última corrida de corte cuyo estado registra el congelamiento.
ultima_corrida_de_corte() {
    local base="$HOME/stockapp-backups-vps/corridas" dir rc
    OBJETIVO=""
    if [[ -z "${HOME:-}" || ! -d "$base" ]]; then
        return 1
    fi
    for dir in "$base"/corte-*/; do
        dir="${dir%/}"
        [[ -d "$dir" && -f "$dir/estado" && "${dir##*/}" =~ ^corte-[0-9]{8}T[0-9]{6}Z$ ]] || continue
        rc=0
        orq_estado_de "$dir/estado" congelado_utc >/dev/null 2>&1 || rc=$?
        if [[ "$rc" -eq 0 ]]; then
            OBJETIVO="$dir"
        fi
    done
    [[ -n "$OBJETIVO" ]]
}

# ayuda_de_revertir <codigo>: lo que lee el operador cuando la vuelta atrás no terminó (producción caída).
ayuda_de_revertir() {
    {
        echo
        echo "== La vuelta atrás no terminó (exit $1) =="
        if [[ -n "${ORQ_CORRIDA:-}" ]]; then
            echo "Registro: $ORQ_CORRIDA/registro.log"
        fi
        echo "Re-correrla no rompe nada (cada paso mira el estado real y hace solo lo que falta):"
        echo "    deploy/migrar-vps.sh --revertir${OBJETIVO:+ $OBJETIVO}"
        echo "A mano: primero, en el VPS nuevo,"
        echo "    sudo systemctl stop stockapp-api"
        echo "    sudo systemctl disable stockapp-api"
        echo "Si el VPS nuevo no responde por ssh, apagalo desde el panel del proveedor (o asegurate de que su"
        echo "API no atiende) antes de tocar el viejo: con dos APIs vivas, lo escrito en la nueva se pierde."
        echo "y después, en el VPS viejo y en este orden (el unmask si is-enabled dice masked; el mv solo si"
        echo "existe el .congelado y NO existe la unit; si existen los dos, no pises ninguno):"
        comandos_de_vuelta_atras
    } >&2
}

# poner_en_objetivo <clave> <valor>: escribe en el estado de la corrida revertida (la lib escribe en ORQ_CORRIDA).
poner_en_objetivo() {
    local propia="$ORQ_CORRIDA" rc=0
    ORQ_CORRIDA="$OBJETIVO"
    orq_estado_poner "$1" "$2" || rc=$?
    ORQ_CORRIDA="$propia"
    return "$rc"
}

# habilitacion_revertir -> HABILITACION: la palabra de is-enabled del viejo, o not-found si systemctl no
# encuentra la unit (salida vacía con 1: la unit está en .congelado). Lo demás es "no pude consultar".
habilitacion_revertir() {
    orq_ssh VIEJO "systemctl is-enabled stockapp-api"
    case "$ORQ_RC" in
        0 | 1) ;;
        *) consulta_ok "la habilitación de stockapp-api en el VPS viejo" ;;
    esac
    if [[ "$ORQ_RC" -eq 1 && -z "$ORQ_SALIDA" ]]; then
        HABILITACION="not-found"
    elif [[ "$ORQ_SALIDA" =~ ^[a-z-]+$ ]]; then
        HABILITACION="$ORQ_SALIDA"
    else
        abortar 8 "no pude interpretar 'systemctl is-enabled stockapp-api' del VPS viejo: '${ORQ_SALIDA}'."
    fi
}

# existe_en_viejo <ruta>: 0 si existe, 1 si no. Lo que no se puede consultar es 8, nunca "no existe".
existe_en_viejo() {
    orq_ssh VIEJO "test -e $1"
    if [[ "$ORQ_RC" -eq 0 && -z "$ORQ_STDERR" ]]; then
        return 0
    fi
    if [[ "$ORQ_RC" -eq 1 && -z "$ORQ_STDERR" ]]; then
        return 1
    fi
    volcar_stderr
    abortar 8 "no pude consultar si existe $1 en el VPS viejo (salió con $ORQ_RC)."
}

# archivos_de_la_unit -> HAY_UNIT y HAY_CONGELADA. Con la unit enmascarada, en su ruta está el symlink
# del mask (test -e lo ve): por eso los dos archivos son un problema solo sin el mask.
archivos_de_la_unit() {
    HAY_UNIT=0
    HAY_CONGELADA=0
    if existe_en_viejo "$UNIT_VIEJO"; then
        HAY_UNIT=1
    fi
    if existe_en_viejo "$UNIT_CONGELADA_VIEJO"; then
        HAY_CONGELADA=1
    fi
}

revertir_contexto() {
    if [[ -z "$REVERTIR_CORRIDA" ]]; then
        if ! ultima_corrida_de_corte; then
            abortar 3 "no hay ninguna corrida de corte que registre el congelamiento (congelado_utc) en ~/stockapp-backups-vps/corridas: sin la corrida no puedo comprobar la identidad de los servidores. Pasala explícita (--revertir <corrida>) o usá los comandos a mano."
        fi
    else
        OBJETIVO="$REVERTIR_CORRIDA"
    fi
    if [[ ! -d "$OBJETIVO" || ! -f "$OBJETIVO/estado" ]]; then
        abortar 3 "$OBJETIVO no es una corrida con estado."
    fi
    leer_clave_continuar modo || abortar 3 "el estado de $OBJETIVO no dice el modo."
    if [[ ( "$VALOR_CLAVE" != corte && "$VALOR_CLAVE" != ensayo ) || "${OBJETIVO##*/}" != "$VALOR_CLAVE-"* ]]; then
        abortar 3 "el estado de $OBJETIVO dice modo=$VALOR_CLAVE: --revertir deshace corridas de corte o de ensayo."
    fi
    leer_clave_continuar env_viejo || abortar 3 "el estado de $OBJETIVO no dice env_viejo: esa corrida no llegó a guardar contra qué servidores corría (si no congeló nada, cerrala con --continuar)."
    ENV_VIEJO="$VALOR_CLAVE"
    leer_clave_continuar env_nuevo || abortar 3 "el estado de $OBJETIVO no dice env_nuevo."
    ENV_NUEVO="$VALOR_CLAVE"
    cargar_parametros_de_arnes
    cargar_config
    UNIT_VIEJO="${RAIZ_VIEJO}/etc/systemd/system/stockapp-api.service"
    UNIT_CONGELADA_VIEJO="${UNIT_VIEJO}.congelado"
    # Identidad: los mismos servidores que la corrida (un .env que cambió de servidor es un error).
    verificar_identidad
    leer_clave_continuar machine_id_viejo || abortar 3 "el estado de $OBJETIVO no dice machine_id_viejo: no puedo comprobar que el viejo sea el de la corrida."
    if [[ "$VALOR_CLAVE" != "$MACHINE_ID_VIEJO" ]]; then
        abortar 9 "el VPS viejo de $ENV_VIEJO (machine-id ${MACHINE_ID_VIEJO:0:8}) no es el de la corrida (${VALOR_CLAVE:0:8}). No se tocó nada."
    fi
    leer_clave_continuar machine_id_nuevo || abortar 3 "el estado de $OBJETIVO no dice machine_id_nuevo: no puedo comprobar que el nuevo sea el de la corrida."
    if [[ "$VALOR_CLAVE" != "$MACHINE_ID_NUEVO" ]]; then
        abortar 9 "el VPS nuevo de $ENV_NUEVO (machine-id ${MACHINE_ID_NUEVO:0:8}) no es el de la corrida (${VALOR_CLAVE:0:8}). No se tocó nada."
    fi
}

# Paso 1: sudo -n en los dos y lo que se observa del viejo, todo de solo lectura. Los estados que no se
# pueden deshacer sin pisar algo salen con 8 ANTES de tocar nada (tampoco se detiene el nuevo).
revertir_prevuelo() {
    local p nombre
    abrir_paso 1 "pre-vuelo"
    for p in VIEJO NUEVO; do
        nombre="${p}_VPS_USER"
        orq_ssh "$p" "sudo -n true"
        if [[ "$ORQ_RC" -eq 255 ]]; then
            consulta_ok "el acceso por ssh al VPS ${p,,}"
        elif [[ "$ORQ_RC" -ne 0 ]]; then
            volcar_stderr
            abortar 3 "'sudo -n true' falló en el VPS ${p,,}: ${!nombre} necesita sudo sin contraseña. No se tocó nada."
        fi
    done
    estado_api VIEJO
    habilitacion_revertir
    archivos_de_la_unit
    ESTADO_VIEJO_TXT="stockapp-api dice '$ESTADO_API', su unit '$HABILITACION'"
    if [[ "$HABILITACION" == "masked" ]]; then
        if [[ "$HAY_CONGELADA" -eq 0 ]]; then
            abortar 8 "stockapp-api está enmascarada en el VPS viejo pero no existe $UNIT_CONGELADA_VIEJO: no sé dónde quedó la unit y no la invento. No se tocó nada."
        fi
        ESTADO_VIEJO_TXT+=" y la unit está en .congelado"
    elif [[ "$HAY_UNIT" -eq 1 && "$HAY_CONGELADA" -eq 1 ]]; then
        abortar 8 "en el VPS viejo existen $UNIT_VIEJO Y $UNIT_CONGELADA_VIEJO: no se pisa ninguno. Mirá cuál es la buena (diff) y dejá una sola. No se tocó nada."
    elif [[ "$HAY_UNIT" -eq 0 && "$HAY_CONGELADA" -eq 0 ]]; then
        abortar 8 "en el VPS viejo no existe $UNIT_VIEJO ni $UNIT_CONGELADA_VIEJO: no hay unit que devolver. No se tocó nada."
    elif [[ "$HAY_CONGELADA" -eq 1 ]]; then
        ESTADO_VIEJO_TXT+=" y la unit está en .congelado"
    else
        ESTADO_VIEJO_TXT+=" y la unit está en su lugar"
    fi
    echo "  - viejo: $ESTADO_VIEJO_TXT"
    cerrar_paso 1 OK
}

# Paso 2: la guardia de escrituras reales. Si LogsAuditoria del nuevo creció desde la verificación, hubo
# PCs trabajando: ya no es R-A sino R-B, y lo escrito en el nuevo no vuelve al viejo.
revertir_guardia() {
    local referencia="" actual delta
    abrir_paso 2 "escrituras en el VPS nuevo"
    if leer_clave_continuar logs_auditoria_post_verificacion; then
        referencia="$VALOR_CLAVE"
    fi
    psql_nuevo 'SELECT count(*) FROM "LogsAuditoria";'
    if [[ -z "$referencia" ]]; then
        # La corrida no llegó a verificar: sin referencia no hay guardia, solo se informa. Que no se
        # pueda contar (la base vacía de una restauración fallida no tiene la tabla) no frena la vuelta.
        if [[ "$ORQ_RC" -eq 0 && "$ORQ_SALIDA" =~ ^[0-9]+$ ]]; then
            echo "  - la corrida no llegó a verificar: sin valor de referencia. LogsAuditoria del nuevo tiene $ORQ_SALIDA registros (solo informativo)."
        else
            echo "  - la corrida no llegó a verificar: sin valor de referencia, y no pude contar LogsAuditoria del nuevo (salió con $ORQ_RC; solo informativo)."
        fi
        cerrar_paso 2 OMITIDO "sin valor de referencia"
        return 0
    fi
    if [[ ! "$referencia" =~ ^[0-9]+$ ]]; then
        abortar 8 "logs_auditoria_post_verificacion='$referencia' del estado no es un número: no puedo interpretarlo."
    fi
    if [[ "$ORQ_RC" -ne 0 || ! "$ORQ_SALIDA" =~ ^[0-9]+$ ]]; then
        if [[ "$AUN_CON_ESCRITURAS" -eq 1 ]]; then
            DELTA_ESCRITURAS="no pude contar LogsAuditoria del nuevo (salió con $ORQ_RC): las escrituras posteriores a la verificación son DESCONOCIDAS"
            echo "  - ATENCIÓN: $DELTA_ESCRITURAS; --aun-con-escrituras: sigue la confirmación."
            cerrar_paso 2 OMITIDO "--aun-con-escrituras"
            return 0
        fi
        volcar_stderr
        abortar 8 "no pude contar LogsAuditoria en el VPS nuevo (salió con $ORQ_RC): no sé si hubo escrituras reales después de la verificación, y eso NO es 'cero'. Si sabés que sí y querés volver igual al viejo: --aun-con-escrituras."
    fi
    actual="$ORQ_SALIDA"
    if [[ "$actual" -le "$referencia" ]]; then
        echo "  - LogsAuditoria del nuevo: $actual (al verificar: $referencia): sin escrituras posteriores a la verificación."
        cerrar_paso 2 OK
        return 0
    fi
    delta=$((actual - referencia))
    DELTA_ESCRITURAS="$delta registros nuevos en LogsAuditoria del VPS nuevo desde la verificación ($referencia -> $actual); lo escrito ahí NO vuelve al viejo"
    if [[ "$AUN_CON_ESCRITURAS" -ne 1 ]]; then
        SIN_AYUDA_DE_REVERTIR=1
        abortar 3 "hubo escrituras reales en el VPS nuevo después de la verificación: $DELTA_ESCRITURAS. Ya no es R-A sino R-B: seguí la sección R-B de deploy/MIGRACION-VPS.md. Si igual hay que volver al viejo, --aun-con-escrituras muestra el delta y pide la palabra. No se tocó nada."
    fi
    echo "  - ATENCIÓN: $DELTA_ESCRITURAS; --aun-con-escrituras: sigue la confirmación."
    cerrar_paso 2 OK "--aun-con-escrituras: $delta registros"
}

revertir_confirmacion() {
    local texto aviso=""
    if [[ -n "$DELTA_ESCRITURAS" ]]; then
        aviso="ATENCIÓN: $DELTA_ESCRITURAS."
    fi
    texto="$(
        cat <<EOF

== Confirmación ==
Vas a VOLVER AL VPS VIEJO (R-A) de la corrida ${OBJETIVO}.
  nuevo: ${NUEVO_VPS_HOST} (machine-id ${MACHINE_ID_NUEVO:0:8}): primero se detiene stockapp-api.
  viejo: ${VIEJO_VPS_HOST} (machine-id ${MACHINE_ID_VIEJO:0:8}): ${ESTADO_VIEJO_TXT}; se deshace el congelamiento y se arranca.
${aviso}
Para continuar escribí exactamente REVERTIR y Enter. Cualquier otra cosa cancela sin tocar nada:
EOF
    )"
    abrir_paso 3 "confirmación"
    orq_confirmar REVERTIR "$texto"
    cerrar_paso 3 OK
}

# Paso 4: la API nueva, PRIMERO: con dos APIs vivas, lo escrito en la nueva se perdería.
# habilitacion_nuevo -> HABILITACION_NUEVO: la palabra de is-enabled de la API nueva (0 y 1 son respuestas).
HABILITACION_NUEVO=""
habilitacion_nuevo() {
    orq_ssh NUEVO "systemctl is-enabled stockapp-api"
    case "$ORQ_RC" in
        0 | 1) ;;
        *) consulta_ok "la habilitación de stockapp-api en el VPS nuevo" ;;
    esac
    if [[ ! "$ORQ_SALIDA" =~ ^[a-z-]+$ ]]; then
        abortar 8 "no pude interpretar 'systemctl is-enabled stockapp-api' del VPS nuevo: '${ORQ_SALIDA}'."
    fi
    HABILITACION_NUEVO="$ORQ_SALIDA"
}

# Paso 4, también: deshabilitada (R44), para que un reinicio del VPS nuevo no la vuelva a arrancar con el
# viejo sirviendo. Un corte posterior no la levanta: hace falta deploy-vps.sh (R56).
revertir_detener_nuevo() {
    local hizo=0
    abrir_paso 4 "detener la API nueva"
    estado_api NUEVO
    if [[ "$ESTADO_API" != "inactive" && "$ESTADO_API" != "failed" ]]; then
        orq_ssh NUEVO "sudo -n systemctl stop stockapp-api"
        falla_remota 6 "'systemctl stop stockapp-api' en el VPS nuevo"
        hizo=1
        estado_api NUEVO
        # failed tampoco atiende (R36): ningún proceso de la API nueva escribe en la base.
        if [[ "$ESTADO_API" != "inactive" && "$ESTADO_API" != "failed" ]]; then
            abortar 6 "tras el stop, stockapp-api del VPS nuevo dice '$ESTADO_API': no se toca el viejo con dos APIs vivas. El viejo no se tocó."
        fi
    fi
    habilitacion_nuevo
    if [[ "$HABILITACION_NUEVO" == "enabled" ]]; then
        orq_ssh NUEVO "sudo -n systemctl disable stockapp-api"
        falla_remota 6 "'systemctl disable stockapp-api' en el VPS nuevo"
        hizo=1
        habilitacion_nuevo
        if [[ "$HABILITACION_NUEVO" == "enabled" ]]; then
            abortar 6 "tras el disable, stockapp-api del VPS nuevo sigue enabled: un reinicio la volvería a arrancar. El viejo no se tocó."
        fi
    fi
    if [[ "$hizo" -eq 1 ]]; then
        cerrar_paso 4 OK "$ESTADO_API y $HABILITACION_NUEVO"
    else
        cerrar_paso 4 "YA ESTABA" "stockapp-api dice $ESTADO_API y $HABILITACION_NUEVO"
    fi
}

# Pasos 5 a 8: los cuatro comandos de R-A, cada uno solo si hace falta.
CAMBIO_EN_EL_VIEJO=0
revertir_viejo() {
    abrir_paso 5 "viejo: unmask"
    habilitacion_revertir
    if [[ "$HABILITACION" == "masked" ]]; then
        orq_ssh VIEJO "sudo -n systemctl unmask stockapp-api"
        falla_remota 6 "'systemctl unmask stockapp-api' en el VPS viejo"
        CAMBIO_EN_EL_VIEJO=1
        habilitacion_revertir
        if [[ "$HABILITACION" == "masked" ]]; then
            abortar 6 "tras el unmask, stockapp-api del VPS viejo sigue masked."
        fi
        cerrar_paso 5 OK
    else
        cerrar_paso 5 "YA ESTABA" "no está enmascarada"
    fi

    abrir_paso 6 "viejo: la unit a su lugar"
    archivos_de_la_unit
    if [[ "$HAY_UNIT" -eq 1 && "$HAY_CONGELADA" -eq 1 ]]; then
        abortar 8 "en el VPS viejo existen $UNIT_VIEJO Y $UNIT_CONGELADA_VIEJO: no se pisa ninguno. Mirá cuál es la buena y dejá una sola."
    elif [[ "$HAY_UNIT" -eq 0 && "$HAY_CONGELADA" -eq 1 ]]; then
        orq_ssh VIEJO "sudo -n mv ${UNIT_CONGELADA_VIEJO} ${UNIT_VIEJO}"
        falla_remota 6 "el mv de ${UNIT_CONGELADA_VIEJO} a ${UNIT_VIEJO} en el VPS viejo"
        CAMBIO_EN_EL_VIEJO=1
        archivos_de_la_unit
        if [[ "$HAY_UNIT" -ne 1 || "$HAY_CONGELADA" -ne 0 ]]; then
            abortar 6 "tras el mv, la unit del VPS viejo no quedó en $UNIT_VIEJO (o sigue el .congelado)."
        fi
        cerrar_paso 6 OK
    elif [[ "$HAY_UNIT" -eq 1 ]]; then
        cerrar_paso 6 "YA ESTABA" "la unit está en su lugar"
    else
        abortar 8 "en el VPS viejo no existe $UNIT_VIEJO ni $UNIT_CONGELADA_VIEJO: no hay unit que devolver."
    fi

    abrir_paso 7 "viejo: daemon-reload"
    estado_api VIEJO
    if [[ "$CAMBIO_EN_EL_VIEJO" -eq 0 && "$ESTADO_API" == "active" ]]; then
        cerrar_paso 7 "YA ESTABA" "nada cambió y stockapp-api ya está active"
    else
        # Relee las units del disco (la que volvió a su lugar); no reinicia ningún otro servicio.
        orq_ssh VIEJO "sudo -n systemctl daemon-reload"
        falla_remota 6 "'systemctl daemon-reload' en el VPS viejo"
        cerrar_paso 7 OK
    fi

    abrir_paso 8 "viejo: start"
    estado_api VIEJO
    if [[ "$ESTADO_API" == "active" ]]; then
        cerrar_paso 8 "YA ESTABA" "stockapp-api ya está active"
    else
        orq_ssh VIEJO "sudo -n systemctl start stockapp-api"
        falla_remota 6 "'systemctl start stockapp-api' en el VPS viejo"
        cerrar_paso 8 OK
    fi
}

# Paso 9: el viejo active Y atendiendo por loopback (espera acotada, reloj monotónico), no enmascarado,
# con el mismo docker ps que antes del corte; el nuevo, detenido.
# diferencia_de_contenedores <antes> <ahora> -> CONTENEDORES_CAMBIARON (texto, vacío si no hay
# diferencias): los stockapp* (nuestros: hay que investigar) separados de los ajenos (la otra
# aplicación del VPS compartido: solo informativo).
CONTENEDORES_CAMBIARON=""
diferencia_de_contenedores() {
    local faltan nuevos fp="" np="" fa="" na="" c
    faltan="$(LC_ALL=C comm -23 <(printf '%s\n' "$1") <(printf '%s\n' "$2"))"
    nuevos="$(LC_ALL=C comm -13 <(printf '%s\n' "$1") <(printf '%s\n' "$2"))"
    while IFS= read -r c; do
        [[ -n "$c" ]] || continue
        if [[ "$c" == stockapp* ]]; then fp+="${fp:+ }$c"; else fa+="${fa:+ }$c"; fi
    done <<< "$faltan"
    while IFS= read -r c; do
        [[ -n "$c" ]] || continue
        if [[ "$c" == stockapp* ]]; then np+="${np:+ }$c"; else na+="${na:+ }$c"; fi
    done <<< "$nuevos"
    CONTENEDORES_CAMBIARON="  - propios (stockapp*): "
    if [[ -z "$fp$np" ]]; then
        CONTENEDORES_CAMBIARON+="sin cambios"
    else
        CONTENEDORES_CAMBIARON+="faltan: ${fp:-(ninguno)}; nuevos: ${np:-(ninguno)}. Son de StockApp: investigalo."
    fi
    CONTENEDORES_CAMBIARON+=$'\n'"  - ajenos (la otra aplicación del VPS compartido; solo informativo): "
    if [[ -z "$fa$na" ]]; then
        CONTENEDORES_CAMBIARON+="sin cambios"
    else
        CONTENEDORES_CAMBIARON+="faltan: ${fa:-(ninguno)}; nuevos: ${na:-(ninguno)}"
    fi
}

revertir_verificar() {
    local limite actual esperado lista="$OBJETIVO/contenedores-viejo.txt"
    abrir_paso 9 "verificación"
    limite=$(($(orq_reloj) + ARRANQUE_ESPERA))
    while :; do
        estado_api VIEJO
        case "$ESTADO_API" in
            active)
                orq_ssh VIEJO "curl -fsS --max-time 10 -o /dev/null ${RESOLVE_VIEJO}${URL_VIEJO}/"
                if [[ "$ORQ_RC" -eq 0 ]]; then
                    break
                fi
                if [[ "$ORQ_RC" -eq 255 ]]; then
                    consulta_ok "la API vieja por loopback"
                fi
                ;;
            failed)
                abortar 6 "stockapp-api del VPS viejo quedó en 'failed' después del start: no volvió a servir."
                ;;
        esac
        if [[ "$(orq_reloj)" -ge "$limite" ]]; then
            abortar 6 "stockapp-api del VPS viejo no respondió por loopback en ${ARRANQUE_ESPERA} s (is-active: $ESTADO_API)."
        fi
        sleep 2
    done
    echo "  - viejo: stockapp-api active y responde GET / por loopback"
    habilitacion_revertir
    if [[ "$HABILITACION" == "masked" ]]; then
        abortar 6 "stockapp-api del VPS viejo atiende pero su unit sigue masked."
    fi
    if [[ -f "$lista" ]]; then
        orq_ssh VIEJO "docker ps --format '{{.Names}}'"
        consulta_ok "la lista de contenedores del VPS viejo"
        if [[ -z "$ORQ_SALIDA" ]]; then
            abortar 8 "la lista de contenedores del VPS viejo vino vacía: no puedo interpretarla."
        fi
        actual="$(printf '%s\n' "$ORQ_SALIDA" | LC_ALL=C sort)"
        esperado="$(cat "$lista")" || abortar 8 "no pude leer $lista."
        if [[ "$actual" != "$esperado" ]]; then
            # R43: R-A ya se cumplió; la diferencia se informa al final, con la corrida cerrada.
            diferencia_de_contenedores "$esperado" "$actual"
            echo "  - viejo: la lista de contenedores NO es la de antes del corte (ver el final)"
        else
            echo "  - viejo: los mismos contenedores que antes del corte"
        fi
    else
        echo "  - viejo: la corrida no guardó contenedores-viejo.txt (no llegó al paso 5): no se compara"
    fi
    estado_api NUEVO
    if [[ "$ESTADO_API" != "inactive" && "$ESTADO_API" != "failed" ]]; then
        abortar 6 "stockapp-api del VPS nuevo dice '$ESTADO_API': tiene que quedar detenida (dos APIs vivas)."
    fi
    habilitacion_nuevo
    if [[ "$HABILITACION_NUEVO" == "enabled" ]]; then
        abortar 6 "stockapp-api del VPS nuevo quedó enabled: un reinicio la volvería a arrancar."
    fi
    echo "  - nuevo: stockapp-api detenida ($ESTADO_API) y $HABILITACION_NUEVO"
    cerrar_paso 9 OK
}

revertir() {
    MODO="revertir"
    PASOS_TOTAL=9
    # Fase A, de solo lectura: la corrida, sus .env, sus contenedores y raíces, y la identidad.
    revertir_contexto

    # Fase B: la corrida propia (revertir-<ts>), su log y el lock.
    orq_dir_corrida revertir >/dev/null
    orq_log_iniciar
    orq_lock
    LOCK_TOMADO=1
    echo "migrar-vps: --revertir (R-A)"
    echo "  corrida revertida: $OBJETIVO"
    echo "  corrida: $ORQ_CORRIDA"
    echo "  viejo: $VIEJO_VPS_HOST (machine-id ${MACHINE_ID_VIEJO:0:8})"
    echo "  nuevo: $NUEVO_VPS_HOST (machine-id ${MACHINE_ID_NUEVO:0:8})"
    orq_estado_poner modo revertir
    orq_estado_poner inicio_utc "$(ahora_utc)"
    orq_estado_poner corrida_revertida "$OBJETIVO"
    orq_estado_poner env_viejo "$ENV_VIEJO"
    orq_estado_poner env_nuevo "$ENV_NUEVO"
    orq_estado_poner machine_id_viejo "$MACHINE_ID_VIEJO"
    orq_estado_poner machine_id_nuevo "$MACHINE_ID_NUEVO"

    revertir_prevuelo
    revertir_guardia
    revertir_confirmacion
    revertir_detener_nuevo
    revertir_viejo
    revertir_verificar

    # El resultado que tenía (GO, NO-GO, FALLO-<n>) no se pierde; re-revertir no lo pisa con REVERTIDO.
    if leer_clave_continuar resultado && [[ "$VALOR_CLAVE" != "REVERTIDO" ]]; then
        poner_en_objetivo resultado_previo "$VALOR_CLAVE" || abortar 8 "no pude escribir resultado_previo en $OBJETIVO/estado."
    fi
    poner_en_objetivo resultado REVERTIDO || abortar 8 "el viejo ya atiende, pero no pude escribir resultado=REVERTIDO en $OBJETIVO/estado."
    orq_estado_poner resultado REVERTIDO
    echo
    echo "== Vuelta atrás terminada: el VPS viejo vuelve a servir =="
    echo "  corrida revertida: $OBJETIVO (resultado=REVERTIDO)"
    echo "  VPS nuevo: stockapp-api detenida; no la arranques a mano mientras el viejo sirve."
    echo "  Para migrar otra vez: una corrida nueva (deploy/migrar-vps.sh --ensayo y después el corte)."
    if [[ -n "$CONTENEDORES_CAMBIARON" ]]; then
        # R43: la corrida queda cerrada (si no, R24 bloquearía todo corte por un cambio de la otra app).
        echo
        echo "ATENCIÓN: la lista de contenedores del VPS viejo no es la de antes del corte ($OBJETIVO/contenedores-viejo.txt):"
        printf '%s\n' "$CONTENEDORES_CAMBIARON"
        SIN_AYUDA_DE_REVERTIR=1
        abortar 9 "la vuelta atrás se hizo (el viejo atiende, el nuevo está detenido y la corrida quedó REVERTIDO), pero cambiaron contenedores del VPS viejo: un humano tiene que mirarlos."
    fi
    exit 0
}

main() {
    if [[ "$REVERTIR" -eq 1 ]]; then
        revertir
    fi
    if [[ -n "$CONTINUAR" ]]; then
        continuar
    fi
    if [[ "$MODO" == "post-corte" ]]; then
        post_corte
    fi
    # --desde-backup: el backup se comprueba acá, antes de cualquier servidor y de crear la corrida.
    if [[ "$MODO" == "desde-backup" ]]; then
        validar_backup
    fi
    cargar_config
    exigir_terminal
    verificar_identidad

    # Fase B: la corrida, su log y el lock, en este orden (el log antes del lock: el tee no debe
    # heredar el fd del lock). orq_dir_corrida se llama SIN $(): define ORQ_CORRIDA y puede salir.
    orq_dir_corrida "$MODO" >/dev/null
    orq_log_iniciar
    orq_lock
    LOCK_TOMADO=1

    echo "migrar-vps: $MODO"
    echo "  corrida: $ORQ_CORRIDA"
    if ! solo_nuevo; then
        echo "  viejo: $VIEJO_VPS_HOST (machine-id ${MACHINE_ID_VIEJO:0:8})"
    fi
    echo "  nuevo: $NUEVO_VPS_HOST (machine-id ${MACHINE_ID_NUEVO:0:8})"

    # El ensayo también: opera los mismos dos servidores, y recrear la base del nuevo o volcar el viejo
    # en medio de un corte sin terminar pisaría lo que ese corte dejó a medias.
    verificar_corridas_sin_terminar
    if [[ "$MODO" == "desde-backup" ]]; then
        desde_backup
    fi
    orq_estado_poner modo "$MODO"
    orq_estado_poner inicio_utc "$(ahora_utc)"
    orq_estado_poner env_viejo "$ENV_VIEJO"
    orq_estado_poner env_nuevo "$ENV_NUEVO"
    orq_estado_poner machine_id_viejo "$MACHINE_ID_VIEJO"
    orq_estado_poner machine_id_nuevo "$MACHINE_ID_NUEVO"
    # R38: el contexto del arnés (vacío o el default en un corte real) también se relee al continuar.
    orq_estado_poner contenedor_viejo "$CONTENEDOR_VIEJO"
    orq_estado_poner contenedor_nuevo "$CONTENEDOR_NUEVO"
    orq_estado_poner raiz_remota_viejo "$RAIZ_VIEJO"
    orq_estado_poner raiz_remota_nuevo "$RAIZ_NUEVO"

    abrir_paso 1 "pre-vuelo"
    prevuelo
    cerrar_paso 1 OK

    PASO_T0="$(orq_reloj)"
    if [[ "$MODO" == "corte" ]]; then
        orq_paso 2 "$PASOS_TOTAL" "confirmación"
        confirmacion
        cerrar_paso 2 OK
    else
        orq_paso 2 "$PASOS_TOTAL" "confirmación"
        cerrar_paso 2 OMITIDO "ensayo: sin confirmación" omitido
    fi

    # El ensayo corre la MISMA secuencia: el volcado con --ensayo (paso 6) y V10 invertido.
    corte_paso3
    corte_paso4
    corte_paso5
    corte_paso6
    corte_paso7
    corte_paso8
    corte_paso9
    corte_paso10
    corte_paso11
    corte_paso12
    corte_paso13
}

# ---------------------------------------------------------------------------------------
# --post-corte <corrida-del-corte> [--antes-de-plazo] (spec 6.5.8, D+14)
# ---------------------------------------------------------------------------------------
# Archiva el dump del corte (lo MUEVE, nunca lo borra) y después ofrece borrar, uno por uno y con la
# palabra BORRAR cada vez, las copias de producción que quedaron de la migración. Nunca toca el VPS viejo:
# lo que hay que hacer en él solo se IMPRIME. Las líneas de paso no se anotan en el estado del corte (sus
# paso_<n> son los del corte); lo único que agrega al estado es dónde quedó el archivo y el sha256 del
# manifiesto, con resultado=GO siempre al final.

PLAZO_DIAS=14
PASOS_POST_CORTE=5
ARCHIVO_CORTE=""
PLAZO_CUMPLIDO=1
RESPUESTA=""

pc_paso() {
    orq_paso "$1" "$PASOS_POST_CORTE" "$2"
    PASO_ABIERTO=1
}

pc_fin() {
    PASO_ABIERTO=0
    orq_fin_paso "$@"
}

# leer_respuesta -> RESPUESTA: una línea de la terminal (de stdin en el arnés, ORQ_ENSAYO=1; nunca de
# /dev/tty). Al final de la entrada vale lo que se leyó, o vacío: lo que no se confirma se conserva.
leer_respuesta() {
    local rc=0
    RESPUESTA=""
    if [[ "$ORQ_ENSAYO" -eq 1 ]]; then
        IFS= read -r RESPUESTA || rc=$?
    else
        IFS= read -r RESPUESTA </dev/tty || rc=$?
    fi
    # rc distinto de 0 es fin de la entrada: no es un error, y RESPUESTA ya tiene lo que haya llegado.
    return 0
}

# preguntar_borrar <qué> -> 0 solo si el operador escribió exactamente BORRAR.
preguntar_borrar() {
    printf '¿Borrar %s? Escribí BORRAR para borrarlo; cualquier otra cosa lo conserva:\n' "$1"
    leer_respuesta
    [[ "$RESPUESTA" == "BORRAR" ]]
}

sha_de() {
    local sha
    sha="$(sha256sum -- "$1")" || return 1
    printf '%s' "${sha%% *}"
}

# logs_auditoria_del_manifiesto <manifiesto> -> LOGS_MANIFIESTO (la línea LogsAuditoria de [conteos]).
LOGS_MANIFIESTO=""
logs_auditoria_del_manifiesto() {
    local linea dentro=0 n=0 re=$'^LogsAuditoria\t([0-9]+)$'
    LOGS_MANIFIESTO=""
    while IFS= read -r linea || [[ -n "$linea" ]]; do
        if [[ "$linea" == "[conteos]" ]]; then
            dentro=1
        elif [[ "$dentro" -eq 1 && "$linea" =~ $re ]]; then
            LOGS_MANIFIESTO="${BASH_REMATCH[1]}"
            n=$((n + 1))
        fi
    done < "$1" || abortar 8 "no pude leer el manifiesto $1."
    if [[ "$n" -ne 1 ]]; then
        abortar 8 "el manifiesto $1 no trae exactamente una línea LogsAuditoria en [conteos]: no puedo comprobar que el nuevo tenga toda la auditoría, y no sigo."
    fi
}

# archivar_uno <origen> <destino> <sha-esperado|""> -> ARCHIVADO (movido|ya-estaba).
# Mueve sin pisar, con el sha256 antes y después. Un archivo ya movido se verifica contra el sha esperado.
ARCHIVADO=""
archivar_uno() {
    local origen="$1" destino="$2" esperado="$3" nombre="${1##*/}" antes despues
    if [[ -e "$origen" && -e "$destino" ]]; then
        abortar 5 "$nombre está en la corrida Y en el archivo ($destino): no se pisa ninguno. Mirá cuál es el bueno."
    fi
    if [[ ! -e "$origen" && ! -e "$destino" ]]; then
        abortar 5 "no encuentro $nombre ni en la corrida ($origen) ni en el archivo ($destino)."
    fi
    if [[ ! -e "$origen" ]]; then
        if [[ -z "$esperado" ]]; then
            abortar 8 "$destino ya está en el archivo, pero el estado no dice qué sha256 tenía: no puedo verificarlo."
        fi
        despues="$(sha_de "$destino")" || abortar 5 "no pude calcular el sha256 de $destino."
        if [[ "$despues" != "$esperado" ]]; then
            abortar 5 "el sha256 de $destino ($despues) no es el esperado ($esperado): el archivo del corte cambió."
        fi
        echo "  - $nombre: ya estaba en el archivo, sha256 $despues"
        ARCHIVADO="ya-estaba"
        return 0
    fi
    antes="$(sha_de "$origen")" || abortar 5 "no pude calcular el sha256 de $origen."
    if [[ -n "$esperado" && "$antes" != "$esperado" ]]; then
        abortar 5 "el sha256 de $origen ($antes) no es el del estado del corte ($esperado): no es el volcado de este corte. No se movió nada."
    fi
    echo "  - $nombre: sha256 antes  $antes"
    mv -n -- "$origen" "$destino" || abortar 5 "no pude mover $origen a $destino."
    if [[ -e "$origen" || ! -f "$destino" ]]; then
        abortar 5 "después del mv, $nombre no quedó solo en $destino."
    fi
    despues="$(sha_de "$destino")" || abortar 5 "no pude calcular el sha256 de $destino."
    echo "  - $nombre: sha256 después $despues"
    if [[ "$despues" != "$antes" ]]; then
        abortar 5 "el sha256 de $destino cambió al moverlo ($antes -> $despues)."
    fi
    ARCHIVADO="movido"
}

post_corte_contexto() {
    local congelado e ahora faltan
    OBJETIVO="$POST_CORTE"
    if [[ ! -d "$POST_CORTE" || ! -f "$POST_CORTE/estado" ]]; then
        abortar 3 "$POST_CORTE no es una corrida con estado."
    fi
    # Los temporales (el stderr de ssh) van a la corrida del corte desde el primer comando: ninguno a /tmp.
    ORQ_CORRIDA="$POST_CORTE"
    leer_clave_continuar modo || abortar 3 "el estado de $POST_CORTE no dice el modo."
    if [[ "$VALOR_CLAVE" != "corte" ]]; then
        abortar 3 "el estado de $POST_CORTE dice modo=$VALOR_CLAVE: --post-corte es para un corte."
    fi
    # Solo tras un corte con GO: con cualquier otro resultado (o sin resultado) el viejo puede seguir
    # siendo la única copia buena, y el dump del corte no es todavía "el archivo".
    if ! leer_clave_continuar resultado; then
        abortar 3 "la corrida $POST_CORTE no terminó (no tiene resultado=): --post-corte es solo para un corte con resultado=GO. No se tocó nada."
    fi
    if [[ "$VALOR_CLAVE" != "GO" ]]; then
        abortar 3 "la corrida $POST_CORTE terminó con resultado=$VALOR_CLAVE: --post-corte es solo para un corte con resultado=GO. No se tocó nada."
    fi
    leer_clave_continuar congelado_utc || abortar 3 "el estado de $POST_CORTE no dice congelado_utc: no sé desde cuándo corre el plazo de $PLAZO_DIAS días."
    congelado="$VALOR_CLAVE"
    if ! e="$(date -u -d "$congelado" +%s 2>/dev/null)"; then
        abortar 8 "no pude interpretar congelado_utc=$congelado del estado."
    fi
    ahora="$(date -u +%s)"
    if [[ "$ahora" -lt $((e + PLAZO_DIAS * 86400)) ]]; then
        PLAZO_CUMPLIDO=0
        faltan=$(((e + PLAZO_DIAS * 86400 - ahora + 86399) / 86400))
        if [[ "$ANTES_DE_PLAZO" -ne 1 ]]; then
            abortar 3 "todavía no pasaron $PLAZO_DIAS días desde el congelamiento ($congelado; faltan $faltan): el rollback sigue abierto. Antes de eso, solo con --antes-de-plazo y la palabra BORRAR. No se tocó nada."
        fi
    fi
    leer_clave_continuar env_nuevo || abortar 3 "el estado de $POST_CORTE no dice env_nuevo."
    ENV_NUEVO="$VALOR_CLAVE"
    cargar_parametros_de_arnes
    cargar_config
    exigir_terminal
    verificar_identidad
    leer_clave_continuar machine_id_nuevo || abortar 3 "el estado de $POST_CORTE no dice machine_id_nuevo: no puedo comprobar que el nuevo sea el del corte."
    if [[ "$VALOR_CLAVE" != "$MACHINE_ID_NUEVO" ]]; then
        abortar 9 "el VPS nuevo de $ENV_NUEVO (machine-id ${MACHINE_ID_NUEVO:0:8}) no es el del corte (${VALOR_CLAVE:0:8}): no se archiva ni se borra nada. No se tocó nada."
    fi
    cargar_volcado_dir
    leer_clave_continuar volcado || abortar 3 "el estado de $POST_CORTE no dice volcado=: no sé cuál es el dump del corte."
    VOLCADO="$VALOR_CLAVE"
    if [[ ! "$VOLCADO" =~ ^volcado-([0-9]{8}T[0-9]{6}Z)\.dump$ ]]; then
        abortar 3 "volcado=$VOLCADO del estado no tiene la forma que deja migrar."
    fi
    MANIFIESTO="volcado-${BASH_REMATCH[1]}.manifiesto"
    leer_clave_continuar sha256 || abortar 3 "el estado de $POST_CORTE no dice sha256= del dump del corte."
    SHA_VOLCADO="$VALOR_CLAVE"
    ARCHIVO_CORTE="$HOME/stockapp-backups-vps/${POST_CORTE##*/}"
}

post_corte_auditoria() {
    local manifiesto actual
    pc_paso 2 "LogsAuditoria del nuevo >= manifiesto"
    if [[ -f "$POST_CORTE/$VOLCADO_DIR/$MANIFIESTO" ]]; then
        manifiesto="$POST_CORTE/$VOLCADO_DIR/$MANIFIESTO"
    elif [[ -f "$ARCHIVO_CORTE/$MANIFIESTO" ]]; then
        manifiesto="$ARCHIVO_CORTE/$MANIFIESTO"
    else
        abortar 5 "no encuentro el manifiesto del corte ($MANIFIESTO) ni en la corrida ni en $ARCHIVO_CORTE."
    fi
    logs_auditoria_del_manifiesto "$manifiesto"
    psql_nuevo 'SELECT count(*) FROM "LogsAuditoria";'
    if [[ "$ORQ_RC" -ne 0 || ! "$ORQ_SALIDA" =~ ^[0-9]+$ ]]; then
        volcar_stderr
        abortar 8 "no pude contar LogsAuditoria en el VPS nuevo (salió con $ORQ_RC): sin ese dato no se archiva ni se borra nada. Esto NO es 'cero'."
    fi
    actual="$ORQ_SALIDA"
    if [[ "$actual" -lt "$LOGS_MANIFIESTO" ]]; then
        abortar 3 "LogsAuditoria del VPS nuevo tiene $actual registros y el manifiesto del corte $LOGS_MANIFIESTO: falta auditoría en el nuevo. No se archiva ni se borra nada."
    fi
    pc_fin OK "$actual >= $LOGS_MANIFIESTO"
}

post_corte_archivar() {
    local dir="$POST_CORTE/$VOLCADO_DIR" sha_man="" modo_dir hubo=0
    pc_paso 3 "archivar el dump del corte"
    echo "  - archivo: $ARCHIVO_CORTE"
    if [[ ! -d "$ARCHIVO_CORTE" ]]; then
        ( umask 077; mkdir -p -- "${ARCHIVO_CORTE%/*}" ) || abortar 5 "no pude crear ${ARCHIVO_CORTE%/*}."
        mkdir -m 700 -- "$ARCHIVO_CORTE" || abortar 5 "no pude crear $ARCHIVO_CORTE."
    fi
    modo_dir="$(stat -c %a -- "$ARCHIVO_CORTE")" || abortar 5 "no pude leer los permisos de $ARCHIVO_CORTE."
    if [[ "$modo_dir" != "700" ]]; then
        abortar 5 "$ARCHIVO_CORTE tiene permisos $modo_dir: el archivo del corte tiene que ser 700 (lleva datos de producción)."
    fi
    archivar_uno "$dir/$VOLCADO" "$ARCHIVO_CORTE/$VOLCADO" "$SHA_VOLCADO"
    [[ "$ARCHIVADO" == "movido" ]] && hubo=1
    if leer_clave_continuar sha256_manifiesto_corte; then
        sha_man="$VALOR_CLAVE"
    fi
    archivar_uno "$dir/$MANIFIESTO" "$ARCHIVO_CORTE/$MANIFIESTO" "$sha_man"
    [[ "$ARCHIVADO" == "movido" ]] && hubo=1
    if [[ -z "$sha_man" ]]; then
        sha_man="$(sha_de "$ARCHIVO_CORTE/$MANIFIESTO")" || abortar 5 "no pude calcular el sha256 del manifiesto archivado."
        orq_estado_poner sha256_manifiesto_corte "$sha_man"
    fi
    orq_estado_poner archivo_corte "$ARCHIVO_CORTE"
    if [[ "$hubo" -eq 1 ]]; then
        pc_fin OK "sha256 verificado antes y después"
    else
        pc_fin "YA ESTABA" "sha256 verificado"
    fi
}

# Los candidatos locales, por LISTA BLANCA (R48): las corridas de ensayo terminadas y lo que deja el runbook
# manual, por patrón exacto (ensayo-<fecha>/, ensayo-e2-<fecha>/, contenedores-viejo-<fecha>.txt,
# v10-viejo.txt, token; migracion-<fecha>/ no, ver R52 abajo). NUNCA se ofrece: el archivo de un corte (corte-*,
# de cualquier formato: el de este script o el corte-%Y%m%d del runbook manual), un volcado inverso
# (inversa-*), una corrida de corte (ni esta, que se copió al archivo, ni otra con su volcado sin archivar),
# ni lo que no sea de la migración (las salidas de bajar-backup-vps.sh son la última copia fuera del
# servidor). Todo lo que no se ofrece se lista como "se conserva", con el motivo.
CANDIDATOS_LOCALES=()
FECHA_RUNBOOK='[0-9]{8}'
conservar() {
    anotar "  - se conserva ($1): $2"
}

candidatos_locales() {
    local base="$HOME/stockapp-backups-vps" e nombre rc
    CANDIDATOS_LOCALES=()
    for e in "$base"/*; do
        [[ -e "$e" ]] || continue
        nombre="${e##*/}"
        [[ "$nombre" == "corridas" && -d "$e" ]] && continue
        [[ "$e" == "$ARCHIVO_CORTE" ]] && continue
        if [[ "$nombre" == corte-* || "$nombre" == inversa-* ]]; then
            conservar "nunca se ofrece: archivo de un corte o volcado inverso" "$e"
            continue
        fi
        # R52: migracion-<fecha>/ del runbook manual NO: puede tener el dump de un corte manual sin
        # archivar, la única copia fuera del servidor.
        if [[ "$nombre" =~ ^migracion-${FECHA_RUNBOOK}$ ]]; then
            conservar "puede tener el dump de un corte manual sin archivar" "$e"
            continue
        fi
        if [[ -d "$e" && ! -L "$e" && "$nombre" =~ ^(ensayo|ensayo-e2)-${FECHA_RUNBOOK}$ ]] ||
            [[ -f "$e" && ! -L "$e" && ( "$nombre" =~ ^contenedores-viejo-${FECHA_RUNBOOK}\.txt$ || "$nombre" == "v10-viejo.txt" || "$nombre" == "token" ) ]]; then
            CANDIDATOS_LOCALES+=("$e")
            continue
        fi
        conservar "no es de esta migración" "$e"
    done
    for e in "$base/corridas"/*; do
        [[ -e "$e" ]] || continue
        nombre="${e##*/}"
        if [[ "$nombre" == corte-* ]]; then
            conservar "nunca se ofrece: una corrida de corte" "$e"
            continue
        fi
        if [[ ! -d "$e" || -L "$e" || ! "$nombre" =~ ^ensayo-[0-9]{8}T[0-9]{6}Z$ ]]; then
            conservar "no es una corrida de ensayo" "$e"
            continue
        fi
        rc=0
        orq_estado_de "$e/estado" resultado >/dev/null 2>&1 || rc=$?
        if [[ "$rc" -ne 0 ]]; then
            conservar "sin terminar" "$e"
            continue
        fi
        CANDIDATOS_LOCALES+=("$e")
    done
}

# anotar <línea>: a la pantalla (y al registro.log de la corrida, por el tee) Y, sincrónico, al
# registro.log del archivo del corte: lo que se borró y lo que se conservó queda junto al dump (R49).
anotar() {
    printf '%s\n' "$1"
    if [[ -n "$REGISTRO_ARCHIVO" ]]; then
        printf '%s\n' "$1" >> "$REGISTRO_ARCHIVO" || abortar 5 "no pude escribir en $REGISTRO_ARCHIVO."
    fi
}

# copiar_corrida_al_archivo (R49): la corrida del corte nunca se borra, pero su estado y su registro.log
# (con este --post-corte) se copian además al archivo, junto al dump, con el sha256 del dump y del
# manifiesto en formato de sha256sum: el archivo se explica solo aunque la corrida se pierda.
REGISTRO_ARCHIVO=""
copiar_corrida_al_archivo() {
    local sha_dump sha_man
    sha_dump="$(sha_de "$ARCHIVO_CORTE/$VOLCADO")" || abortar 5 "no pude calcular el sha256 de $ARCHIVO_CORTE/$VOLCADO."
    sha_man="$(sha_de "$ARCHIVO_CORTE/$MANIFIESTO")" || abortar 5 "no pude calcular el sha256 de $ARCHIVO_CORTE/$MANIFIESTO."
    (
        umask 077
        printf '%s  %s\n' "$sha_dump" "$VOLCADO" > "$ARCHIVO_CORTE/$VOLCADO.sha256" &&
            printf '%s  %s\n' "$sha_man" "$MANIFIESTO" > "$ARCHIVO_CORTE/$MANIFIESTO.sha256" &&
            cp -f -- "$POST_CORTE/estado" "$ARCHIVO_CORTE/estado" &&
            cp -f -- "$POST_CORTE/registro.log" "$ARCHIVO_CORTE/registro.log" &&
            chmod 600 -- "$ARCHIVO_CORTE/estado" "$ARCHIVO_CORTE/registro.log" "$ARCHIVO_CORTE/$VOLCADO.sha256" "$ARCHIVO_CORTE/$MANIFIESTO.sha256"
    ) || abortar 5 "no pude copiar el estado, el registro y los sha256 del corte a $ARCHIVO_CORTE: no se ofrece borrar nada."
    REGISTRO_ARCHIVO="$ARCHIVO_CORTE/registro.log"
    anotar "  - copiados a $ARCHIVO_CORTE: estado, registro.log, $VOLCADO.sha256 y $MANIFIESTO.sha256"
}

post_corte_borrar() {
    local nombres=() n tam d borrados=0 conservados=0 rc
    pc_paso 4 "borrar copias, una por una"
    copiar_corrida_al_archivo
    anotar "== --post-corte $(ahora_utc): borrados =="
    # En el VPS nuevo: los directorios de ~/stockapp-deploy/migracion (corridas, con su seguridad/, y lo
    # apartado). Si el directorio ya no existe, no hay nada que ofrecer ahí (R51): no es "no pude consultar".
    orq_ssh NUEVO 'test -d "$HOME/stockapp-deploy/migracion"'
    if [[ "$ORQ_RC" -eq 1 && -z "$ORQ_STDERR" ]]; then
        anotar "  - ~/stockapp-deploy/migracion ya no estaba en el VPS nuevo: nada que ofrecer ahí"
    else
        consulta_ok "si existe ~/stockapp-deploy/migracion en el VPS nuevo"
        orq_ssh NUEVO "find \"\$HOME/stockapp-deploy/migracion\" -mindepth 1 -maxdepth 1 -type d -printf '%f\\n'"
        consulta_ok "los directorios de ~/stockapp-deploy/migracion en el VPS nuevo"
        # A un arreglo, no a un `while read ... <<<`: dentro del bucle, la respuesta del operador se lee de
        # stdin, y la redirección del bucle se la comería.
        mapfile -t nombres < <(printf '%s\n' "$ORQ_SALIDA" | LC_ALL=C sort)
    fi
    for n in "${nombres[@]}"; do
        [[ -n "$n" ]] || continue
        if [[ ! "$n" =~ $NOMBRE_SEGURO ]]; then
            conservar "nombre inesperado" "~/stockapp-deploy/migracion/$n (VPS nuevo)"
            continue
        fi
        orq_ssh NUEVO "du -sh -- \"\$HOME/stockapp-deploy/migracion/$n\""
        consulta_ok "el tamaño de ~/stockapp-deploy/migracion/$n en el VPS nuevo"
        tam="${ORQ_SALIDA%%[[:space:]]*}"
        if preguntar_borrar "en el VPS nuevo ~/stockapp-deploy/migracion/$n ($tam)"; then
            orq_ssh NUEVO "rm -r -- \"\$HOME/stockapp-deploy/migracion/$n\""
            falla_remota 5 "el borrado de ~/stockapp-deploy/migracion/$n en el VPS nuevo"
            anotar "  - borrado: ~/stockapp-deploy/migracion/$n (VPS nuevo)"
            borrados=$((borrados + 1))
        else
            anotar "  - se conserva: ~/stockapp-deploy/migracion/$n (VPS nuevo)"
            conservados=$((conservados + 1))
        fi
    done

    # En esta máquina, por ruta exacta y solo lo de la lista blanca.
    candidatos_locales
    for d in "${CANDIDATOS_LOCALES[@]}"; do
        rc=0
        tam="$(du -sh -- "$d")" || rc=$?
        if [[ "$rc" -ne 0 ]]; then
            abortar 8 "no pude medir $d: no lo ofrezco a ciegas."
        fi
        tam="${tam%%[[:space:]]*}"
        if preguntar_borrar "$d ($tam)"; then
            rm -r -- "$d" || abortar 5 "no pude borrar $d."
            if [[ -e "$d" ]]; then
                abortar 5 "después del borrado, $d sigue existiendo."
            fi
            anotar "  - borrado: $d"
            borrados=$((borrados + 1))
        else
            anotar "  - se conserva: $d"
            conservados=$((conservados + 1))
        fi
    done
    anotar "  - $borrados borrados, $conservados conservados; ni el archivo ni la corrida del corte se ofrecen"
    pc_fin OK "$borrados borrados, $conservados conservados"
}

post_corte_referencia() {
    pc_paso 5 "referencia para desmantelar el VPS viejo"
    cat <<EOF
  Referencia para desmantelar el VPS viejo: no se ejecuta nada desde acá (desmantelarlo es una decisión de
  Martin). Siempre acotado por nombre a StockApp: el VPS viejo es compartido con otra aplicación. En el VPS
  viejo, como el usuario de deploy:
    cd ~/stockapp-deploy && docker compose -f docker-compose.postgres.yml --env-file .env down
      (sin la opción que borra volúmenes: los datos siguen en stockapp_pgdata hasta el comando siguiente)
    docker volume rm stockapp_pgdata
      (recién ahora tiene respaldo: el dump del corte está en ${ARCHIVO_CORTE}, verificado, y LogsAuditoria
      del nuevo es >= al del manifiesto)
    stockapp-api queda enmascarada y no arranca; un systemctl disable solo tiene sentido después de un unmask.
  El certificado del dominio viejo queda a decisión de Martin. El proxy de la otra aplicación no se toca.
EOF
    pc_fin OK "impresa, no ejecutada"
}

post_corte() {
    # Fase A, de solo lectura: el corte (GO y el plazo), su .env del nuevo y la identidad del nuevo.
    post_corte_contexto

    # Fase B: el log del corte (se agrega) y el lock.
    orq_log_iniciar
    orq_lock
    LOCK_TOMADO=1
    echo "migrar-vps: --post-corte"
    echo "  corrida del corte: $POST_CORTE"
    echo "  nuevo: $NUEVO_VPS_HOST (machine-id ${MACHINE_ID_NUEVO:0:8})"

    pc_paso 1 "corte con GO y plazo de $PLAZO_DIAS días"
    if [[ "$PLAZO_CUMPLIDO" -eq 1 ]]; then
        pc_fin OK "GO, pasaron $PLAZO_DIAS días desde el congelamiento"
    else
        orq_confirmar BORRAR "$(
            cat <<EOF

== Confirmación (--antes-de-plazo) ==
El corte $POST_CORTE todavía está dentro de los $PLAZO_DIAS días de rollback: después de esto, el dump del corte
queda solo en el archivo y se ofrece borrar las copias. Para continuar escribí exactamente BORRAR y Enter.
Cualquier otra cosa cancela sin tocar nada:
EOF
        )"
        pc_fin OK "GO, antes del plazo (--antes-de-plazo confirmado)"
    fi
    post_corte_auditoria
    post_corte_archivar
    post_corte_borrar
    post_corte_referencia
    exit 0
}

# ---------------------------------------------------------------------------------------
# --desde-backup <dump> [--manifiesto <m>] (spec 6.5.7): la mitad "VPS nuevo" de la secuencia
# ---------------------------------------------------------------------------------------
# Los mismos pasos del corte, con los números del corte: 3, 4, 8, 9, 11, 12 y 13. El 5, 6, 7 y 10 son del
# VPS viejo o de su volcado y quedan OMITIDO. Nunca escribe congelado_utc: el trap cierra la corrida que
# falla (FALLO-<n>) y la salida es volver a correrla entera.

desde_backup() {
    local omitido="desde-backup: no hay VPS viejo"
    PASO_NOMBRES[4]="apartar los backups propios del destino"
    PASO_NOMBRES[12]="verificación (V1-V8)"
    V_ORDEN=(V1 V2 V3 V4 V5 V6 V7 V8)
    orq_estado_poner modo "$MODO"
    orq_estado_poner inicio_utc "$(ahora_utc)"
    orq_estado_poner env_nuevo "$ENV_NUEVO"
    orq_estado_poner machine_id_nuevo "$MACHINE_ID_NUEVO"
    orq_estado_poner contenedor_nuevo "$CONTENEDOR_NUEVO"
    orq_estado_poner raiz_remota_nuevo "$RAIZ_NUEVO"
    orq_estado_poner backup "$BACKUP"
    orq_estado_poner volcado "$VOLCADO"
    orq_estado_poner sha256 "$SHA_VOLCADO"
    orq_estado_poner fecha_backup "$FECHA_BACKUP"
    if [[ -n "$MANIFIESTO_RUTA" ]]; then
        orq_estado_poner manifiesto "$MANIFIESTO_RUTA"
    fi

    abrir_paso 1 "pre-vuelo"
    prevuelo_desde_backup
    cerrar_paso 1 OK

    abrir_paso 2 "confirmación"
    confirmacion_desde_backup
    cerrar_paso 2 OK

    corte_paso3
    corte_paso4
    omitir_paso 5 "$omitido"
    omitir_paso 6 "$omitido"
    omitir_paso 7 "$omitido: el backup ya se identificó en el pre-vuelo"
    corte_paso8
    corte_paso9
    omitir_paso 10 "el backup no trae el historial de backups"
    corte_paso11
    corte_paso12
    corte_paso13
}

main
