"""Generates src/InstaChat/app.ico - an Instagram-gradient chat-bubble icon.

Pure standard-library Python (no PIL): renders each size with 4x supersampling
and writes 32bpp BMP entries into a Windows .ico container.
"""

import struct
from pathlib import Path

SIZES = [16, 20, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling factor

# Instagram-ish gradient stops (r, g, b)
TOP = (131, 58, 180)     # purple
MID = (225, 48, 108)     # pink
BOT = (247, 119, 55)     # orange

WHITE = (255, 255, 255)


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(x, y, s):
    """Diagonal three-stop gradient: purple -> pink -> orange."""
    t = (x + y) / (2.0 * s)
    if t < 0.5:
        return lerp(TOP, MID, t * 2.0)
    return lerp(MID, BOT, (t - 0.5) * 2.0)


def inside_rounded_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    dx, dy = x - cx, y - cy
    return dx * dx + dy * dy <= r * r


def inside_circle(x, y, cx, cy, r):
    dx, dy = x - cx, y - cy
    return dx * dx + dy * dy <= r * r


def inside_tail(x, y, s):
    """Chat-bubble tail: a small triangle below the bubble, bottom-left."""
    ax, ay = 0.26 * s, 0.62 * s
    bx, by = 0.26 * s, 0.80 * s
    cx, cy = 0.46 * s, 0.62 * s
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-9:
        return False
    w1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d
    w2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d
    w3 = 1.0 - w1 - w2
    return w1 >= 0 and w2 >= 0 and w3 >= 0


def render(size):
    n = size * SS
    acc = bytearray(size * size * 4)

    bubble_x0, bubble_y0 = 0.17 * n, 0.20 * n
    bubble_x1, bubble_y1 = 0.83 * n, 0.66 * n
    bubble_r = 0.13 * n
    dot_r = 0.045 * n
    dot_y = 0.43 * n
    dot_xs = [0.36 * n, 0.50 * n, 0.64 * n]

    for py in range(size):
        for px in range(size):
            r = g = b = a = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = px * SS + sx + 0.5
                    y = py * SS + sy + 0.5
                    col = None
                    if inside_rounded_rect(x, y, 0, 0, n - 1, n - 1, 0.225 * n):
                        # background tile
                        col = gradient(x, y, n)
                        # white bubble + tail on top
                        if inside_rounded_rect(x, y, bubble_x0, bubble_y0, bubble_x1, bubble_y1, bubble_r) \
                           or inside_tail(x, y, n):
                            col = WHITE
                            for dx in dot_xs:
                                if inside_circle(x, y, dx, dot_y, dot_r):
                                    col = gradient(x, y, n)
                                    break
                    if col is not None:
                        r += col[0]
                        g += col[1]
                        b += col[2]
                        a += 255
            samples = SS * SS
            i = (py * size + px) * 4
            acc[i] = round(r / samples)
            acc[i + 1] = round(g / samples)
            acc[i + 2] = round(b / samples)
            acc[i + 3] = round(a / samples)
    return bytes(acc), size


def bmp_entry(rgba, size):
    """32bpp BITMAPINFOHEADER image + empty AND mask, bottom-up rows."""
    header = struct.pack(
        "<IiiHHIIiiII",
        40, size, size * 2, 1, 32, 0,
        size * size * 4 + (size * ((size + 31) // 32) * 4),
        0, 0, 0, 0,
    )
    stride = size * 4
    xor = bytearray(size * size * 4)
    for row in range(size):
        src = rgba[(size - 1 - row) * stride:(size - row) * stride]
        # BGRA
        for i in range(0, stride, 4):
            xor[row * stride + i] = src[i + 2]
            xor[row * stride + i + 1] = src[i + 1]
            xor[row * stride + i + 2] = src[i]
            xor[row * stride + i + 3] = src[i + 3]
    and_stride = ((size + 31) // 32) * 4
    and_mask = bytes(and_stride * size)  # alpha channel fully opaque/transparent per pixel
    return header + bytes(xor) + and_mask


def main():
    out = Path(__file__).resolve().parent.parent / "src" / "InstaChat" / "app.ico"
    out.parent.mkdir(parents=True, exist_ok=True)

    images = []
    for size in SIZES:
        rgba, _ = render(size)
        images.append((size, bmp_entry(rgba, size)))
        print(f"rendered {size}x{size}")

    count = len(images)
    data = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = b""
    body = b""
    for size, img in images:
        b = size if size < 256 else 0
        entries += struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(img), offset)
        body += img
        offset += len(img)

    out.write_bytes(data + entries + body)
    print(f"wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
