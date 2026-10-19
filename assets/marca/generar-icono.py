#!/usr/bin/env python3
"""Regenera icono-app.ico (frames 16,24,32,48,64,128,256) desde los SVG de esta carpeta.

Uso:  python3 assets/marca/generar-icono.py
Requisito: chrome-headless-shell de Playwright (~/.cache/ms-playwright/chromium_headless_shell-*).
16/24/32 salen de icono-app-chico.svg (simplificado); 48 en adelante de icono-app.svg.
Cada frame va como PNG dentro del ICO (soportado desde Windows Vista); no depende de PIL.
"""
import glob, os, struct, subprocess, sys, tempfile

AQUI = os.path.dirname(os.path.abspath(__file__))
GRANDE = os.path.join(AQUI, "icono-app.svg")
CHICO = os.path.join(AQUI, "icono-app-chico.svg")
DESTINO = os.path.join(AQUI, "icono-app.ico")
TAMANOS = [16, 24, 32, 48, 64, 128, 256]


def chrome():
    hallados = sorted(glob.glob(os.path.expanduser(
        "~/.cache/ms-playwright/chromium_headless_shell-*/*/chrome-headless-shell")))
    if not hallados:
        sys.exit("No se encontro chrome-headless-shell de Playwright")
    return hallados[-1]


def renderizar(exe, svg, n, tmp):
    html = os.path.join(tmp, f"r{n}.html")
    png = os.path.join(tmp, f"r{n}.png")
    with open(html, "w") as f:
        f.write(f'<html><body style="margin:0;background:transparent">'
                f'<img src="file://{svg}" style="width:{n}px;height:{n}px;display:block"></body></html>')
    p = subprocess.Popen([exe, "--no-sandbox", "--disable-gpu", "--hide-scrollbars",
                          "--force-device-scale-factor=1", "--default-background-color=00000000",
                          f"--window-size={n},{n}", f"--screenshot={png}", "file://" + html],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    p.wait(timeout=60)
    with open(png, "rb") as f:
        return f.read()


def main():
    exe = chrome()
    with tempfile.TemporaryDirectory() as tmp:
        frames = [(n, renderizar(exe, CHICO if n <= 32 else GRANDE, n, tmp)) for n in TAMANOS]
    cab = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    dirs, datos = b"", b""
    for n, png in frames:
        dim = 0 if n >= 256 else n
        dirs += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset + len(datos))
        datos += png
    with open(DESTINO, "wb") as f:
        f.write(cab + dirs + datos)
    print("ok", DESTINO, [n for n, _ in frames])


main()
