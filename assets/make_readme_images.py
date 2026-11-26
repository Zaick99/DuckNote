"""L'immagine disegnata del README: le 26 papere degli avatar, con gli asset veri
dell'applicazione. Gli altri (assets/readme/*-light.png e *-dark.png) sono screenshot dell'app,
fatti su una nota di prova con indirizzi privati e nomi example.local.
Uso: python assets/make_readme_images.py"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, 'src', 'DuckNote.App', 'Assets')
OUT = os.path.join(ROOT, 'assets', 'readme')
os.makedirs(OUT, exist_ok=True)

def font(size, bold=False):
    for name in (['Inter-Bold.ttf', 'DejaVuSans-Bold.ttf'] if bold else ['Inter-Regular.ttf', 'DejaVuSans.ttf']):
        for base in ('/usr/share/fonts', '/usr/local/share/fonts', 'C:/Windows/Fonts'):
            for dirpath, _, files in os.walk(base):
                if name in files:
                    return ImageFont.truetype(os.path.join(dirpath, name), size)
    return ImageFont.load_default()

BG = (247, 247, 249, 255); CARD = (255, 255, 255, 255); EDGE = (226, 228, 233, 255)
INK = (29, 29, 31, 255); MUTED = (110, 110, 118, 255); ORANGE = (255, 149, 0, 255)

def card(canvas, box, r=26):
    x0, y0, x1, y1 = box
    shadow = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((x0, y0 + 5, x1, y1 + 5), r, fill=(0, 0, 0, 18))
    canvas.alpha_composite(shadow)
    ImageDraw.Draw(canvas).rounded_rectangle(box, r, fill=CARD, outline=EDGE, width=2)

# le papere degli avatar, una per lettera
cols, size, gap = 13, 120, 18
W = 40 * 2 + cols * size + (cols - 1) * gap
H = 40 * 2 + 2 * (size + 46) + gap
img = Image.new('RGBA', (W, H), BG)
d = ImageDraw.Draw(img)
for i in range(26):
    x = 40 + (i % cols) * (size + gap); y = 40 + (i // cols) * (size + 46 + gap)
    duck = Image.open(os.path.join(APP, 'avatars', f'duck{i + 1:02d}.png')).convert('RGBA').resize((size, size), Image.LANCZOS)
    img.alpha_composite(duck, (x, y))
    letter = chr(ord('A') + i)
    lw = d.textlength(letter, font=font(28, True))
    d.text((x + (size - lw) / 2, y + size + 6), letter, font=font(28, True), fill=MUTED)
img.convert('RGB').save(os.path.join(OUT, 'avatars.png'), optimize=True)

print('ok')
