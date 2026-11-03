#!/usr/bin/env python3
"""从内置橘猫生成拎起帧和抖毛帧, 写回 BuiltinSkin.swift. 用法: build_assets.py [预览目录]
给了预览目录再出 GIF (需要 Pillow)."""
import json, math, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = open(os.path.join(ROOT, "Sources/PerchCatCore/BuiltinSkin.swift"), encoding="utf-8").read()
SKIN = json.loads(SRC.split('builtinSkinJSON = #"""\n')[1].split('\n"""#')[0])
PAL = {k: tuple(int(v[i:i + 2], 16) for i in (1, 3, 5)) for k, v in SKIN["palette"].items()}

# ---------- 拎起: 从认命垂吊 (A) 一节节坠成一条猫 (D) ----------
A = [
    ".......KWWBBBBWWK.......",
    ".......KWWBBBBWWK.......",
    ".......KKWKBBKWKK.......",
    ".......KKWKBBKWKK.......",
    ".......KKBKWWKBKK.......",
    ".......KWKKSWKKWK.......",
    ".......KWWWWSWWWKKK.....",
    "........KKWKKWKKWWSK....",
    "........KWWKKWWKKKWWK...",
    "........KBBKKBBK..KKSK..",
]


def long_body(rows, wide):
    """rows 行长的身子: 胸口, 细长的腰, 两条腿, 尾巴比脚垂得低 (rows 够长时)."""
    top = [".......KWWBBBBWWK......."] * 2
    mid = "........KWBBBBWK........" if wide else ".........KWBBWK........."
    midS = "........KSBBBBWK........" if wide else ".........KSBBWK........."
    midT = "........KWBBBBSK........" if wide else ".........KWBBSK........."
    tail_x = 16 if wide else 15
    legs = ["........KWWKKWWK........", "........KBBKKBBK........", ".........KK..KK........."] if wide else \
           [".........KWKKWK.........", ".........KBKKBK.........", "..........K..K.........."]
    n_mid = rows - len(top) - len(legs) - 2
    body = [top[0], top[1]]
    for i in range(n_mid):
        body.append([midS, mid, midT, mid][i % 4])
    body += legs + ["." * 24, "." * 24]
    tail = ["KWK", "KSK", "KWK", "KSK", "KWK"]
    start = len(body) - len(tail) - 1 - (0 if rows >= 18 else -1)
    for i, t in enumerate(tail):
        y = start + i
        if 0 <= y < len(body):
            r = list(body[y])
            for j, ch in enumerate(t):
                r[tail_x + j] = ch
            body[y] = "".join(r)
    if start + len(tail) < len(body):
        r = list(body[start + len(tail)])
        r[tail_x + 1] = "K"
        body[start + len(tail)] = "".join(r)
    return body


STAGES = [("A", A), ("T1", long_body(12, True)), ("T2", long_body(15, True)),
          ("T3", long_body(18, False)), ("D", long_body(21, False))]
# 每帧、每段 (肩/腰/腿) 的水平偏移: 头和拎点不动, 越往下越晚越大.
SMALL = [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0), (0, 0, 0), (0, 0, -1), (0, -1, -1), (0, -1, 0)]
BIG = [(0, 0, 0), (0, 1, 1), (1, 1, 2), (1, 2, 2), (0, 1, 1), (0, -1, -1), (-1, -1, -2), (-1, -2, -2)]


def head():
    """从 stand@half 抠头: 第 10-23 列, 第 5-17 行. 第 17 行混了身子, 只留脸的部分."""
    rows = [list(r[10:]) for r in SKIN["poses"]["stand@half"][5:18]]
    rows[12][0] = rows[12][1] = rows[11][0] = "."
    return rows


def compose_lift(body, offs):
    g = [["."] * 24 for _ in range(max(24, 13 + len(body)))]
    for i, row in enumerate(body):
        dx = offs[0] if i < 3 else offs[1] if i < 7 else offs[2]
        for c, ch in enumerate(row):
            if ch != "." and 0 <= c + dx < 24:
                g[13 + i][c + dx] = ch
    for y, row in enumerate(head()):
        for x, ch in enumerate(row):
            if ch != ".":
                g[y][5 + x] = ch
    return ["".join(r) for r in g]


def scruff():
    return {"palette": SKIN["palette"], "grip": [11, 3], "headMidCol": 12,
            "stages": [{"name": n, "small": [compose_lift(b, o) for o in SMALL],
                        "big": [compose_lift(b, o) for o in BIG]} for n, b in STAGES]}


# ---------- 抖毛: 头以鼻子为中心甩, 拖影 + 括号线 ----------
PAD, ANGLE = 8, 34
NX, NY = 13.5, 12.0
SIT = SKIN["poses"]["sit@happy"]
W, H = len(SIT[0]), len(SIT)
HEAD_ROWS = range(3, 14)
FEATURES = {(9, 11), (10, 10), (11, 10), (12, 11), (15, 11), (16, 10), (17, 10), (18, 11),
            (13, 12), (14, 12), (12, 13), (15, 13), (8, 12), (9, 12), (18, 12), (19, 12)}
HEAD = {(x, y): SIT[y][x] for y in HEAD_ROWS for x in range(W) if SIT[y][x] != "."}
for p in FEATURES & set(HEAD):
    HEAD[p] = "W"
BODY = {(x, y): SIT[y][x] for y in range(H) for x in range(W) if y not in HEAD_ROWS and SIT[y][x] != "."}
EYES = {1: {10.5: [(1, 1), (0, 1), (-1, 1), (1, 0), (1, -1)], 16.5: [(-1, -1), (0, -1), (1, -1), (-1, 0), (-1, 1)]},
        -1: {10.5: [(1, -1), (0, -1), (-1, -1), (1, 0), (1, 1)], 16.5: [(-1, 1), (0, 1), (1, 1), (-1, 0), (-1, -1)]}}
PAREN_L = [(1, 0), (0, 1), (0, 2), (0, 3), (1, 4)]
PAREN_R = [(0, 0), (1, 1), (1, 2), (1, 3), (0, 4)]
LINES = {-1: [(0, 3, PAREN_L), (-3, 8, PAREN_L), (25, 3, PAREN_R), (28, 8, PAREN_R)],
         1: [(-2, 4, PAREN_L), (1, 9, PAREN_L), (27, 4, PAREN_R), (24, 9, PAREN_R)]}
LINE = (150, 150, 165)


def rot(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a); out = {}
    for y in range(-PAD, H + PAD):
        for x in range(-PAD, W + PAD):
            dx, dy = x - NX, y - NY
            k = (round(c * dx + s * dy + NX), round(-s * dx + c * dy + NY))
            if k in HEAD:
                out[(x, y)] = PAL[HEAD[k]]
    return out


def face(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a); sg = 1 if deg > 0 else -1
    def R(x, y):
        dx, dy = x - NX, y - NY
        return round(c * dx - s * dy + NX), round(s * dx + c * dy + NY)
    out = {}
    for ex, shape in EYES[sg].items():
        x, y = R(ex, 10.5)
        for dx, dy in shape:
            out[(x + dx, y + dy)] = PAL["K"]
    for cx in (8.5, 18.5):
        x, y = R(cx, 12.2)
        for t in (0, 1):
            out[(x + t, y + t * sg)] = PAL["C"]
    x, y = R(13.5, 12); out[(x, y)] = PAL["P"]; out[(x + 1, y)] = PAL["P"]
    x, y = R(13.5, 13.4); out[(x, y)] = PAL["K"]
    return out


def shake_frame(sign):
    size = W + 2 * PAD
    buf = [[(0, 0, 0, 0)] * size for _ in range(size)]
    def over(pts, alpha):
        for (x, y), c in pts.items():
            X, Y = x + PAD, y + PAD
            if not (0 <= X < size and 0 <= Y < size):
                continue
            r0, g0, b0, a0 = buf[Y][X]
            a = alpha / 255; A = a + a0 / 255 * (1 - a)
            if A == 0:
                continue
            mix = lambda s, d: round((s * a + d * a0 / 255 * (1 - a)) / A)
            buf[Y][X] = (mix(c[0], r0), mix(c[1], g0), mix(c[2], b0), round(A * 255))
    deg = sign * ANGLE
    over({p: PAL[c] for p, c in BODY.items()}, 255)
    for k in range(4):                                  # 从另一边扫过来的弧形拖影
        over(rot(-deg + 2 * deg * k / 4), int(35 + 85 * k / 3))
    over(rot(deg), 255)
    over(face(deg), 255)
    for ox, oy, sh in LINES[sign]:
        over({(x + ox, y + oy): LINE for x, y in sh}, 255)
    return buf


def shake():
    size = W + 2 * PAD
    frames = ["".join("%02x%02x%02x%02x" % px for row in shake_frame(s) for px in row) for s in (-1, 1)]
    return {"pad": PAD, "width": size, "height": size, "frames": frames}


def preview(out, sc, sh):
    from PIL import Image
    S, bg = 10, (246, 244, 238, 255)
    def lift_im(rows):
        im = Image.new("RGBA", (24 * S, 34 * S), bg)
        for y, r in enumerate(rows):
            for x, ch in enumerate(r):
                if ch in PAL:
                    im.paste(PAL[ch] + (255,), (x * S, y * S, x * S + S, y * S + S))
        return im
    strip = Image.new("RGB", (24 * S * 5, 34 * S), bg[:3])
    for i, st in enumerate(sc["stages"]):
        strip.paste(lift_im(st["small"][0]).convert("RGB"), (i * 24 * S, 0))
    strip.save(f"{out}/lift-stages.png")
    fr = [lift_im(st["big"][k]).convert("RGB") for st in sc["stages"] for k in range(8)]
    fr[0].save(f"{out}/lift.gif", save_all=True, append_images=fr[1:], duration=120, loop=0)
    ims = []
    for hx in sh["frames"]:
        b = bytes.fromhex(hx)
        small = Image.frombytes("RGBA", (sh["width"], sh["height"]), b).resize((sh["width"] * S, sh["height"] * S), Image.NEAREST)
        im = Image.new("RGBA", small.size, bg); im.alpha_composite(small); ims.append(im.convert("RGB"))
    ims[0].save(f"{out}/shake.gif", save_all=True, append_images=ims[1:], duration=125, loop=0)
    s2 = Image.new("RGB", (ims[0].width * 2, ims[0].height)); s2.paste(ims[0], (0, 0)); s2.paste(ims[1], (ims[0].width, 0)); s2.save(f"{out}/shake-strip.png")
    open(f"{out}/index.html", "w").write("""<!doctype html><meta charset=utf-8><title>橘猫素材预览</title><style>body{background:#f6f4ee;font:16px sans-serif;margin:24px}h2{margin:18px 0 6px}img{image-rendering:pixelated;max-width:100%}</style>
<h2>甩干</h2><img src=shake.gif height=320> <img src=shake-strip.png width=640>
<h2>拎起: 坠成一条猫</h2><img src=lift.gif height=420> <img src=lift-stages.png width=900>""")


if __name__ == "__main__":
    sc, sh = scruff(), shake()
    skin = json.dumps(SKIN, separators=(",", ":"), sort_keys=True)
    with open(os.path.join(ROOT, "Sources/PerchCatCore/BuiltinSkin.swift"), "w", encoding="utf-8") as f:
        f.write("// 内置的原创橘猫 (scripts/build_assets.py 生成, 别手改). 用 --skin 或默认目录里的 pet-skin.json 可以换猫.\n")
        for name, data in (("builtinSkinJSON", skin), ("builtinScruffJSON", json.dumps(sc, separators=(",", ":"))),
                           ("builtinShakeJSON", json.dumps(sh, separators=(",", ":")))):
            f.write(f'public let {name} = #"""\n{data}\n"""#\n')
    if len(sys.argv) > 1:
        preview(sys.argv[1], sc, sh)
