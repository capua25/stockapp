# Validación manual — Incremento 7 Fase A (Distribución)

> Empaquetado Velopack 1.2.0 + actualizador in-app. Código y scripts completos.
> Falta ejecutar esta validación manual — no es automatizable con xUnit.

## Estado

- **Código**: completo (empaquetado + actualizador + UX por severidad).
- **Empaquetado**: nunca se corrió. No existe carpeta `releases/` ni artefactos generados todavía.
- **Entorno del validador en esta ronda**:
  - Windows real (máquina de desarrollo, con .NET 10 SDK y `vpk`).
  - Una máquina Windows **sin .NET instalado**, para probar el self-contained.
  - **No hay Linux limpio disponible.** Los casos de AppImage quedan pendientes hasta contar con esa máquina (o smoke-test en WSL, que no reemplaza la validación oficial).

---

## Prerequisitos por máquina

### Máquina de empaquetado (Windows, la que corre `pack-win.ps1`)

| Herramienta | Versión mínima | Verificación |
|-------------|-----------------|--------------|
| .NET SDK    | 10.0            | `dotnet --version` → debe mostrar `10.x.x` |
| vpk CLI     | 1.2.x           | `vpk --version` → no debe decir "command not found" |

Si `vpk` no aparece en el PATH después de `dotnet tool install -g vpk`, agregar `%USERPROFILE%\.dotnet\tools`.

### Máquina "sin .NET" (para probar instalación limpia)

- **NO debe tener el runtime .NET instalado.** Este es justamente el punto: el `Setup.exe` es self-contained y tiene que arrancar la app sin depender de nada preinstalado.
- Si en algún momento se instaló .NET en esa máquina para otra prueba, desinstalarlo antes de este caso o conseguir otra máquina limpia — si no, V3 no está validando lo que dice validar.

### Máquina Linux limpia

- **No disponible en esta ronda.** Los casos V2 (parcial), V4 y V6 quedan pendientes.

---

## Cobertura de esta ronda

| Caso | Descripción | Cobertura actual |
|------|-------------|-------------------|
| V1 | Empaquetado Windows | ✅ Validable |
| V2 | Empaquetado Linux | 🟡 Parcial (solo smoke-test en WSL, no oficial) |
| V3 | Instalación limpia Windows | ✅ Validable |
| V4 | Instalación limpia Linux | 🔲 Pendiente (sin Linux limpio) |
| V5 | Update end-to-end Windows | ✅ Validable (requiere publicar releases reales en GitHub) |
| V6 | Update end-to-end Linux | 🔲 Pendiente (sin Linux limpio) |
| V7 | Severity UX por modo | ✅ Validable |
| V8 | Modo degradado (critical sin red) | ✅ Validable |
| V9 | Threading del overlay | ✅ Validable |
| V10 | Corrida en dev (no empaquetado) | ✅ Validable |
| V11 | Fallback de fuente | 🔲 Diferido a Fase B (feed propio no existe todavía) |

**Resumen: 6 validables ahora, 1 parcial, 3 pendientes por entorno, 1 diferido a Fase B.**

---

## Casos de validación

### V1. Empaquetado Windows

- **Aplica a**: Windows
- **Estado en esta ronda**: ✅ Validable ahora
- **Precondición**: .NET 10 SDK y `vpk` instalados. `<Version>` en `src/StockApp.Presentation/StockApp.Presentation.csproj` en `0.1.0`.
- **Pasos**:
  ```powershell
  .\build\pack-win.ps1
  ```
- **Resultado esperado**:
  - El script termina sin errores (exit code 0).
  - Se crea `releases/win/` con: `Setup.exe`, `StockApp-0.1.0-full.nupkg`, `releases.win.json`.
  - No hay `-delta.nupkg` en esta primera corrida (no hay release previa).
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V2. Empaquetado Linux

- **Aplica a**: Linux
- **Estado en esta ronda**: 🟡 Parcial — smoke-test en WSL únicamente. La validación oficial requiere Linux limpio (no WSL) y queda pendiente.
- **Precondición**: bash 4+, .NET 10 SDK, `vpk` instalados.
- **Pasos**:
  ```bash
  chmod +x build/pack-linux.sh   # solo la primera vez
  ./build/pack-linux.sh
  ```
- **Resultado esperado**:
  - El script termina sin errores.
  - Se crea `releases/linux/` con: `StockApp.AppImage`, `StockApp-0.1.0-linux-full.nupkg`, `releases.linux.json`, `assets.linux.json`, `RELEASES-linux`.
- **Resultado**: [ ] PASA (smoke-test WSL)  [ ] FALLA  [ ] NO CORRIDO
  - Notas: ______________________________________

---

### V3. Instalación limpia Windows

- **Aplica a**: Windows
- **Estado en esta ronda**: ✅ Validable ahora
- **Precondición**: `Setup.exe` generado en V1. Máquina destino **sin .NET instalado**.
- **Pasos**:
  1. Copiar `Setup.exe` a la máquina sin .NET.
  2. Ejecutarlo con doble clic.
- **Resultado esperado**:
  - SmartScreen muestra "Windows protegió tu PC — El editor de esta app es desconocido" (**esperado, sin code signing en Fase A**).
  - Clic en "Más información" → aparece botón "Ejecutar de todas formas" → clic ahí.
  - La instalación completa y la app arranca correctamente sin requerir instalar .NET aparte.
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V4. Instalación limpia Linux

- **Aplica a**: Linux
- **Estado en esta ronda**: 🔲 Pendiente (falta entorno — sin máquina Linux limpia)
- **Precondición**: `AppImage` generado en V2. Máquina Linux sin .NET instalado.
- **Pasos**:
  1. Copiar el `StockApp.AppImage` a `$HOME` en la máquina destino.
  2. `chmod +x StockApp.AppImage`
  3. Ejecutarlo.
- **Resultado esperado**: La app arranca sin requerir .NET preinstalado en el sistema.
- **Resultado**: [ ] PASA  [ ] FALLA  [ ] NO EJECUTADO (sin entorno)
  - Notas: ______________________________________

---

### V5. Update end-to-end Windows

- **Aplica a**: Windows
- **Estado en esta ronda**: ✅ Validable ahora — requiere publicar releases reales en GitHub (repo `capua25/stockapp`).
- **Precondición**: V1 y V3 hechos (v0.1.0 instalada en la máquina destino). Fuente de updates configurada: GitHub Releases (feed propio deshabilitado en Fase A).
- **Pasos**:
  1. Publicar la v0.1.0 en un GitHub Release (ver sección "Flujo del ciclo de update" más abajo).
  2. Confirmar que v0.1.0 quedó instalada y funcionando en la máquina destino.
  3. En la máquina de empaquetado: bump `<Version>` en el csproj a `0.2.0`.
  4. Editar `build/RELEASE_NOTES.md` con la nueva versión y `severity` elegida.
  5. Re-correr `.\build\pack-win.ps1` (con `releases/win/` de la v0.1.0 todavía presente, para que genere el `-delta.nupkg`).
  6. Publicar la v0.2.0 en un nuevo GitHub Release.
  7. Reabrir la app instalada (o esperar el chequeo automático en background).
- **Resultado esperado**:
  - Se genera `StockApp-0.2.0-delta.nupkg` en `releases/win/` (prueba de que el delta funciona).
  - La app detecta la v0.2.0 disponible según la UX de la severity configurada.
  - Al confirmar la actualización, `ApplyUpdatesAndRestart` cierra y reinicia la app, que queda en v0.2.0 (verificar en "Acerca de").
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V6. Update end-to-end Linux

- **Aplica a**: Linux
- **Estado en esta ronda**: 🔲 Pendiente (falta entorno — sin máquina Linux limpia)
- **Precondición**: V4 hecho (v0.1.0 corriendo como AppImage en `$HOME`).
- **Pasos**: análogos a V5, pero con `pack-linux.sh` y el AppImage.
- **Resultado esperado**:
  - A diferencia de Windows, el update **no aplica in-place**: toma efecto recién en la **próxima ejecución** del AppImage.
  - Probar con el AppImage ubicado en `$HOME` (sin directorios protegidos, para evitar el prompt de `pkexec`).
- **Resultado**: [ ] PASA  [ ] FALLA  [ ] NO EJECUTADO (sin entorno)
  - Notas: ______________________________________

---

### V7. Severity UX por modo

- **Aplica a**: cualquiera (validable en Windows en esta ronda)
- **Estado en esta ronda**: ✅ Validable ahora
- **Precondición**: capacidad de publicar releases sucesivas con distinta `severity` (reusar el flujo de V5).
- **Pasos**: repetir el ciclo de update con cada valor de `severity` en `build/RELEASE_NOTES.md`:
  1. `severity: normal` → publicar → reabrir app.
  2. `severity: important` → publicar nueva versión → reabrir app.
  3. `severity: critical` → publicar nueva versión → reabrir app.
- **Resultado esperado**:
  - `normal`: banner discreto no bloqueante, no interrumpe el uso.
  - `important`: modal posponible al arrancar; reaparece en cada arranque hasta actualizar.
  - `critical`: overlay rojo bloqueante, no se puede seguir usando la app sin actualizar (o entrar en modo degradado, ver V8).
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V8. Modo degradado (critical sin red)

- **Aplica a**: cualquiera (validable en Windows en esta ronda)
- **Estado en esta ronda**: ✅ Validable ahora
- **Precondición**: release publicada con `severity: critical`.
- **Pasos**:
  1. Desconectar la red de la máquina.
  2. Abrir la app (que detecta que hay una versión critical pendiente pero no puede descargarla).
- **Resultado esperado**:
  - La app **no queda bloqueada**: sigue siendo operable.
  - Aparece un banner rojo permanente, **no cerrable**.
  - Al reconectar red y reabrir la app, reintenta el chequeo/descarga en el próximo arranque.
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V9. Threading del overlay

- **Aplica a**: cualquiera (validable en Windows en esta ronda)
- **Estado en esta ronda**: ✅ Validable ahora
- **Precondición**: cualquier severity que dispare overlay/modal/banner (usar `critical` para el caso más exigente, dado que se asigna desde background al arranque).
- **Pasos**: abrir la app empaquetada (no `dotnet run`) varias veces con distintas severities, prestando atención a la UI mientras se dispara `CoordinadorActualizacion` en background.
- **Resultado esperado**:
  - El overlay/modal/banner se renderiza correctamente en la UI de Avalonia, sin excepciones de threading ni elementos visuales corruptos.
  - Esto se prueba en la app real (no en tests headless), porque Avalonia requiere que las modificaciones al árbol visual ocurran en el hilo de UI (Dispatcher), y `CoordinadorActualizacion` corre en background.
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V10. Corrida en dev (no empaquetado)

- **Aplica a**: cualquiera — validable incluso en WSL/Linux de desarrollo
- **Estado en esta ronda**: ✅ Validable ahora
- **Precondición**: ninguna (no requiere empaquetado ni instalación previa).
- **Pasos**:
  ```bash
  dotnet run --project src/StockApp.Presentation
  ```
- **Resultado esperado**:
  - `NotInstalledException` (lanzada por Velopack al no estar la app instalada vía `vpk`) se traga internamente en `VelopackUpdateService`.
  - La app arranca normalmente, sin mensajes de error ni crashes relacionados a updates.
- **Resultado**: [ ] PASA  [ ] FALLA
  - Notas: ______________________________________

---

### V11. Fallback de fuente

- **Aplica a**: N/A en Fase A
- **Estado en esta ronda**: 🔲 Diferido a Fase B
- **Motivo**: `FeedPropioUrl = null` en `App.axaml.cs` — el feed propio todavía no existe. El orden configurado es `GitHubPrimero`, pero solo hay una fuente real habilitada (GitHub Releases), así que no hay fallback que ejercitar todavía.
- **Acción**: no hacer nada en esta ronda. Retomar cuando Fase B habilite el feed propio.
- **Resultado**: N/A

---

## Flujo del ciclo de update end-to-end

Usar este flujo para V5, V6 y V7. Comandos concretos:

1. **Bump de versión** — editar `src/StockApp.Presentation/StockApp.Presentation.csproj`:
   ```xml
   <Version>0.2.0</Version>
   ```
   (`InformationalVersion` lo toma automáticamente vía `$(Version)`).

2. **Release notes + severity** — editar `build/RELEASE_NOTES.md`, la primera línea del archivo tiene que ser exactamente:
   ```markdown
   severity: normal
   ```
   (o `important` / `critical`, sin espacios ni comentarios antes de esa línea). Debajo, actualizar el título y el detalle de cambios:
   ```markdown
   severity: normal

   # StockApp 0.2.0

   - Cambio A
   - Cambio B
   ```

3. **Empaquetar**:
   ```powershell
   .\build\pack-win.ps1
   ```
   o en Linux:
   ```bash
   ./build/pack-linux.sh
   ```

4. **Publicar en GitHub Releases** (repo `capua25/stockapp`):
   - Ir a "Releases" → "Draft a new release".
   - Tag con la versión (ej: `v0.2.0`).
   - Subir **todo el contenido** de `releases/win/` (o `releases/linux/`, según el OS empaquetado), incluido `releases.<channel>.json` — es el archivo que la app descarga para detectar updates.

5. **Importante**: conservar el contenido de `releases/` entre versiones (no borrar la carpeta entre corridas). Es lo que le permite a `vpk` generar el `-delta.nupkg` en vez de forzar siempre descarga full.

**Nota sobre nombres de artefactos**: en Windows vpk OMITE el channel del nombre del .nupkg (ej: `StockApp-0.1.0-full.nupkg`) porque "win" es el channel por defecto. En Linux SÍ lo incluye (ej: `StockApp-0.1.0-linux-full.nupkg`) y el AppImage sale sin versión (`StockApp.AppImage`). Verificado en ambos OS.

---

## Registro de resultados

| Fecha | OS | Caso | PASA/FALLA | Notas |
|-------|----|------|------------|-------|
|       |    |      |            |       |
|       |    |      |            |       |
|       |    |      |            |       |
|       |    |      |            |       |
|       |    |      |            |       |
|       |    |      |            |       |

**Recordatorio**: una vez completada esta ronda (aunque queden ítems Linux pendientes por falta de entorno), actualizar `docs/plans/2026-06-08-00-roadmap.md` — sección "Incremento 7: Distribución" — dejando constancia de qué se validó en Windows y qué sigue pendiente para cuando haya un entorno Linux limpio disponible.
