severity: normal

# StockApp 0.1.5

- Actualizador con aviso más claro y botón que aplica la actualización.
- La versión de la app se muestra en pantalla.
- Correcciones de estabilidad y mejoras menores.

---
<!-- INSTRUCCIONES PARA EL PUBLICADOR — leer antes de empaquetar:

LINEA DE SEVERITY (OBLIGATORIA — DEBE SER LA PRIMERA LINEA DEL ARCHIVO):
  severity: normal | important | critical

  Que hace cada valor:
    normal    -> Banner discreto no-bloqueante. El usuario puede ignorarlo.
                 La actualizacion se aplica en el proximo reinicio voluntario.
                 Si la descarga falla, reintenta en silencio en el proximo arranque.

    important -> Modal al arrancar, posponible. El usuario puede continuar usando la app,
                 pero el modal reaparece en cada arranque hasta actualizar.
                 Si la descarga falla, reintenta en cada arranque.

    critical  -> Overlay ROJO BLOQUEANTE. La app no se puede usar hasta actualizar.
                 Si la descarga NO puede completarse (sin red, GitHub caido, etc.),
                 la app entra en MODO DEGRADADO: sigue siendo operable pero con un
                 banner rojo permanente no-cerrable, y reintenta en cada arranque.

  ESTA LINEA DEBE ESTAR AL TOPE DEL ARCHIVO, sin espacios ni comentarios antes.
  El actualizador la lee antes de descargar el paquete (pre-descarga).
  Si la linea esta ausente o el valor es invalido, se usa "normal" por defecto.

FLUJO DE USO:
  1. Editar <Version> en src/StockApp.Presentation/StockApp.Presentation.csproj
  2. Actualizar este archivo:
     a. Cambiar la linea `severity:` segun el nivel de urgencia de la release.
     b. Reemplazar el titulo con la nueva version: "# StockApp X.Y.Z"
     c. Documentar los cambios de esta version bajo el titulo.
  3. Ejecutar el script de empaquetado del OS:
     Windows: .\build\pack-win.ps1
     Linux:   ./build/pack-linux.sh
  4. Subir los artefactos de releases/win o releases/linux a un GitHub Release.

Ver build/README-empaquetado.md para el flujo completo.
-->
