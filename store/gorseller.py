"""Mağaza görselleri üretici.

  python store/gorseller.py grafik            → Play öne çıkan grafik (1024×500) + 512 simge
  python store/gorseller.py ekran <klasör>    → klasördeki ham ekran görüntülerini (01.png, 02.png …)
                                                 başlıklı mağaza görsellerine çevirir (iPhone, iPad, Android)

Çıktı: ../MAGAZA_GORSELLERI/ (depo dışı)
Tasarım ana sayfadaki mavi devre kartıyla aynı: lehim maskesi noktaları, bakır yollar, altın vurgular.
"""
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(os.path.dirname(ROOT), "MAGAZA_GORSELLERI")
ICON = os.path.join(ROOT, "core", "content", "brand", "icon_1024.png")
FONT_B = r"C:\Windows\Fonts\segoeuib.ttf"
FONT_SB = r"C:\Windows\Fonts\seguisb.ttf"
FONT_R = r"C:\Windows\Fonts\segoeui.ttf"

BOARD, BOARD2 = (8, 33, 82), (12, 46, 110)
DOT = (61, 123, 217)
COPPER, COPPER_HI, GOLD = (192, 138, 62), (242, 178, 102), (228, 192, 106)
SILK, SILK_DIM = (239, 243, 251), (159, 182, 220)

# başlıklar: ekran görüntüsü sırasıyla eşleşir (01.png → ilk başlık)
CAPTIONS = [
    ("DXF projelerinizi açın", "AutoCAD çizimleri telefonda ve tablette"),
    ("Çizin, ölçün, düzenleyin", "Yakalama, hizalama izleri, ölçülendirme"),
    ("Elektrik sembolleri", "Armatür, priz, anahtar, pano ve daha fazlası"),
    ("Tablolar tek dokunuşla", "Aydınlatma hesabı, toplam güç, antet"),
    ("13 hesap modülü", "Yükleme cetveli, gerilim düşümü, topraklama"),
    ("PDF çıktısı ve keşif", "A4–A0 PDF, sembol adet listesi"),
    ("İnternetsiz çalışır", "Projeleriniz yalnızca cihazınızda"),
]


def font(path, size):
    return ImageFont.truetype(path, size)


def board(w, h, seed=11):
    """Mavi devre kartı zemini"""
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(BOARD2[i] + (BOARD[i] - BOARD2[i]) * t) for i in range(3)))
    k = w / 1080
    step = int(18 * 2.2 * k) or 18
    r = max(1, int(1.1 * 2.2 * k))
    over = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    o = ImageDraw.Draw(over)
    for y in range(step // 2, h, step):
        for x in range(step // 2, w, step):
            o.ellipse([x - r, y - r, x + r, y + r], fill=DOT + (40,))
    rnd = random.Random(seed)
    lw = max(2, int(3.5 * k))
    for _ in range(16):
        y0 = rnd.random() * h
        x0 = rnd.random() * w * 0.6
        ln = w * (0.2 + rnd.random() * 0.4)
        bend = int(30 * k) * (1 if rnd.random() < 0.5 else -1)
        pts = [(x0, y0), (x0 + ln * 0.4, y0), (x0 + ln * 0.4 + abs(bend), y0 + bend), (x0 + ln, y0 + bend)]
        o.line(pts, fill=COPPER + (34,), width=lw, joint="curve")
        for p in (pts[0], pts[-1]):
            rr = int(8 * k)
            o.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], fill=COPPER + (60,))
            rr2 = int(3 * k)
            o.ellipse([p[0] - rr2, p[1] - rr2, p[0] + rr2, p[1] + rr2], fill=BOARD + (255,))
    img.paste(over, (0, 0), over)
    return img


def rounded(img, radius):
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, img.size[0] - 1, img.size[1] - 1], radius, fill=255)
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.paste(img, (0, 0), m)
    return out


def shadow(size, radius, blur, alpha=150):
    s = Image.new("RGBA", (size[0] + blur * 4, size[1] + blur * 4), (0, 0, 0, 0))
    ImageDraw.Draw(s).rounded_rectangle([blur * 2, blur * 2, blur * 2 + size[0], blur * 2 + size[1]], radius, fill=(0, 0, 0, alpha))
    return s.filter(ImageFilter.GaussianBlur(blur))


def feature_graphic():
    w, h = 1024, 500
    img = board(w, h, seed=7).convert("RGBA")
    d = ImageDraw.Draw(img)
    # sol: logo; sağ: başlık
    icon = Image.open(ICON).convert("RGB").resize((220, 220), Image.LANCZOS)
    icon = rounded(icon, 50)
    sh = shadow((220, 220), 50, 14)
    img.alpha_composite(sh, (70 - 28, 140 - 28 + 8))
    img.alpha_composite(icon, (70, 140))
    # logo ile başlık arası bakır yol + akım darbesi
    d.line([(290, 250), (330, 250), (350, 230), (370, 230)], fill=COPPER_HI + (200,), width=4, joint="curve")
    for x, a in ((318, 90), (318, 255)):
        d.ellipse([x - 9, 250 - 9, x + 9, 250 + 9], fill=COPPER_HI + (a // 4,))
    d.ellipse([318 - 3, 247, 318 + 3, 253], fill=(255, 255, 255, 235))
    x0 = 385
    d.text((x0, 128), "MühenKit", font=font(FONT_B, 82), fill=SILK)
    d.text((x0 + 4, 222), "ELEKTRİK", font=font(FONT_B, 40), fill=GOLD, spacing=0)
    d.text((x0 + 4, 290), "DXF proje çizimi · Elektrik hesapları", font=font(FONT_SB, 28), fill=SILK)
    d.text((x0 + 4, 334), "Aydınlatma · Yükleme cetveli · Topraklama · PDF", font=font(FONT_R, 22), fill=SILK_DIM)
    # alt şerit
    d.text((x0 + 4, 400), "İnternetsiz · Reklamsız · Türkçe", font=font(FONT_SB, 20), fill=COPPER_HI)
    img.convert("RGB").save(os.path.join(OUT, "play_one_cikan_grafik_1024x500.png"))
    Image.open(ICON).convert("RGB").resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, "play_simge_512.png"))


def framed(shot, w, h, title, sub, corner):
    """Ham ekran görüntüsü → başlıklı mağaza görseli (w×h)"""
    img = board(w, h, seed=hash(title) % 1000).convert("RGBA")
    d = ImageDraw.Draw(img)
    k = w / 1290
    tf, sf = font(FONT_B, int(92 * k)), font(FONT_SB, int(50 * k))
    def center(text, f, y, fill):
        tw = d.textlength(text, font=f)
        d.text(((w - tw) / 2, y), text, font=f, fill=fill)
    top = int(150 * k)
    center(title, tf, top, SILK)
    center(sub, sf, top + int(125 * k), GOLD)
    # ekran görüntüsü: altta, yuvarlak köşe + gölge + ince çerçeve
    area_top = top + int(260 * k)
    max_w, max_h = w * 0.80, h - area_top - int(70 * k)
    sw, shh = shot.size
    s = min(max_w / sw, max_h / shh)
    nw, nh = int(sw * s), int(shh * s)
    pic = rounded(shot.convert("RGB").resize((nw, nh), Image.LANCZOS), int(corner * k))
    x, y = (w - nw) // 2, area_top
    blur = int(30 * k)
    img.alpha_composite(shadow((nw, nh), int(corner * k), blur), (x - blur * 2, y - blur * 2 + int(20 * k)))
    bezel = int(12 * k)
    ImageDraw.Draw(img).rounded_rectangle([x - bezel, y - bezel, x + nw + bezel, y + nh + bezel], int(corner * k) + bezel,
                                          fill=(14, 20, 28, 255), outline=(44, 58, 78, 255), width=max(2, int(3 * k)))
    img.alpha_composite(pic, (x, y))
    return img.convert("RGB")


def screens(folder):
    """Klasör adı 'android' içeriyorsa Android, 'ipad' içeriyorsa iPad, yoksa iPhone boyutu.
    basliklar.txt varsa her satır "Başlık | alt başlık" (görüntü sırasıyla)."""
    shots = sorted((f for f in os.listdir(folder) if f.lower().endswith((".png", ".jpg", ".jpeg"))),
                   key=lambda f: (len(f), f))
    if not shots:
        print("Klasörde ekran görüntüsü yok:", folder)
        return
    caps = CAPTIONS
    cf = os.path.join(folder, "basliklar.txt")
    if os.path.exists(cf):
        caps = [tuple(x.strip() for x in l.split("|", 1)) for l in open(cf, encoding="utf-8") if "|" in l]
    low = folder.lower()
    if "android" in low:
        name, (w, h, corner), crop = "android_telefon", (1080, 1920, 70), (0.032, 0.046)  # durum ve gezinme çubuğu
    elif "ipad" in low:
        name, (w, h, corner), crop = "ipad_13", (2064, 2752, 40), (0.0, 0.0)
    else:
        name, (w, h, corner), crop = "iphone_6.9", (1320, 2868, 110), (0.038, 0.0)  # durum çubuğu
    out = os.path.join(OUT, name)
    os.makedirs(out, exist_ok=True)
    for i, f in enumerate(shots):
        title, sub = caps[i % len(caps)]
        im = Image.open(os.path.join(folder, f)).convert("RGB")
        if crop != (0.0, 0.0):
            im = im.crop((0, int(im.height * crop[0]), im.width, int(im.height * (1 - crop[1]))))
        framed(im, w, h, title, sub, corner).save(os.path.join(out, f"{i + 1:02d}.png"))
    print(name, len(shots), "gorsel:", out)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    if len(sys.argv) > 1 and sys.argv[1] == "ekran":
        screens(sys.argv[2])
    else:
        feature_graphic()
        print("cikti:", OUT)
