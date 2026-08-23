severity: normal

# Gestión Municipal 0.2.0

Novedades:
- Rediseño visual completo de la aplicación: nueva paleta de colores, tipografía y un estilo homogéneo en todas las pantallas.
- La aplicación pasa a llamarse **Gestión Municipal**.
- Nuevo módulo de Finanzas: gastos, pagos, ingresos de caja, presupuesto (POA), libro de caja, fuentes de financiamiento y rubros, con importación de planillas y adjuntos de comprobantes.
- Nuevo módulo de Documentos administrativos: alta, seguimiento, adjuntos e historial de documentos.
- Nuevo módulo de Tareas: gestión de tareas con vencimientos, prioridades y notas.
- Nueva pantalla de administración de usuarios: alta de usuarios, cambio de rol y permisos configurables por operador.
- Ingreso de stock por factura: la carga de artículos se hace ahora en campos arriba de la tabla, con un botón que valida los datos antes de agregar la fila.
- Nuevo configurador de conexión: un programa aparte para configurar la dirección y el puerto del servidor.
- Nueva sección en Mantenimiento: backup manual con un botón y aviso configurable si un backup falla.

Mejoras:
- Las pantallas avisan cuando no se tiene permiso para algo, en vez de quedar vacías.
- Mejor contraste y legibilidad en los textos de toda la aplicación.
- El stock y los saldos negativos ahora también se indican con la palabra "negativo", no solo con color.
- Los 4 campos del panel de nuevo usuario ahora tienen etiqueta.
- Las contraseñas nuevas requieren al menos 8 caracteres, con una letra y un número.

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
