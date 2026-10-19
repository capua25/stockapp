# Ícono de la app (variante 1d-i)

Edificio con cúpula y bandera amarilla sobre cuadrado azul redondeado.

- `icono-app.svg`: versión grande (frames 48, 64, 128, 256).
- `icono-app-chico.svg`: versión simplificada para 16, 24 y 32 px.
- `icono-app.ico`: resultado (7 frames), usado por `ApplicationIcon` de Presentation y
  Configurador, por `Window.Icon` (como recurso Avalonia `Assets/icono-app.ico`) y por
  `build/pack-win.ps1` (`vpk pack --icon`).

Regenerar tras editar un SVG: `python3 assets/marca/generar-icono.py` (requiere el
chrome-headless-shell de Playwright). Commitear el `.ico` resultante.
