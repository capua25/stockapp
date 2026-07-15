# Incremento 7 — Fase B: Licenciamiento offline firmado + Reset de Admin firmado

**Fecha:** 2026-07-15
**Estado:** Diseño aprobado
**Spec fuente:** docs/specs/2026-06-08-control-stock-ferreteria-design.md §5.1 (reset firmado) y §11.4 (licenciamiento)
**Depende de:** Inc 7 Fase A (distribución Velopack), Fase 2/3 (arquitectura cliente-servidor API + terminales)

## 1. Contexto y decisiones de alcance

La spec original (§11.4) se escribió cuando la app era monolítica local. Hoy la arquitectura es cliente-servidor: una API en LAN + N terminales desktop. Decisiones cerradas con el usuario (2026-07-15):

- **Se licencia el SERVIDOR**: una licencia atada al fingerprint de la máquina donde corre la API. Terminales ilimitadas. Sin licencia válida, la API se bloquea y el desktop muestra pantalla de bloqueo.
- **Activación desde el desktop**: la pantalla de bloqueo muestra el código de máquina del servidor y un campo para pegar la licencia; un endpoint de activación la valida y persiste. Nadie necesita acceso físico/SSH al servidor.
- **Cripto: ECDSA P-256 nativo** (System.Security.Cryptography, cero dependencias nuevas). La misma primitiva firma licencias y tokens de reset.
- **Licencia perpetua, sin expiración** (venta única, spec §11.4).

## 2. Arquitectura general

Una sola pieza criptográfica compartida sirve a dos features: licencia offline y reset de Admin firmado. Todo el enforcement vive en la API; el desktop solo muestra pantallas y pega textos.

Componentes nuevos, por capa:

- **`StockApp.Application/Licenciamiento/`** — lógica pura y testeable: `ValidadorFirma` (verifica ECDSA P-256 contra la clave pública embebida), contratos `LicenciaPayload` y `TokenResetPayload` (JSON), `IFingerprintMaquina`, `IAlmacenLicencia`, `ServicioLicencia` (orquesta: leer → validar firma → validar fingerprint → estado).
- **`StockApp.Infrastructure/Licenciamiento/`** — adaptadores: `FingerprintMaquinaWindows` (lee `MachineGuid` del registro) y `FingerprintMaquinaLinux` (lee `/etc/machine-id`), ambos hasheados con SHA-256 y presentados como código agrupado tipo `A3F2-9B41-...` (nunca se expone el id crudo de la máquina); `AlmacenLicenciaArchivo` (persiste `licencia.lic` en el directorio de datos de la API, que los updates de Velopack no tocan).
- **`StockApp.Api`** — endpoints `/licencia/*` y `/auth/reset-admin/*` + middleware de bloqueo: sin licencia válida, TODO devuelve `423 Locked` salvo los endpoints de licencia y de reset. El estado se valida al arranque y queda cacheado en un singleton (`EstadoLicencia`); con licencia válida el costo por request es cero.
- **`tools/StockApp.Licencias.Cli`** — proyecto consola nuevo, en el repo pero jamás empaquetado ni distribuido: genera el par de claves, emite licencias y emite tokens de reset con la clave privada del desarrollador.
- **Desktop (`Presentation` + `ApiClient`)** — pantalla de bloqueo (código de máquina + campo para pegar licencia) y flujo "No puedo entrar / resetear Admin" en el login.

La clave privada vive en un archivo del desarrollador, fuera del repo, siempre. La pública va embebida como constante base64 en `Application`.

## 3. Formato de licencia y fingerprint

Formato compacto de un solo string, pegable a mano (estilo JWS): `base64url(payload JSON) + "." + base64url(firma ECDSA P-256)`.

Payload de licencia:

```json
{ "ver": 1, "cliente": "Ferretería X", "maquina": "<fingerprint>", "emitida": "2026-07-15" }
```

Validación en la API: firma correcta + `maquina` == fingerprint propio. Sin expiración. Cambio de PC del servidor → nuevo código de máquina en la pantalla de bloqueo → reemisión con la CLI.

Fingerprint: SHA-256 del id de máquina del OS (Windows: `MachineGuid` del registro; Linux: `/etc/machine-id`), presentado agrupado (`A3F2-9B41-…`).

## 4. Flujo de activación y modo bloqueado

**Arranque de la API:** lee `licencia.lic` del directorio de datos → valida firma + fingerprint → cachea estado. Sin archivo o inválida → arranca igual, en modo bloqueado.

**Endpoints `/licencia/*` (anónimos — son pre-login por definición):**
- `GET /licencia/estado` → `{ activada: bool, codigoMaquina }`. El código de máquina es público por diseño.
- `POST /licencia/activar` → recibe el string de licencia, valida firma + fingerprint, persiste `licencia.lic`, actualiza el estado cacheado y responde el nuevo estado. Inválida → `400` con motivo. Activaciones exitosas E intentos fallidos se registran en `LogAuditoria`.

**Middleware de bloqueo:** si `EstadoLicencia.Activada == false`, toda request que no sea `/licencia/*` ni `/auth/reset-admin/*` devuelve `423 Locked` con body de error consistente con el resto de la API. El login incluido.

**Desktop:** al arrancar, antes del login, consulta `GET /licencia/estado`. No activada → pantalla de bloqueo (reemplaza al login): código de máquina con botón Copiar, campo para pegar licencia, botón Activar. Activación ok → login normal. Además `ApiSession` trata cualquier `423` inesperado como "licencia desactivada" y vuelve a la pantalla de bloqueo (caso borde: borran `licencia.lic` con la app abierta).

## 5. Reset de Admin firmado (spec §5.1)

Desde el login, link "No puedo entrar / resetear Admin":

1. `POST /auth/reset-admin/desafio` → la API genera un nonce cripto-seguro, lo guarda en memoria con TTL de 24 h (uno solo activo; pedir otro invalida el anterior) y responde `{ desafio, codigoMaquina }`.
2. La pantalla muestra ambos códigos con Copiar → el cliente los comunica al desarrollador.
3. El desarrollador emite con la CLI un token firmado: payload `{ "ver": 1, "accion": "reset-admin", "maquina": "<fingerprint>", "desafio": "<nonce>" }`.
4. El cliente pega token + nueva contraseña de Admin → `POST /auth/reset-admin` valida: firma, `accion`, `maquina` == fingerprint propio, `desafio` == nonce vivo. Todo ok → resetea la contraseña del Admin (o lo recrea vía `PrimerArranqueService` si no quedan admins), consume el nonce (un solo uso) y audita en `LogAuditoria`.

Propiedades: un solo uso (el nonce muere al usarse o expirar), no transferible (atado al fingerprint), no pre-generable (el desafío nace en esa máquina en ese momento). Nota de seguridad: estos endpoints son anónimos como los eliminados en la deuda D7, pero acá la protección es criptográfica — sin la clave privada el endpoint es inerte, y el nonce en memoria evita replay.

## 6. CLI generadora y manejo de claves

Proyecto consola `tools/StockApp.Licencias.Cli` — en el repo (con tests), excluido de todo empaquetado (los scripts de Velopack solo publican `Presentation`). Tres comandos:

- `generar-claves --salida <dir>` → genera el par ECDSA P-256 una única vez. Privada en PEM a un directorio del desarrollador fuera del repo; pública impresa para embeberla en `Application` como constante base64.
- `emitir-licencia --clave <pem> --cliente "Ferretería X" --maquina A3F2-...` → imprime el string de licencia.
- `emitir-reset --clave <pem> --maquina A3F2-... --desafio <nonce>` → imprime el token de reset.

Reglas de oro: la clave privada jamás entra al repo (entrada en `.gitignore` como red de seguridad). La CLI reutiliza los MISMOS payloads y firmador que valida `Application` — un solo formato que no puede divergir, garantizado por tests de round-trip (CLI firma → Application valida) que usan el código real de ambos lados.

## 7. Manejo de errores

Resultados explícitos por enum, nunca excepciones para flujo:

- `ResultadoValidacionLicencia { Valida, FormatoInvalido, FirmaInvalida, MaquinaDistinta }`
- Análogo para reset, más `DesafioInvalido`, `DesafioExpirado`.

La pantalla de bloqueo y la de reset muestran mensaje claro por cada caso. Fingerprint ilegible (registro/machine-id inaccesible) → la API arranca en modo bloqueado con log de error explícito, nunca crashea.

## 8. Testing (TDD)

- **Application:** unit tests puros del validador con un par de claves de TEST fijo: firma válida, corrupta, payload adulterado, máquina distinta, formatos rotos, nonce equivocado/expirado/reusado.
- **Api:** integración con `WebApplicationFactory`: matriz de modo bloqueado (endpoint normal → 423, `/licencia/*` pasa), activación end-to-end con licencia de test, reset end-to-end (desafío → token → contraseña nueva → login ok), replay de token → rechazado, auditoría escrita. La `ApiFactory` de tests inyecta fingerprint fake y clave pública de test vía DI (los tests nunca dependen de la máquina real).
- **CLI:** round-trip real firmar → validar.
- **Desktop:** tests de ViewModels de las dos pantallas nuevas (bloqueo y reset) con fakes del cliente de API.

## 9. Fuera de alcance

- Límite de terminales concurrentes (se descartó el modelo híbrido).
- Expiración/suscripción de licencias.
- Code signing de binarios (sigue pendiente de Fase A).
- Validación manual en Windows/Linux real (se hará junto con la validación pendiente de Fase A, todo junto).
