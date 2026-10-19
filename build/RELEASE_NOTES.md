severity: normal

# Gestión Municipal 0.5.0

Novedades:
- Nuevo diseño de toda la aplicación (versión 2): pantallas más claras y ordenadas, con el mismo estilo en Inicio, Productos, Movimientos, Finanzas, Reportes, Documentos, Tareas, Usuarios, Catálogos y Mantenimiento.
- Las actualizaciones ahora se descargan del servidor del municipio. La aplicación busca versiones nuevas cada una hora, sin interrumpir lo que estés haciendo.
- Si aparece una actualización crítica mientras trabajás, se muestra un aviso rojo fijo en la parte de arriba que no tapa el contenido.
- Reportes: nueva pantalla de entrada que reúne los seis reportes, y el Log de auditoría ahora muestra la acción en un texto entendible.
- Libro de caja: se puede ver y exportar a PDF el año completo, con el saldo inicial y final del año y el nombre de cada mes.
- Catálogos: ahora hay una única sección con todos los catálogos, con búsqueda y acceso directo a cada uno.

Mejoras:
- Todas las pantallas indican cuando se está cargando, cuando no hay conexión con el servidor (con botón Reintentar) y cuando falta permiso, en vez de mostrar "No hay datos".
- En Gastos, Ingresos de caja y Calendario de pagos, los filtros se aplican solos al cambiar; ya no hace falta apretar Filtrar.
- Los formularios de gastos, ingresos y pagos marcan el error en cada campo y no dejan guardar hasta corregirlo.
- Un gasto anulado queda en solo lectura: no se pueden agregarle adjuntos ni modificarlo, y no se puede anular dos veces.
- Al quitar un adjunto, la aplicación pide confirmación.
- Registrar entrada y salida: el precio solo es obligatorio en las compras y se oculta en uso o consumo; en una salida nunca se pide.
- No se puede dar de baja una categoría que tiene productos activos.
- Las columnas de las tablas se ajustan al contenido y los textos largos muestran el detalle al pasar el mouse.
- Inicio: las métricas indican cuando no hay dato, y el panel de tareas muestra hasta 4 filas con acceso a Ver todas.
- Si cambian tus permisos, los botones y el menú lateral se actualizan sin tener que reabrir las pantallas.
- Los códigos de rubro y los ejercicios del POA ahora se ordenan por número y no como texto.
- En Documentos, el historial muestra el estado en palabras (por ejemplo, "En proceso").

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
