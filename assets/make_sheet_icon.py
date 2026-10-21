"""L'icona del foglio nello stile della papera Duck: contorno nero spesso, carta bianca,
angolo piegato e righe di testo. Va su una tessera colorata (un colore per foglio).
Uso: python assets/make_sheet_icon.py src/DuckNote.App/Assets/sheet.png"""
import sys
from PIL import Image, ImageDraw

S = 1024
K = (0, 0, 0, 255)
W = (255, 255, 255, 255)
WS = (226, 230, 236, 255)
L = 70  # contorno

img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
x0, y0, x1, y1, ear = 200, 110, 824, 914, 230
paper = [(x0, y0 + 60), (x0 + 60, y0), (x1 - ear, y0), (x1, y0 + ear), (x1, y1 - 60), (x1 - 60, y1), (x0 + 60, y1), (x0, y1 - 60)]
d.polygon(paper, fill=W)
# ombra a mezzaluna in basso a sinistra, come nelle illustrazioni Duck
d.polygon([(x0, y1 - 300), (x0 + 120, y1 - 180), (x0 + 120, y1), (x0 + 60, y1), (x0, y1 - 60)], fill=WS)
d.line(paper + [paper[0]], fill=K, width=L, joint='curve')
# angolo piegato
d.polygon([(x1 - ear, y0), (x1 - ear, y0 + ear), (x1, y0 + ear)], fill=WS)
d.line([(x1 - ear, y0), (x1 - ear, y0 + ear), (x1, y0 + ear)], fill=K, width=L, joint='curve')
# righe di testo
for i, (a, b) in enumerate([(320, 560), (320, 700), (320, 700), (320, 600)]):
    y = 400 + i * 130
    d.line([(a, y), (b, y)], fill=K, width=56)
    for p in ((a, y), (b, y)):
        d.ellipse([p[0] - 28, p[1] - 28, p[0] + 28, p[1] + 28], fill=K)
img.resize((128, 128), Image.LANCZOS).save(sys.argv[1], optimize=True)
