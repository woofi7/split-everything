#!/usr/bin/env python3
"""
Fails when a tracked file carries a character outside plain ASCII.

The rule this enforces is about typographic ornaments, not about language: an em
dash, an arrow, an ellipsis or a curly quote pasted from somewhere else is what
this is looking for. Two things are exempt, both deliberately:

  frontend/src/i18n/fr.ts  the French strings, which need their accents
  **/Fixtures/*.csv        real exports kept byte for byte as test fixtures

Run it with no arguments to check everything git knows about.
"""
from __future__ import annotations

import subprocess
import sys
from fnmatch import fnmatch
from pathlib import Path

EXEMPT = (
    'frontend/src/i18n/fr.ts',
    '*/Fixtures/*.csv',
    '*.png',
    '*.jpg',
    '*.jpeg',
    '*.ico',
    '*.pdf',
    '*.woff',
    '*.woff2',
    '*-lock.json',
    '*.lock',
)

# What each offender is, so a failure reads as an instruction rather than a hex dump.
NAMES = {
    '—': 'em dash',
    '–': 'en dash',
    '→': 'right arrow',
    '←': 'left arrow',
    '…': 'ellipsis',
    '‘': 'curly quote',
    '’': 'curly quote',
    '“': 'curly double quote',
    '”': 'curly double quote',
    '•': 'bullet',
    ' ': 'non-breaking space',
    '✓': 'check mark',
    '﻿': 'byte order mark',
}


def exempt(path: str) -> bool:
    return any(fnmatch(path, pattern) or path == pattern for pattern in EXEMPT)


def tracked_files() -> list[str]:
    listed = subprocess.run(
        ['git', 'ls-files'], capture_output=True, text=True, check=True).stdout
    return [line for line in listed.splitlines() if line]


def offences(path: str) -> list[str]:
    try:
        text = Path(path).read_text(encoding='utf-8')
    except (UnicodeDecodeError, FileNotFoundError, IsADirectoryError):
        # Binary, or gone since git listed it. Neither is this check's business.
        return []

    # A byte order mark at the very start is the .NET tooling's doing: every
    # csproj, sln and generated migration it writes carries one, and it is an
    # encoding marker rather than a character anybody typed.
    if text.startswith('\ufeff'):
        text = text[1:]

    found = []
    for number, line in enumerate(text.splitlines(), start=1):
        for column, character in enumerate(line, start=1):
            if ord(character) < 128:
                continue
            name = NAMES.get(character, f'U+{ord(character):04X}')
            found.append(f'{path}:{number}:{column}: {name} ({character!r})')
    return found


def main(argv: list[str]) -> int:
    paths = argv[1:] or tracked_files()
    problems: list[str] = []

    for path in paths:
        if exempt(path):
            continue
        problems.extend(offences(path))

    if not problems:
        print(f'Plain ASCII: {len(paths)} files checked.')
        return 0

    print('Characters outside plain ASCII:', file=sys.stderr)
    for problem in problems[:50]:
        print(f'  {problem}', file=sys.stderr)
    if len(problems) > 50:
        print(f'  ... and {len(problems) - 50} more', file=sys.stderr)

    print(
        '\nWrite the plain equivalent: a hyphen for a dash, "..." for an ellipsis,\n'
        'a straight quote for a curly one. If a file genuinely needs the character,\n'
        'add it to EXEMPT in scripts/check-ascii.py and say why.',
        file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
