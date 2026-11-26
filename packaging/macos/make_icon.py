"""L'icona del Mac: la papera di assets/duck.png davanti a una piastrella bianca uniforme, con i
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


def tile_mask():
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle(TILE, RADIUS, fill=255)
    return mask


def main():
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # l'ombra sotto alla piastrella, come nelle icone di sistema: senza, il bianco sparisce
    # sui fondi chiari
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((TILE[0], TILE[1] + 12, TILE[2], TILE[3] + 12), RADIUS, fill=(0, 0, 0, 90))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))

    # la piastrella: bianco pieno, uniforme
    white = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 255))
    canvas.paste(white, (0, 0), tile_mask())

    # la papera davanti, al centro della piastrella
    duck = Image.open(ROOT / "assets" / "duck.png").convert("RGBA").resize((620, 620), Image.LANCZOS)
    canvas.alpha_composite(duck, ((SIZE - 620) // 2, (SIZE - 620) // 2))

    canvas.save(HERE / "DuckNote.icns", sizes=[(16, 16), (32, 32), (64, 64), (128, 128), (256, 256), (512, 512), (1024, 1024)])
    print(HERE / "DuckNote.icns")


if __name__ == "__main__":
    main()
