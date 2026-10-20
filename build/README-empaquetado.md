# Flujo de empaquetado y publicacion — Gestión Municipal

> Este documento cubre el flujo **manual** de empaquetado por OS, publicacion en el feed propio del VPS,
> y la convencion de versionado y severidades. No hay CI/CD en esta fase (Incremento 7 Fase A).

---

## Prerequisitos

### Todos los OS

| Herramienta | Version minima | Como instalar |
|-------------|----------------|---------------|
| .NET SDK    | 10.0           | https://dot.net → "Download .NET 10" |
| vpk CLI     | 1.2.x          | `dotnet tool install -g vpk` |

Verificar instalacion:

```bash
dotnet --version   # debe mostrar 10.x.x
vpk --version      # debe mostrar algo (no "command not found")
```

Si `vpk` no se encuentra despues de instalarlo, agregar el directorio de tools globales al PATH:

- Windows: `%USERPROFILE%\.dotnet\tools`
- Linux/macOS: `$HOME/.dotnet/tools`

### Windows adicional

Ejecutar desde PowerShell 5.1 o superior (viene con Windows 10/11).

### Linux adicional

El script requiere bash 4+ y las herramientas estandar (`grep`, `mkdir`), que ya vienen en
cualquier distro moderna.

---

## Paso a paso por OS

### Windows

Desde la raiz del repositorio, en PowerShell:

```powershell
.\build\pack-win.ps1
```

El script:
1. Verifica que `dotnet` y `vpk` esten instalados.
2. Lee la version de `src/StockApp.Presentation/StockApp.Presentation.csproj`.
3. Ejecuta `dotnet publish -r win-x64 --self-contained true`.
4. Ejecuta `vpk pack --channel win`.
5. Deja los artefactos en `releases/win/`.

Sobreescribir la version (sin editar el csproj):

```powershell
.\build\pack-win.ps1 -Version 1.2.0
```

### Empaquetar desde WSL

Tambien se puede empaquetar para Windows sin salir de WSL, invocando `powershell.exe` directamente:

```bash
powershell.exe -File build\pack-win.ps1
```

**Recomendacion de rendimiento:** copiar el arbol del repo a una ruta nativa de Windows (ej.
`C:\temp\stockapp`) antes de empaquetar, en vez de publicar directo sobre la ruta UNC
`\\wsl.localhost\...`. `dotnet publish` sobre UNC es notablemente mas lento.

**Gotcha confirmado:** `dotnet publish` deja vivo un build server (`VBCSCompiler.exe`) que
mantiene abiertos archivos de la carpeta temporal de publicacion y bloquea su borrado. Si el
script falla al limpiar o sobreescribir esa carpeta, correr:

```powershell
dotnet build-server shutdown
```

### Linux

Desde la raiz del repositorio, en bash:

```bash
chmod +x build/pack-linux.sh   # solo la primera vez
./build/pack-linux.sh
```

El script hace lo mismo que el de Windows, pero publica para `linux-x64` y produce un AppImage.
Artefactos en `releases/linux/`.

Sobreescribir la version:

```bash
./build/pack-linux.sh 1.2.0
```

---

## Bumping de version

La version vive **en un solo lugar**: el `<Version>` del csproj de Presentation.

```
src/StockApp.Presentation/StockApp.Presentation.csproj
```

```xml
<Version>0.1.0</Version>
<InformationalVersion>$(Version)</InformationalVersion>
```

Pasos para una nueva release:

1. Editar `<Version>` con la nueva version SemVer2 (ej: `0.2.0`, `1.0.0`, `1.1.0-beta.1`).
2. Editar `build/RELEASE_NOTES.md` (ver seccion siguiente).
3. Ejecutar el script de empaquetado del OS.
4. Publicar en el VPS con `build/publicar-release.sh <version>` (ver "Publicar en el VPS").

**No editar la version en ningun otro archivo.** Los scripts la leen del csproj automaticamente.

---

## Archivo de release notes y severidades

Antes de empaquetar, editar `build/RELEASE_NOTES.md`:

```markdown
severity: normal

# Gestión Municipal 0.2.0

- Cambio A
- Cambio B
```

### La linea `severity:` (OBLIGATORIA — debe ser la primera linea)

| Valor      | Comportamiento en la app del cliente |
|------------|--------------------------------------|
| `normal`   | Banner discreto no-bloqueante. Se aplica en el proximo reinicio voluntario. |
| `important`| Modal posponible al arrancar. Reaparece en cada arranque hasta actualizar. |
| `critical` | Overlay rojo bloqueante. Si no se puede descargar, la app entra en modo degradado (operable con banner rojo permanente no-cerrable). |

Reglas:
- La linea `severity:` debe ser la **primera linea** del archivo, sin espacios ni comentarios antes.
- El actualizador la parsea **antes de descargar** el paquete (necesario para decidir el modo `critical`).
- Si la linea esta ausente o el valor es invalido, se usa `normal` por defecto.

---

## Artefactos generados

### Windows (`releases/win/`)

`vpk` antepone el packId y el channel al nombre de archivo — el instalador **no** se llama
`Setup.exe`. Artefactos reales de una corrida limpia (verificado 2026-08-23 con vpk 1.2.0):

| Archivo | Descripcion |
|---------|-------------|
| `GestionMunicipal-win-Setup.exe` | Instalador para el usuario final. Incluye .NET 10 runtime embebido. |
| `GestionMunicipal-win-Portable.zip` | Version portable sin instalador (extraer y ejecutar). |
| `GestionMunicipal-X.Y.Z-full.nupkg` | Paquete completo (primera instalacion o sin release previa). |
| `GestionMunicipal-X.Y.Z-delta.nupkg` | Paquete diferencial. Solo se genera cuando ya hay una release previa en `releases/win/`; no aparece en una corrida limpia. |
| `releases.win.json` | Feed de updates que la app consulta al arrancar. |
| `assets.win.json` | Metadata de los assets del feed (hashes, tamaños). |
| `RELEASES` | Indice de releases en formato Squirrel/Velopack. **El cliente NO lo usa**: Velopack 1.2.0 pide solo `releases.win.json` y los `.nupkg`. |

### Linux (`releases/linux/`)

| Archivo | Descripcion |
|---------|-------------|
| `GestionMunicipal-X.Y.Z-linux.AppImage` | Ejecutable portable. Sin instalador, sin privilegios. |
| `GestionMunicipal-X.Y.Z-full.nupkg` | Paquete completo. |
| `GestionMunicipal-X.Y.Z-delta.nupkg` | Paquete diferencial. |
| `releases.linux.json` | Feed de updates. |

Los deltas se generan **automaticamente** si ya hay una release previa en el directorio de salida.
Conservar el contenido de `releases/` entre versiones para aprovechar los deltas.

---

## Publicar en el VPS

La app consulta el feed propio que sirve `StockApp.Api` en `/updates` (carpeta del servidor
`/var/lib/stockapp/updates`, dueño `stockapp:stockapp`, modo 750). Ya no se usa GitHub Releases.

```bash
build/publicar-release.sh 0.2.0                      # usa <repo>/releases/win
build/publicar-release.sh 0.2.0 --origen /ruta/win   # pack-win.ps1 corrio en otro checkout (p. ej. Windows)
```

Configuracion: `deploy/.env` local o entorno. `VPS_HOST`, `VPS_USER` y `VPS_SSH_PORT` son
**obligatorios y no tienen default** (un default subiria el feed al servidor equivocado en
silencio). Opcionales: `VPS_DIR` (staging del usuario SSH, default `~/stockapp-deploy`),
`API_SCHEME`/`API_HOSTNAME`/`API_PORT` (como `deploy/deploy-vps.sh`) y `FEED_VERSIONES_MAX`
(default `5`, entero positivo; ver "Poda del feed"). Hace falta `jq` local y
`sudo` sin contrasena para el usuario SSH. El script publica solo el canal Windows
(`releases.win.json`).

Orden que sigue el script (el feed va **ultimo**, asi nunca apunta a paquetes que todavia no estan):

1. Valida local: `<version>` es el `Full` de mayor version del `releases.win.json`; **poda el feed**
   (ver "Poda del feed") y, sobre el feed podado, cada `.nupkg` existe en el origen con el `Size` y
   un nombre seguro.
2. Releva el servidor (nombres, tamanos y sha256) y lee el `releases.win.json` que sirve hoy. Si no
   puede relevar, aborta sin subir nada. Si `<version>` es **menor** que la que sirve el servidor,
   aborta (ver `--forzar` abajo).
3. Si el servidor ya tiene un paquete con el mismo nombre pero **otro contenido** (sha256 distinto al
   del feed nuevo), aborta: nunca pisa un paquete en el lugar. Sube por `scp` a un staging
   (`<VPS_DIR>/updates-staging`) solo los paquetes que el servidor no tiene, mas el JSON. El staging se borra siempre al terminar.
4. Instala los paquetes con `sudo install` en la carpeta del feed.
5. Re-releva y verifica que **todos** los paquetes del feed nuevo estan con su tamano y sha256. Si no, aborta
   y el feed vigente queda intacto.
6. Publica `releases.win.json` con rename atomico (`install` a `.tmp` + `mv -f`).
7. Verifica por HTTP (`curl` en el VPS, y desde la maquina del operador si `API_SCHEME=https`) que el
   feed servido contiene la version y que el full responde 200 con el `Content-Length` correcto.
   La API no se reinicia.

### Poda del feed

El `releases.win.json` lleva las notas de cada version y crece sin parar (~35 KB con 3 versiones);
el cliente lo baja entero en cada chequeo (ahora cada 15 minutos). Por eso el script publica una
copia **podada** que conserva solo los assets (`Full` y `Delta`) de las `FEED_VERSIONES_MAX`
versiones mayores (default 5, orden `sort -V`). Detalles:

- Es **no destructiva**: no se borra ningun archivo del servidor; los `.nupkg` de versiones viejas
  siguen ahi, solo dejan de figurar en el feed.
- La poda se hace en un temporal: el `releases.win.json` de `--origen` no se modifica. Con
  `FEED_VERSIONES_MAX` o menos versiones el feed se publica byte a byte igual.
- Todo lo posterior (que cada paquete exista en el origen, el re-relevamiento de tamano y sha256
  del paso 5, la subida) opera sobre el feed podado.
- Un cliente mas viejo que la ventana conservada no tiene delta hacia la version actual y
  descarga el ultimo `Full`.
- `FEED_VERSIONES_MAX` invalido (no entero positivo) aborta antes de relevar o subir nada.
- **Excepcion: criticas viejas.** Ademas de la ventana, el feed conserva siempre la entrada `Full`
  de cualquier version mas vieja cuyas notas empiecen con `severity: critical` (los `Delta` de esas
  versiones si se podan). Razon: el cliente calcula la severidad efectiva como el maximo entre las
  releases que hay entre la instalada y la destino, leidas del feed; sin esa entrada, un cliente
  con mas de N versiones de atraso perderia la marca critica. El parseo espeja a `SeverityParser`
  (primera linea, `severity:` sin distinguir mayusculas, valor recortado; ausente o invalido = no
  critica). Ese `Full` viejo ya esta en el servidor de publicaciones anteriores, por eso **no se exige
  en `--origen`**: si falta ahi, el script exige que el servidor lo tenga con el tamano y el sha256
  del feed, y si no aborta sin subir nada. Si esta en `--origen` se valida como cualquier paquete.

Reglas:

- **Conservar `releases/win` entre versiones.** Sin esa carpeta la proxima version sale solo con
  `full` (~53 MB por cliente en vez de un delta) y el script exige que todos los paquetes del feed
  (ya podado) existan en el origen.
- **Retirar una version mala = publicar la ultima buena con un numero MAYOR.** Velopack no hace
  downgrade: re-empaquetar el codigo bueno con una version nueva (ej. la mala es 0.3.0, la buena es
  0.2.0 -> publicar 0.3.1) y correr el script. Por eso el script **se niega** a publicar una
  version menor que la que sirve el servidor; `--forzar` lo permite, pero es solo una salida de
  emergencia (los clientes ya en la version mayor no bajan), no el camino del rollback.
- **El feed es publico** (`/updates` no pide autenticacion): no subir nada que no pueda leer cualquiera.
- Los `.nupkg` ya publicados con el mismo contenido (tamano y sha256) no se vuelven a subir:
  republicar la misma version es seguro **si el empaquetado es el mismo**. Si se re-empaqueto la misma
  version (los hashes cambian), el script aborta: publicar con un numero mayor.
- No usar prerelease (`-beta`) como version a publicar: la comparacion de "mayor version" es `sort -V`.

---

## Caveat Linux: AppImage en directorios protegidos

Si el AppImage esta instalado en `/opt`, `/usr/local`, u otro directorio protegido, Velopack
intentara aplicar updates usando `pkexec`, lo que genera un prompt de autenticacion grafico.
Si `pkexec` no esta disponible o el usuario lo rechaza, la actualizacion falla.

**Recomendacion:** instalar el AppImage en `$HOME` (ej: `$HOME/Applications/`). Desde ahi,
Velopack puede actualizar sin privilegios.

Si la actualizacion falla en entorno `critical` → la app entra en modo degradado (operable,
banner rojo permanente, reintenta en el proximo arranque).

---

## Nota: SmartScreen en Windows (Fase A, sin code signing)

Sin un certificado de code signing, Windows Defender SmartScreen mostrara:
"Windows proteccion tu PC — El editor de esta app es desconocido."

El usuario puede hacer clic en "Mas informacion" y luego "Ejecutar de todas formas".
Esto es **esperado y documentado** para instalaciones B2B directas (Fase A).

Los scripts tienen la linea `--signParams` comentada. Cuando haya un certificado, descomentar
esa linea en `pack-win.ps1` y configurar la variable de entorno `CERT_PWD`.

---

## Licenciamiento (Fase B)

Prerequisito **obligatorio** antes de la primera release con licenciamiento: la API arranca en
modo bloqueado (fail-closed por diseno) si no tiene una clave publica configurada, y ninguna
licencia puede activarse sin ella.

1. **Generar el par de claves — UNA sola vez**, fuera del repositorio:

   ```bash
   dotnet run --project tools/StockApp.Licencias.Cli -- generar-claves --salida <dir-privado-fuera-del-repo>
   ```

   Esto produce una clave privada (queda en poder de quien firma licencias, nunca se commitea
   ni se distribuye) y una clave publica en Base64.

2. **Configurar la clave publica en la API**, via `Licencia:ClavePublicaBase64` en
   `appsettings.json` o variable de entorno equivalente. Sin esto, la API arranca en modo
   bloqueado y no hay forma de activar ninguna licencia (fail-closed intencional, no es un bug).

3. **La CLI (`StockApp.Licencias.Cli`) jamas se distribuye** al cliente ni se empaqueta junto a
   la app de escritorio. Es una herramienta interna del proveedor para generar claves, licencias
   y tokens de reset — vive y se ejecuta solo en el entorno de quien administra las licencias.

---

## Checklist de validacion manual

Estas verificaciones NO son automatizables con xUnit. Deben hacerse en un entorno real por OS.

### Empaquetado base

- [ ] `.\build\pack-win.ps1` termina sin errores en Windows.
- [ ] `./build/pack-linux.sh` termina sin errores en Linux.
- [ ] Los artefactos aparecen en `releases/win/` y `releases/linux/` respectivamente.

### Instalacion limpia

- [ ] `Setup.exe` (Windows): instala en una maquina **sin .NET instalado**. La app arranca.
      SmartScreen muestra advertencia → aceptar → instala igual.
- [ ] `AppImage` (Linux): ejecutable en `$HOME` sin instalar nada. La app arranca.
      Verificar que no requiere .NET instalado en el sistema.

### Ciclo de actualizacion end-to-end

- [ ] Publicar v1 (ej: 0.1.0) con `build/publicar-release.sh` → instalar en maquina limpia.
- [ ] Publicar v2 (ej: 0.2.0) con `severity: normal` → abrir la app →
      el banner discreto aparece en la barra de la app.
- [ ] Repetir con `severity: important` → el modal posponible aparece al arrancar.
- [ ] Repetir con `severity: critical` → el overlay rojo bloquea la app.
- [ ] Simular fallo de red con `severity: critical` (desconectar antes de abrir) →
      la app entra en modo degradado: operable, banner rojo no-cerrable.
- [ ] Windows: "Actualizar ahora" en `critical` → `ApplyUpdatesAndRestart` reinicia la app en v2.
- [ ] Linux: el update toma efecto en la **proxima ejecucion** del AppImage (no reinicia in-place).

### Threading del overlay (CONFIRMAR en app real)

- [ ] El overlay de actualizacion (especialmente `critical`) asignado desde el background del
      `CoordinadorActualizacion` se renderiza correctamente en la UI de Avalonia.
      Esto debe verificarse en la app real (no en tests headless), ya que Avalonia requiere
      que las modificaciones al arbol visual ocurran en el hilo de UI (Dispatcher).

### Corrida en dev (no empaquetado)

- [ ] `dotnet run --project src/StockApp.Presentation` → `NotInstalledException` se traga
      internamente → la app arranca normalmente sin mensajes de error relacionados a updates.

### Servidor sin feed

- [ ] Con la API caida o `/updates` vacio → el actualizador no muestra errores al usuario
      (no hay fuente de respaldo: el feed propio es la unica fuente).
