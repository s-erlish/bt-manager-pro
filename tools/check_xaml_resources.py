#!/usr/bin/env python3
"""Verifies that every {StaticResource} / {DynamicResource} key actually exists.

The XAML compiler does not resolve resource keys — a typo in `{StaticResource
Brush.Acccent}` builds cleanly and throws at runtime, on the machine of whoever
launched the app. Since this project targets Windows but is often built elsewhere,
this check runs the lookup statically instead.

Scopes are modelled the way WPF resolves them:
  * a theme dictionary can only see keys defined in the theme dictionaries;
  * a window or app file can see its own keys plus everything in the themes.

Usage:  python3 tools/check_xaml_resources.py [src_dir]
Exit code is 1 if any key is unresolved.
"""

from __future__ import annotations

import os
import re
import sys

KEY_PATTERN = re.compile(r'x:Key\s*=\s*"([^"]+)"')
REFERENCE_PATTERN = re.compile(r'\{(?:Static|Dynamic)Resource\s+([^}\s,]+)\s*\}')

# Keys that WPF itself provides.
BUILTIN = {
    "ScrollBar.PageDownCommand",
    "ScrollBar.PageUpCommand",
}


def collect(path: str) -> tuple[set[str], list[tuple[int, str]]]:
    with open(path, encoding="utf-8") as handle:
        text = handle.read()

    keys = set(KEY_PATTERN.findall(text))
    references: list[tuple[int, str]] = []
    for number, line in enumerate(text.splitlines(), start=1):
        for name in REFERENCE_PATTERN.findall(line):
            references.append((number, name))

    return keys, references


def main() -> int:
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "src")
    root = os.path.abspath(root)

    files: list[str] = []
    for directory, _, names in os.walk(root):
        files.extend(os.path.join(directory, n) for n in sorted(names) if n.endswith(".xaml"))

    if not files:
        print(f"no XAML found under {root}")
        return 1

    per_file = {path: collect(path) for path in files}
    theme_keys = set(BUILTIN)
    for path, (keys, _) in per_file.items():
        if os.sep + "Themes" + os.sep in path:
            theme_keys |= keys

    problems = 0
    for path, (keys, references) in sorted(per_file.items()):
        visible = theme_keys if os.sep + "Themes" + os.sep in path else theme_keys | keys
        # App.xaml declares converters the windows rely on.
        if os.path.basename(path) != "App.xaml":
            visible = visible | per_file.get(os.path.join(root, "BluetoothManagerPro", "App.xaml"),
                                             (set(), []))[0]

        for number, name in references:
            if name not in visible:
                print(f"{os.path.relpath(path, root)}:{number}: unresolved resource '{name}'")
                problems += 1

    total = sum(len(refs) for _, refs in per_file.values())
    if problems:
        print(f"\n{problems} unresolved of {total} references across {len(files)} files")
        return 1

    print(f"all {total} resource references resolve across {len(files)} XAML files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
