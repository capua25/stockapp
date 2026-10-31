severity: critical

# Gestión Municipal 0.7.0

Mejoras:
- Nuevo módulo Proyectos: cada proyecto tiene estado, prioridad, fecha límite, notas, zona, dimensión temática, organismo responsable, origen de financiamiento y un presupuesto opcional.
- Los gastos (y los ingresos por factura) se pueden imputar a un proyecto; la ficha del proyecto muestra el total gastado, el presupuesto, lo disponible y el sobregiro.
- Las tareas pueden vincularse a un proyecto y siguen pudiendo vincularse a un expediente. Las tareas ya no llevan zona, dimensión, organismo ni origen de financiamiento: esos datos ahora son del proyecto.
- La estadística por clasificadores ahora cuenta proyectos.
- La auditoría muestra nombres, montos y fechas legibles.

Esta actualización es obligatoria: las versiones anteriores no son compatibles con el servidor actualizado.

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
                 Si la descarga NO puede completarse (sin red, servidor caido, etc.),
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
  4. Publicar con build/publicar-release.sh <version> (sube releases/win al feed del VPS).

Ver build/README-empaquetado.md para el flujo completo.
-->
