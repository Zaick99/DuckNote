"""Le immagini del README, fatte con gli asset veri dell'applicazione: le illustrazioni
delle sezioni, le 26 papere degli avatar e le icone dei fogli.
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

# 1. le quattro parti della finestra
parts = [('note.png', 'Fogli', 'il quaderno: fogli, titoli,\nindirizzi colorati per stato'),
         ('network.png', 'Rete', 'scanner e tabella degli host,\nscheda di ogni host'),
         ('vault.png', 'Cassaforte', 'Argon2id + AES-256,\nblocco automatico'),
         ('settings.png', 'Impostazioni', 'tema, monitoraggio,\nraccolta, sistema')]
W, H = 1800, 560
img = Image.new('RGBA', (W, H), BG)
d = ImageDraw.Draw(img)
cw = (W - 80 - 3 * 40) // 4
for i, (art, title, text) in enumerate(parts):
    x = 40 + i * (cw + 40)
    card(img, (x, 40, x + cw, H - 40))
    pic = Image.open(os.path.join(APP, 'art', art)).convert('RGBA').resize((230, 230), Image.LANCZOS)
    img.alpha_composite(pic, (x + (cw - 230) // 2, 80))
    tw = d.textlength(title, font=font(40, True))
    d.text((x + (cw - tw) / 2, 330), title, font=font(40, True), fill=INK)
    for j, line in enumerate(text.split('\n')):
        lw = d.textlength(line, font=font(26))
        d.text((x + (cw - lw) / 2, 392 + j * 36), line, font=font(26), fill=MUTED)
img.convert('RGB').save(os.path.join(OUT, 'sections.png'), optimize=True)

# 2. le papere degli avatar, una per lettera
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

# 3. i fogli nella sidebar: tessere di colore diverso con titoli H1/H2
tiles = [((255, 149, 0), 'Rete di casa', ['H1 Router', 'H2 Wi-Fi ospiti']),
         ((0, 122, 255), 'Server', ['H1 NAS', 'H1 Backup']),
         ((52, 199, 89), 'Ufficio', ['H1 Stampanti']),
         ((175, 82, 222), 'Da fare', [])]
sheet = Image.open(os.path.join(APP, 'sheet.png')).convert('RGBA').resize((34, 34), Image.LANCZOS)
W, H = 760, 40 + sum(64 + 44 * len(t[2]) for t in tiles) + 30
img = Image.new('RGBA', (W, H), BG)
card(img, (20, 20, W - 20, H - 20), 22)
d = ImageDraw.Draw(img)
y = 44
for colour, name, heads in tiles:
    d.rounded_rectangle((50, y, 92, y + 42), 11, fill=colour + (255,))
    img.alpha_composite(sheet, (54, y + 4))
    d.text((112, y + 4), name, font=font(28, True), fill=INK)
    y += 64
    for h in heads:
        tag, text = h.split(' ', 1)
        x = 112 if tag == 'H1' else 140
        d.rounded_rectangle((x, y + 2, x + 46, y + 32), 7, fill=(255, 236, 209, 255))
        d.text((x + 7, y + 3), tag, font=font(20, True), fill=ORANGE)
        d.text((x + 60, y), text, font=font(26), fill=MUTED)
        y += 44
img.convert('RGB').save(os.path.join(OUT, 'sheets.png'), optimize=True)
print('ok')
