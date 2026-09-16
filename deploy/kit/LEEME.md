# LEEME — Kit de instalación StockApp / Gestión Municipal

Esto lo lee la persona parada frente al servidor, en el municipio, probablemente sin internet
y sin poder volver a pasar otro día. Todo lo que no esté escrito acá, se pierde.

---

## 1. El flujo del día, en seis líneas

```
1. sudo ./00-preflight.sh              # diagnóstico, no toca nada. Imprime el código de máquina.
   (mandá el código de máquina a quien tiene la clave privada — que emita la licencia YA,
    en paralelo, mientras vos seguís con los pasos de abajo)
2. sudo ./01-bootstrap.sh              # Docker, Postgres, secretos (.env), aviso de firewall
3. sudo ./02-instalar.sh               # instala/actualiza la API
4. sudo ./04-licencia.sh activar <archivo-de-licencia>
5. sudo ./03-verificar.sh              # 8 chequeos end-to-end; imprime la URL para las PC
6. Clientes: Setup.exe en cada PC (ver clientes/LEEME-clientes.md)
```

**La licencia se activa ANTES de tocar ninguna PC (paso 4, antes del paso 6).** Sin licencia
activada, la API devuelve 423 en casi todo y no tiene sentido configurar clientes contra un
servidor bloqueado. Pedila apenas corras `00-preflight.sh`: ese script te da el código de
máquina, y emitirla no depende de nada de lo que hagas en el servidor — se puede hacer en
paralelo mientras corrés `01` y `02`.

---

## 2. Qué hacer con la IP del servidor, ANTES de instalar

Los scripts de este kit **no tocan la configuración de red a propósito**: tocar `netplan` en
una red que no es tuya, con un router que no administrás vos, es la forma más rápida de
quedarte sin conectividad al servidor a mitad de la instalación — y la política de asignación
de IPs de ese municipio no es una decisión que te corresponda a vos.

Antes de correr `01-bootstrap.sh`, asegurate de que este servidor tenga una IP que **no vaya a
cambiar**. En orden de preferencia:

1. **Reserva DHCP por MAC en el router** (preferible). La hace el área de sistemas del
   municipio o quien administre el router. Es reversible y no requiere tocar nada en el
   servidor.
2. **IP estática en netplan**, si no hay forma de hacer la reserva por MAC.

La consecuencia, sin adornos: **si el servidor cambia de IP después de instalado, todas las PC
pierden la conexión al mismo tiempo**, y no hay forma remota de arreglarlo — hay que pasar el
Configurador por cada PC, a mano, sin tu ayuda (vos ya no vas a estar ahí). Confirmá la reserva
DHCP antes de irte (ver el checklist, sección 4).

---

## 3. Rescate: qué hacer si algo falla a mitad de camino

### `01-bootstrap.sh` falló instalando los `.deb` de Docker o de postgresql-client-16

El kit fue armado para una versión exacta de Ubuntu (ver `VERSION`) y este servidor tiene otra.
**No hay arreglo en el lugar.** El mensaje de error del script te dice qué versión espera el
kit y cuál tiene este servidor (`. /etc/os-release`). **No sigas** intentando con los mismos
`.deb`: hay que rearmar el kit con `deploy/armar-kit.sh` contra la versión de Ubuntu correcta
y volver con un pendrive nuevo.

### `01-bootstrap.sh` falló DESPUÉS de haber creado `/etc/stockapp/.env`

**No borres `/etc/stockapp/.env`.** Ese archivo tiene la contraseña de Postgres, y si ya se
levantó el contenedor `stockapp-pg` con ella, el volumen de datos quedó grabado con esa
contraseña. Volvé a correr:

```bash
sudo ./01-bootstrap.sh
```

Es idempotente: si `/etc/stockapp/.env` ya existe, el script lo detecta, no lo toca y sigue
adelante con lo que falte (Docker, la imagen de Postgres, levantar el contenedor).

### Necesitás empezar de cero de verdad (reinstalación total)

Esto **borra la base de datos**. Solo si estás seguro de que no hay datos que conservar:

```bash
docker compose -f /opt/stockapp/docker-compose.postgres.yml down -v
rm /etc/stockapp/.env
```

**ESO BORRA TODOS LOS DATOS DE POSTGRES.** Recién después de las dos líneas de arriba volvés a
correr `01-bootstrap.sh` desde cero.

### `02-instalar.sh` falló

`install.sh` (el script que corre por debajo) ya respaldó la instalación anterior, si había
una, en:

```
/var/backups/stockapp-api/<timestamp>/
```

Antes de tocar nada, mirá qué pasó:

```bash
journalctl -u stockapp-api -n 100 --no-pager
```

Si la unit de systemd quedó en estado `failed` porque se agotaron los 5 intentos de arranque
en 10 minutos (protección de `stockapp-api.service` contra reintentar para siempre en
silencio), limpiá el contador antes de volver a intentar:

```bash
systemctl reset-failed stockapp-api
```

Después, corregí lo que haya fallado (revisá el log) y volvé a correr `sudo ./02-instalar.sh`.

### La API no responde desde la IP de la LAN, pero sí en loopback (127.0.0.1)

Es casi siempre una de dos cosas:

- **`API_BIND` quedó en `127.0.0.1`** en `/etc/stockapp/.env` (la API solo escucha localhost).
  Corregilo a `API_BIND=0.0.0.0` y volvé a correr `sudo ./02-instalar.sh`.
- **El firewall bloquea el puerto.** Este kit **no activa `ufw` automáticamente** (ver sección
  5). Si el servidor tiene `ufw`, revisá `sudo ufw status` y los comandos exactos que
  `01-bootstrap.sh` dejó guardados en:

  ```
  /etc/stockapp/POST-INSTALACION.txt
  ```

  (con la subred y el puerto ya completados, no como variables).

### La licencia dice "emitida para otra máquina"

```bash
sudo ./04-licencia.sh fingerprint
```

Te muestra el código de máquina actual **según la API**, que es el que realmente se usa para
validar (puede diferir del calculado localmente si `/etc/machine-id` cambió después de
instalar, o si la API está corriendo en otra máquina de la que creés). Mandale ese código a
quien tiene la clave privada para que emita una licencia nueva, y activala de nuevo con
`sudo ./04-licencia.sh activar <archivo>`.

---

## 4. Checklist de antes de irte

- [ ] Copiaste `/etc/stockapp/.env` al pendrive. Tiene la contraseña de Postgres, el
      `JWT_SECRET` y la clave pública de licencia — **no vas a tener acceso remoto a este
      servidor de nuevo**, y si se pierde, no hay forma de recuperarlo.
- [ ] Copiaste el log de la instalación (la ruta exacta te la imprime `03-verificar.sh` al
      final).
- [ ] Anotaste, en un lugar que el municipio pueda consultar, la **IP del servidor** y el
      **código de máquina** (por si hace falta reemitir la licencia más adelante).
- [ ] Confirmaste que la **reserva DHCP por MAC** (o la IP estática) quedó hecha y no va a
      cambiar.
- [ ] Dejaste escrito, en el municipio, **quién** revisa `GET /backups/salud` y **cada
      cuánto** — el chequeo 7 de `03-verificar.sh` solo confirma que el sistema de backups
      responde el día de la instalación, no que alguien lo va a seguir mirando después. El
      procedimiento detallado de esa revisión periódica todavía no existe como documento
      (`deploy/PROCEDIMIENTOS.md`, planeado para una fase posterior) — hasta que exista, dejalo
      escrito a mano vos mismo.

---

## 5. Lo que este kit NO hace

- **No configura la red.** No toca `netplan`, no asigna IP, no hace reserva DHCP (ver sección
  2). Eso es responsabilidad de quien administra la red del municipio.
- **No pone HTTPS.** El tráfico entre las PC y el servidor viaja en **HTTP plano** dentro de la
  LAN: usuario, contraseña y token JWT sin cifrar. Es una decisión deliberada para esta primera
  fase (LAN cerrada), no un olvido.
- **No instala Windows ni toca las PC más allá de correr el `Setup.exe`.** No configura
  usuarios de Windows, políticas de dominio, antivirus, ni nada fuera del instalador de
  Gestión Municipal.
- **No deja acceso remoto a este servidor.** Sin VPN, sin túnel SSH configurado por el kit, sin
  ningún mecanismo para volver a entrar después de irte. Lo único que queda es lo que hayas
  copiado al pendrive.
