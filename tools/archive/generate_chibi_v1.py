#!/usr/bin/env python3
"""Generate the Feynman desktop-pet sprites without external dependencies.

The source image in the task is a design reference.  The sprites below are an
original, coherent redraw: chibi proportions, dark curly hair, a white shirt,
charcoal trousers, and a curious Feynman-like expression.  Every animation
frame is written as an RGBA PNG on a genuinely transparent canvas.
"""

from __future__ import annotations

import json
import math
import os
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence


ROOT = Path(__file__).resolve().parents[1]
ASSET_ROOT = ROOT / "assets" / "feynman"
LOGICAL_W, LOGICAL_H = 140, 160
PIXEL_SCALE = 2


def rgba(value: str) -> tuple[int, int, int, int]:
    value = value.lstrip("#")
    if len(value) == 6:
        value += "FF"
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4, 6))


INK = rgba("#2A211F")
INK_SOFT = rgba("#443431")
SKIN = rgba("#F0C49D")
SKIN_LIGHT = rgba("#FFD9B5")
SKIN_SHADOW = rgba("#D89A73")
SKIN_DEEP = rgba("#C87C5D")
BLUSH = rgba("#E99A99")
HAIR = rgba("#3B2927")
HAIR_DARK = rgba("#241B1C")
HAIR_LIGHT = rgba("#62423B")
HAIR_HIGHLIGHT = rgba("#7A5448")
SHIRT = rgba("#F7F1E5")
SHIRT_LIGHT = rgba("#FFFCF4")
SHIRT_SHADOW = rgba("#D8CFC1")
PANTS = rgba("#363740")
PANTS_LIGHT = rgba("#4B4D58")
SHOE = rgba("#24242B")
SHOE_LIGHT = rgba("#3F3F49")
WHITE = rgba("#FFFFFF")
CREAM = rgba("#FFF3CB")
GOLD = rgba("#E8B84A")
GOLD_LIGHT = rgba("#FFE08A")
BLUE = rgba("#76A5C8")
BLUE_DARK = rgba("#446D94")
BLUE_LIGHT = rgba("#C9E0EF")
PILLOW = rgba("#D9EAF4")
BOARD = rgba("#263B39")
BOARD_DARK = rgba("#182725")
WOOD = rgba("#A66F47")
WOOD_LIGHT = rgba("#D19A69")
DUST = rgba("#C2B8A9")
DUST_LIGHT = rgba("#E0D8CB")
ROSE = rgba("#E56F7C")
ROSE_LIGHT = rgba("#FFB0B9")


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
        dst_r, dst_g, dst_b, dst_a = self.pixels[index : index + 4]
        src_a = a / 255.0
        dst_a = dst_a / 255.0
        out_a = src_a + dst_a * (1.0 - src_a)
        if out_a <= 0:
            return
        out_r = round((r * src_a + dst_r * dst_a * (1.0 - src_a)) / out_a)
        out_g = round((g * src_a + dst_g * dst_a * (1.0 - src_a)) / out_a)
        out_b = round((b * src_a + dst_b * dst_a * (1.0 - src_a)) / out_a)
        self.pixels[index : index + 4] = bytes(
            (out_r, out_g, out_b, round(out_a * 255))
        )

    def rect(
        self, x: int, y: int, width: int, height: int, color: tuple[int, int, int, int]
    ) -> None:
        x0, y0 = max(0, x), max(0, y)
        x1, y1 = min(self.width, x + width), min(self.height, y + height)
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
            dy = (y + 0.5 - cy) / ry
            if abs(dy) > 1:
                continue
            span = rx * math.sqrt(max(0.0, 1.0 - dy * dy))
            start = math.ceil(cx - span - 0.15)
            end = math.floor(cx + span + 0.15)
            for x in range(max(x0, start), min(x1, end) + 1):
                self.blend(x, y, color)

    def ellipse_outline(
        self,
        cx: float,
        cy: float,
        rx: float,
        ry: float,
        color: tuple[int, int, int, int],
        width: int = 2,
    ) -> None:
        self.ellipse(cx, cy, rx + width, ry + width, color)
        self.ellipse(cx, cy, max(0.2, rx), max(0.2, ry), rgba("#00000000"))

    def outlined_ellipse(
        self,
        cx: float,
        cy: float,
        rx: float,
        ry: float,
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] = INK,
        border: int = 2,
    ) -> None:
        self.ellipse(cx, cy, rx + border, ry + border, outline)
        self.ellipse(cx, cy, rx, ry, fill)

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
            x0, y0 = a
            x1, y1 = b
            dx, dy = x1 - x0, y1 - y0
            steps = max(1, int(max(abs(dx), abs(dy)) * 2))
            for step in range(steps + 1):
                t = step / steps
                x = round(x0 + dx * t)
                y = round(y0 + dy * t)
                if radius == 0:
                    self.blend(x, y, color)
                else:
                    self.ellipse(x, y, radius, radius, color)

    def polygon(
        self,
        points: Sequence[tuple[float, float]],
        color: tuple[int, int, int, int],
    ) -> None:
        if len(points) < 3:
            return
        min_y = max(0, math.floor(min(y for _, y in points)))
        max_y = min(self.height - 1, math.ceil(max(y for _, y in points)))
        for y in range(min_y, max_y + 1):
            intersections: list[float] = []
            scan_y = y + 0.5
            for a, b in zip(points, points[1:] + points[:1]):
                x0, y0 = a
                x1, y1 = b
                if (y0 <= scan_y < y1) or (y1 <= scan_y < y0):
                    t = (scan_y - y0) / (y1 - y0)
                    intersections.append(x0 + (x1 - x0) * t)
            intersections.sort()
            for start, end in zip(intersections[::2], intersections[1::2]):
                for x in range(
                    max(0, math.ceil(start - 0.1)),
                    min(self.width - 1, math.floor(end + 0.1)) + 1,
                ):
                    self.blend(x, y, color)

    def outlined_polygon(
        self,
        points: Sequence[tuple[float, float]],
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] = INK,
        width: int = 2,
    ) -> None:
        self.polygon(points, fill)
        self.line(list(points) + [points[0]], outline, width)

    def rounded_rect(
        self,
        x: float,
        y: float,
        width: float,
        height: float,
        radius: float,
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] | None = None,
        border: int = 2,
    ) -> None:
        if outline:
            self.rounded_rect(
                x - border,
                y - border,
                width + border * 2,
                height + border * 2,
                radius + border,
                outline,
            )
        self.rect(int(x + radius), int(y), int(width - radius * 2), int(height), fill)
        self.rect(int(x), int(y + radius), int(width), int(height - radius * 2), fill)
        for cx, cy in (
            (x + radius, y + radius),
            (x + width - radius, y + radius),
            (x + radius, y + height - radius),
            (x + width - radius, y + height - radius),
        ):
            self.ellipse(cx, cy, radius, radius, fill)

    def cloud(
        self,
        circles: Iterable[tuple[float, float, float]],
        fill: tuple[int, int, int, int],
        outline: tuple[int, int, int, int] = INK,
        border: int = 2,
    ) -> None:
        circles = list(circles)
        for cx, cy, radius in circles:
            self.ellipse(cx, cy, radius + border, radius + border, outline)
        for cx, cy, radius in circles:
            self.ellipse(cx, cy, radius, radius, fill)

    def upscale(self, scale: int) -> "Canvas":
        result = Canvas(self.width * scale, self.height * scale)
        for y in range(self.height):
            row = self.pixels[y * self.width * 4 : (y + 1) * self.width * 4]
            target_row = bytearray()
            for x in range(self.width):
                pixel = row[x * 4 : x * 4 + 4]
                target_row.extend(pixel * scale)
            for sy in range(scale):
                target_y = y * scale + sy
                start = target_y * result.width * 4
                result.pixels[start : start + result.width * 4] = target_row
        return result

    def resize_nearest(self, width: int, height: int) -> "Canvas":
        result = Canvas(width, height)
        for y in range(height):
            source_y = min(self.height - 1, y * self.height // height)
            for x in range(width):
                source_x = min(self.width - 1, x * self.width // width)
                source = (source_y * self.width + source_x) * 4
                target = (y * width + x) * 4
                result.pixels[target : target + 4] = self.pixels[source : source + 4]
        return result

    def write_png(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        raw = bytearray()
        stride = self.width * 4
        for y in range(self.height):
            raw.append(0)
            raw.extend(self.pixels[y * stride : (y + 1) * stride])

        def chunk(kind: bytes, data: bytes) -> bytes:
            body = kind + data
            return (
                struct.pack(">I", len(data))
                + body
                + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
            )

        png = b"\x89PNG\r\n\x1a\n"
        png += chunk(
            b"IHDR", struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0)
        )
        png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        png += chunk(b"IEND", b"")
        path.write_bytes(png)


FONT_5X7 = {
    "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
    "F": ("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
    "M": ("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
    "C": ("01110", "10001", "10000", "10000", "10000", "10001", "01110"),
    "=": ("00000", "11111", "00000", "11111", "00000", "00000", "00000"),
    "2": ("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
    "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
    "+": ("00000", "00100", "00100", "11111", "00100", "00100", "00000"),
    "?": ("01110", "10001", "00001", "00010", "00100", "00000", "00100"),
    "Z": ("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
}


def draw_text(
    canvas: Canvas,
    text: str,
    x: int,
    y: int,
    color: tuple[int, int, int, int],
    scale: int = 2,
) -> None:
    cursor = x
    for char in text:
        glyph = FONT_5X7.get(char)
        if glyph:
            for row, bits in enumerate(glyph):
                for column, bit in enumerate(bits):
                    if bit == "1":
                        canvas.rect(
                            cursor + column * scale,
                            y + row * scale,
                            scale,
                            scale,
                            color,
                        )
        cursor += 6 * scale


@dataclass(frozen=True)
class CharacterOptions:
    cx: float = 70
    cy: float = 58
    dy: int = 0
    head_dx: float = 0
    head_dy: float = 0
    eye: str = "open"
    mouth: str = "smile"
    brow: str = "normal"
    arms: str = "down"
    legs: str = "stand"
    phase: int = 0
    look: float = 0


def draw_hair(
    canvas: Canvas, cx: float, cy: float, wind: float = 0.0
) -> None:
    back = [
        (cx - 22, cy - 3, 9),
        (cx - 19, cy - 14, 10),
        (cx - 11, cy - 23, 10),
        (cx + 1, cy - 27, 11),
        (cx + 13, cy - 24, 10),
        (cx + 22, cy - 14, 10),
        (cx + 25, cy - 2, 9),
        (cx + 23, cy + 10, 8),
        (cx - 24, cy + 10, 8),
    ]
    back = [(x + wind * (1 if x > cx else -0.4), y, r) for x, y, r in back]
    canvas.cloud(back, HAIR, HAIR_DARK, 2)

    # Curl separations make the silhouette read as hair rather than a helmet.
    for x, y, radius in back:
        if radius >= 9:
            canvas.line(
                [(x - radius * 0.35, y + radius * 0.35), (x + radius * 0.2, y + radius * 0.1)],
                HAIR_DARK,
                1,
            )
    for x, y, radius in back[:7]:
        canvas.line(
            [(x - radius * 0.55, y - radius * 0.35), (x - radius * 0.1, y - radius * 0.7)],
            HAIR_LIGHT,
            2,
        )
        canvas.line(
            [(x + radius * 0.25, y - radius * 0.65), (x + radius * 0.55, y - radius * 0.25)],
            HAIR_HIGHLIGHT,
            1,
        )


def draw_eyes(
    canvas: Canvas,
    cx: float,
    cy: float,
    eye: str,
    look: float = 0.0,
    brow: str = "normal",
) -> None:
    eye_y = cy + 2
    for side in (-1, 1):
        ex = cx + side * 8
        if eye == "open":
            canvas.outlined_ellipse(ex, eye_y, 3.6, 4.7, WHITE, INK, 1)
            px = round(ex + look * 1.4)
            canvas.ellipse(px, eye_y + 0.6, 2.2, 2.8, rgba("#513B30"))
            canvas.ellipse(px, eye_y + 0.7, 1.25, 2.0, INK)
            canvas.rect(px - 1, round(eye_y - 1.8), 2, 2, WHITE)
        elif eye == "wide":
            canvas.outlined_ellipse(ex, eye_y, 4.0, 5.2, WHITE, INK, 1)
            px = round(ex + look * 1.6)
            canvas.ellipse(px, eye_y - 0.3, 2.0, 2.7, rgba("#513B30"))
            canvas.ellipse(px, eye_y - 0.4, 1.1, 2.0, INK)
            canvas.rect(px - 1, round(eye_y - 2.2), 2, 2, WHITE)
        elif eye in ("blink", "half"):
            if eye == "half":
                canvas.line([(ex - 4, eye_y), (ex + 4, eye_y - 0.5)], INK, 2)
                canvas.line([(ex - 4, eye_y), (ex + 4, eye_y)], SKIN_DEEP, 1)
            else:
                canvas.line([(ex - 4, eye_y), (ex + 4, eye_y)], INK, 2)
        elif eye == "happy":
            canvas.line(
                [(ex - 4, eye_y + 1.5), (ex, eye_y - 1.5), (ex + 4, eye_y + 1.5)],
                INK,
                2,
            )
        elif eye == "sleep":
            canvas.line(
                [(ex - 4, eye_y), (ex, eye_y + 2), (ex + 4, eye_y)],
                INK,
                1,
            )

    brow_y = cy - 7
    if brow == "raised":
        canvas.line([(cx - 12, brow_y), (cx - 5, brow_y - 2)], HAIR_DARK, 2)
        canvas.line([(cx + 5, brow_y - 2), (cx + 12, brow_y)], HAIR_DARK, 2)
    elif brow == "curious":
        canvas.line([(cx - 12, brow_y + 1), (cx - 5, brow_y - 2)], HAIR_DARK, 2)
        canvas.line([(cx + 5, brow_y - 1), (cx + 12, brow_y - 3)], HAIR_DARK, 2)
    elif brow == "concerned":
        canvas.line([(cx - 12, brow_y - 1), (cx - 5, brow_y + 1)], HAIR_DARK, 2)
        canvas.line([(cx + 5, brow_y + 1), (cx + 12, brow_y - 1)], HAIR_DARK, 2)
    else:
        canvas.line([(cx - 12, brow_y), (cx - 5, brow_y - 1)], HAIR_DARK, 2)
        canvas.line([(cx + 5, brow_y - 1), (cx + 12, brow_y)], HAIR_DARK, 2)


def draw_head(
    canvas: Canvas,
    cx: float,
    cy: float,
    eye: str = "open",
    mouth: str = "smile",
    brow: str = "normal",
    look: float = 0.0,
    wind: float = 0.0,
) -> None:
    draw_hair(canvas, cx, cy, wind)
    canvas.outlined_ellipse(cx - 21, cy + 4, 4, 6, SKIN, INK, 2)
    canvas.outlined_ellipse(cx + 21, cy + 4, 4, 6, SKIN, INK, 2)
    canvas.outlined_ellipse(cx, cy + 2, 20, 21, SKIN, INK, 2)

    # One-pixel art shading on the right cheek and under the chin.
    canvas.polygon(
        [
            (cx + 13, cy - 10),
            (cx + 19, cy - 2),
            (cx + 19, cy + 9),
            (cx + 12, cy + 19),
            (cx + 8, cy + 19),
            (cx + 14, cy + 8),
            (cx + 15, cy - 2),
        ],
        SKIN_SHADOW,
    )
    canvas.line([(cx - 9, cy + 21), (cx + 8, cy + 21)], SKIN_SHADOW, 1)

    # Curly fringe, deliberately overlapping the top of the face.
    fringe = [
        (cx - 16, cy - 12, 7),
        (cx - 8, cy - 17, 7),
        (cx + 1, cy - 18, 7),
        (cx + 9, cy - 16, 7),
        (cx + 16, cy - 11, 6),
        (cx - 20, cy - 2, 5),
        (cx + 21, cy - 1, 5),
        (cx - 17, cy + 9, 5),
        (cx + 18, cy + 9, 5),
    ]
    canvas.cloud(fringe, HAIR, HAIR_DARK, 1)
    canvas.line(
        [(cx - 8, cy - 20), (cx - 12, cy - 16), (cx - 11, cy - 10)],
        HAIR_LIGHT,
        2,
    )
    canvas.line(
        [(cx + 4, cy - 22), (cx + 8, cy - 19), (cx + 7, cy - 14)],
        HAIR_HIGHLIGHT,
        1,
    )
    canvas.line(
        [(cx + 16, cy - 15), (cx + 20, cy - 11), (cx + 18, cy - 7)],
        HAIR_LIGHT,
        1,
    )

    draw_eyes(canvas, cx, cy, eye, look, brow)
    canvas.line([(cx + 1, cy + 8), (cx - 1, cy + 10), (cx + 2, cy + 10)], SKIN_DEEP, 1)

    if mouth == "smile":
        canvas.line(
            [(cx - 5, cy + 14), (cx, cy + 17), (cx + 6, cy + 14)],
            INK,
            2,
        )
        canvas.rect(cx + 3, cy + 15, 2, 1, WHITE)
    elif mouth == "grin":
        canvas.polygon(
            [(cx - 7, cy + 13), (cx + 7, cy + 13), (cx + 4, cy + 18), (cx, cy + 19), (cx - 4, cy + 18)],
            INK,
        )
        canvas.rect(cx - 4, cy + 15, 8, 2, WHITE)
        canvas.rect(cx - 1, cy + 17, 4, 2, ROSE)
    elif mouth == "talk":
        canvas.outlined_ellipse(cx, cy + 15, 4, 3, INK, INK, 0)
        canvas.ellipse(cx, cy + 16.3, 2.2, 1.3, ROSE)
    elif mouth == "o":
        canvas.outlined_ellipse(cx, cy + 15, 2.6, 2.8, INK, INK, 0)
    elif mouth == "sleep":
        canvas.line(
            [(cx - 4, cy + 15), (cx, cy + 17), (cx + 4, cy + 14)],
            INK,
            1,
        )

    canvas.rect(round(cx - 16), round(cy + 11), 4, 2, BLUSH)
    canvas.rect(round(cx + 12), round(cy + 11), 4, 2, BLUSH)


def draw_hand(
    canvas: Canvas,
    x: float,
    y: float,
    scale: float = 1.0,
    point: tuple[float, float] | None = None,
) -> None:
    canvas.outlined_ellipse(x, y, 4 * scale, 4.2 * scale, SKIN, INK, 1)
    if point:
        canvas.line([(x, y), point], INK, 3)
        canvas.line([(x, y), point], SKIN, 2)
        canvas.ellipse(point[0], point[1], 1.4, 1.4, SKIN)


def draw_arm(
    canvas: Canvas,
    shoulder: tuple[float, float],
    elbow: tuple[float, float],
    wrist: tuple[float, float],
    hand: tuple[int, int, int, int] = SKIN,
    point: tuple[float, float] | None = None,
) -> None:
    path = [shoulder, elbow, wrist]
    canvas.line(path, INK, 11)
    canvas.line(path, SHIRT, 7)
    canvas.line(path, SHIRT_SHADOW, 1)
    canvas.line([elbow, (elbow[0] + (wrist[0] - elbow[0]) * 0.72, elbow[1] + (wrist[1] - elbow[1]) * 0.72)], SHIRT_LIGHT, 2)
    canvas.ellipse(wrist[0], wrist[1], 4.5, 4.5, INK)
    canvas.ellipse(wrist[0], wrist[1], 3.2, 3.2, SHIRT_SHADOW)
    draw_hand(canvas, wrist[0], wrist[1], 1.0, point)


def draw_legs(canvas: Canvas, cx: float, cy: float, pose: str, phase: int) -> None:
    hip_y = cy + 62
    ankle_y = cy + 90
    if pose == "walk":
        swing = (10, 6, 0, -7, -10, -5)[phase % 6]
        left_foot = (cx - 7 + swing, ankle_y)
        right_foot = (cx + 7 - swing, ankle_y)
        left_knee = (cx - 7 + swing * 0.35, cy + 77)
        right_knee = (cx + 7 - swing * 0.35, cy + 77)
    elif pose == "crouch":
        left_foot = (cx - 12, ankle_y)
        right_foot = (cx + 12, ankle_y)
        left_knee = (cx - 14, cy + 74)
        right_knee = (cx + 14, cy + 74)
    elif pose == "air":
        left_foot = (cx - 10, cy + 82)
        right_foot = (cx + 10, cy + 82)
        left_knee = (cx - 8, cy + 70)
        right_knee = (cx + 8, cy + 70)
    else:
        left_foot = (cx - 8, ankle_y)
        right_foot = (cx + 8, ankle_y)
        left_knee = (cx - 7, cy + 77)
        right_knee = (cx + 7, cy + 77)

    for side, knee, foot in (
        (-1, left_knee, left_foot),
        (1, right_knee, right_foot),
    ):
        hip = (cx + side * 7, hip_y)
        canvas.line([hip, knee, foot], INK, 11)
        canvas.line([hip, knee, foot], PANTS, 7)
        canvas.line([(cx - 8, hip_y + 4), (knee[0] - 1, knee[1])], PANTS_LIGHT, 1)
        shoe_y = foot[1] - 1
        shoe_dir = -1 if foot[0] < cx else 1
        canvas.outlined_polygon(
            [
                (foot[0] - 6, shoe_y - 2),
                (foot[0] + 4, shoe_y - 2),
                (foot[0] + 8 + shoe_dir, shoe_y + 2),
                (foot[0] + 6 + shoe_dir, shoe_y + 5),
                (foot[0] - 7, shoe_y + 5),
            ],
            SHOE,
            INK,
            1,
        )
        canvas.line(
            [(foot[0] - 4, shoe_y + 2), (foot[0] + 4, shoe_y + 2)],
            SHOE_LIGHT,
            1,
        )


def draw_torso(canvas: Canvas, cx: float, top_y: float) -> None:
    points = [
        (cx - 17, top_y + 5),
        (cx - 10, top_y),
        (cx - 4, top_y + 4),
        (cx + 4, top_y + 4),
        (cx + 10, top_y),
        (cx + 17, top_y + 5),
        (cx + 14, top_y + 36),
        (cx - 14, top_y + 36),
    ]
    canvas.outlined_polygon(points, SHIRT, INK, 2)
    canvas.line([(cx - 13, top_y + 8), (cx - 11, top_y + 31)], SHIRT_SHADOW, 1)
    canvas.line([(cx + 12, top_y + 8), (cx + 11, top_y + 29)], SHIRT_LIGHT, 1)
    canvas.polygon(
        [(cx - 10, top_y + 1), (cx - 2, top_y + 5), (cx, top_y + 15)],
        SHIRT_LIGHT,
    )
    canvas.polygon(
        [(cx + 10, top_y + 1), (cx + 2, top_y + 5), (cx, top_y + 15)],
        SHIRT_LIGHT,
    )
    canvas.line([(cx - 10, top_y + 1), (cx, top_y + 15), (cx + 10, top_y + 1)], INK_SOFT, 1)
    canvas.rect(round(cx - 9), round(top_y + 15), 6, 7, rgba("#E7DDCC"))
    canvas.line(
        [(cx - 9, top_y + 15), (cx - 3, top_y + 15), (cx - 3, top_y + 22)],
        INK_SOFT,
        1,
    )
    canvas.line([(cx - 7, top_y + 15), (cx - 7, top_y + 12)], BLUE_DARK, 1)
    canvas.line([(cx - 5, top_y + 15), (cx - 5, top_y + 13)], GOLD, 1)
    for y in (top_y + 23, top_y + 29):
        canvas.ellipse(cx, y, 1.2, 1.2, INK_SOFT)


def draw_character(canvas: Canvas, options: CharacterOptions) -> None:
    cx = options.cx
    cy = options.cy + options.dy + options.head_dy
    shoulder_y = cy + 28
    upper_y = options.cy + options.dy + 24
    arm_state = options.arms

    # Back arm
    if arm_state == "walk":
        swing = (-8, -5, 0, 7, 9, 4)[options.phase % 6]
        draw_arm(
            canvas,
            (cx - 16, upper_y + 4),
            (cx - 22 - swing * 0.3, upper_y + 18),
            (cx - 19 - swing, upper_y + 31),
        )
    elif arm_state in ("cheer", "jump"):
        draw_arm(
            canvas,
            (cx - 16, upper_y + 4),
            (cx - 25, upper_y - 3),
            (cx - 26, upper_y - 13),
        )
    elif arm_state == "hug":
        draw_arm(
            canvas,
            (cx - 16, upper_y + 4),
            (cx - 18, upper_y + 18),
            (cx - 2, upper_y + 21),
        )
    else:
        draw_arm(
            canvas,
            (cx - 16, upper_y + 4),
            (cx - 23, upper_y + 19),
            (cx - 22, upper_y + 30),
        )

    draw_legs(canvas, cx, options.cy + options.dy, options.legs, options.phase)
    draw_torso(canvas, cx, upper_y)
    canvas.rect(round(cx - 4), round(cy + 20), 8, 7, SKIN_SHADOW)
    draw_head(
        canvas,
        cx + options.head_dx,
        cy,
        options.eye,
        options.mouth,
        options.brow,
        options.look,
        wind=1.5 if arm_state == "walk" else 0,
    )

    # Front arm and action-specific hand pose.
    if arm_state in ("chin", "think"):
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 22, upper_y + 16),
            (cx + 10, cy + 17),
        )
    elif arm_state in ("cheer", "jump"):
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 25, upper_y - 4),
            (cx + 27, upper_y - 14),
        )
    elif arm_state == "point":
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 21, upper_y + 20),
            (cx + 14, upper_y + 31),
        )
        draw_arm(
            canvas,
            (cx - 16, upper_y + 4),
            (cx - 24, upper_y + 13),
            (cx - 43, upper_y + 5),
            point=(cx - 48, upper_y + 4),
        )
    elif arm_state == "walk":
        swing = (-8, -5, 0, 7, 9, 4)[options.phase % 6]
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 22 + swing * 0.3, upper_y + 18),
            (cx + 19 + swing, upper_y + 31),
        )
    elif arm_state == "hug":
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 18, upper_y + 18),
            (cx + 2, upper_y + 21),
        )
    elif arm_state == "wave":
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 26, upper_y + 1),
            (cx + 30, upper_y - 10),
        )
    elif arm_state == "pocket":
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 19, upper_y + 19),
            (cx + 11, upper_y + 27),
        )
    else:
        draw_arm(
            canvas,
            (cx + 16, upper_y + 4),
            (cx + 23, upper_y + 19),
            (cx + 22, upper_y + 30),
        )


def draw_sparkle(
    canvas: Canvas, x: float, y: float, size: float, color: tuple[int, int, int, int] = GOLD_LIGHT
) -> None:
    canvas.rect(round(x - 1), round(y - size), 2, int(size * 2 + 1), color)
    canvas.rect(round(x - size), round(y - 1), int(size * 2 + 1), 2, color)
    canvas.ellipse(x, y, 2, 2, WHITE)


def draw_dust(canvas: Canvas, x: float, y: float, strength: int = 1) -> None:
    if strength % 2:
        canvas.ellipse(x, y, 5, 2.5, DUST)
        canvas.ellipse(x - 5, y - 1, 2, 2, DUST_LIGHT)
    else:
        canvas.ellipse(x + 4, y - 2, 4, 2, DUST)
        canvas.ellipse(x + 9, y - 1, 2, 1.5, DUST_LIGHT)


def draw_heart(canvas: Canvas, x: float, y: float, scale: float = 1.0) -> None:
    canvas.ellipse(x - 4 * scale, y, 5 * scale, 5 * scale, INK)
    canvas.ellipse(x + 4 * scale, y, 5 * scale, 5 * scale, INK)
    canvas.polygon(
        [(x - 9 * scale, y + 2 * scale), (x + 9 * scale, y + 2 * scale), (x, y + 13 * scale)],
        INK,
    )
    canvas.ellipse(x - 3 * scale, y, 3.5 * scale, 3.5 * scale, ROSE)
    canvas.ellipse(x + 3 * scale, y, 3.5 * scale, 3.5 * scale, ROSE)
    canvas.polygon(
        [(x - 6.3 * scale, y + 1.4 * scale), (x + 6.3 * scale, y + 1.4 * scale), (x, y + 9.7 * scale)],
        ROSE,
    )
    canvas.ellipse(x - 4 * scale, y - 1.5 * scale, 1.4 * scale, 1.4 * scale, ROSE_LIGHT)


def draw_atom(canvas: Canvas, cx: float, cy: float, phase: int = 0) -> None:
    canvas.ellipse(cx, cy, 2.5, 2.5, GOLD)
    for angle in (0, 60, 120):
        theta = math.radians(angle + phase * 20)
        rx, ry = 15, 6
        points = []
        for i in range(25):
            t = math.tau * i / 24
            x = math.cos(t) * rx
            y = math.sin(t) * ry
            points.append(
                (
                    cx + x * math.cos(theta) - y * math.sin(theta),
                    cy + x * math.sin(theta) + y * math.cos(theta),
                )
            )
        canvas.line(points, BLUE_DARK, 1)
    dots = [
        (cx + 13, cy - 2),
        (cx - 10, cy - 7),
        (cx - 1, cy + 12),
    ]
    for index, (x, y) in enumerate(dots):
        if (phase + index) % 3 != 0:
            canvas.ellipse(x, y, 2, 2, GOLD)


def draw_thought_bubble(canvas: Canvas, x: float, y: float, phase: int) -> None:
    circles = [
        (x - 14, y, 8),
        (x, y - 5, 11),
        (x + 15, y, 8),
        (x + 2, y + 7, 9),
        (x - 23, y + 14, 3),
        (x - 30, y + 22, 2),
    ]
    canvas.cloud(circles, rgba("#FFFDF5"), INK_SOFT, 2)
    draw_atom(canvas, x + 1, y + 1, phase)


def draw_board(canvas: Canvas, phase: int) -> None:
    x, y, width, height = 4, 25, 58, 47
    canvas.rect(x - 2, y + height, 5, 16, WOOD)
    canvas.rect(x + width - 3, y + height, 5, 16, WOOD)
    canvas.outlined_polygon(
        [
            (x - 3, y - 3),
            (x + width + 3, y - 3),
            (x + width + 3, y + height + 3),
            (x - 3, y + height + 3),
        ],
        WOOD,
        INK,
        2,
    )
    canvas.rect(x, y, width, height, BOARD)
    canvas.line([(x + 2, y + 2), (x + 2, y + height - 2)], BOARD_DARK, 1)
    canvas.rect(x + 3, y + height + 2, width - 6, 4, WOOD_LIGHT)
    draw_text(canvas, "E=MC", x + 10, y + 10, CREAM, 1)
    draw_text(canvas, "2", x + 30, y + 5, CREAM, 1)
    canvas.line([(x + 10, y + 21), (x + 34, y + 21)], rgba("#A9C9B4"), 1)
    canvas.ellipse(x + 17, y + 34, 2, 2, GOLD_LIGHT)
    for angle in (20, 80, 140):
        theta = math.radians(angle + phase * 15)
        points = []
        for i in range(20):
            t = math.tau * i / 19
            px = math.cos(t) * 12
            py = math.sin(t) * 4
            points.append(
                (
                    x + 17 + px * math.cos(theta) - py * math.sin(theta),
                    y + 34 + px * math.sin(theta) + py * math.cos(theta),
                )
            )
        canvas.line(points, rgba("#B7D0C1"), 1)
    if phase % 3 == 1:
        canvas.line([(x + 37, y + 14), (x + 47, y + 18)], rgba("#8FB5A0"), 1)
    elif phase % 3 == 2:
        canvas.line([(x + 39, y + 16), (x + 48, y + 12), (x + 48, y + 21)], rgba("#8FB5A0"), 1)


def render_state(state: str, index: int, total: int) -> Canvas:
    canvas = Canvas(LOGICAL_W, LOGICAL_H)
    phase = index

    if state == "idle":
        bob = (0, 1, 1, 0)[index % 4]
        look = (-1, 0, 1, 0)[index % 4] * 0.35
        draw_character(
            canvas,
            CharacterOptions(cy=58 + bob, eye="open" if index != 2 else "half", look=look),
        )
        if index == 1:
            draw_sparkle(canvas, 105, 65, 2.2)

    elif state == "blink":
        eyes = ("open", "half", "blink", "blink", "half", "open")
        draw_character(canvas, CharacterOptions(cy=58, eye=eyes[index % len(eyes)]))

    elif state == "walk":
        bob = (0, 1, 0, 0, 1, 0)[index % 6]
        draw_character(
            canvas,
            CharacterOptions(
                cy=58 + bob,
                eye="open",
                mouth="smile",
                arms="walk",
                legs="walk",
                phase=index,
                look=1,
            ),
        )
        draw_dust(canvas, 43 if index % 2 == 0 else 92, 151, index)
        if index in (1, 4):
            canvas.rect(35, 137, 2, 2, DUST_LIGHT)

    elif state == "jump":
        poses = [
            (6, "crouch", "concerned", "o", "down"),
            (-5, "stand", "raised", "grin", "cheer"),
            (-19, "air", "happy", "grin", "jump"),
            (-11, "air", "wide", "talk", "cheer"),
            (3, "crouch", "concerned", "smile", "down"),
            (1, "stand", "normal", "smile", "down"),
        ]
        dy, legs, brow, mouth, arms = poses[index % len(poses)]
        draw_character(
            canvas,
            CharacterOptions(
                cy=58,
                dy=dy,
                eye="happy" if index == 2 else ("wide" if index == 3 else "open"),
                mouth=mouth,
                brow=brow,
                arms=arms,
                legs=legs,
                look=0,
            ),
        )
        if index in (1, 2, 3):
            canvas.line([(34, 137), (28, 141)], DUST, 2)
            canvas.line([(107, 137), (113, 141)], DUST, 2)

    elif state == "sleep":
        breath = (0, 1, 0)[index % 3]
        # A side-on nap: pillow, head, a soft blanket, and a visible resting hand.
        canvas.rounded_rect(25, 121, 78, 25, 9, PILLOW, INK_SOFT, 2)
        canvas.line([(34, 128), (86, 128)], BLUE_LIGHT, 2)
        canvas.outlined_polygon(
            [
                (62, 112 + breath),
                (118, 106 + breath),
                (132, 123 + breath),
                (126, 144),
                (70, 148),
                (58, 137),
            ],
            rgba("#4F83A9"),
            INK,
            2,
        )
        canvas.polygon(
            [
                (67, 116 + breath),
                (112, 111 + breath),
                (123, 120 + breath),
                (117, 127 + breath),
                (69, 132 + breath),
            ],
            rgba("#79A9C8"),
        )
        canvas.line([(77, 120 + breath), (102, 117 + breath), (119, 123 + breath)], BLUE_LIGHT, 2)
        canvas.line([(84, 140), (113, 137)], BLUE_DARK, 2)
        draw_head(canvas, 54, 112 - breath, "sleep", "sleep", "normal", -0.6)
        canvas.outlined_ellipse(73, 126, 5, 7, SKIN, INK, 1)
        canvas.rect(70, 121, 7, 4, SHIRT_LIGHT)
        canvas.line([(68, 121), (78, 121)], INK_SOFT, 1)
        for i, (x, y, size) in enumerate(((91, 87, 2), (104, 74, 2), (116, 59, 1))):
            if index >= i:
                draw_text(canvas, "Z", x, y - breath * i, BLUE_DARK, size)
        if index == 1:
            draw_sparkle(canvas, 123, 101, 1.5, BLUE_LIGHT)

    elif state == "pet":
        tilts = (1, 2, 2, 1, 0)
        tilt = tilts[index % len(tilts)]
        draw_character(
            canvas,
            CharacterOptions(
                cy=58 + tilt,
                head_dx=tilt,
                eye="happy",
                mouth="grin" if index in (1, 2) else "smile",
                brow="normal",
                arms="hug" if index > 0 else "down",
            ),
        )
        heart_scale = (0.55, 0.9, 1.1, 0.85, 0.55)[index % 5]
        draw_heart(canvas, 103, 46 + index * 2, heart_scale)
        if index in (1, 2):
            draw_sparkle(canvas, 113, 63, 2)

    elif state == "think":
        draw_character(
            canvas,
            CharacterOptions(
                cx=68,
                cy=58,
                head_dx=1,
                eye="wide" if index != 2 else "half",
                mouth="o" if index < 2 else "smile",
                brow="curious",
                arms="chin",
                look=1,
            ),
        )
        draw_thought_bubble(canvas, 119, 38, index)
        if index == 3:
            draw_sparkle(canvas, 112, 26, 2.5)

    elif state == "explain":
        draw_board(canvas, index)
        draw_character(
            canvas,
            CharacterOptions(
                cx=104,
                cy=58,
                eye="happy" if index == 3 else ("half" if index == 5 else "open"),
                mouth="talk" if index % 2 == 0 else "smile",
                brow="raised" if index % 2 == 0 else "normal",
                arms="point",
                look=-1,
            ),
        )
        canvas.line(
            [
                (61, 82),
                (50, 76 if index % 2 == 0 else 70),
                (39, 72 if index % 2 == 0 else 64),
            ],
            CREAM,
            1,
        )
        if index in (2, 4):
            draw_sparkle(canvas, 73, 51, 2.2)

    else:
        raise ValueError(f"Unknown state: {state}")

    return canvas


def make_contact_sheet(sheets: dict[str, list[Canvas]]) -> Canvas:
    state_order = ["idle", "blink", "walk", "jump", "sleep", "pet", "think", "explain"]
    cell_w, cell_h = 112, 132
    header = 30
    output = Canvas(int(cell_w * 6), int(header + (cell_h + 18) * len(state_order)))
    output.rect(0, 0, output.width, output.height, rgba("#27323C"))
    for row, state in enumerate(state_order):
        labels = {
            "idle": "IDLE",
            "blink": "BLINK",
            "walk": "WALK",
            "jump": "JUMP",
            "sleep": "SLEEP",
            "pet": "PET",
            "think": "THINK",
            "explain": "EXPLAIN PHYSICS",
        }
        label = labels[state]
        draw_text(output, label, 5, header + row * (cell_h + 18) - 12, CREAM, 1)
        for col, source in enumerate(sheets[state][:6]):
            small = source.resize_nearest(92, 105)
            ox = 5 + col * cell_w + (cell_w - small.width) // 2
            oy = header + row * (cell_h + 18) + (cell_h - small.height) // 2
            for y in range(small.height):
                for x in range(small.width):
                    si = (y * small.width + x) * 4
                    if small.pixels[si + 3] == 0:
                        continue
                    dx, dy = ox + x, oy + y
                    di = (dy * output.width + dx) * 4
                    output.pixels[di : di + 4] = small.pixels[si : si + 4]
    return output


def main() -> None:
    ASSET_ROOT.mkdir(parents=True, exist_ok=True)
    specs = {
        "idle": (4, 4, 290, True, "待机与呼吸"),
        "blink": (6, 6, 90, False, "眨眼"),
        "walk": (6, 6, 110, True, "行走"),
        "jump": (6, 6, 100, False, "跳跃"),
        "sleep": (3, 3, 440, True, "睡觉"),
        "pet": (5, 5, 110, False, "摸头回应"),
        "think": (4, 4, 280, True, "思考"),
        "explain": (6, 6, 210, True, "讲解物理"),
    }
    sheets: dict[str, list[Canvas]] = {}
    manifest_states: dict[str, dict[str, object]] = {}
    for state, (count, _, delay, loop, description) in specs.items():
        frames: list[Canvas] = []
        frame_paths: list[str] = []
        for index in range(count):
            logical = render_state(state, index, count)
            final = logical.upscale(PIXEL_SCALE)
            relative = f"{state}/frame_{index:02d}.png"
            final.write_png(ASSET_ROOT / relative)
            frame_paths.append(relative.replace("\\", "/"))
            frames.append(final)
        sheets[state] = frames
        manifest_states[state] = {
            "label": description,
            "frames": frame_paths,
            "delay_ms": delay,
            "loop": loop,
        }

    manifest = {
        "name": "费曼 Q 版像素桌宠",
        "character": "Richard Feynman inspired chibi",
        "source": "Original redraw based on the supplied design overview",
        "canvas": {
            "width": LOGICAL_W * PIXEL_SCALE,
            "height": LOGICAL_H * PIXEL_SCALE,
            "format": "RGBA PNG",
            "background": "transparent",
        },
        "pixel_scale": PIXEL_SCALE,
        "states": manifest_states,
        "interaction": {
            "head_hitbox": [70, 58, 29],
            "notes": "Drag to move; click head for pet response; double-click for physics explanation.",
        },
    }
    (ASSET_ROOT / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    sheet = make_contact_sheet(sheets)
    sheet.write_png(ASSET_ROOT / "preview_sheet.png")
    print(f"Generated {sum(len(frames) for frames in sheets.values())} transparent frames")
    print(f"Asset root: {ASSET_ROOT}")


if __name__ == "__main__":
    main()
