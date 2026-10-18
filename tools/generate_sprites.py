#!/usr/bin/env python3
"""Generate mature-proportion Feynman desktop-pet pixel art.

No external image libraries are used.  Every frame is drawn as native RGBA
pixels at each requested display size, so the small default sprite stays crisp
instead of being a large illustration squeezed down by the window manager.
"""

from __future__ import annotations

import json
import math
import struct
import zlib
from pathlib import Path
from typing import Iterable, Sequence


ROOT = Path(__file__).resolve().parents[1]
ASSET_ROOT = ROOT / "assets" / "feynman"
PROFILES = ((48, 72), (64, 96), (96, 144))
DESIGN_W, DESIGN_H = 64, 96


def rgba(value: str) -> tuple[int, int, int, int]:
    value = value.lstrip("#")
    if len(value) == 6:
        value += "FF"
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4, 6))


INK = rgba("#171315")
INK_SOFT = rgba("#2B2323")
HAIR_DARK = rgba("#211A1C")
HAIR = rgba("#342625")
HAIR_MID = rgba("#4A3531")
HAIR_LIGHT = rgba("#715147")
HAIR_WARM = rgba("#8A6250")
SKIN = rgba("#D9A77F")
SKIN_LIGHT = rgba("#F0C69C")
SKIN_HIGHLIGHT = rgba("#F8D7B0")
SKIN_SHADOW = rgba("#B87859")
SKIN_DEEP = rgba("#95583F")
SHIRT = rgba("#E9ECE7")
SHIRT_LIGHT = rgba("#FAFBF7")
SHIRT_SHADOW = rgba("#BFC5C1")
SHIRT_DEEP = rgba("#8F9895")
PANTS = rgba("#2B2D35")
PANTS_LIGHT = rgba("#444854")
PANTS_DARK = rgba("#1B1D23")
SHOE = rgba("#17191D")
SHOE_LIGHT = rgba("#343842")
WHITE = rgba("#FFFDF5")
EYE_WHITE = rgba("#F7EBDD")
IRIS = rgba("#3E2C24")
BLUSH = rgba("#C67C74")
CREAM = rgba("#FFF1C5")
GOLD = rgba("#E6B84D")
GOLD_LIGHT = rgba("#FFE294")
TEAL = rgba("#6EA2A2")
TEAL_LIGHT = rgba("#B4D4CF")
ROSE = rgba("#C85F69")
ROSE_LIGHT = rgba("#F19AA0")
DUST = rgba("#C5BFB2")
DUST_LIGHT = rgba("#E4DED2")


class Canvas:
    def __init__(self, width: int, height: int) -> None:
        self.width = width
        self.height = height
        self.pixels = bytearray(width * height * 4)

    def blend(self, x: int, y: int, color: tuple[int, int, int, int]) -> None:
        if x < 0 or y < 0 or x >= self.width or y >= self.height:
            return
        r, g, b, a = color
        if a == 0:
            return
        index = (y * self.width + x) * 4
        if a == 255:
            self.pixels[index : index + 4] = bytes((r, g, b, 255))
            return
        dr, dg, db, da = self.pixels[index : index + 4]
        sa = a / 255.0
        oa = sa + (da / 255.0) * (1.0 - sa)
        if oa <= 0:
            return
        self.pixels[index : index + 4] = bytes(
            (
                round((r * sa + dr * (da / 255.0) * (1.0 - sa)) / oa),
                round((g * sa + dg * (da / 255.0) * (1.0 - sa)) / oa),
                round((b * sa + db * (da / 255.0) * (1.0 - sa)) / oa),
                round(oa * 255),
            )
        )

    def rect(
        self, x: float, y: float, width: float, height: float, color: tuple[int, int, int, int]
    ) -> None:
        x0, y0 = max(0, math.floor(x)), max(0, math.floor(y))
        x1, y1 = min(self.width, math.ceil(x + width)), min(self.height, math.ceil(y + height))
        for py in range(y0, y1):
            for px in range(x0, x1):
                self.blend(px, py, color)

    def ellipse(
        self,
        cx: float,
        cy: float,
        rx: float,
        ry: float,
        color: tuple[int, int, int, int],
    ) -> None:
        if rx <= 0 or ry <= 0:
            return
        x0 = max(0, math.floor(cx - rx))
        x1 = min(self.width - 1, math.ceil(cx + rx))
        y0 = max(0, math.floor(cy - ry))
        y1 = min(self.height - 1, math.ceil(cy + ry))
        for y in range(y0, y1 + 1):
            ny = (y + 0.5 - cy) / ry
            if abs(ny) > 1:
                continue
            span = rx * math.sqrt(max(0.0, 1.0 - ny * ny))
            for x in range(max(x0, math.ceil(cx - span)), min(x1, math.floor(cx + span)) + 1):
                self.blend(x, y, color)

    def line(
        self,
        points: Sequence[tuple[float, float]],
        color: tuple[int, int, int, int],
        width: int = 1,
    ) -> None:
        if not points:
            return
        radius = max(0, width // 2)
        for a, b in zip(points, points[1:]):
            dx, dy = b[0] - a[0], b[1] - a[1]
            steps = max(1, int(max(abs(dx), abs(dy)) * 2))
            for index in range(steps + 1):
                t = index / steps
                x = round(a[0] + dx * t)
                y = round(a[1] + dy * t)
                if radius <= 0:
                    self.blend(x, y, color)
                else:
                    self.ellipse(x, y, radius, radius, color)

    def polygon(
        self, points: Sequence[tuple[float, float]], color: tuple[int, int, int, int]
    ) -> None:
        if len(points) < 3:
            return
        min_y = max(0, math.floor(min(y for _, y in points)))
        max_y = min(self.height - 1, math.ceil(max(y for _, y in points)))
        for y in range(min_y, max_y + 1):
            scan = y + 0.5
            hits: list[float] = []
            for a, b in zip(points, points[1:] + points[:1]):
                if (a[1] <= scan < b[1]) or (b[1] <= scan < a[1]):
                    hits.append(a[0] + (b[0] - a[0]) * (scan - a[1]) / (b[1] - a[1]))
            hits.sort()
            for start, end in zip(hits[::2], hits[1::2]):
                for x in range(
                    max(0, math.ceil(start)),
                    min(self.width - 1, math.floor(end)) + 1,
                ):
                    self.blend(x, y, color)

    def crop(self, x: int, y: int, width: int, height: int) -> "Canvas":
        result = Canvas(width, height)
        for row in range(height):
            src = ((y + row) * self.width + x) * 4
            dst = row * width * 4
            result.pixels[dst : dst + width * 4] = self.pixels[src : src + width * 4]
        return result

    def resize_nearest(self, width: int, height: int) -> "Canvas":
        result = Canvas(width, height)
        for y in range(height):
            sy = min(self.height - 1, y * self.height // height)
            for x in range(width):
                sx = min(self.width - 1, x * self.width // width)
                si = (sy * self.width + sx) * 4
                di = (y * width + x) * 4
                result.pixels[di : di + 4] = self.pixels[si : si + 4]
        return result

    def png_bytes(self) -> bytes:
        raw = bytearray()
        stride = self.width * 4
        for y in range(self.height):
            raw.append(0)
            raw.extend(self.pixels[y * stride : (y + 1) * stride])

        def chunk(kind: bytes, payload: bytes) -> bytes:
            body = kind + payload
            return (
                struct.pack(">I", len(payload))
                + body
                + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
            )

        return (
            b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b"")
        )

    def write_png(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.png_bytes())


class P:
    """Normalized painter: design coordinates are always 64 x 96."""

    def __init__(self, canvas: Canvas) -> None:
        self.canvas = canvas
        self.sx = canvas.width / DESIGN_W
        self.sy = canvas.height / DESIGN_H

    def x(self, value: float) -> float:
        return value * self.sx

    def y(self, value: float) -> float:
        return value * self.sy

    def pt(self, point: tuple[float, float]) -> tuple[float, float]:
        return self.x(point[0]), self.y(point[1])

    def rect(
        self, x: float, y: float, width: float, height: float, color: tuple[int, int, int, int]
    ) -> None:
        self.canvas.rect(
            round(self.x(x)),
            round(self.y(y)),
            max(1, round(width * self.sx)),
            max(1, round(height * self.sy)),
            color,
        )

    def ellipse(
        self,
        cx: float,
        cy: float,
        rx: float,
        ry: float,
        color: tuple[int, int, int, int],
    ) -> None:
        self.canvas.ellipse(
            self.x(cx),
            self.y(cy),
            max(0.55, rx * self.sx),
            max(0.55, ry * self.sy),
            color,
        )

    def outlined_ellipse(
        self,
        cx: float,
        cy: float,
        rx: float,
        ry: float,
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] = INK,
    ) -> None:
        self.canvas.ellipse(
            self.x(cx),
            self.y(cy),
            max(0.8, rx * self.sx + 1),
            max(0.8, ry * self.sy + 1),
            outline,
        )
        self.ellipse(cx, cy, rx, ry, fill)

    def line(
        self,
        points: Sequence[tuple[float, float]],
        color: tuple[int, int, int, int],
        width: float = 1,
        scaled: bool = True,
    ) -> None:
        actual = max(1, round(width * min(self.sx, self.sy))) if scaled else max(1, round(width))
        self.canvas.line([self.pt(point) for point in points], color, actual)

    def polygon(
        self, points: Sequence[tuple[float, float]], color: tuple[int, int, int, int]
    ) -> None:
        self.canvas.polygon([self.pt(point) for point in points], color)

    def outlined_polygon(
        self,
        points: Sequence[tuple[float, float]],
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] = INK,
    ) -> None:
        self.polygon(points, fill)
        self.line(list(points) + [points[0]], outline, 1, False)

    def cloud(
        self,
        circles: Iterable[tuple[float, float, float]],
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] = INK,
    ) -> None:
        circles = list(circles)
        for cx, cy, radius in circles:
            self.ellipse(cx, cy, radius + 1 / self.sx, radius + 1 / self.sy, outline)
        for cx, cy, radius in circles:
            self.ellipse(cx, cy, radius, radius, fill)


def draw_limb(
    p: P,
    points: Sequence[tuple[float, float]],
    width: float,
    color: tuple[int, int, int, int],
    outline: tuple[int, int, int, int] = INK,
) -> None:
    p.line(points, outline, width + 1.4)
    p.line(points, color, width)


def draw_hand(
    p: P,
    x: float,
    y: float,
    skin: tuple[int, int, int, int] = SKIN,
    size: float = 2.0,
) -> None:
    p.outlined_ellipse(x, y, size, size * 0.9, skin)
    p.rect(x - 0.8, y - 1.0, 1.2, 1.0, SKIN_LIGHT)


def draw_hair(
    p: P,
    cx: float,
    cy: float,
    wind: float = 0.0,
) -> None:
    lobes = [
        (cx - 13.0, cy - 3.0, 4.7),
        (cx - 12.2, cy - 8.0, 4.8),
        (cx - 8.2, cy - 12.0, 5.0),
        (cx - 2.0, cy - 14.2, 5.3),
        (cx + 4.8, cy - 13.5, 5.2),
        (cx + 10.2, cy - 9.0, 4.9),
        (cx + 13.0, cy - 3.0, 4.5),
        (cx + 12.5, cy + 3.0, 4.1),
        (cx - 12.6, cy + 3.0, 4.0),
        (cx - 9.2, cy + 8.0, 3.7),
        (cx + 9.0, cy + 8.0, 3.7),
    ]
    lobes = [
        (
            cx + (x - cx) * 0.86 + wind * (0.7 if x > cx else -0.2),
            y,
            r * 0.93,
        )
        for x, y, r in lobes
    ]
    p.cloud(lobes, HAIR, HAIR_DARK)

    for x, y, radius in lobes:
        if radius >= 4.2:
            p.line(
                [(x - radius * 0.5, y + radius * 0.15), (x + radius * 0.1, y + radius * 0.55)],
                HAIR_MID,
                1,
                False,
            )
    for x, y, radius in lobes[:7]:
        p.line(
            [(x - radius * 0.55, y - radius * 0.3), (x - radius * 0.12, y - radius * 0.65)],
            HAIR_LIGHT,
            1,
            False,
        )
        p.line(
            [(x + radius * 0.15, y - radius * 0.65), (x + radius * 0.48, y - radius * 0.25)],
            HAIR_WARM,
            1,
            False,
        )


def draw_eyes(
    p: P,
    cx: float,
    cy: float,
    expression: str = "open",
    look: float = 0.0,
    brow: str = "confident",
) -> None:
    eye_y = cy + 1.7
    brow_y = cy - 3.6
    for side in (-1, 1):
        ex = cx + side * 3.9
        if expression in ("open", "wide"):
            rx = 1.85 if expression == "open" else 2.15
            ry = 1.35 if expression == "open" else 1.7
            p.outlined_ellipse(ex, eye_y, rx, ry, EYE_WHITE, INK)
            px = ex + look * 0.45
            p.ellipse(px, eye_y + 0.15, 0.82, 1.15, IRIS)
            p.rect(round(px - 0.3), round(eye_y - 0.7), 1, 1, WHITE)
        elif expression == "half":
            p.line([(ex - 2.0, eye_y), (ex + 2.0, eye_y - 0.2)], INK, 1, False)
            p.line([(ex - 1.6, eye_y - 0.8), (ex + 1.3, eye_y - 0.4)], SKIN_SHADOW, 1, False)
        elif expression == "closed":
            p.line([(ex - 2.1, eye_y), (ex + 2.1, eye_y)], INK, 1, False)
        elif expression == "happy":
            p.line([(ex - 2.0, eye_y + 0.7), (ex, eye_y - 0.8), (ex + 2.0, eye_y + 0.7)], INK, 1, False)
        elif expression == "sleep":
            p.line([(ex - 2.1, eye_y), (ex, eye_y + 0.8), (ex + 2.1, eye_y)], INK, 1, False)

    if brow == "raised":
        p.line([(cx - 6.0, brow_y), (cx - 2.6, brow_y - 1.0)], HAIR_DARK, 1, False)
        p.line([(cx + 2.5, brow_y - 1.0), (cx + 5.8, brow_y + 0.2)], HAIR_DARK, 1, False)
    elif brow == "curious":
        p.line([(cx - 6.2, brow_y + 0.3), (cx - 2.5, brow_y - 0.9)], HAIR_DARK, 1, False)
        p.line([(cx + 2.3, brow_y - 0.4), (cx + 6.0, brow_y - 1.4)], HAIR_DARK, 1, False)
    elif brow == "focused":
        p.line([(cx - 6.1, brow_y - 0.5), (cx - 2.2, brow_y + 0.5)], HAIR_DARK, 1, False)
        p.line([(cx + 2.2, brow_y + 0.5), (cx + 6.2, brow_y - 0.6)], HAIR_DARK, 1, False)
    else:
        p.line([(cx - 6.0, brow_y + 0.1), (cx - 2.3, brow_y - 0.7)], HAIR_DARK, 1, False)
        p.line([(cx + 2.3, brow_y - 0.7), (cx + 6.0, brow_y + 0.1)], HAIR_DARK, 1, False)


def draw_head(
    p: P,
    cx: float,
    cy: float,
    expression: str = "open",
    mouth: str = "smirk",
    brow: str = "confident",
    look: float = 0.0,
    wind: float = 0.0,
    blush: bool = False,
) -> None:
    draw_hair(p, cx, cy, wind)
    p.outlined_ellipse(cx - 8.9, cy + 1.5, 2.0, 3.0, SKIN_SHADOW, INK_SOFT)
    p.outlined_ellipse(cx + 8.9, cy + 1.5, 2.0, 3.0, SKIN_SHADOW, INK_SOFT)
    p.outlined_ellipse(cx, cy + 0.7, 8.7, 10.0, SKIN, INK_SOFT)

    # Narrow cheek shading and jaw line keep the face adult rather than circular.
    p.polygon(
        [
            (cx + 6.4, cy - 5.0),
            (cx + 8.2, cy - 1.0),
            (cx + 7.7, cy + 5.5),
            (cx + 5.2, cy + 8.7),
            (cx + 3.6, cy + 8.5),
            (cx + 6.0, cy + 3.5),
            (cx + 6.6, cy - 1.0),
        ],
        SKIN_SHADOW,
    )
    p.line([(cx - 4.8, cy + 9.1), (cx + 2.7, cy + 9.0)], SKIN_DEEP, 1, False)

    fringe = [
        (cx - 6.4, cy - 6.7, 3.2),
        (cx - 2.5, cy - 8.7, 3.4),
        (cx + 1.6, cy - 8.9, 3.2),
        (cx + 5.4, cy - 7.3, 3.2),
        (cx - 7.7, cy - 1.0, 2.4),
        (cx + 7.8, cy - 0.5, 2.5),
    ]
    p.cloud(fringe, HAIR, HAIR_DARK)
    p.line([(cx - 4.0, cy - 10.2), (cx - 6.2, cy - 8.3), (cx - 5.6, cy - 5.8)], HAIR_LIGHT, 1, False)
    p.line([(cx + 1.5, cy - 11.2), (cx + 3.6, cy - 9.2), (cx + 3.0, cy - 7.2)], HAIR_WARM, 1, False)
    p.line([(cx + 6.5, cy - 9.4), (cx + 8.0, cy - 6.5)], HAIR_LIGHT, 1, False)

    draw_eyes(p, cx, cy, expression, look, brow)
    p.line([(cx + 0.4, cy + 3.8), (cx - 0.2, cy + 5.1), (cx + 1.2, cy + 5.1)], SKIN_DEEP, 1, False)

    if mouth == "smirk":
        p.line([(cx - 2.8, cy + 7.4), (cx + 1.8, cy + 7.1), (cx + 3.0, cy + 6.4)], INK_SOFT, 1, False)
        p.rect(round(cx + 1.0), round(cy + 7.3), 1, 1, WHITE)
    elif mouth == "smile":
        p.line([(cx - 2.6, cy + 6.8), (cx, cy + 8.1), (cx + 2.8, cy + 6.8)], INK_SOFT, 1, False)
    elif mouth == "talk":
        p.outlined_ellipse(cx + 0.5, cy + 7.0, 1.6, 1.25, INK, INK_SOFT)
        p.rect(round(cx), round(cy + 7.0), 1, 1, ROSE)
    elif mouth == "o":
        p.outlined_ellipse(cx + 0.4, cy + 7.0, 1.15, 1.3, INK, INK_SOFT)
    elif mouth == "sleep":
        p.line([(cx - 2.0, cy + 7.5), (cx + 0.1, cy + 8.2), (cx + 2.2, cy + 7.2)], INK_SOFT, 1, False)
    elif mouth == "grin":
        p.polygon([(cx - 3.0, cy + 6.2), (cx + 3.2, cy + 6.2), (cx + 1.4, cy + 8.3), (cx - 1.5, cy + 8.2)], INK_SOFT)
        p.rect(round(cx - 1.5), round(cy + 7.0), 3, 1, WHITE)

    if blush:
        p.rect(round(cx - 6.4), round(cy + 4.8), 2, 1, BLUSH)
        p.rect(round(cx + 4.8), round(cy + 4.8), 2, 1, BLUSH)


def draw_torso(
    p: P,
    cx: float,
    top: float,
) -> None:
    points = [
        (cx - 11.0, top + 3.0),
        (cx - 5.0, top + 0.5),
        (cx - 2.0, top + 2.6),
        (cx + 2.0, top + 2.6),
        (cx + 5.0, top + 0.5),
        (cx + 11.0, top + 3.0),
        (cx + 8.6, top + 22.0),
        (cx - 8.6, top + 22.0),
    ]
    p.outlined_polygon(points, SHIRT, INK_SOFT)
    p.line([(cx - 8.4, top + 5.0), (cx - 7.1, top + 20.0)], SHIRT_SHADOW, 1, False)
    p.line([(cx + 8.1, top + 4.2), (cx + 6.8, top + 19.5)], SHIRT_LIGHT, 1, False)
    p.polygon([(cx - 5.2, top + 0.7), (cx - 1.6, top + 3.3), (cx, top + 8.0)], SHIRT_LIGHT)
    p.polygon([(cx + 5.2, top + 0.7), (cx + 1.6, top + 3.3), (cx, top + 8.0)], SHIRT_LIGHT)
    p.line([(cx - 5.2, top + 0.7), (cx, top + 8.0), (cx + 5.2, top + 0.7)], INK_SOFT, 1, False)
    p.rect(round(cx - 6.1), round(top + 7.0), 3.7, 4.0, SHIRT_SHADOW)
    p.line(
        [
            (cx - 6.1, top + 7.0),
            (cx - 2.4, top + 7.0),
            (cx - 2.4, top + 11.0),
        ],
        SHIRT_DEEP,
        1,
        False,
    )
    p.line([(cx - 5.2, top + 7.0), (cx - 5.2, top + 5.3)], TEAL, 1, False)
    p.line([(cx - 4.0, top + 7.0), (cx - 4.0, top + 5.7)], GOLD, 1, False)
    p.rect(round(cx), round(top + 11.0), 1, 1, INK_SOFT)
    p.rect(round(cx), round(top + 16.0), 1, 1, INK_SOFT)
    p.line([(cx - 7.7, top + 21.2), (cx + 7.7, top + 21.2)], PANTS_LIGHT, 1, False)


def draw_legs(
    p: P,
    cx: float,
    hip_y: float,
    pose: str,
    phase: int,
) -> None:
    if pose == "walk":
        swing = (-4.0, -2.6, -0.6, 2.3, 4.0, 2.5, 0.6, -2.4)[phase % 8]
        left = (cx - 4.0 + swing, 88.0 - (1 if phase in (2, 3) else 0))
        right = (cx + 4.0 - swing, 88.0 - (1 if phase in (6, 7) else 0))
    elif pose == "jump":
        left = (cx - 5.3, 85.0)
        right = (cx + 5.3, 85.0)
    elif pose == "crouch":
        left = (cx - 6.0, 88.0)
        right = (cx + 6.0, 88.0)
    else:
        left = (cx - 4.3, 90.0)
        right = (cx + 4.3, 90.0)

    for side, foot in ((-1, left), (1, right)):
        hip = (cx + side * 4.0, hip_y)
        if pose == "walk":
            knee = (cx + side * 4.0 + (foot[0] - (cx + side * 4.0)) * 0.28, 73.5)
        elif pose == "jump":
            knee = (cx + side * 5.0, 67.0)
        elif pose == "crouch":
            knee = (cx + side * 6.4, 71.5)
        else:
            knee = (cx + side * 4.3, 73.0)
        draw_limb(p, [hip, knee, foot], 4.0, PANTS)
        p.line([hip, knee], PANTS_LIGHT, 1, False)

        shoe = [
            (foot[0] - 3.6, foot[1] - 1.2),
            (foot[0] + 2.8, foot[1] - 1.2),
            (foot[0] + 4.6, foot[1] + 1.4),
            (foot[0] + 3.0, foot[1] + 3.3),
            (foot[0] - 4.3, foot[1] + 3.3),
        ]
        p.outlined_polygon(shoe, SHOE, INK)
        p.line(
            [(foot[0] - 2.7, foot[1] + 1.0), (foot[0] + 2.6, foot[1] + 1.0)],
            SHOE_LIGHT,
            1,
            False,
        )


def draw_arm(
    p: P,
    shoulder: tuple[float, float],
    elbow: tuple[float, float],
    wrist: tuple[float, float],
    hand_size: float = 2.0,
) -> None:
    draw_limb(p, [shoulder, elbow, wrist], 3.1, SHIRT)
    p.line([elbow, wrist], SHIRT_SHADOW, 1, False)
    draw_hand(p, wrist[0], wrist[1], size=hand_size)


def draw_full_character(
    p: P,
    *,
    upper_dy: float = 0.0,
    head_dx: float = 0.0,
    head_extra_y: float = 0.0,
    expression: str = "open",
    mouth: str = "smirk",
    brow: str = "confident",
    look: float = 0.0,
    pose: str = "idle",
    phase: int = 0,
    wind: float = 0.0,
    blush: bool = False,
) -> None:
    cx = 32.0
    top = 38.0 + upper_dy
    head_y = 23.5 + upper_dy + head_extra_y

    if pose == "walk":
        arm_swing = (2.8, 1.8, 0.4, -1.8, -2.8, -1.7, -0.3, 1.7)[phase % 8]
        draw_arm(p, (cx - 10.8, top + 3.0), (cx - 12.5 + arm_swing, top + 12.0), (cx - 11.3 + arm_swing, top + 19.5))
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 12.5 - arm_swing, top + 12.0), (cx + 11.3 - arm_swing, top + 19.5))
    elif pose == "jump":
        draw_arm(p, (cx - 10.8, top + 3.0), (cx - 14.6, top + 7.0), (cx - 15.7, top - 1.0))
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 14.6, top + 7.0), (cx + 15.7, top - 1.0))
    elif pose == "crouch":
        draw_arm(p, (cx - 10.8, top + 3.0), (cx - 13.3, top + 14.0), (cx - 10.8, top + 20.0))
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 13.3, top + 14.0), (cx + 10.8, top + 20.0))
    elif pose == "click":
        draw_arm(p, (cx - 10.8, top + 3.0), (cx - 14.4, top + 9.0), (cx - 18.0, top + 8.0))
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 14.4, top + 9.0), (cx + 18.0, top + 8.0))
    elif pose == "pocket":
        draw_arm(p, (cx - 10.8, top + 3.0), (cx - 12.0, top + 12.5), (cx - 7.4, top + 16.5), 1.8)
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 11.8, top + 13.0), (cx + 10.8, top + 20.5), 1.8)
    else:
        draw_arm(p, (cx - 10.8, top + 3.0), (cx - 12.2, top + 13.0), (cx - 11.2, top + 20.5), 1.8)
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 12.2, top + 13.0), (cx + 11.2, top + 20.5), 1.8)

    draw_legs(p, cx, top + 22.0, "crouch" if pose == "crouch" else ("jump" if pose == "jump" else ("walk" if pose == "walk" else "stand")), phase)
    p.rect(round(cx - 3.0), round(head_y + 9.0), 6, 6, SKIN_SHADOW)
    draw_torso(p, cx, top)
    draw_head(
        p,
        cx + head_dx,
        head_y,
        expression,
        mouth,
        brow,
        look,
        wind,
        blush,
    )

    # Action-specific foreground arm / hand.
    if pose == "think":
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 13.8, top + 13.0), (cx + 7.0, head_y + 7.5), 1.8)
    elif pose == "explain":
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 15.4, top + 10.0), (cx + 21.0, top + 7.0), 1.8)
    elif pose == "pet":
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 13.7, top + 12.0), (cx + 5.2, top + 17.0), 1.8)
    elif pose == "sleep":
        draw_arm(p, (cx + 10.8, top + 3.0), (cx + 12.8, top + 13.0), (cx + 4.0, top + 18.0), 1.8)


def draw_thought_prop(p: P, phase: int) -> None:
    x, y = 53.0, 12.0
    p.cloud(
        [(x - 4.0, y + 1.0, 3.4), (x, y - 2.0, 4.6), (x + 4.0, y + 1.0, 3.2), (x, y + 3.2, 3.3)],
        WHITE,
        INK_SOFT,
    )
    p.ellipse(x, y, 1.2, 1.2, GOLD)
    for angle in (15, 75, 135):
        theta = math.radians(angle + phase * 16)
        points = []
        for i in range(14):
            t = math.tau * i / 13
            px, py = math.cos(t) * 4.4, math.sin(t) * 1.7
            points.append(
                (
                    x + px * math.cos(theta) - py * math.sin(theta),
                    y + px * math.sin(theta) + py * math.cos(theta),
                )
            )
        p.line(points, TEAL, 0.65, False)
    if phase in (2, 3):
        p.rect(round(x + 2), round(y - 4), 1, 1, GOLD_LIGHT)
        p.rect(round(x - 4), round(y + 3), 1, 1, GOLD_LIGHT)


def draw_atom_spark(p: P, x: float, y: float, phase: int) -> None:
    p.ellipse(x, y, 1.0, 1.0, GOLD_LIGHT)
    for angle in (20, 80, 140):
        theta = math.radians(angle + phase * 14)
        points = []
        for i in range(12):
            t = math.tau * i / 11
            px, py = math.cos(t) * 4.0, math.sin(t) * 1.5
            points.append(
                (
                    x + px * math.cos(theta) - py * math.sin(theta),
                    y + px * math.sin(theta) + py * math.cos(theta),
                )
            )
        p.line(points, TEAL, 0.65, False)


def draw_heart(p: P, x: float, y: float, size: float = 1.0) -> None:
    p.ellipse(x - 1.4 * size, y, 1.5 * size, 1.5 * size, INK)
    p.ellipse(x + 1.4 * size, y, 1.5 * size, 1.5 * size, INK)
    p.polygon(
        [
            (x - 2.8 * size, y + 0.5 * size),
            (x + 2.8 * size, y + 0.5 * size),
            (x, y + 4.0 * size),
        ],
        INK,
    )
    p.ellipse(x - 1.1 * size, y, 1.0 * size, 1.0 * size, ROSE)
    p.ellipse(x + 1.1 * size, y, 1.0 * size, 1.0 * size, ROSE)
    p.polygon(
        [
            (x - 2.0 * size, y + 0.3 * size),
            (x + 2.0 * size, y + 0.3 * size),
            (x, y + 3.0 * size),
        ],
        ROSE,
    )
    p.rect(round(x - 1.2 * size), round(y - 0.6 * size), 1, 1, ROSE_LIGHT)


def draw_z(p: P, x: float, y: float, scale: int = 1, color: tuple[int, int, int, int] = TEAL) -> None:
    p.line([(x, y), (x + 4 * scale, y), (x, y + 5 * scale), (x + 4 * scale, y + 5 * scale)], color, 1, False)


def draw_sleeping(p: P, phase: int) -> None:
    breath = (0, 1, 0)[phase % 3]
    # Folded legs, crossed forearms and a drooping head form a compact seated nap.
    draw_limb(p, [(28, 75), (22, 82), (20, 90)], 4.4, PANTS)
    draw_limb(p, [(36, 75), (42, 82), (44, 90)], 4.4, PANTS)
    p.outlined_polygon([(17, 88), (23, 87), (26, 92), (15, 92)], SHOE, INK)
    p.outlined_polygon([(42, 87), (48, 88), (50, 92), (39, 92)], SHOE, INK)
    p.rect(29, 67, 8, 10, SKIN_SHADOW)
    draw_torso(p, 32, 56 + breath)
    draw_arm(p, (23, 60 + breath), (21, 69 + breath), (31, 74 + breath), 1.8)
    draw_arm(p, (41, 60 + breath), (43, 69 + breath), (34, 74 + breath), 1.8)
    draw_head(p, 33, 43 + breath, "sleep", "sleep", "confident", -0.6, 0, True)
    draw_z(p, 50, 22, 1)
    if phase >= 1:
        draw_z(p, 54, 15, 1)
    if phase >= 2:
        draw_z(p, 49, 8, 1)
    if phase == 3:
        p.rect(58, 29, 1, 1, TEAL_LIGHT)
    p.line([(24, 85), (31, 83), (39, 85)], PANTS_LIGHT, 1, False)


def render_state(state: str, index: int, width: int, height: int) -> Canvas:
    canvas = Canvas(width, height)
    p = P(canvas)

    if state == "idle":
        bob = (0, 0, 1, 1, 0, 0)[index]
        look = (-0.6, 0.0, 0.7, 0.3, -0.3, 0.0)[index]
        draw_full_character(
            p,
            upper_dy=bob,
            head_extra_y=-bob * 0.3,
            expression="open",
            mouth="smirk",
            brow="confident",
            look=look,
            pose="pocket",
            wind=(0, 0.4, 0.8, 0.2, -0.2, 0)[index],
        )
        if index == 3:
            p.rect(48, 32, 1, 1, GOLD)
            p.rect(49, 31, 1, 1, GOLD_LIGHT)

    elif state == "blink":
        expressions = ("open", "half", "closed", "closed", "half", "open")
        draw_full_character(
            p,
            expression=expressions[index],
            mouth="smirk",
            brow="confident",
            look=0.2,
            pose="pocket",
        )

    elif state == "walk":
        bob = (0, 0, 1, 0, 0, 0, 1, 0)[index]
        draw_full_character(
            p,
            upper_dy=bob,
            expression="open",
            mouth="smile" if index not in (1, 5) else "smirk",
            brow="confident",
            look=1.0,
            pose="walk",
            phase=index,
            wind=0.8,
        )
        if index in (2, 6):
            p.rect(17 if index == 2 else 45, 89, 2, 1, DUST)
            p.rect(13 if index == 2 else 48, 88, 1, 1, DUST_LIGHT)

    elif state == "jump":
        frames = (
            (2.0, 0.0, "crouch", "open", "o", "focused"),
            (-0.5, -0.5, "jump", "wide", "grin", "raised"),
            (-3.0, -1.0, "jump", "happy", "grin", "raised"),
            (-2.0, -0.5, "jump", "wide", "o", "raised"),
            (1.0, 0.0, "crouch", "open", "smile", "focused"),
            (0.0, 0.0, "pocket", "open", "smirk", "confident"),
        )
        dy, head_extra, pose, expression, mouth, brow = frames[index]
        draw_full_character(
            p,
            upper_dy=dy,
            head_extra_y=head_extra,
            expression=expression,
            mouth=mouth,
            brow=brow,
            pose=pose,
            phase=index,
            wind=0.6,
        )
        if index in (1, 2, 3):
            p.line([(19, 86), (14, 89)], DUST, 1, False)
            p.line([(45, 86), (50, 89)], DUST, 1, False)
            p.rect(10, 85, 1, 1, DUST_LIGHT)
            p.rect(53, 85, 1, 1, DUST_LIGHT)

    elif state == "sleep":
        draw_sleeping(p, index)

    elif state == "think":
        look = (0.8, 1.0, 0.8, 0.5, 0.8, 0.8)[index]
        expression = "half" if index == 3 else "open"
        mouth = "o" if index < 2 else "smirk"
        draw_full_character(
            p,
            upper_dy=0 if index != 4 else 1,
            head_dx=0.4,
            head_extra_y=-0.2,
            expression=expression,
            mouth=mouth,
            brow="curious",
            look=look,
            pose="think",
            wind=0.4,
            blush=False,
        )
        draw_thought_prop(p, index)

    elif state == "explain":
        pointing = (0, 1, 2, 1, 0, -1)[index]
        draw_full_character(
            p,
            upper_dy=0 if index != 4 else 1,
            head_dx=0.4,
            expression="happy" if index == 4 else ("half" if index == 5 else "open"),
            mouth="talk" if index in (0, 1, 3) else "smile",
            brow="raised" if index in (1, 2, 3) else "confident",
            look=1.0,
            pose="explain",
            wind=0.4,
        )
        p.line([(52.0, 47.0 + pointing), (57.0, 45.0 + pointing)], CREAM, 1, False)
        if index in (1, 2, 3, 4):
            draw_atom_spark(p, 54.5, 27.0 + (1 if index == 2 else 0), index)

    elif state == "pet":
        tilt = (0, 0.4, 0.8, 0.6, 0.2, 0)[index]
        draw_full_character(
            p,
            upper_dy=1 if index in (1, 2) else 0,
            head_dx=tilt,
            head_extra_y=0.5,
            expression="happy",
            mouth="grin" if index in (1, 2, 3) else "smile",
            brow="confident",
            look=0,
            pose="pet",
            blush=True,
        )
        heart_y = (23, 19, 16, 14, 12, 11)[index]
        draw_heart(p, 49, heart_y, 0.8 + min(index, 3) * 0.08)
        if index in (2, 3):
            p.rect(56, 24 - index, 1, 1, ROSE_LIGHT)

    elif state == "click":
        dy = (0, -1, 0, 0)[index]
        draw_full_character(
            p,
            upper_dy=dy,
            expression="wide" if index < 2 else "open",
            mouth="o" if index < 2 else "smile",
            brow="raised",
            look=0.4,
            pose="click" if index < 2 else "pocket",
            wind=0.7 if index < 2 else 0.2,
        )
        if index < 2:
            p.rect(49, 13, 1, 4, GOLD)
            p.rect(49, 19, 1, 1, GOLD)

    else:
        raise ValueError("Unknown state: " + state)

    return canvas


def alpha_bbox(canvas: Canvas) -> tuple[int, int, int, int]:
    min_x, min_y = canvas.width, canvas.height
    max_x = max_y = -1
    for y in range(canvas.height):
        for x in range(canvas.width):
            if canvas.pixels[(y * canvas.width + x) * 4 + 3] > 8:
                min_x = min(min_x, x)
                min_y = min(min_y, y)
                max_x = max(max_x, x)
                max_y = max(max_y, y)
    if max_x < 0:
        return (0, 0, 0, 0)
    return (min_x, min_y, max_x - min_x + 1, max_y - min_y + 1)


def composite(
    background: Canvas,
    source: Canvas,
    x: int,
    y: int,
) -> None:
    for row in range(source.height):
        for column in range(source.width):
            si = (row * source.width + column) * 4
            alpha = source.pixels[si + 3]
            if alpha == 0:
                continue
            dx, dy = x + column, y + row
            if dx < 0 or dy < 0 or dx >= background.width or dy >= background.height:
                continue
            di = (dy * background.width + dx) * 4
            if alpha == 255:
                background.pixels[di : di + 4] = source.pixels[si : si + 4]
            else:
                background.blend(dx, dy, tuple(source.pixels[si : si + 4]))


def draw_label(
    canvas: Canvas,
    text: str,
    x: int,
    y: int,
    color: tuple[int, int, int, int],
    scale: int = 2,
) -> None:
    glyphs = {
        "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
        "B": ("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
        "C": ("01111", "10000", "10000", "10000", "10000", "10000", "01111"),
        "D": ("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
        "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
        "F": ("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
        "G": ("01111", "10000", "10000", "10111", "10001", "10001", "01111"),
        "H": ("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
        "I": ("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
        "J": ("00111", "00010", "00010", "00010", "10010", "10010", "01100"),
        "K": ("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
        "L": ("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
        "M": ("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
        "N": ("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
        "O": ("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
        "P": ("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
        "Q": ("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
        "R": ("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
        "S": ("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
        "T": ("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
        "U": ("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
        "V": ("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
        "W": ("10001", "10001", "10001", "10101", "10101", "11011", "10001"),
        "X": ("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
        "Y": ("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
        "Z": ("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
        " ": ("00000",) * 7,
        "/": ("00001", "00010", "00010", "00100", "01000", "01000", "10000"),
        "-": ("00000", "00000", "00000", "11111", "00000", "00000", "00000"),
    }
    cursor = x
    for char in text.upper():
        glyph = glyphs.get(char, glyphs[" "])
        for row, bits in enumerate(glyph):
            for column, bit in enumerate(bits):
                if bit == "1":
                    canvas.rect(cursor + column * scale, y + row * scale, scale, scale, color)
        cursor += 6 * scale


def make_preview(all_frames: dict[tuple[int, int], dict[str, list[Canvas]]]) -> Canvas:
    frames = all_frames[(64, 96)]
    states = ["idle", "blink", "walk", "jump", "sleep", "think", "explain", "pet", "click"]
    columns = 8
    cell_w, cell_h = 76, 112
    row_gap = 28
    header = 28
    output = Canvas(columns * cell_w + 16, header + len(states) * (cell_h + row_gap))
    output.rect(0, 0, output.width, output.height, rgba("#253038"))
    for row, state in enumerate(states):
        y0 = header + row * (cell_h + row_gap)
        draw_label(output, state, 8, y0 - 20, rgba("#E9E1D2"), 2)
        for col, frame in enumerate(frames[state][:columns]):
            x0 = 8 + col * cell_w + (64 - frame.width) // 2
            composite(output, frame, x0, y0)
    return output


def write_ico(path: Path, image: Canvas) -> None:
    icon = image.resize_nearest(32, 32)
    payload = icon.png_bytes()
    header = struct.pack("<HHH", 0, 1, 1)
    entry = struct.pack("<BBBBHHII", 32, 32, 0, 0, 1, 32, len(payload), 22)
    path.write_bytes(header + entry + payload)


def main() -> None:
    ASSET_ROOT.mkdir(parents=True, exist_ok=True)
    specs = {
        "idle": (6, 260, True, "待机与呼吸"),
        "blink": (6, 80, False, "眨眼"),
        "walk": (8, 100, True, "行走（窗口可水平翻转）"),
        "jump": (6, 100, False, "跳跃"),
        "sleep": (4, 450, True, "坐着打盹"),
        "think": (6, 260, True, "思考物理问题"),
        "explain": (6, 220, True, "讲解物理"),
        "pet": (6, 120, False, "摸头回应"),
        "click": (4, 100, False, "点击回应"),
    }
    all_frames: dict[tuple[int, int], dict[str, list[Canvas]]] = {}
    manifest: dict[str, object] = {
        "name": "成熟像素风·理查德·费曼桌面宠物",
        "design": "Mature-proportion retro RPG sprite, original pixel redraw",
        "canvas_reference": [DESIGN_W, DESIGN_H],
        "default_size": [64, 96],
        "transparent": True,
        "profiles": {},
    }
    profiles: dict[str, object] = {}
    for width, height in PROFILES:
        profile_frames: dict[str, list[Canvas]] = {}
        profile_states: dict[str, object] = {}
        for state, (count, delay, loop, label) in specs.items():
            frames = [render_state(state, index, width, height) for index in range(count)]
            profile_frames[state] = frames
            paths: list[str] = []
            union_min_x, union_min_y = width, height
            union_max_x = union_max_y = -1
            for index, frame in enumerate(frames):
                relative = f"{width}x{height}/{state}/frame_{index:02d}.png"
                frame.write_png(ASSET_ROOT / relative)
                paths.append(relative)
                bx, by, bw, bh = alpha_bbox(frame)
                union_min_x = min(union_min_x, bx)
                union_min_y = min(union_min_y, by)
                union_max_x = max(union_max_x, bx + bw - 1)
                union_max_y = max(union_max_y, by + bh - 1)
            profile_states[state] = {
                "label": label,
                "frames": paths,
                "delay_ms": delay,
                "loop": loop,
                "content_bbox": [
                    union_min_x,
                    union_min_y,
                    union_max_x - union_min_x + 1,
                    union_max_y - union_min_y + 1,
                ],
            }
        all_frames[(width, height)] = profile_frames
        profiles[f"{width}x{height}"] = {
            "width": width,
            "height": height,
            "states": profile_states,
        }
    manifest["profiles"] = profiles
    (ASSET_ROOT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    preview = make_preview(all_frames)
    preview.write_png(ASSET_ROOT / "preview_sheet.png")
    icon_source = all_frames[(64, 96)]["idle"][0].crop(15, 1, 34, 34)
    write_ico(ASSET_ROOT / "feynman.ico", icon_source)

    total = sum(len(frames) for profile in all_frames.values() for frames in profile.values())
    print(f"Generated {total} native-size transparent frames across {len(PROFILES)} profiles")
    print(f"Asset root: {ASSET_ROOT}")


if __name__ == "__main__":
    main()
