"""26 papere vestite in modo buffo (una per lettera), nello stile della papera Duck:
contorno nero spesso, colori pieni. Uso: python assets/make_avatars.py src/DuckNote.App/Assets/duck.png src/DuckNote.App/Assets/avatars anteprima.png
L'ordine dei costumi e' quello delle lettere: A cowboy, B cilindro, ... Z detective."""
import sys, os, math
from PIL import Image, ImageChops, ImageDraw

F = 4                     # disegno a 4x, poi riduco: bordi lisci
OX, OY = 64, 120          # la papera (512) dentro una tela 640x640: spazio per cappelli e pipe
LW = 13                   # spessore del contorno, in unita' della papera
K = (0, 0, 0, 255)
def c(h): h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4)) + (255,)

class Art:
    def __init__(self, duck):
        self.img = Image.new('RGBA', (640*F, 640*F), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.img.alpha_composite(duck.resize((512*F, 512*F), Image.LANCZOS), (OX*F, OY*F))
    def P(self, x, y): return ((x+OX)*F, (y+OY)*F)
    def B(self, b): return [*self.P(b[0], b[1]), *self.P(b[2], b[3])]
    def poly(self, pts, fill, w=LW):
        self.d.polygon([self.P(*p) for p in pts], fill=fill, outline=K, width=w*F)
    def ell(self, b, fill, w=LW):
        self.d.ellipse(self.B(b), fill=fill, outline=K if w else None, width=w*F)
    def rect(self, b, fill, r=8, w=LW):
        self.d.rounded_rectangle(self.B(b), r*F, fill=fill, outline=K if w else None, width=w*F)
    def dome(self, b, fill, w=LW):   # meta' superiore di un'ellisse, con la base
        self.d.pieslice(self.B(b), 180, 360, fill=fill, outline=K, width=w*F)
    def line(self, pts, w=LW, color=K):
        pts = [self.P(*p) for p in pts]
        self.d.line(pts, fill=color, width=w*F, joint='curve')
        for p in (pts[0], pts[-1]):
            r = w*F/2; self.d.ellipse([p[0]-r, p[1]-r, p[0]+r, p[1]+r], fill=color)
    def dot(self, x, y, r, fill, w=0):
        self.ell((x-r, y-r, x+r, y+r), fill, w)
    def star(self, x, y, r, fill):
        pts = []
        for i in range(10):
            a = -math.pi/2 + i*math.pi/5; rr = r if i % 2 == 0 else r*0.45
            pts.append((x+rr*math.cos(a), y+rr*math.sin(a)))
        self.poly(pts, fill, w=5)

EYE = (372, 148)
# ---------------------------------------------------------------- i costumi
def cowboy(a):
    a.poly([(268, 92), (282, -2), (340, 18), (398, -2), (412, 92)], c('A8743F'))
    a.rect((274, 58, 406, 84), c('5B3A1E'), r=4, w=8)
    a.ell((200, 72, 480, 116), c('C08A4E'))
    a.star(340, 70, 13, c('FFD54A'))
def tophat(a):
    a.rect((282, -78, 398, 66), c('3A3A44'), r=6)
    a.rect((282, 22, 398, 50), c('E5484D'), r=2, w=8)
    a.ell((236, 50, 444, 84), c('3A3A44'))
    a.ell((EYE[0]-28, EYE[1]-28, EYE[0]+28, EYE[1]+28), (200, 230, 255, 90), w=8)
    a.line([(EYE[0]-20, EYE[1]+22), (350, 220), (330, 250)], w=4, color=c('E0B640'))
def crown(a):
    a.poly([(266, 66), (258, -14), (300, 26), (340, -30), (380, 26), (422, -14), (414, 66)], c('FFD23F'))
    for x, col in ((300, 'E5484D'), (340, '3B82F6'), (380, '22C55E')):
        a.dot(x, 48, 11, c(col), w=6)
    for x, y in ((258, -14), (340, -30), (422, -14)):
        a.dot(x, y, 10, c('FFF1A8'), w=6)
def pirate(a):
    a.poly([(206, 78), (252, -6), (340, -26), (428, -6), (474, 78), (340, 52)], c('2B2B33'))
    a.dot(340, 24, 15, c('FFFFFF'), w=6)
    a.line([(330, 46), (350, 56)], w=5); a.line([(350, 46), (330, 56)], w=5)
    a.line([(236, 96), (EYE[0], EYE[1]), (452, 176)], w=8)
    a.ell((EYE[0]-26, EYE[1]-24, EYE[0]+26, EYE[1]+24), c('2B2B33'))
def chef(a):
    a.rect((276, 6, 404, 68), c('FFFFFF'), r=6)
    a.ell((240, -70, 330, 20), c('FFFFFF')); a.ell((350, -70, 440, 20), c('FFFFFF'))
    a.ell((290, -100, 390, -6), c('FFFFFF'))
    a.rect((276, 6, 404, 68), c('FFFFFF'), r=6)
    a.line([(306, 22), (306, 56)], w=5, color=c('D9DDE3')); a.line([(340, 22), (340, 56)], w=5, color=c('D9DDE3'))
    a.line([(374, 22), (374, 56)], w=5, color=c('D9DDE3'))
def sunglasses(a):
    a.line([(240, 128), (330, 140)], w=9)
    a.rect((330, 118, 412, 172), c('1F2937'), r=18)
    a.line([(344, 130), (370, 130)], w=5, color=c('6B7280'))
    a.line([(412, 140), (440, 150)], w=8)
def party(a):
    a.poly([(286, 58), (394, 58), (348, -104)], c('EC4899'))
    for t in (0.33, 0.62):
        y = 58 - t*162; half = 54*(1-t)
        a.line([(340+half*0.95, y+4), (340-half*1.05, y-6)], w=10, color=c('FDE047'))
    a.dot(348, -110, 20, c('60A5FA'), w=8)
def propeller(a):
    a.dome((262, -14, 418, 118), c('3B82F6'))
    a.d.pieslice(a.B((262, -14, 418, 118)), 240, 300, fill=c('EF4444'), outline=K, width=8*F)
    a.line([(340, -14), (340, -44)], w=8)
    a.ell((278, -60, 340, -36), c('FDE047'), w=8); a.ell((340, -60, 402, -36), c('22C55E'), w=8)
    a.dot(340, -48, 9, c('EF4444'), w=6)
def wizard(a):
    a.poly([(250, 74), (430, 74), (366, -40), (300, -150), (318, -30)], c('7C3AED'))
    a.ell((214, 56, 466, 96), c('6D28D9'))
    a.star(330, 0, 16, c('FDE047')); a.star(370, 40, 11, c('FDE047')); a.star(300, -80, 10, c('FDE047'))
def headphones(a):
    a.d.arc(a.B((214, 4, 466, 270)), 195, 345, fill=K, width=30*F)
    a.d.arc(a.B((214, 4, 466, 270)), 197, 343, fill=c('6B7280'), width=14*F)
    a.rect((252, 98, 312, 198), c('EF4444'), r=26)
    a.rect((268, 116, 296, 180), c('B91C1C'), r=12, w=0)

def nerd(a):
    a.line([(250, 134), (340, 142)], w=8)
    a.ell((EYE[0]-36, EYE[1]-36, EYE[0]+36, EYE[1]+36), (210, 235, 255, 110), w=10)
    a.poly([(388, 270), (330, 238), (330, 302)], c('EF4444'), w=10)
    a.poly([(388, 270), (446, 238), (446, 302)], c('EF4444'), w=10)
    a.dot(388, 270, 16, c('B91C1C'), w=8)
def hardhat(a):
    a.dome((244, -8, 436, 140), c('FB923C'))
    a.rect((222, 60, 458, 84), c('FB923C'), r=10)
    a.rect((330, -2, 350, 64), c('FDBA74'), r=4, w=6)
def beanie(a):
    a.dome((236, -12, 444, 160), c('14B8A6'))
    a.rect((230, 56, 450, 92), c('0F766E'), r=14)
    for x in (262, 298, 334, 370, 406):
        a.line([(x, 62), (x, 86)], w=5, color=c('5EEAD4'))
    a.dot(340, -18, 26, c('FFFFFF'), w=10)
def graduation(a):
    a.rect((284, 24, 396, 80), c('1F2937'), r=6)
    a.poly([(206, 22), (340, -24), (474, 22), (340, 66)], c('1F2937'))
    a.line([(340, 20), (448, 32), (452, 104)], w=6, color=c('FBBF24'))
    a.poly([(440, 100), (464, 100), (470, 138), (434, 138)], c('FBBF24'), w=6)
    a.dot(340, 20, 9, c('FBBF24'), w=5)
def sweatband(a):
    a.poly([(240, 76), (430, 76), (443, 112), (227, 112)], c('EF4444'))
    a.line([(236, 94), (436, 94)], w=7, color=c('FFFFFF'))

def beret(a):
    a.ell((226, 4, 426, 70), c('DC2626'))
    a.line([(330, 4), (336, -16)], w=10)
    a.d.chord(a.B((420, 214, 462, 246)), 0, 180, fill=K)
    a.ell((404, 222, 446, 246), c('3F2A1D'), w=6); a.ell((436, 222, 478, 246), c('3F2A1D'), w=6)
def santa(a):
    a.poly([(252, 74), (424, 74), (350, -20), (270, -46), (196, -10), (300, 10)], c('DC2626'))
    a.rect((236, 52, 444, 92), c('FFFFFF'), r=20)
    a.dot(192, -14, 24, c('FFFFFF'), w=10)
def viking(a):
    a.poly([(262, 44), (204, -50), (226, -54), (286, 22)], c('FEF3C7'))
    a.poly([(418, 44), (476, -50), (454, -54), (394, 22)], c('FEF3C7'))
    a.dome((254, -10, 426, 130), c('9CA3AF'))
    a.rect((246, 50, 434, 78), c('92400E'), r=6)
    for x in (280, 320, 360, 400):
        a.dot(x, 64, 5, c('FDE68A'))
def bow(a):
    a.poly([(320, 40), (256, -10), (250, 84)], c('F472B6'))
    a.poly([(320, 40), (384, -10), (390, 84)], c('F472B6'))
    a.dot(320, 40, 18, c('DB2777'), w=10)
def astronaut(a):
    glass = Image.new('RGBA', a.img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glass).ellipse(a.B((196, -10, 500, 294)), fill=(200, 230, 255, 55))
    a.img.alpha_composite(glass)
    a.d.ellipse(a.B((196, -10, 500, 294)), outline=K, width=LW*F)
    a.d.arc(a.B((222, 14, 474, 268)), 200, 250, fill=(255, 255, 255, 230), width=12*F)
    a.rect((236, 262, 470, 304), c('E5E7EB'), r=18)
    a.dot(300, 283, 8, c('EF4444')); a.dot(330, 283, 8, c('3B82F6'))

def swimmer(a):
    a.dome((222, 30, 452, 250), c('38BDF8'))
    a.line([(250, 120), (330, 138)], w=10)
    a.ell((EYE[0]-30, EYE[1]-24, EYE[0]+30, EYE[1]+24), (167, 243, 208, 170), w=10)
    a.dot(300, 70, 10, c('E0F2FE'))
def flower(a):
    cx, cy = 290, 56
    for i in range(5):
        ang = -math.pi/2 + i*2*math.pi/5
        a.dot(cx+30*math.cos(ang), cy+30*math.sin(ang), 24, c('F472B6'), w=8)
    a.dot(cx, cy, 16, c('FDE047'), w=8)
    a.poly([(320, 80), (360, 60), (352, 92)], c('22C55E'), w=7)
def captain(a):
    a.ell((246, -10, 434, 56), c('FFFFFF'))
    a.rect((266, 30, 414, 70), c('1E3A8A'), r=6)
    a.poly([(360, 64), (452, 66), (436, 88), (352, 80)], c('111827'))
    a.dot(340, 46, 11, c('FBBF24'), w=6)
def alien(a):
    a.line([(306, 44), (276, -40)], w=10); a.line([(374, 44), (404, -40)], w=10)
    a.dot(274, -46, 22, c('84CC16'), w=10); a.dot(406, -46, 22, c('84CC16'), w=10)
    a.dot(268, -52, 6, c('ECFCCB')); a.dot(400, -52, 6, c('ECFCCB'))
def baseball(a):
    a.dome((244, -6, 432, 132), c('2563EB'))
    a.poly([(250, 60), (186, 62), (176, 80), (252, 80)], c('1E40AF'))
    a.dot(338, -6, 9, c('1E40AF'), w=6)
    a.line([(340, 0), (330, 56)], w=4, color=c('93C5FD'))

def detective(a):
    a.dome((250, -10, 430, 130), c('A16207'))
    a.poly([(410, 50), (470, 60), (420, 74)], c('854D0E'), w=9)
    a.poly([(270, 50), (214, 64), (262, 76)], c('854D0E'), w=9)
    for x in (290, 330, 370):
        a.line([(x, 0), (x+10, 54)], w=4, color=c('713F12'))
    a.line([(478, 222), (514, 236)], w=10, color=c('5B3A1E'))
    a.rect((502, 222, 534, 266), c('7C4A23'), r=6, w=9)

COSTUMES = [cowboy, tophat, crown, pirate, chef, sunglasses, party, propeller, wizard, headphones,
            nerd, hardhat, beanie, graduation, sweatband, beret, santa, viking, bow, astronaut,
            swimmer, flower, captain, alien, baseball, detective]
BACKS = ['FDE9C8', 'DCE7FB', 'DDF3E2', 'FBDDE3', 'EADFFB', 'D9F2F3', 'FBF3C8', 'FDE2D2', 'F7DDF0', 'E3F4D2', 'DEE1FA', 'D5F0EA', 'ECEFF3']

def main():
    duck = Image.open(sys.argv[1]).convert('RGBA'); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
    S = 256; tiles = []
    for i, dress in enumerate(COSTUMES):
        art = Art(duck); dress(art)
        img = art.img.resize((640, 640), Image.LANCZOS)
        # stessa inquadratura per tutte: testa, costume e collo; la coda resta fuori
        cx, cy, side = OX + 348, OY + 112, 470
        fig = img.crop((cx - side//2, cy - side//2, cx + side//2, cy + side//2))
        k = S / max(fig.size)
        fig = fig.resize((max(1, int(fig.width*k)), max(1, int(fig.height*k))), Image.LANCZOS)
        canvas = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        mask = Image.new('L', (S*4, S*4), 0); ImageDraw.Draw(mask).ellipse((0, 0, S*4-1, S*4-1), fill=255)
        canvas.paste(Image.new('RGBA', (S, S), c(BACKS[i % len(BACKS)])), (0, 0), mask.resize((S, S), Image.LANCZOS))
        canvas.alpha_composite(fig, ((S-fig.width)//2, (S-fig.height)//2))
        canvas.putalpha(ImageChops.multiply(canvas.split()[3], mask.resize((S, S), Image.LANCZOS)))
        canvas.save(f'{out}/duck{i+1:02d}.png', optimize=True); tiles.append(canvas)
    prev = Image.new('RGBA', (13*136, 2*136), (255, 255, 255, 255))
    for i, t in enumerate(tiles):
        prev.alpha_composite(t.resize((128, 128), Image.LANCZOS), ((i % 13)*136+4, (i//13)*136+4))
    prev.save(sys.argv[3])

main()
