"""Lo sfondo della finestra del .dmg: papere e teschi, la freccia d'accento dall'app ad
Applicazioni e la frase che dice cosa fare. I teschi sono quelli del profilo GitHub
(skulls.json, copiato da scripts/skulls.json di Zaick99/Zaick99): bianchi, contorno d'inchiostro
largo 5, orbite e naso pieni, come li disegna il profilo. La papera e la freccia seguono lo stile
delle illustrazioni Duck (contorno nero spesso, colori piatti, luce in alto a destra).

    python3 packaging/macos/make_dmg_background.py

Scrive packaging/macos/dmg-background.png (660x400) e dmg-background@2x.png (1320x800).
Le posizioni delle icone stanno anche in dmg_settings.py: se cambiano qui, cambiano li'.
"""
import json
import random
import re
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
W, H = 1320, 800                     # @2x
APP_AT, APPS_AT = (330, 400), (990, 400)   # centri delle icone @2x (165,200 e 495,200 @1x)
ICON = 224                           # icone di 112 punti @1x

K = (0, 0, 0, 255)
ORANGE, ORANGE_SHADE = (255, 159, 46, 255), (232, 116, 0, 255)
LABEL, LABEL_SECONDARY = (29, 29, 31, 255), (110, 110, 115, 255)
TAG = (111, 119, 133, 255)           # leggibile sia col testo nero sia col bianco (contrasto ~4,6)

FONT_TITLE = "/usr/share/fonts/opentype/inter/InterDisplay-ExtraBold.otf"
FONT_TEXT = "/usr/share/fonts/opentype/inter/Inter-Medium.otf"


# ---------------------------------------------------------------- i teschi del profilo

INK = (13, 13, 15, 255)               # l'inchiostro del profilo, #0d0d0f
SKULLS = json.loads((HERE / "skulls.json").read_text(encoding="utf-8"))


def subpaths(data):
    """I percorsi del profilo usano solo M, C e Z assoluti: ogni curva diventa una spezzata."""
    numbers = [float(v) for v in re.findall(r"-?\d+(?:\.\d+)?", data)]
    commands = re.findall(r"[MCZ]", data)
    shapes, points, i = [], [], 0
    for command in commands:
        if command == "M":
            if points:
                shapes.append(points)
            points = [(numbers[i], numbers[i + 1])]
            i += 2
        elif command == "C":
            (x0, y0), (x1, y1, x2, y2, x3, y3) = points[-1], numbers[i:i + 6]
            i += 6
            for step in range(1, 25):
                t = step / 24
                u = 1 - t
                points.append((u ** 3 * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t ** 3 * x3,
                               u ** 3 * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t ** 3 * y3))
        elif command == "Z" and points:
            shapes.append(points)
            points = []
    if points:
        shapes.append(points)
    return shapes


def skull(height, which=0):
    """Il teschio numero <which> del profilo, alto <height> pixel, come lo disegna il profilo."""
    spec = SKULLS[which % len(SKULLS)]
    vx, vy, vw, vh = (float(v) for v in spec["viewBox"].split())
    pad = 6                                  # il contorno sborda di mezzo tratto
    scale = (height * 4) / (vh + 2 * pad)
    w, h = round((vw + 2 * pad) * scale), round((vh + 2 * pad) * scale)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def place(points):
        return [((x - vx + pad) * scale, (y - vy + pad) * scale) for x, y in points]

    stroke = max(2, round(5 * scale))
    for shape in subpaths(spec["outline"]):
        d.polygon(place(shape), fill=(255, 255, 255, 255))
        d.line(place(shape + shape[:1]), fill=INK, width=stroke, joint="curve")
    for hole in spec["holes"]:
        for shape in subpaths(hole):
            d.polygon(place(shape), fill=INK)
            d.line(place(shape + shape[:1]), fill=INK, width=max(2, round(scale)), joint="curve")
    return img.resize((max(1, w // 4), max(1, h // 4)), Image.LANCZOS)


def arrow(length, height):
    """La freccia d'accento, nello stesso stile: asta tonda e punta, contorno nero."""
    n = 4
    img = Image.new("RGBA", (length * n, height * n), (0, 0, 0, 0))
    line = round(height * n * 0.11)
    shaft_h = height * n * 0.42
    head_w = height * n * 0.95
    cy = height * n / 2
    body = (line, cy - shaft_h / 2, length * n - head_w * 0.8, cy + shaft_h / 2)
    head = [(length * n - head_w, line * 0.9), (length * n - line, cy), (length * n - head_w, height * n - line * 0.9)]

    def shape(d, grow, colour):
        d.rounded_rectangle((body[0] - grow, body[1] - grow, body[2] + grow, body[3] + grow), shaft_h / 2 + grow, fill=colour)
        cx = sum(p[0] for p in head) / 3
        grown = [(cx + (x - cx) * (1 + grow / (head_w * 0.6)), cy + (y - cy) * (1 + grow / (head_w * 0.55))) for x, y in head]
        d.polygon(grown, fill=colour)

    d = ImageDraw.Draw(img)
    shape(d, line // 2 + line // 4, K)
    m = Image.new("L", img.size, 0)
    shape(ImageDraw.Draw(m), 0, 255)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    layer.paste(Image.new("RGBA", img.size, ORANGE_SHADE), (0, 0), m)
    shift = round(height * n * 0.12)
    layer.paste(Image.new("RGBA", img.size, ORANGE), (0, 0), ImageChops.multiply(ImageChops.offset(m, shift // 2, -shift), m))
    img.alpha_composite(layer)
    return img.resize((length, height), Image.LANCZOS)


# ---------------------------------------------------------------- la tela

def faded(sticker, alpha):
    """Un adesivo grigio chiarissimo, per la carta da parati."""
    grey = sticker.convert("LA").convert("RGBA")
    r, g, b, a = grey.split()
    lighter = Image.merge("RGBA", (r.point(lambda v: 150 + v * 0.4), g.point(lambda v: 150 + v * 0.4), b.point(lambda v: 160 + v * 0.38), a.point(lambda v: int(v * alpha))))
    return lighter


def wallpaper(canvas, duck, skulls):
    rng = random.Random(15)            # sempre lo stesso disegno
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    step = 156
    for row, y in enumerate(range(-30, H + 80, step // 2 + 34)):
        for col, x in enumerate(range(-60 + (row % 2) * step // 2, W + 80, step)):
            item = duck if (row + col) % 2 == 0 else skulls[(row // 2 + col // 2) % len(skulls)]
            rotated = item.rotate(rng.uniform(-16, 16), resample=Image.BICUBIC, expand=True)
            layer.alpha_composite(rotated, (x - rotated.width // 2, y - rotated.height // 2))

    # titolo, icone con i loro nomi e frase restano puliti: la carta da parati fa da cornice
    clear = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(clear)
    d.rounded_rectangle((330, 26, W - 330, 150), 60, fill=255)
    d.rounded_rectangle((110, 222, W - 110, 600), 110, fill=255)
    d.rounded_rectangle((250, 662, W - 250, 752), 45, fill=255)
    clear = clear.filter(ImageFilter.GaussianBlur(28))
    alpha = layer.getchannel("A")
    layer.putalpha(ImageChops.multiply(alpha, ImageChops.invert(clear)))
    canvas.alpha_composite(layer)


def text(canvas, words, font, size, colour, centre_x, top):
    face = ImageFont.truetype(font, size)
    d = ImageDraw.Draw(canvas)
    width = d.textlength(words, font=face)
    d.text((centre_x - width / 2, top), words, font=face, fill=colour)


def tag(canvas, centre_x, top, width=230, height=42):
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle((centre_x - width / 2, top, centre_x + width / 2, top + height), height / 2, fill=TAG)


def main():
    canvas = Image.new("RGBA", (W, H), (255, 255, 255, 255))

    duck_art = Image.open(ROOT / "assets" / "duck.png").convert("RGBA")
    pirate = Image.open(ROOT / "src" / "DuckNote.App" / "Assets" / "avatars" / "duck04.png").convert("RGBA")

    wallpaper(canvas, faded(duck_art.resize((74, 74), Image.LANCZOS), 0.42),
              [faded(skull(70, which), 0.42) for which in range(len(SKULLS))])

    # i nomi sotto alle icone li scrive il Finder: un'etichetta grigia media li tiene leggibili
    # sia col testo nero (tema chiaro) sia col bianco (tema scuro)
    for (x, _) in (APP_AT, APPS_AT):
        tag(canvas, x, APP_AT[1] + ICON // 2 + 10)

    gap = 70
    length = (APPS_AT[0] - ICON // 2 - gap) - (APP_AT[0] + ICON // 2 + gap)
    canvas.alpha_composite(arrow(length, 96), (APP_AT[0] + ICON // 2 + gap, APP_AT[1] - 48))

    text(canvas, "DuckNote", FONT_TITLE, 58, LABEL, W // 2, 52)
    text(canvas, "Trascina la papera nella cartella Applicazioni", FONT_TEXT, 30, LABEL_SECONDARY, W // 2, 690)

    # agli angoli in basso la ciurma: la papera pirata e il teschio del profilo
    corner = pirate.resize((190, 190), Image.LANCZOS).rotate(8, resample=Image.BICUBIC, expand=True)
    canvas.alpha_composite(corner, (18, H - corner.height - 18))
    jolly = skull(210, 0).rotate(-9, resample=Image.BICUBIC, expand=True)
    canvas.alpha_composite(jolly, (W - jolly.width - 26, H - jolly.height - 16))

    canvas = canvas.convert("RGB")
    canvas.save(HERE / "dmg-background@2x.png", optimize=True)
    canvas.resize((W // 2, H // 2), Image.LANCZOS).save(HERE / "dmg-background.png", optimize=True)
    print(HERE / "dmg-background@2x.png")


if __name__ == "__main__":
    main()
