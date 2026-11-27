"""Generate the contributor artwork from geometric shapes using the Python standard library."""
import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def chunk(kind, data):
    size = struct.pack(">I", len(data))
    checksum = struct.pack(">I", zlib.crc32(kind + data))
    return size + kind + data + checksum


def png(width, height, pixel):
    rows = bytearray()
    for y in range(height):
        rows.append(0)
        for x in range(width):
            rows.extend(pixel(x, y))
    header = struct.pack(">2I5B", width, height, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b""))


def save(path, data):
    dest = ROOT / path
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(data)


def landscape(width, height, phase):
    def pixel(x, y):
        u, v = x / width, y / height
        for level in range(3):
            ridge = 0.73 - level * 0.17 + 0.08 * math.cos(u * 9 + phase + level)
            if v > ridge:
                return (14 + level * 5, 32 + level * 9, 30 + level * 8, 255)
        return (46 + int(27 * v), 59 + int(19 * v), 62 + int(12 * v), 255)
    return png(width, height, pixel)


def badge(size, color):
    def pixel(x, y):
        dx, dy = abs((x + 0.5) / size - 0.5), abs((y + 0.5) / size - 0.5)
        if dx + dy > 0.48:
            return (0, 0, 0, 0)
        if 0.31 < dx + dy < 0.35 or dx + dy < 0.13:
            return (*color, 255)
        return (25, 39, 42, 255)
    return png(size, size, pixel)


def icon(color):
    sizes = (16, 32, 48, 64, 256)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(sizes)))
    images = bytearray()
    for size in sizes:
        image = badge(size, color)
        offset = 6 + 16 * len(sizes) + len(images)
        directory.extend(struct.pack("<4B2H2I", size % 256, size % 256, 0, 0, 1, 32, len(image), offset))
        images.extend(image)
    return directory + images


if __name__ == "__main__":
    assets = "src/RealmOfThronesHost/Assets/"
    save(assets + "rot-backdrop-ember-green.png", landscape(768, 432, 0))
    save(assets + "realm-hero.png", landscape(640, 320, 2))
    for index in range(1, 5):
        save(assets + f"realm-tile-{index}.png", landscape(256, 128, index))
    save(assets + "realm-emblem.png", badge(128, (217, 174, 97)))
    save("assets/icons/coodsz-rot.ico", icon((217, 174, 97)))
    save("assets/icons/coodsz-rot-client.ico", icon((124, 197, 165)))
