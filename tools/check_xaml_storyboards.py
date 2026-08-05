#!/usr/bin/env python3
"""Two things about the state storyboards that only ever fail once the app is running.

**Target names.** Interaction states are animated by storyboards held as shared resources:
one "fade the layer named Hover" storyboard serves every control template that has a layer
by that name. That is what keeps the styles from being a wall of copies — but it also means
a template and a storyboard can drift apart, and nothing catches it. Storyboard.TargetName
is resolved against the template's name scope when the animation *begins*, so a renamed
part builds cleanly, passes the resource check, and throws the first time a user hovers the
control. Here, the names a storyboard targets must be declared by the template that runs it.

**Mode-conditioned exits.** Lite mode is a condition on the triggers that enter a state, so
each state has an "and animated" branch and an "and lite" one. A trigger fires its
ExitActions whenever its condition set stops matching and cannot tell why — so putting the
mode in a branch that also *leaves* a state meant that turning lite mode off read as every
checkbox, switch and swatch being deselected at once, and the fade-out undid the branch
that had just lit them. Leaving a state has to be driven by the state alone.

Stopping a storyboard on exit is fine and is not flagged: the scan bar's repeating sweep
genuinely should stop when the mode changes, so the other one can take over.

Usage:  python3 tools/check_xaml_storyboards.py [src_dir]
Exit code is 1 if anything is unresolved or a mode-conditioned trigger begins a storyboard
on its way out.
"""

from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET

XAML = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

STATIC_RESOURCE = re.compile(r"^\{StaticResource\s+([^}\s]+)\s*\}$")

MODE_PROPERTY = "infra:ThemeProps.Animated"


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1] if "}" in tag else tag


def target_names(storyboard: ET.Element) -> set[str]:
    """Every name the storyboard's timelines address."""
    names = {
        node.get(f"{XAML}Storyboard.TargetName") or node.get("Storyboard.TargetName")
        for node in storyboard.iter()
    }
    return {name for name in names if name}


def declared_names(template: ET.Element) -> set[str]:
    return {node.get(f"{X}Name") for node in template.iter() if node.get(f"{X}Name")}


def collect_shared(tree: ET.ElementTree) -> dict[str, set[str]]:
    """Storyboards declared as resources, keyed by x:Key."""
    shared: dict[str, set[str]] = {}
    for node in tree.iter():
        if local(node.tag) == "Storyboard" and node.get(f"{X}Key"):
            shared[node.get(f"{X}Key")] = target_names(node)

    return shared


def check_mode_exits(tree: ET.ElementTree, path: str) -> list[str]:
    """Reports every mode-conditioned trigger that begins a storyboard on the way out."""
    problems: list[str] = []

    for trigger in tree.iter():
        if local(trigger.tag) != "MultiTrigger":
            continue

        conditions = [n for n in trigger.iter() if local(n.tag) == "Condition"]
        if MODE_PROPERTY not in [c.get("Property") for c in conditions]:
            continue

        for section in trigger:
            if local(section.tag) != "MultiTrigger.ExitActions":
                continue

            if any(local(action.tag) == "BeginStoryboard" for action in section):
                states = [c.get("Property") for c in conditions if c.get("Property") != MODE_PROPERTY]
                problems.append(
                    f"{path}: trigger on {', '.join(states) or '(mode only)'} begins a storyboard "
                    f"when its conditions stop matching — a mode change would run it and undo the state")

    return problems


def check_file(path: str, shared: dict[str, set[str]]) -> list[str]:
    """Reports every BeginStoryboard whose targets the enclosing template lacks."""
    tree = ET.parse(path)
    problems: list[str] = check_mode_exits(tree, path)

    # Walking templates rather than BeginStoryboards gives each one its name scope.
    for template in tree.iter():
        if local(template.tag) not in ("ControlTemplate", "DataTemplate"):
            continue

        available = declared_names(template)

        for node in template.iter():
            if local(node.tag) != "BeginStoryboard":
                continue

            reference = node.get("Storyboard")
            if reference:
                match = STATIC_RESOURCE.match(reference.strip())
                if not match:
                    problems.append(f"{path}: BeginStoryboard has a non-resource Storyboard")
                    continue

                key = match.group(1)
                if key not in shared:
                    problems.append(f"{path}: BeginStoryboard references unknown storyboard '{key}'")
                    continue

                wanted = shared[key]
            else:
                wanted = set()
                for child in node:
                    if local(child.tag) == "Storyboard":
                        wanted |= target_names(child)

            for name in sorted(wanted - available):
                problems.append(
                    f"{path}: storyboard targets '{name}', which no part of this template declares")

    return problems


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

    # Storyboard resources live in the theme dictionaries and are visible everywhere.
    shared: dict[str, set[str]] = {}
    for path in files:
        shared.update(collect_shared(ET.parse(path)))

    problems: list[str] = []
    checked = 0
    for path in files:
        found = check_file(path, shared)
        problems.extend(os.path.relpath(p, root) if p.startswith(root) else p for p in found)
        checked += 1

    if problems:
        for problem in problems:
            print(problem)

        print(f"\n{len(problems)} storyboard problem(s) across {checked} XAML files")
        return 1

    print(f"all {len(shared)} shared storyboards resolve in every template that runs them, "
          f"and no mode-conditioned trigger animates on exit")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
