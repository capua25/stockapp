@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul 2>&1

rem ============================================================================================
rem configurar-cliente.cmd
rem
rem Escribe el archivo de conexion que usa Gestion Municipal para saber a que servidor
rem conectarse, como alternativa a abrir el Configurador grafico (GestionMunicipal.Configurador.exe)
rem a mano en cada PC.
rem
rem Ruta y clave verificadas contra el codigo real del desktop:
rem   - src/StockApp.Configuracion/RutaConexion.cs (NombreCarpeta="GestionMunicipal",
rem     NombreArchivo="conexion.json") -> %%AppData%%\GestionMunicipal\conexion.json
rem   - src/StockApp.Configuracion/ConexionDefaults.cs (ClaveApiBaseUrl="Api:BaseUrl")
rem   - src/StockApp.Configuracion/ConexionConfigStore.cs -> forma exacta del JSON:
rem     {"Api":{"BaseUrl":"http://<IP>:<PUERTO>"}}
rem
rem Uso:
rem   configurar-cliente.cmd                  (pregunta servidor, puerto opcional y esquema
rem                                             de forma interactiva)
rem   configurar-cliente.cmd <IP> <PUERTO>    (retrocompatible: arma http://IP:PUERTO, sin
rem                                             preguntar nada - forma de siempre, IGUAL que
rem                                             antes de que este script soportara https)
rem   configurar-cliente.cmd <URL>            (URL completa, para https o puerto omitido:
rem                                             ej. https://stockapp.midominio.dev:8080 o
rem                                             https://stockapp.midominio.dev sin puerto)
rem
rem IMPORTANTE: este script REQUIERE PRUEBA EN WINDOWS REAL. Se escribio y se revisó a mano
rem contra el formato exacto que espera el codigo, pero NO se pudo ejecutar ni verificar desde
rem este entorno (WSL2 / Linux no puede correr un .cmd de Windows). Antes de confiar en el en
rem el municipio, probalo en una PC Windows real: corré el script, fijate que el archivo quede
rem bien escrito, y abrí Gestion Municipal para confirmar que conecta.
rem ============================================================================================

echo.
echo ======================================================================
echo   Configurador de conexion - Gestion Municipal (linea de comandos)
echo ======================================================================
echo.

set "ARG1=%~1"
set "ARG2=%~2"

rem ── Modo URL completa: un solo argumento que ya empieza con http:// o https:// ────────────
set "ES_URL="
if not "%ARG1%"=="" if "%ARG2%"=="" (
    if /I "%ARG1:~0,8%"=="https://" set "ES_URL=1"
    if /I "%ARG1:~0,7%"=="http://" set "ES_URL=1"
)

if defined ES_URL (
    set "URL=%ARG1%"
    goto :url_lista
)

rem ── Modo retrocompatible: dos argumentos, IP y PUERTO, siempre http (comportamiento de
rem    siempre, sin cambios) ─────────────────────────────────────────────────────────────────
rem NOTA: acá adentro hay que usar %ARG1%/%ARG2% (no una variable recién asignada en este
rem mismo bloque) porque EnableDelayedExpansion está activo: dentro de un bloque entre
rem paréntesis, %VAR% se expande UNA sola vez al entrar al bloque, así que asignar y leer la
rem misma variable con %...% en el mismo bloque lee el valor VIEJO (vacío acá). ARG1/ARG2 se
rem asignaron en una línea anterior, fuera de este bloque, así que %ARG1%/%ARG2% sí traen el
rem valor correcto.
if not "%ARG1%"=="" if not "%ARG2%"=="" (
    set "URL=http://%ARG1%:%ARG2%"
    goto :url_lista
)

rem ── Modo interactivo: servidor, esquema (S/N) y puerto opcional ───────────────────────────
set "IP=%ARG1%"
if "%IP%"=="" (
    set /p "IP=Ingresa la IP o nombre del servidor: "
)
if "%IP%"=="" (
    echo.
    echo ERROR: no ingresaste ningun servidor. Cancelando, no se escribio nada.
    goto :fin_error
)

set "USARHTTPS="
set /p "USARHTTPS=Usar HTTPS? (S/N, Enter = N): "
if /I "%USARHTTPS%"=="S" (set "ESQUEMA=https") else (set "ESQUEMA=http")

set /p "PUERTO=Ingresa el puerto del servidor (opcional, Enter para omitirlo): "

if "%PUERTO%"=="" (
    set "URL=%ESQUEMA%://%IP%"
) else (
    set "URL=%ESQUEMA%://%IP%:%PUERTO%"
)

:url_lista

set "CARPETA_DESTINO=%AppData%\GestionMunicipal"
set "ARCHIVO_DESTINO=%CARPETA_DESTINO%\conexion.json"

echo.
echo Se va a configurar Gestion Municipal para conectarse a:
echo.
echo     %URL%
echo.
echo El archivo se va a guardar en:
echo.
echo     %ARCHIVO_DESTINO%
echo.

if exist "%ARCHIVO_DESTINO%" (
    echo Ya existe un archivo de conexion en esa ruta.
    set /p "CONFIRMAR=Queres sobreescribirlo? (S/N): "
    if /I not "!CONFIRMAR!"=="S" (
        echo.
        echo Cancelado. No se toco el archivo existente.
        goto :fin_ok
    )
)

if not exist "%CARPETA_DESTINO%" (
    mkdir "%CARPETA_DESTINO%" >nul 2>&1
    if errorlevel 1 (
        echo.
        echo ERROR: no se pudo crear la carpeta "%CARPETA_DESTINO%".
        goto :fin_error
    )
)

rem Escribimos el JSON con echo/redireccion (sin dependencias externas). Formato exacto
rem verificado contra ConexionConfigStore.cs: {"Api":{"BaseUrl":"..."}}
(
    echo {
    echo   "Api": {
    echo     "BaseUrl": "%URL%"
    echo   }
    echo }
) > "%ARCHIVO_DESTINO%"

if errorlevel 1 (
    echo.
    echo ERROR: fallo al escribir "%ARCHIVO_DESTINO%".
    goto :fin_error
)

echo.
echo Listo. Se guardo la configuracion en:
echo     %ARCHIVO_DESTINO%
echo.
echo Abri Gestion Municipal para confirmar que conecta contra %URL%.
goto :fin_ok

:fin_error
echo.
echo No se realizaron cambios.
endlocal
exit /b 1

:fin_ok
endlocal
exit /b 0
