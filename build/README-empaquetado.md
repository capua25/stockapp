# Flujo de empaquetado y publicacion — StockApp

> Este documento cubre el flujo **manual** de empaquetado por OS, publicacion en GitHub Releases,
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
4. Publicar en GitHub Releases.

**No editar la version en ningun otro archivo.** Los scripts la leen del csproj automaticamente.

---

## Archivo de release notes y severidades

Antes de empaquetar, editar `build/RELEASE_NOTES.md`:

```markdown
severity: normal

# StockApp 0.2.0

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

| Archivo | Descripcion |
|---------|-------------|
| `Setup.exe` | Instalador para el usuario final. Incluye .NET 10 runtime embebido. |
| `StockApp-X.Y.Z-full.nupkg` | Paquete completo (primera instalacion o sin release previa). |
| `StockApp-X.Y.Z-delta.nupkg` | Paquete diferencial (solo si hay release previa en `releases/win/`). |
| `releases.win.json` | Feed de updates que la app consulta al arrancar. |

### Linux (`releases/linux/`)

| Archivo | Descripcion |
|---------|-------------|
| `StockApp-X.Y.Z-linux.AppImage` | Ejecutable portable. Sin instalador, sin privilegios. |
| `StockApp-X.Y.Z-full.nupkg` | Paquete completo. |
| `StockApp-X.Y.Z-delta.nupkg` | Paquete diferencial. |
| `releases.linux.json` | Feed de updates. |

Los deltas se generan **automaticamente** si ya hay una release previa en el directorio de salida.
Conservar el contenido de `releases/` entre versiones para aprovechar los deltas.

---

## Publicar en GitHub Releases

La fuente de updates configurada en la app es GitHub Releases (fuente primaria real en Fase A).

Pasos:

1. Ir al repositorio en GitHub → "Releases" → "Draft a new release".
2. Crear un tag con la version (ej: `v0.2.0`).
3. Subir **todo el contenido** de `releases/win/` (para la release de Windows).
4. Repetir subiendo el contenido de `releases/linux/` en la misma release (o en una separada por canal).
5. El `releases.<channel>.json` debe estar incluido: es el archivo que la app descarga para detectar updates.

Cuando exista el feed propio, los mismos assets se publican tambien ahi. El orden de fuentes
(feed propio primero, GitHub como fallback) se configura en `UpdaterOptions` sin tocar codigo.

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

- [ ] Publicar v1 (ej: 0.1.0) en GitHub Release → instalar en maquina limpia.
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

### Fallback de fuente (cuando exista el feed propio)

- [ ] Con `FeedPropioUrl` configurado y el servidor caido → el actualizador cae a GitHub
      sin errores visibles para el usuario (aplica cuando se configure el feed propio).
