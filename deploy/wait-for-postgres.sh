#!/usr/bin/env bash
set -euo pipefail

# Espera activa a que Postgres (contenedor stockapp-pg, 127.0.0.1:5433) responda antes de
# que arranque StockApp.Api.
#
# Por qué existe este script: Program.cs corre `await db.Database.MigrateAsync()` ANTES de
# `app.Run()`. Si Postgres todavía no levantó (por ejemplo, el VPS acaba de reiniciar y
# Docker tarda unos segundos más que systemd en tener el contenedor listo), ese await queda
# COLGADO EN SILENCIO -- sin loguear nada, sin fallar, el servicio queda "activo" para
# systemd pero nunca responde un solo request. Preferimos fallar ruidoso acá (ExecStartPre)
# antes que dejar que la API cuelgue en un estado indefinido.
#
# Instalado por install.sh en /usr/local/lib/stockapp-api/wait-for-postgres.sh (fuera del
# directorio de la release, /opt/stockapp-api, para que actualizar la API no lo pise).
# Referenciado desde stockapp-api.service como ExecStartPre.

readonly HOST="127.0.0.1"
readonly PORT="5433"
readonly MAX_INTENTOS=30
readonly ESPERA_SEGUNDOS=2

command -v pg_isready >/dev/null 2>&1 || {
    echo "[wait-for-postgres] ERROR: pg_isready no está en el PATH. ¿Se instaló postgresql-client-16? (install.sh debería haberlo hecho)." >&2
    exit 1
}

for intento in $(seq 1 "$MAX_INTENTOS"); do
    if pg_isready -h "$HOST" -p "$PORT" -q; then
        echo "[wait-for-postgres] Postgres respondió en ${HOST}:${PORT} (intento ${intento}/${MAX_INTENTOS})."
        exit 0
    fi
    echo "[wait-for-postgres] Postgres no responde todavía en ${HOST}:${PORT} (intento ${intento}/${MAX_INTENTOS})..." >&2
    sleep "$ESPERA_SEGUNDOS"
done

echo "[wait-for-postgres] ERROR: Postgres no respondió en ${HOST}:${PORT} tras $((MAX_INTENTOS * ESPERA_SEGUNDOS)) segundos." >&2
echo "[wait-for-postgres] Verificá: docker compose --env-file deploy/.env -f deploy/docker-compose.postgres.yml ps" >&2
echo "[wait-for-postgres] Abortando el arranque de stockapp-api (mejor fallar ruidoso que colgar en silencio)." >&2
exit 1
