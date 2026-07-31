#!/usr/bin/env bash
# seed-demo.sh — siembra 10 registros de ejemplo de cada entidad principal de StockApp
# vía la API HTTP, respetando el orden de dependencias.
#
# Uso:
#   deploy/seed-demo.sh [base_url] [archivo_env]
#
# Ejemplos:
#   deploy/seed-demo.sh                                          # http://127.0.0.1:5080 + .env
#   deploy/seed-demo.sh http://127.0.0.1:5080 deploy/.env         # corriendo EN el VPS
#   deploy/seed-demo.sh http://194.163.142.86:5080 deploy/.env    # corriendo desde afuera
#
# Es tolerante a reintentos: si una entidad ya existe (409), la cuenta como "ya existía"
# y sigue. Las credenciales de admin se leen de BOOTSTRAP_ADMIN_USER / BOOTSTRAP_PASSWORD
# en el archivo .env indicado — nunca se hardcodean acá.

set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:5080}"
ENV_FILE="${2:-.env}"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: no existe el archivo de entorno '${ENV_FILE}'." >&2
    exit 1
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

: "${BOOTSTRAP_ADMIN_USER:?falta BOOTSTRAP_ADMIN_USER en ${ENV_FILE}}"
: "${BOOTSTRAP_PASSWORD:?falta BOOTSTRAP_PASSWORD en ${ENV_FILE}}"

HAS_JQ=0
if command -v jq >/dev/null 2>&1; then
    HAS_JQ=1
fi

# ---------------------------------------------------------------------------
# Helpers JSON (jq si está disponible, sino python3)
# ---------------------------------------------------------------------------

json_get() {
    # json_get <json> <campo> -> valor del campo top-level (string), vacío si no está
    local json="$1" campo="$2"
    if [[ "$HAS_JQ" -eq 1 ]]; then
        printf '%s' "$json" | jq -r --arg f "$campo" '.[$f] // empty' 2>/dev/null
    else
        CAMPO_JG="$campo" python3 -c "
import json, os, sys
try:
    data = json.loads(sys.stdin.read())
    val = data.get(os.environ['CAMPO_JG'])
    print('' if val is None else val)
except Exception:
    print('')
" <<< "$json"
    fi
}

id_por_campo() {
    # id_por_campo <json_array> <campo> <valor> -> id del primer elemento cuyo campo == valor
    local json="$1" campo="$2" valor="$3"
    if [[ "$HAS_JQ" -eq 1 ]]; then
        printf '%s' "$json" | jq -r --arg c "$campo" --arg v "$valor" \
            '[.[] | select((.[$c]|tostring) == $v)] | (.[0].id // empty)' 2>/dev/null
    else
        CAMPO_IF="$campo" VALOR_IF="$valor" python3 -c "
import json, os, sys
try:
    data = json.loads(sys.stdin.read())
    campo = os.environ['CAMPO_IF']
    valor = os.environ['VALOR_IF']
    for item in data:
        if str(item.get(campo)) == valor:
            print(item.get('id'))
            break
except Exception:
    pass
" <<< "$json"
    fi
}

# ---------------------------------------------------------------------------
# Estado global / contadores
# ---------------------------------------------------------------------------

TOKEN=""
declare -A CREADO
declare -A EXISTIA
declare -A ERRORES
RESULT_ID=""

login() {
    echo "== Login como ${BOOTSTRAP_ADMIN_USER} =="
    local resp status body
    resp=$(curl -s -w '\n%{http_code}' -X POST "${BASE_URL}/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"nombreUsuario\":\"${BOOTSTRAP_ADMIN_USER}\",\"contrasena\":\"${BOOTSTRAP_PASSWORD}\"}")
    status="${resp##*$'\n'}"
    body="${resp%$'\n'*}"
    if [[ "$status" != "200" ]]; then
        echo "ERROR: login falló (HTTP ${status}): ${body}" >&2
        exit 1
    fi
    TOKEN=$(json_get "$body" "token")
    if [[ -z "$TOKEN" ]]; then
        echo "ERROR: no se pudo extraer el token de la respuesta de login: ${body}" >&2
        exit 1
    fi
    echo "Login OK."
}

# get_lista <endpoint> -> body del GET (para resolver ids en el fallback de 409)
get_lista() {
    local endpoint="$1"
    curl -s -X GET "${BASE_URL}${endpoint}" -H "Authorization: Bearer ${TOKEN}"
}

# crear_o_reusar <entidad> <endpoint_post> <json_body> <endpoint_get_lista> <campo_dedupe> <valor_dedupe>
# Deja el id resultante (creado o resuelto por 409) en RESULT_ID. Devuelve 0 si hay id
# utilizable, 1 si no (400/500/409 sin poder resolver).
crear_o_reusar() {
    local entidad="$1" endpoint_post="$2" json_body="$3" endpoint_get="$4" campo="$5" valor="$6"
    local resp status body
    RESULT_ID=""

    resp=$(curl -s -w '\n%{http_code}' -X POST "${BASE_URL}${endpoint_post}" \
        -H "Content-Type: application/json" -H "Authorization: Bearer ${TOKEN}" \
        -d "$json_body")
    status="${resp##*$'\n'}"
    body="${resp%$'\n'*}"

    case "$status" in
        200|201)
            CREADO["$entidad"]=$(( ${CREADO["$entidad"]:-0} + 1 ))
            RESULT_ID=$(json_get "$body" "id")
            [[ -n "$RESULT_ID" ]]
            return $?
            ;;
        409)
            EXISTIA["$entidad"]=$(( ${EXISTIA["$entidad"]:-0} + 1 ))
            local lista
            lista=$(get_lista "$endpoint_get")
            RESULT_ID=$(id_por_campo "$lista" "$campo" "$valor")
            [[ -n "$RESULT_ID" ]]
            return $?
            ;;
        400)
            ERRORES["$entidad"]=$(( ${ERRORES["$entidad"]:-0} + 1 ))
            echo "  [400] POST ${endpoint_post} -> ${body}" >&2
            return 1
            ;;
        *)
            ERRORES["$entidad"]=$(( ${ERRORES["$entidad"]:-0} + 1 ))
            echo "  [HTTP ${status}] POST ${endpoint_post} -> ${body}" >&2
            return 1
            ;;
    esac
}

# crear_simple <entidad> <endpoint_post> <json_body> — sin dedupe (transaccionales:
# movimientos, gastos, ingresos). Tolera 409 contándolo, sin intentar resolver id.
crear_simple() {
    local entidad="$1" endpoint_post="$2" json_body="$3"
    local resp status body
    RESULT_ID=""

    resp=$(curl -s -w '\n%{http_code}' -X POST "${BASE_URL}${endpoint_post}" \
        -H "Content-Type: application/json" -H "Authorization: Bearer ${TOKEN}" \
        -d "$json_body")
    status="${resp##*$'\n'}"
    body="${resp%$'\n'*}"

    case "$status" in
        200|201)
            CREADO["$entidad"]=$(( ${CREADO["$entidad"]:-0} + 1 ))
            RESULT_ID=$(json_get "$body" "id")
            return 0
            ;;
        409)
            EXISTIA["$entidad"]=$(( ${EXISTIA["$entidad"]:-0} + 1 ))
            return 1
            ;;
        400)
            ERRORES["$entidad"]=$(( ${ERRORES["$entidad"]:-0} + 1 ))
            echo "  [400] POST ${endpoint_post} -> ${body}" >&2
            return 1
            ;;
        *)
            ERRORES["$entidad"]=$(( ${ERRORES["$entidad"]:-0} + 1 ))
            echo "  [HTTP ${status}] POST ${endpoint_post} -> ${body}" >&2
            return 1
            ;;
    esac
}

# ---------------------------------------------------------------------------
# Datos de ejemplo
# ---------------------------------------------------------------------------

CATEGORIA_NOMBRES=(
    "Ferretería General" "Plomería" "Electricidad" "Pinturería"
    "Herramientas Eléctricas" "Tornillería y Fijaciones" "Materiales de Construcción"
    "Jardín y Vivero" "Seguridad e Higiene" "Limpieza y Mantenimiento"
)

# nombre|telefono|email|direccion|notas  (notas puede ir vacío -> null)
PROVEEDOR_FILAS=(
    "Corralón San Martín|4542-1200|ventas@corralonsanmartin.com.uy|Ruta 21 Km 3, Carmelo|Entrega en 24hs para materiales de construcción"
    "Pinturerías Once S.R.L.|4542-3050|contacto@pintureriasonce.com.uy|Av. 19 de Abril 850|Descuento por volumen a partir de 20 unidades"
    "Ferretería del Litoral|4542-1780|litoral@ferreterialitoral.com.uy|Uruguay 445|"
    "Distribuidora Eléctrica Rivera|4542-2290|info@distelectricarivera.com.uy|Rivera 1120|Proveedor habitual de materiales eléctricos"
    "Suministros Industriales Colonia|4522-8810|ventas@suministroscolonia.com.uy|Ruta 1 Km 175|"
    "Materiales El Progreso|4542-0033|elprogreso@gmail.com|Flores 230|Pago a 30 días"
    "Herrajes y Tornillos Uruguay|4542-4410|contacto@herrajesuy.com|18 de Julio 300|"
    "Insumos de Seguridad SafeWork|4542-5567|safework@seguridaduy.com.uy|Artigas 670|Certificados de calidad IRAM"
    "Jardinería y Vivero La Espiga|4542-6689|laespiga@vivero.com.uy|Camino Rural s/n|"
    "Limpieza Total S.A.|4542-7723|limpiezatotal@gmail.com|Sarandí 512|Entrega semanal programada"
)

# nombre|abreviatura
UNIDAD_FILAS=(
    "Unidad|UN" "Kilogramo|KG" "Litro|L" "Metro|M" "Metro cuadrado|M2"
    "Caja|CJ" "Paquete|PQ" "Rollo|RL" "Bolsa|BL" "Docena|DC"
)

FUENTE_NOMBRES=(
    "Rentas Generales" "Fondo de Libre Disponibilidad" "Convenio con Intendencia de Colonia"
    "Fondo de Inversión Municipal" "Cooperación Internacional" "Fondo Vial Departamental"
    "Recursos Propios" "Partida Especial de Obras" "Fondo de Contingencia" "Transferencia MTOP"
)

# codigo|nombre
RUBRO_FILAS=(
    "101|Combustibles y Lubricantes" "102|Mantenimiento de Vehículos" "103|Materiales de Construcción"
    "104|Herramientas y Repuestos" "105|Servicios de Limpieza" "106|Insumos de Oficina"
    "107|Energía Eléctrica" "108|Agua y Saneamiento" "109|Seguros" "110|Publicidad y Difusión"
)

# nombre|programa (ejercicio fijo 2026, una asignación por línea sobre la fuente del mismo índice)
LINEA_POA_FILAS=(
    "Mantenimiento Vial Zona Norte|Obras Públicas"
    "Ampliación Red de Alumbrado|Servicios Urbanos"
    "Construcción Plaza de Deportes|Infraestructura Social"
    "Reparación de Bacheo Centro|Obras Públicas"
    "Equipamiento Taller Municipal|Logística y Talleres"
    "Forestación de Espacios Públicos|Medio Ambiente"
    "Mejora de Saneamiento Barrial|Servicios Urbanos"
    "Señalización Vial Departamental|Tránsito y Seguridad Vial"
    "Refacción Edificio Municipal|Infraestructura Social"
    "Programa de Limpieza de Costas|Medio Ambiente"
)
LINEA_POA_MONTOS=(850000 620000 1200000 430000 380000 260000 990000 310000 540000 275000)

# codigo|nombre|descripcion|idxCategoria|idxProveedor|idxUnidad|costo|venta|stockMinimo
PRODUCTO_FILAS=(
    "FER-001|Martillo carpintero 27mm|Martillo con mango de fibra de vidrio|0|0|0|380|590|5"
    "FER-002|Destornillador Phillips N2|Destornillador punta Phillips mango ergonómico|0|2|0|120|220|10"
    "PLU-001|Caño PVC 110mm x 3m|Caño de PVC para desagüe cloacal|1|0|3|890|1350|8"
    "PLU-002|Llave de paso 1/2 pulgada|Llave de paso de bronce|1|2|0|450|780|6"
    "ELE-001|Cable unipolar 2.5mm negro|Cable unipolar por metro para instalaciones|2|3|3|65|110|100"
    "ELE-002|Lámpara LED 12W luz cálida|Lámpara LED rosca E27|2|3|0|210|380|20"
    "PIN-001|Pintura látex interior blanco 20L|Balde de pintura látex para interiores|3|1|0|3200|4800|4"
    "HER-001|Amoladora angular 4.5 pulgadas|Amoladora angular 850W|4|6|0|2800|4200|3"
    "TOR-001|Tornillo autoperforante 8x1 caja x100|Caja de tornillos autoperforantes|5|6|5|340|560|15"
    "CON-001|Cemento Portland 25kg|Bolsa de cemento Portland tipo I|6|5|8|480|690|30"
)

# idxProducto|tipo(0=Entrada,1=Salida)|motivo(0=Compra,1=Venta)|cantidad|precioUnitario|comentario
MOVIMIENTO_FILAS=(
    "0|0|0|50|380|Compra inicial de stock"
    "1|0|0|100|120|Compra inicial de stock"
    "2|0|0|30|890|Compra inicial de stock"
    "3|0|0|40|450|Compra inicial de stock"
    "4|0|0|500|65|Compra inicial de stock"
    "5|0|0|80|210|Compra inicial de stock"
    "6|0|0|15|3200|Compra inicial de stock"
    "0|1|1|5|590|Venta al público"
    "1|1|1|10|220|Venta al público"
    "2|1|1|3|1350|Venta al público"
)

# fecha|concepto|idxFuente|monto
INGRESO_FILAS=(
    "2026-01-15|Transferencia mensual Rentas Generales|0|450000"
    "2026-02-10|Ingreso convenio departamental|2|300000"
    "2026-02-28|Aporte fondo vial|5|180000"
    "2026-03-05|Recursos propios - tasas municipales|6|220000"
    "2026-03-20|Transferencia MTOP obras viales|9|350000"
    "2026-04-02|Cooperación internacional - proyecto ambiental|4|120000"
    "2026-04-15|Fondo de contingencia - refuerzo|8|90000"
    "2026-05-01|Partida especial de obras|7|275000"
    "2026-05-18|Fondo de inversión municipal|3|400000"
    "2026-06-01|Fondo de libre disponibilidad|1|260000"
)

# idxProveedor|factura|orden|detalle|destino|fecha|monto|idxFuente|idxRubro|idxLineaPoa(-1=null)|condicion(0=Contado,1=Credito)|fechaVenc(vacio=null)
GASTO_FILAS=(
    "0|A-0001-00012345|OC-2026-001|Compra de materiales para bacheo|Taller Municipal|2026-01-20|85000|3|2|3|0|"
    "3|B-0002-00054321|OC-2026-002|Materiales eléctricos para alumbrado público|Vía Pública|2026-02-05|62000|1|6|1|0|"
    "7|C-0003-00098765||Elementos de seguridad e higiene para cuadrillas|Depósito Municipal|2026-02-18|45000|6|3|-1|0|"
    "8|D-0004-00011223||Plantines e insumos de forestación|Espacios Verdes|2026-03-01|38000|4|2|5|0|"
    "6|E-0005-00033445|OC-2026-003|Insumos y repuestos para talleres|Taller Municipal|2026-03-10|97000|4|3|4|1|2026-04-10"
    "1|F-0006-00077889|OC-2026-004|Pintura para edificios municipales|Edificio Municipal|2026-03-22|54000|8|2|8|0|"
    "4|G-0007-00012399||Materiales eléctricos para plaza deportiva|Plaza de Deportes|2026-04-01|71000|3|6|2|1|2026-05-01"
    "5|H-0008-00045612|OC-2026-005|Materiales de construcción varios|Depósito Municipal|2026-04-12|63000|0|2|-1|0|"
    "9|I-0009-00078901||Servicio de limpieza de costas|Costanera|2026-04-25|32000|9|4|9|0|"
    "2|J-0010-00099887|OC-2026-006|Herramientas para cuadrilla vial|Taller Municipal|2026-05-05|48000|6|3|0|1|2026-06-05"
)

# ---------------------------------------------------------------------------
# Siembra
# ---------------------------------------------------------------------------

CATEGORIA_IDS=()
PROVEEDOR_IDS=()
UNIDAD_IDS=()
FUENTE_IDS=()
RUBRO_IDS=()
LINEA_POA_IDS=()
PRODUCTO_IDS=()

sembrar_categorias() {
    echo "== Creando 10 categorías =="
    local nombre
    for nombre in "${CATEGORIA_NOMBRES[@]}"; do
        if crear_o_reusar "categorias" "/categorias" "{\"nombre\":\"${nombre}\"}" \
            "/categorias" "nombre" "$nombre"; then
            CATEGORIA_IDS+=("$RESULT_ID")
        else
            CATEGORIA_IDS+=("")
        fi
    done
}

sembrar_proveedores() {
    echo "== Creando 10 proveedores =="
    local fila nombre telefono email direccion notas notas_json
    for fila in "${PROVEEDOR_FILAS[@]}"; do
        IFS='|' read -r nombre telefono email direccion notas <<< "$fila"
        notas_json="null"
        [[ -n "$notas" ]] && notas_json="\"${notas}\""
        if crear_o_reusar "proveedores" "/proveedores" \
            "{\"nombre\":\"${nombre}\",\"telefono\":\"${telefono}\",\"email\":\"${email}\",\"direccion\":\"${direccion}\",\"notas\":${notas_json}}" \
            "/proveedores" "nombre" "$nombre"; then
            PROVEEDOR_IDS+=("$RESULT_ID")
        else
            PROVEEDOR_IDS+=("")
        fi
    done
}

sembrar_unidades() {
    echo "== Creando 10 unidades de medida =="
    local fila nombre abrev
    for fila in "${UNIDAD_FILAS[@]}"; do
        IFS='|' read -r nombre abrev <<< "$fila"
        if crear_o_reusar "unidades-medida" "/unidades-medida" \
            "{\"nombre\":\"${nombre}\",\"abreviatura\":\"${abrev}\"}" \
            "/unidades-medida" "nombre" "$nombre"; then
            UNIDAD_IDS+=("$RESULT_ID")
        else
            UNIDAD_IDS+=("")
        fi
    done
}

sembrar_fuentes() {
    echo "== Creando 10 fuentes de financiamiento =="
    local nombre
    for nombre in "${FUENTE_NOMBRES[@]}"; do
        if crear_o_reusar "finanzas/fuentes" "/finanzas/fuentes" "{\"nombre\":\"${nombre}\"}" \
            "/finanzas/fuentes" "nombre" "$nombre"; then
            FUENTE_IDS+=("$RESULT_ID")
        else
            FUENTE_IDS+=("")
        fi
    done
}

sembrar_rubros() {
    echo "== Creando 10 rubros de gasto =="
    local fila codigo nombre
    for fila in "${RUBRO_FILAS[@]}"; do
        IFS='|' read -r codigo nombre <<< "$fila"
        if crear_o_reusar "finanzas/rubros" "/finanzas/rubros" \
            "{\"codigo\":${codigo},\"nombre\":\"${nombre}\"}" \
            "/finanzas/rubros" "codigo" "$codigo"; then
            RUBRO_IDS+=("$RESULT_ID")
        else
            RUBRO_IDS+=("")
        fi
    done
}

sembrar_lineas_poa() {
    echo "== Creando 10 líneas POA =="
    local i fila nombre programa monto fuente_id asignaciones
    for i in "${!LINEA_POA_FILAS[@]}"; do
        IFS='|' read -r nombre programa <<< "${LINEA_POA_FILAS[$i]}"
        monto="${LINEA_POA_MONTOS[$i]}"
        fuente_id="${FUENTE_IDS[$i]:-}"
        if [[ -z "$fuente_id" ]]; then
            echo "  [saltado] línea POA '${nombre}': no hay id de fuente de financiamiento (índice ${i})." >&2
            ERRORES["finanzas/lineas-poa"]=$(( ${ERRORES["finanzas/lineas-poa"]:-0} + 1 ))
            LINEA_POA_IDS+=("")
            continue
        fi
        asignaciones="[{\"fuenteFinanciamientoId\":${fuente_id},\"monto\":${monto}}]"
        if crear_o_reusar "finanzas/lineas-poa" "/finanzas/lineas-poa" \
            "{\"nombre\":\"${nombre}\",\"programa\":\"${programa}\",\"ejercicio\":2026,\"asignaciones\":${asignaciones}}" \
            "/finanzas/lineas-poa" "nombre" "$nombre"; then
            LINEA_POA_IDS+=("$RESULT_ID")
        else
            LINEA_POA_IDS+=("")
        fi
    done
}

sembrar_productos() {
    echo "== Creando 10 productos =="
    local fila codigo nombre descripcion idxCat idxProv idxUni costo venta stockMin
    local categoria_id proveedor_id unidad_id categoria_json proveedor_json
    for fila in "${PRODUCTO_FILAS[@]}"; do
        IFS='|' read -r codigo nombre descripcion idxCat idxProv idxUni costo venta stockMin <<< "$fila"
        categoria_id="${CATEGORIA_IDS[$idxCat]:-}"
        proveedor_id="${PROVEEDOR_IDS[$idxProv]:-}"
        unidad_id="${UNIDAD_IDS[$idxUni]:-}"
        if [[ -z "$unidad_id" ]]; then
            echo "  [saltado] producto '${codigo}': no hay id de unidad de medida." >&2
            ERRORES["productos"]=$(( ${ERRORES["productos"]:-0} + 1 ))
            PRODUCTO_IDS+=("")
            continue
        fi
        categoria_json="null"; [[ -n "$categoria_id" ]] && categoria_json="$categoria_id"
        proveedor_json="null"; [[ -n "$proveedor_id" ]] && proveedor_json="$proveedor_id"
        if crear_o_reusar "productos" "/productos" \
            "{\"codigo\":\"${codigo}\",\"codigoBarras\":null,\"nombre\":\"${nombre}\",\"descripcion\":\"${descripcion}\",\"categoriaId\":${categoria_json},\"proveedorId\":${proveedor_json},\"unidadMedidaId\":${unidad_id},\"precioCosto\":${costo},\"precioVenta\":${venta},\"stockMinimo\":${stockMin}}" \
            "/productos?texto=${codigo}" "codigo" "$codigo"; then
            PRODUCTO_IDS+=("$RESULT_ID")
        else
            PRODUCTO_IDS+=("")
        fi
    done
}

sembrar_movimientos() {
    echo "== Creando 10 movimientos de stock =="
    local fila idxProd tipo motivo cantidad precio comentario producto_id
    for fila in "${MOVIMIENTO_FILAS[@]}"; do
        IFS='|' read -r idxProd tipo motivo cantidad precio comentario <<< "$fila"
        producto_id="${PRODUCTO_IDS[$idxProd]:-}"
        if [[ -z "$producto_id" ]]; then
            echo "  [saltado] movimiento sobre producto índice ${idxProd}: no hay id de producto." >&2
            ERRORES["movimientos"]=$(( ${ERRORES["movimientos"]:-0} + 1 ))
            continue
        fi
        crear_simple "movimientos" "/movimientos" \
            "{\"productoId\":${producto_id},\"tipo\":${tipo},\"motivo\":${motivo},\"cantidad\":${cantidad},\"precioUnitario\":${precio},\"comentario\":\"${comentario}\",\"forzar\":false}" \
            || true
    done
}

sembrar_ingresos() {
    echo "== Creando 10 ingresos de caja =="
    local fila fecha concepto idxFuente monto fuente_id
    for fila in "${INGRESO_FILAS[@]}"; do
        IFS='|' read -r fecha concepto idxFuente monto <<< "$fila"
        fuente_id="${FUENTE_IDS[$idxFuente]:-}"
        if [[ -z "$fuente_id" ]]; then
            echo "  [saltado] ingreso '${concepto}': no hay id de fuente de financiamiento." >&2
            ERRORES["finanzas/ingresos"]=$(( ${ERRORES["finanzas/ingresos"]:-0} + 1 ))
            continue
        fi
        crear_simple "finanzas/ingresos" "/finanzas/ingresos" \
            "{\"fecha\":\"${fecha}T00:00:00Z\",\"concepto\":\"${concepto}\",\"fuenteFinanciamientoId\":${fuente_id},\"monto\":${monto}}" \
            || true
    done
}

sembrar_gastos() {
    echo "== Creando 10 gastos =="
    local fila idxProv factura orden detalle destino fecha monto idxFuente idxRubro idxLinea condicion fechaVenc
    local proveedor_id fuente_id rubro_id linea_id orden_json linea_json fechaVenc_json
    for fila in "${GASTO_FILAS[@]}"; do
        IFS='|' read -r idxProv factura orden detalle destino fecha monto idxFuente idxRubro idxLinea condicion fechaVenc <<< "$fila"
        proveedor_id="${PROVEEDOR_IDS[$idxProv]:-}"
        fuente_id="${FUENTE_IDS[$idxFuente]:-}"
        rubro_id="${RUBRO_IDS[$idxRubro]:-}"
        if [[ -z "$proveedor_id" || -z "$fuente_id" || -z "$rubro_id" ]]; then
            echo "  [saltado] gasto factura '${factura}': faltan ids de dependencias (proveedor/fuente/rubro)." >&2
            ERRORES["finanzas/gastos"]=$(( ${ERRORES["finanzas/gastos"]:-0} + 1 ))
            continue
        fi
        orden_json="null"; [[ -n "$orden" ]] && orden_json="\"${orden}\""
        linea_json="null"
        if [[ "$idxLinea" != "-1" ]]; then
            linea_id="${LINEA_POA_IDS[$idxLinea]:-}"
            [[ -n "$linea_id" ]] && linea_json="$linea_id"
        fi
        fechaVenc_json="null"; [[ -n "$fechaVenc" ]] && fechaVenc_json="\"${fechaVenc}T00:00:00Z\""
        crear_simple "finanzas/gastos" "/finanzas/gastos" \
            "{\"proveedorId\":${proveedor_id},\"numeroFactura\":\"${factura}\",\"numeroOrden\":${orden_json},\"detalle\":\"${detalle}\",\"destino\":\"${destino}\",\"fecha\":\"${fecha}T00:00:00Z\",\"montoTotal\":${monto},\"fuenteFinanciamientoId\":${fuente_id},\"rubroGastoId\":${rubro_id},\"lineaPoaId\":${linea_json},\"condicionPago\":${condicion},\"fechaVencimiento\":${fechaVenc_json},\"movimientoIds\":null}" \
            || true
    done
}

# ---------------------------------------------------------------------------
# Verificación (GET de listado por entidad)
# ---------------------------------------------------------------------------

verificar() {
    echo
    echo "== Verificación (listados actuales) =="
    local endpoint etiqueta count body
    local -a pares=(
        "/categorias|categorías"
        "/proveedores|proveedores"
        "/unidades-medida|unidades de medida"
        "/finanzas/fuentes|fuentes de financiamiento"
        "/finanzas/rubros|rubros de gasto"
        "/finanzas/lineas-poa|líneas POA"
        "/productos?texto=|productos"
        "/finanzas/ingresos|ingresos de caja"
    )
    for par in "${pares[@]}"; do
        IFS='|' read -r endpoint etiqueta <<< "$par"
        body=$(get_lista "$endpoint")
        if [[ "$HAS_JQ" -eq 1 ]]; then
            count=$(printf '%s' "$body" | jq 'length' 2>/dev/null || echo "?")
        else
            count=$(python3 -c "
import json, sys
try:
    print(len(json.loads(sys.stdin.read())))
except Exception:
    print('?')
" <<< "$body")
        fi
        echo "  ${etiqueta}: ${count} registros"
    done

    body=$(curl -s -X GET "${BASE_URL}/movimientos/historial" -H "Authorization: Bearer ${TOKEN}")
    if [[ "$HAS_JQ" -eq 1 ]]; then
        count=$(printf '%s' "$body" | jq 'length' 2>/dev/null || echo "?")
    else
        count=$(python3 -c "
import json, sys
try:
    print(len(json.loads(sys.stdin.read())))
except Exception:
    print('?')
" <<< "$body")
    fi
    echo "  movimientos (historial): ${count} registros"

    body=$(curl -s -X GET "${BASE_URL}/finanzas/gastos" -H "Authorization: Bearer ${TOKEN}")
    if [[ "$HAS_JQ" -eq 1 ]]; then
        count=$(printf '%s' "$body" | jq 'length' 2>/dev/null || echo "?")
    else
        count=$(python3 -c "
import json, sys
try:
    print(len(json.loads(sys.stdin.read())))
except Exception:
    print('?')
" <<< "$body")
    fi
    echo "  gastos: ${count} registros"
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

login

sembrar_categorias
sembrar_proveedores
sembrar_unidades
sembrar_fuentes
sembrar_rubros
sembrar_lineas_poa
sembrar_productos
sembrar_movimientos
sembrar_ingresos
sembrar_gastos

verificar

echo
echo "== Resumen =="
TODAS_ENTIDADES=(
    "categorias" "proveedores" "unidades-medida" "finanzas/fuentes" "finanzas/rubros"
    "finanzas/lineas-poa" "productos" "movimientos" "finanzas/ingresos" "finanzas/gastos"
)
for e in "${TODAS_ENTIDADES[@]}"; do
    printf '  %-22s creados=%-3s ya_existian=%-3s errores=%-3s\n' \
        "$e" "${CREADO[$e]:-0}" "${EXISTIA[$e]:-0}" "${ERRORES[$e]:-0}"
done

echo
echo "Listo."
