"""L'icona del Mac: la papera di assets/duck.png su una piastrella tonda color acqua, con i
margini e l'ombra della griglia delle icone di macOS (tela 1024, piastrella 824, raggio 185).

Uso: python3 packaging/macos/make_icon.py   (scrive packaging/macos/DuckNote.icns)
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SIZE = 1024
TILE = (100, 100, 924, 924)
RADIUS = 185


def gradient(top, bottom):
    strip = Image.new("RGBA", (1, SIZE))
    for y in range(SIZE):
        t = y / (SIZE - 1)
        strip.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    return strip.resize((SIZE, SIZE))


def tile_mask():
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle(TILE, RADIUS, fill=255)
    return mask


def main():
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # l'ombra sotto alla piastrella, come nelle icone di sistema
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((TILE[0], TILE[1] + 12, TILE[2], TILE[3] + 12), RADIUS, fill=(0, 0, 0, 90))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))

    # acqua: chiara in alto, piu' profonda in basso, con un'onda chiara dove galleggia la papera
    water = gradient((150, 214, 255), (42, 140, 230))
    waves = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(waves)
    draw.ellipse((180, 700, 844, 820), fill=(255, 255, 255, 60))
    draw.ellipse((250, 735, 774, 800), fill=(255, 255, 255, 50))
    water.alpha_composite(waves.filter(ImageFilter.GaussianBlur(6)))
    canvas.paste(water, (0, 0), tile_mask())

    # un filo di luce sul bordo alto della piastrella
    rim = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle(TILE, RADIUS, outline=(255, 255, 255, 70), width=4)
    canvas.alpha_composite(Image.composite(rim, Image.new("RGBA", (SIZE, SIZE)), tile_mask()))

    duck = Image.open(ROOT / "assets" / "duck.png").convert("RGBA").resize((600, 600), Image.LANCZOS)
    # la papera galleggia: il ventre sull'onda chiara
    canvas.alpha_composite(duck, ((SIZE - 600) // 2, 196))

    canvas.save(HERE / "DuckNote.icns", sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
    print(HERE / "DuckNote.icns")


if __name__ == "__main__":
    main()
