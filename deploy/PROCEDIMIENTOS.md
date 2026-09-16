# PROCEDIMIENTOS — Backups y recuperación después de la instalación

Esto lo lee alguien del municipio, no un técnico. Después de que el proveedor instala el
sistema y se va, **nadie vuelve a tener acceso al servidor** (ni SSH, ni control remoto, ni
nada). Todo lo que hay que hacer para mantener el sistema seguro se hace **desde el programa
de escritorio**, con un usuario Administrador.

Este documento reemplaza a una alerta automática que no se pudo usar: el aviso de "backup
fallido" necesita mandar un mensaje a internet, y el firewall de la red del municipio puede
bloquear esa salida. Por eso el control es **humano**: alguien tiene que mirar, con una
frecuencia fija, en vez de esperar a que el sistema avise solo.

Hay tres procedimientos:

1. [Bajada periódica de backups](#procedimiento-1--bajada-periódica-de-backups)
2. [Monitoreo de la salud del backup](#procedimiento-2--monitoreo-de-la-salud-del-backup)
3. [Qué hacer si reinstalan el servidor](#procedimiento-3--reinstalaron-el-servidor)

---

## Procedimiento 1 — Bajada periódica de backups

### Por qué existe

El servidor hace copias de seguridad de la base de datos automáticamente, pero **las guarda en
el mismo disco donde vive la base**. Si ese disco se rompe, se pierden la base de datos Y las
copias de seguridad al mismo tiempo. Un backup que vive al lado de lo que respalda no protege
de nada si se rompe la máquina entera — por eso hay que sacar una copia de esas copias y
guardarla en otro lugar.

### Quién

**PENDIENTE DE COMPLETAR POR EL MUNICIPIO.** Hay que nombrar una persona (con nombre y
apellido, no "el área de sistemas") responsable de bajar los backups. Sin un responsable
nombrado, este procedimiento no se cumple.

Responsable: ______________________________

Suplente (por si el responsable está de licencia o afuera): ______________________________

### Cada cuánto

**Una vez por semana**, siempre el mismo día (por ejemplo, todos los lunes a primera hora).

Este número no es arbitrario — sale de cómo funciona el sistema de backups en el servidor:

- El servidor hace un backup automático **cada 12 horas**, las 24 horas del día
  (`src/StockApp.Api/Backups/BackupProgramadoService.cs:19`, `IntervaloEntreCorridas`).
- El servidor **no guarda todos los backups para siempre**: conserva los 6 más recientes, más
  uno por día de los últimos 7 días, más uno por semana de las últimas 4 semanas
  (`src/StockApp.Application/Backups/PoliticaRetencion.cs:14-16`). Los backups más viejos que
  eso se borran solos.

Bajando una copia una vez por semana, siempre queda al menos un backup de cada semana
guardado afuera del servidor, y nunca se corre el riesgo de que el servidor borre un backup
antes de que alguien se lo haya llevado.

### Cómo — los pasos exactos en el programa

1. Abrí el programa de escritorio y entrá con un usuario **Administrador** (esta pantalla no
   la ve un usuario Operador).
2. En el menú de la izquierda, andá al grupo **"Administración"** y hacé clic en
   **"Mantenimiento"**
   (`src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs:268`).
3. En la sección **"Backups"**, vas a ver una lista de corridas con fecha, resultado
   (éxito o fallo) y tamaño.
4. Elegí el backup más reciente que diga **éxito** (ícono verde) y hacé clic en el botón
   **"Descargar"** de esa fila. El botón "Descargar" solo aparece habilitado en las filas que
   tienen un archivo asociado — las filas de backups fallidos no lo tienen
   (`src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`,
   `IsEnabled="{Binding NombreArchivo, ...}"`).
5. El programa te va a pedir dónde guardar el archivo (una ventana normal de "Guardar como").
   Elegí la carpeta que corresponda (ver el punto siguiente).

Si por algún motivo el backup automático más reciente falló y no hay ninguno exitoso en las
últimas horas, el botón **"Hacer backup ahora"** (arriba a la derecha, misma pantalla) dispara
uno manual en el momento. Esperá unos minutos y volvé a cargar la pantalla antes de descargar.

### Dónde se guardan

**Fuera del servidor.** No sirve guardarlos en una carpeta de la misma máquina del servidor,
ni en el mismo disco: el objetivo entero de este procedimiento es tener una copia que
sobreviva si el servidor se rompe. Opciones razonables, de más a menos preferible:

- Un pendrive o disco externo que se guarde en un lugar físico distinto (otra oficina, una
  caja fuerte).
- Una PC del municipio que no sea el servidor.
- Un servicio de almacenamiento en la nube, si el municipio tiene uno contratado.

Guardá cada backup con la fecha en el nombre (el archivo ya la trae en el nombre, no hace
falta renombrarlo) y no lo sobrescribas: quedate con al menos las últimas 4-5 semanas
bajadas, para tener margen si un problema se descubre tarde.

### Cómo se verifica que el backup bajado realmente sirve

**Un backup que nunca se restauró no es un backup — es un archivo del que no sabés si
funciona.** El único modo de confirmar que un backup sirve es restaurarlo de prueba y ver que
la base de datos resultante tiene los datos esperados.

Este restore de prueba **no se hace en el servidor de producción** (restaurar ahí pisaría los
datos reales). Hace falta un ambiente aparte con Postgres — una PC de prueba, o pedirle al
proveedor que lo verifique de forma remota con el archivo que le mandes.

- **Cada cuánto:** una vez por trimestre (cada 3 meses) alcanza para detectar a tiempo si algo
  se rompió en la cadena de backups (un archivo corrupto, un cambio de formato, etc.) sin que
  restaurar de prueba se vuelva una carga constante.
- **Quién:** la misma persona responsable de la bajada semanal, coordinando con el proveedor
  si el restore de prueba requiere su ayuda técnica.
- **Qué hacer si falla:** si un backup no se puede restaurar, avisar de inmediato al proveedor
  y revisar los backups de las semanas anteriores hasta encontrar uno que sí funcione. No
  esperar a necesitar el backup de verdad para descubrir que no sirve.

---

## Procedimiento 2 — Monitoreo de la salud del backup

### Por qué existe

El sistema tiene una alerta automática por webhook (mandaría un aviso a internet si un backup
falla), pero **el firewall de la red del municipio puede bloquear esa salida**, y entonces la
alerta nunca llega a ningún lado. Como no hay forma de garantizar que ese aviso salga, alguien
tiene que revisar el estado a mano, con una frecuencia fija. **Este documento es el reemplazo
del aviso automático** — sin una persona haciendo esta revisión, no hay forma de enterarse de
que los backups dejaron de funcionar.

### Quién y cada cuánto

La misma persona responsable del Procedimiento 1 (o su suplente) revisa el estado del backup
**cada vez que entra al sistema como Administrador**, y como mínimo **una vez por semana**
aunque no tenga que hacer nada más ese día.

### Cómo — no hace falta ir a buscarlo

A diferencia del resto de este documento, **este paso no requiere navegar a ninguna pantalla
especial**: apenas un usuario Administrador entra al sistema, la pantalla de Inicio consulta
el estado del backup automáticamente y muestra un cartel si hay algo para revisar
(`src/StockApp.Presentation/ViewModels/InicioViewModel.cs:284-320`,
`src/StockApp.Presentation/Views/InicioView.axaml:141-165`). Hay tres situaciones posibles:

1. **No aparece ningún cartel de backup.** Todo está bien: el último backup exitoso tiene
   menos de 26 horas. No hace falta hacer nada.
2. **Cartel rojo "Backup de la base de datos"**, con un texto como *"El último backup exitoso
   fue el [fecha] (hace más de 26 horas)"*. Esto es lo que el sistema llama internamente
   `vencido: true` — el campo real que devuelve el servidor en `GET /backups/salud`
   (`src/StockApp.Application/Backups/BackupDtos.cs:14-16`). Significa que el backup
   automático dejó de correr como se esperaba.
3. **Cartel naranja/amarillo "Backup de la base de datos"**, con el texto *"No se pudo
   verificar el estado del backup."* Esto es distinto del caso anterior: no significa que el
   backup esté vencido, significa que el programa **no pudo ni siquiera preguntarle** al
   servidor (por ejemplo, porque el servidor está caído o no responde). Tratalo con la misma
   urgencia que el cartel rojo — no se sabe si el backup está bien o mal.

Las 26 horas no están hardcodeadas en el texto: ese número lo calcula el propio servidor
(dos ventanas de 12 horas entre corridas, más 2 horas de margen para no generar falsas alarmas
si el servidor se reinició) y viaja en la respuesta como `umbralHoras`
(`src/StockApp.Application/Backups/ServicioConsultaBackups.cs:22`). Si ese número cambia en
una versión futura, el cartel del programa lo va a reflejar solo.

### Qué hacer si aparece cualquiera de los dos carteles

1. **No entrar en pánico ni intentar arreglar nada en el servidor** — no hay acceso a él, y no
   hace falta: el sistema sigue funcionando para las operaciones normales aunque el backup
   esté vencido.
2. Avisar al proveedor del sistema, contando exactamente qué cartel apareció (el rojo de
   "vencido" o el naranja de "no se pudo verificar") y desde cuándo.
3. Mientras se resuelve, **usar el botón "Hacer backup ahora"** en Mantenimiento (ver
   Procedimiento 1) para forzar un backup manual y no quedar sin ninguna copia reciente.
4. Una vez resuelto, confirmar que el cartel desaparece la próxima vez que se entra al
   sistema.

**Qué NO hacer:** no ignorar el cartel esperando que "se arregle solo". Un backup vencido que
nadie revisa es exactamente el escenario que este documento existe para evitar.

---

## Procedimiento 3 — "Reinstalaron el servidor"

### Qué se pierde

**La licencia del sistema.** La licencia queda atada a un identificador único de esa máquina
específica (el `/etc/machine-id` del servidor —
`src/StockApp.Infrastructure/Licenciamiento/FingerprintMaquinaLinux.cs:8-9`). Reinstalar el
sistema operativo del servidor, cambiar de máquina, o restaurar el servidor desde una imagen
distinta **cambia ese identificador**, y la licencia vieja deja de servir: el programa vuelve
a mostrar la pantalla de bloqueo hasta activar una licencia nueva.

Los datos de la base (productos, movimientos, facturas, etc.) **no se pierden por esto** —
eso depende de si además se perdió el disco donde vivía Postgres, no de la licencia. Si además
se perdió el disco, hace falta restaurar el backup más reciente que se haya bajado siguiendo
el Procedimiento 1 — otra razón más para no saltearse esa bajada semanal.

### Qué hace falta para recuperarse

1. El archivo `/etc/stockapp/.env` que el proveedor copió a un pendrive el día de la
   instalación (tiene la contraseña de Postgres, el `JWT_SECRET` y la clave pública de
   licencia — ver el checklist de `deploy/kit/LEEME.md`). **Sin ese archivo, hay que volver a
   configurar el servidor desde cero**, no solo reactivar la licencia.
2. Una licencia **nueva**, emitida específicamente para el identificador de la máquina nueva
   (o reinstalada). La licencia vieja no sirve — no es cuestión de "volver a activarla", hay
   que emitir una distinta.

### Quién puede hacerlo

**Solo quien tiene la clave privada de licenciamiento** puede emitir una licencia nueva. Esa
clave nunca estuvo en el servidor ni en el pendrive del municipio — vive únicamente en la
máquina del proveedor (`~/stockapp-claves/clave-privada.pem`). Si esa clave se pierde, **el
sistema no se puede volver a activar en ninguna máquina**, ni siquiera por el proveedor: por
eso la copia de esa carpeta no es opcional para quien la resguarda.

Esto significa, en la práctica, que este procedimiento **no lo puede completar el municipio
solo**: hace falta que alguien contacte al proveedor.

### Los pasos, en orden

1. En el servidor reinstalado, correr el kit de instalación normal
   (`deploy/kit/00-preflight.sh` en adelante, ver `deploy/kit/LEEME.md`) hasta el paso de
   licencia.
2. Obtener el código de máquina nuevo:

   ```bash
   sudo ./04-licencia.sh fingerprint
   ```

   Esto muestra el código de máquina actual, calculado contra la API que corre en el servidor
   (`deploy/kit/04-licencia.sh`, comando `cmd_fingerprint`).

3. Mandar ese código a quien tiene la clave privada de licenciamiento (el proveedor).
4. El proveedor emite la licencia nueva con esa clave, para ese código de máquina puntual
   (comando `emitir-licencia` de `tools/StockApp.Licencias.Cli`, ver `deploy/DEPLOY.md`
   sección 4) y la envía de vuelta (por ejemplo, por correo).
5. Activar la licencia nueva en el servidor:

   ```bash
   sudo ./04-licencia.sh activar /ruta/al/archivo/de/licencia.txt
   ```

6. Confirmar que el programa de escritorio deja de mostrar la pantalla de bloqueo.
7. Si además se perdió la base de datos (disco roto, no solo reinstalación), restaurar el
   backup más reciente disponible siguiendo el respaldo bajado en el Procedimiento 1, con
   ayuda del proveedor.

### El costo real de este escenario, sin adornos

Este procedimiento implica, casi siempre, que **alguien del proveedor tenga que volver al
municipio o guiar a alguien por teléfono paso a paso** — no es algo que el municipio pueda
resolver completamente solo, porque la clave privada nunca está ahí. Evitar llegar a este
escenario (cuidando el servidor, no reinstalándolo sin necesidad, y manteniendo backups
recientes fuera de él) es mucho más barato que recuperarse de él.
