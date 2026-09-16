# LEEME — Instalación en las PC clientes

Esto es para quien instala Gestión Municipal en cada PC del municipio. Puede no ser la misma
persona que instaló el servidor, y puede ser otro día.

**Antes de arrancar acá: la licencia del servidor tiene que estar activada.** Si nadie corrió
`04-licencia.sh activar` todavía, avisá y esperá — con la licencia sin activar, el servidor
rechaza casi todo y no vas a poder terminar de configurar ninguna PC.

---

## 1. Instalar

Corré `Setup.exe` (está en esta misma carpeta) en la PC. Es un instalador estándar de Windows:
seguí los pasos, no requiere nada especial.

## 2. Abrir el Configurador de conexión

El Configurador **viene incluido dentro del mismo instalador** — no es un programa aparte que
haya que bajar. Después de instalar, **no tiene acceso directo propio** en el escritorio ni en
el menú de inicio (así viene armado el paquete). Para abrirlo:

1. Andá a la carpeta donde se instaló Gestión Municipal (la misma carpeta del acceso directo de
   "Gestión Municipal" — botón derecho sobre el acceso directo → "Abrir ubicación del
   archivo").
2. Ejecutá `GestionMunicipal.Configurador.exe`.

## 3. Cargar la dirección del servidor

En la ventana del Configurador vas a ver dos campos: **IP** y **Puerto**.

Cargá la IP y el puerto **exactos** que imprimió `03-verificar.sh` en el servidor, en la línea:

```
URL PARA EL CONFIGURADOR (esto es lo que se carga en CADA PC):

    http://<IP>:<PUERTO>
```

**No uses un puerto "de memoria".** El puerto de la API es configurable (se elige al correr
`01-bootstrap.sh` en el servidor) y puede no ser el mismo en cada instalación — el único valor
correcto es el que quedó impreso ahí. Si no tenés ese dato a mano, pedíselo a quien instaló el
servidor antes de seguir.

## 4. Probar conexión

Apretá "Probar conexión". Los resultados posibles son:

- **"Conectado: es la API de Gestión Municipal."** (verde) — Todo bien. Pasá al paso 5.
- **"Algo respondió en esa dirección, pero no es la API de Gestión Municipal."** (amarillo) —
  Hay algo escuchando en esa IP y puerto, pero no es el servidor correcto. Revisá que no hayas
  confundido el puerto con el de otro servicio (por ejemplo, el de Postgres) y volvé a
  verificar la IP/puerto contra lo que imprimió `03-verificar.sh`.
- **"No se pudo conectar. Verificá la IP, el puerto y que el servidor esté encendido."** (rojo)
  — No hay nada respondiendo ahí. Puede ser que el servidor esté apagado, que la IP haya
  cambiado, o que un firewall esté bloqueando el puerto. Esto no lo podés arreglar desde la PC
  cliente — hay que revisar el servidor.

> **Fuera de alcance:** el caso "conecta bien, pero la licencia del servidor no está activada"
> **no tiene un resultado propio acá** — el Configurador solo prueba que la API responda, no el
> estado de la licencia. Si la licencia no está activada, buena parte de la app se va a negar a
> funcionar más adelante (errores 423) aunque la conexión se haya probado bien. Asegurate de
> que la licencia esté activada en el servidor (sección de arriba) antes de dar por terminada
> la instalación de las PC.

## 5. Guardar

Apretá "Guardar". El Configurador escribe la configuración en el archivo de conexión de esta PC
y muestra la ruta exacta donde quedó guardado. A partir de ahí, Gestión Municipal (el programa
principal, no el Configurador) va a usar esa dirección para conectarse al servidor.

## 6. Cerrar y abrir Gestión Municipal

Cerrá el Configurador y abrí "Gestión Municipal" desde su acceso directo normal. Debería
conectar directo contra el servidor.
