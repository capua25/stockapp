#!/usr/bin/env python3
"""Captura una ventana X11 (p. ej. un popup de Avalonia) leyendo el servidor X directo.

Uso: capturar-x11.py <window-id> <salida.png>
     <window-id> es decimal (xdotool search --pid <PID> / getwindowgeometry); 0 = raiz X.

Por que existe: WSLg NO compone los popups de Avalonia (calendario, ComboBox) a la
captura de Windows que hace capturar.sh. Aca se lee el pixmap de la ventana desde el
servidor X, asi que ve lo que la app dibujo aunque Windows no lo muestre.
Requiere python3 con python-xlib y Pillow. Ver README, "Popups no compuestos".
"""
import sys
from Xlib import display, X
from PIL import Image

if len(sys.argv) != 3 or not sys.argv[1].isdigit():
    sys.exit("uso: capturar-x11.py <window-id> <salida.png>")

d = display.Display()
wid = int(sys.argv[1])
w = d.create_resource_object("window", wid) if wid else d.screen().root
g = w.get_geometry()
raw = w.get_image(0, 0, g.width, g.height, X.ZPixmap, 0xFFFFFFFF)
img = Image.frombytes("RGBA", (g.width, g.height), raw.data, "raw", "BGRA")
img.save(sys.argv[2])
extrema = img.convert("L").getextrema()
print(g.width, g.height, "extrema", extrema)
if extrema[0] == extrema[1]:
    sys.exit("captura de un solo color: no es confiable")
