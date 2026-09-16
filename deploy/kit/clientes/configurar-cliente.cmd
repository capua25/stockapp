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
rem   configurar-cliente.cmd                  (pregunta IP y puerto de forma interactiva)
rem   configurar-cliente.cmd <IP> <PUERTO>    (los toma como parametros, sin preguntar)
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

set "IP=%~1"
set "PUERTO=%~2"

if "%IP%"=="" (
    set /p "IP=Ingresa la IP del servidor: "
)
if "%IP%"=="" (
    echo.
    echo ERROR: no ingresaste ninguna IP. Cancelando, no se escribio nada.
    goto :fin_error
)

if "%PUERTO%"=="" (
    set /p "PUERTO=Ingresa el puerto del servidor: "
)
if "%PUERTO%"=="" (
    echo.
    echo ERROR: no ingresaste ningun puerto. Cancelando, no se escribio nada.
    goto :fin_error
)

set "URL=http://%IP%:%PUERTO%"

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
