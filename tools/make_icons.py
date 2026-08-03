#!/usr/bin/env python3
"""Generates the application and tray icons.

The whole icon set is one glyph — the Bluetooth rune — drawn at several sizes in a
few colours. Rather than check in binaries nobody can edit, the .ico files are
rasterised here from the same 24x24 path the WPF UI uses, so a colour change is a
one-line edit followed by `python3 tools/make_icons.py`.

Anti-aliasing is done by distance field: coverage of a pixel is derived from its
distance to the nearest stroke segment, which gives round caps and joins for free.
No third-party imaging library is required.

Usage:  python3 tools/make_icons.py [output_dir]
"""

from __future__ import annotations

import math
import os
import struct
import sys
import zlib

# The Bluetooth rune as a polyline on the same 24x24 grid as Themes/Icons.xaml.
RUNE = [
    ((6.5, 6.5), (17.5, 17.5)),
    ((17.5, 17.5), (12.0, 23.0)),
    ((12.0, 23.0), (12.0, 1.0)),
    ((12.0, 1.0), (17.5, 6.5)),
    ((17.5, 6.5), (6.5, 17.5)),
]

SLASH = [((3.5, 20.5), (20.5, 3.5))]

GRID = 24.0
STROKE = 2.4          # in grid units
SIZES = [16, 20, 24, 32, 48, 64, 128, 256]
PNG_FROM = 128        # sizes >= this are stored as PNG entries inside the .ico

ACCENT = (0xD0, 0x8A, 0x2E)
IDLE = (0xC8, 0xC8, 0xD0)
OFF = (0x78, 0x78, 0x82)


def distance_to_segment(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> float:
    """Shortest distance from a point to a line segment."""
    dx, dy = bx - ax, by - ay
    length_squared = dx * dx + dy * dy
    if length_squared == 0.0:
        return math.hypot(px - ax, py - ay)
    t = ((px - ax) * dx + (py - ay) * dy) / length_squared
    t = max(0.0, min(1.0, t))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def render(size: int, colour: tuple[int, int, int], segments) -> bytearray:
    """Rasterises the segments into a top-down RGBA buffer."""
    scale = size / GRID
    half_width = (STROKE * scale) / 2.0
    red, green, blue = colour
    pixels = bytearray(size * size * 4)

    for y in range(size):
        # Sample at pixel centres, in grid coordinates.
        gy = (y + 0.5) / scale
        for x in range(size):
            gx = (x + 0.5) / scale
            nearest = min(
                distance_to_segment(gx, gy, ax, ay, bx, by)
                for (ax, ay), (bx, by) in segments
            )
            # Convert the distance to coverage across one pixel of the final raster.
            coverage = (half_width / scale - nearest) * scale + 0.5
            alpha = max(0.0, min(1.0, coverage))
            if alpha <= 0.0:
                continue
            offset = (y * size + x) * 4
            pixels[offset] = red
            pixels[offset + 1] = green
            pixels[offset + 2] = blue
            pixels[offset + 3] = int(alpha * 255 + 0.5)

    return pixels


def to_png(size: int, rgba: bytearray) -> bytes:
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)  # filter type: none
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def to_bmp(size: int, rgba: bytearray) -> bytes:
    """BITMAPINFOHEADER + bottom-up BGRA + an all-opaque AND mask."""
    header = struct.pack(
        "<IiiHHIIiiII",
        40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)

    body = bytearray()
    stride = size * 4
    for y in range(size - 1, -1, -1):
        row = rgba[y * stride:(y + 1) * stride]
        for x in range(0, stride, 4):
            body += bytes((row[x + 2], row[x + 1], row[x], row[x + 3]))

    mask_stride = ((size + 31) // 32) * 4
    mask = bytes(mask_stride * size)
    return header + bytes(body) + mask


def to_ico(colour: tuple[int, int, int], segments) -> bytes:
    images = []
    for size in SIZES:
        rgba = render(size, colour, segments)
        images.append((size, to_png(size, rgba) if size >= PNG_FROM else to_bmp(size, rgba)))

    directory = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = bytearray()
    payload = bytearray()

    for size, data in images:
        entries += struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,
            size if size < 256 else 0,
            0, 0, 1, 32, len(data), offset)
        payload += data
        offset += len(data)

    return directory + bytes(entries) + bytes(payload)


def main() -> int:
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "src", "BluetoothManagerPro", "Assets")
    target = os.path.abspath(target)
    os.makedirs(target, exist_ok=True)

    icons = {
        "app.ico": (ACCENT, RUNE),
        "tray-active.ico": (ACCENT, RUNE),
        "tray-idle.ico": (IDLE, RUNE),
        "tray-off.ico": (OFF, RUNE + SLASH),
    }

    for name, (colour, segments) in icons.items():
        path = os.path.join(target, name)
        with open(path, "wb") as handle:
            handle.write(to_ico(colour, segments))
        print(f"wrote {path} ({os.path.getsize(path)} bytes)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
