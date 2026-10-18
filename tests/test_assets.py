#!/usr/bin/env python3
"""Validate the native-size mature Feynman sprite set."""

from __future__ import annotations

import json
import struct
import zlib
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ASSET_ROOT = ROOT / "assets" / "feynman"
PROFILES = {
    "48x72": (48, 72),
    "64x96": (64, 96),
    "96x144": (96, 144),
}
STATE_COUNTS = {
    "idle": 6,
    "blink": 6,
    "walk": 8,
    "jump": 6,
    "sleep": 4,
    "think": 6,
    "explain": 6,
    "pet": 6,
    "click": 4,
}


def read_rgba(path: Path) -> tuple[int, int, bytes]:
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", f"not a PNG: {path}"
    offset = 8
    width = height = color_type = bit_depth = None
    compressed = bytearray()
    while offset < len(data):
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        kind = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        offset += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", payload[:10])
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break
    assert (bit_depth, color_type) == (8, 6), f"expected 8-bit RGBA: {path}"
    decompressed = zlib.decompress(bytes(compressed))
    stride = width * 4
    rows = []
    for y in range(height):
        start = y * (stride + 1)
        assert decompressed[start] == 0, f"unexpected PNG filter: {path}"
        rows.append(decompressed[start + 1 : start + 1 + stride])
    return width, height, b"".join(rows)


def bbox(pixels: bytes, width: int, height: int) -> tuple[int, int, int, int]:
    xs: list[int] = []
    ys: list[int] = []
    for y in range(height):
        row = pixels[y * width * 4 : (y + 1) * width * 4]
        for x in range(width):
            if row[x * 4 + 3] > 8:
                xs.append(x)
                ys.append(y)
    assert xs and ys
    return min(xs), min(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1


def main() -> None:
    manifest = json.loads((ASSET_ROOT / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["default_size"] == [64, 96]
    assert set(manifest["profiles"]) == set(PROFILES)
    total = 0
    for profile_name, (expected_width, expected_height) in PROFILES.items():
        states = manifest["profiles"][profile_name]["states"]
        assert set(states) == set(STATE_COUNTS)
        for state, expected_count in STATE_COUNTS.items():
            frames = states[state]["frames"]
            assert len(frames) == expected_count
            union_width = union_height = 0
            for relative in frames:
                path = ASSET_ROOT / relative
                width, height, pixels = read_rgba(path)
                assert (width, height) == (expected_width, expected_height)
                alpha = pixels[3::4]
                assert alpha[0] == 0 and alpha[-1] == 0
                assert min(alpha) == 0 and max(alpha) >= 240
                _, _, bw, bh = bbox(pixels, width, height)
                union_width = max(union_width, bw)
                union_height = max(union_height, bh)
                total += 1
            assert union_width / expected_width >= 0.48, (profile_name, state)
            assert union_height / expected_height >= 0.58, (profile_name, state)
    assert total == 156
    assert (ASSET_ROOT / "feynman.ico").exists()
    print("Asset test passed: 156 native RGBA frames across 3 profiles, 9 states.")


if __name__ == "__main__":
    main()
