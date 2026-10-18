#!/usr/bin/env python3
"""Offline repository hygiene and secret-pattern checks."""

from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EXCLUDED_DIRS = {
    ".git",
    "assets",
    "bin",
    "data",
    "logs",
    "obj",
    "__pycache__",
}
TEXT_SUFFIXES = {
    ".cs",
    ".py",
    ".ps1",
    ".md",
    ".json",
    ".ini",
    ".example",
    ".txt",
    ".yml",
    ".yaml",
}

CREDENTIAL_PATTERNS = [
    re.compile(r"sk-[A-Za-z0-9_-]{20,}"),
    re.compile(r"gh[pousr]_[A-Za-z0-9]{20,}"),
    re.compile(r"github_pat_[A-Za-z0-9_]{20,}"),
    re.compile(r"AKIA[0-9A-Z]{16}"),
    re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
    re.compile(
        r"(?im)^[ \t]*(?:DEEPSEEK_API_KEY|ELEVENLABS_API_KEY)[ \t]*=[ \t]*"
        r"(?![ \t]*$)(?![<\[])[A-Za-z0-9_\-]{12,}[ \t]*$"
    ),
]


def iter_text_files():
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(ROOT)
        if any(part in EXCLUDED_DIRS for part in relative.parts):
            continue
        if path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        yield path


def main() -> None:
    ignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
    required_ignore_entries = ["/bin/", "/data/", "/logs/", "*.exe", "*.wav"]
    for entry in required_ignore_entries:
        assert entry in ignore, f"missing .gitignore entry: {entry}"

    findings: list[str] = []
    for path in iter_text_files():
        text = path.read_text(encoding="utf-8", errors="replace")
        for pattern in CREDENTIAL_PATTERNS:
            for match in pattern.finditer(text):
                findings.append(f"{path.relative_to(ROOT)}: {match.group(0)[:16]}...")
    assert not findings, "credential-like values found:\n" + "\n".join(findings)

    example = ROOT / "config" / "assistant.example.ini"
    assert example.exists()
    assert "DEEPSEEK_API_KEY" not in example.read_text(encoding="utf-8")
    env_example = (ROOT / ".env.example").read_text(encoding="utf-8")
    assert "DEEPSEEK_API_KEY=\n" in env_example
    assert "ELEVENLABS_API_KEY=\n" in env_example

    print("Repository test passed: no credential-like values or tracked local data paths.")


if __name__ == "__main__":
    main()
