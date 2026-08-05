#!/usr/bin/env python3
"""Four rules about the state storyboards, each of which only ever fails once the app runs.

The XAML compiler checks none of them, and WPF reports none of them: every one of these
mistakes builds cleanly and then quietly does nothing, or does the wrong thing, on a user's
machine. Three shipped defects came from these four rules before they were written down.

**Target names.** Interaction states are animated by storyboards held as shared resources:
one "fade the layer named Hover" storyboard serves every control template that has a layer
by that name. That is what keeps the styles from being a wall of copies — but it also means
a template and a storyboard can drift apart, and nothing catches it. Storyboard.TargetName
is resolved against the template's name scope when the animation *begins*, so a renamed
part builds cleanly, passes the resource check, and throws the first time a user hovers the
control. Here, the names a storyboard targets must be declared by the template that runs it.

**Shared storyboards versus template transforms.** A storyboard held in a resource
dictionary cannot animate a transform declared inside a control template. WPF works out
which of a template's freezables have to stay mutable by reading the template, and a
storyboard living in another dictionary is not part of what it reads — so the transform is
frozen, the animation is dropped without a word, and the control simply never moves. This
is what stopped every toggle switch from throwing. Shared storyboards must therefore target
properties of the elements themselves (Opacity, Margin), never a named transform.

**Animations must not hold values.** The states themselves are Setters; the storyboards
only walk a property to the value the Setter already gives it. That works because every
state storyboard ends with FillBehavior="Stop" and hands the property back when it
finishes. Let one hold its end value instead and it outranks the Setter, and the interface
starts depending on the animation having run — which is how switching lite mode off blanked
every checkbox and how both tabs ended up lit at once.

**Mode-conditioned exits.** A trigger fires its ExitActions whenever its condition set stops
matching and cannot tell why, so a trigger that is conditioned on lite mode runs them when
the *mode* changes. That is only safe if the same trigger also carries the Setters for the
state, so the value it is walking towards is one it owns; otherwise it undoes a state some
other trigger is asserting.

Usage:  python3 tools/check_xaml_storyboards.py [src_dir]
Exit code is 1 if any of the four rules is broken.
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


def transform_names(template: ET.Element) -> set[str]:
    """Named transforms in the template — the things a shared storyboard must not touch."""
    return {
        node.get(f"{X}Name")
        for node in template.iter()
        if node.get(f"{X}Name") and local(node.tag).endswith("Transform")
    }


def collect_shared(tree: ET.ElementTree) -> dict[str, set[str]]:
    """Storyboards declared as resources, keyed by x:Key."""
    shared: dict[str, set[str]] = {}
    for node in tree.iter():
        if local(node.tag) == "Storyboard" and node.get(f"{X}Key"):
            shared[node.get(f"{X}Key")] = target_names(node)

    return shared


def check_fill_behaviour(tree: ET.ElementTree, path: str) -> list[str]:
    """Every animation in a shared state storyboard must give the property back when done."""
    problems: list[str] = []

    for node in tree.iter():
        key = node.get(f"{X}Key")
        if local(node.tag) != "Storyboard" or not key:
            continue

        for animation in node:
            if not local(animation.tag).endswith("Animation"):
                continue

            if animation.get("FillBehavior") != "Stop":
                problems.append(
                    f"{path}: '{key}' animates {animation.get('Storyboard.TargetName')}."
                    f"{animation.get('Storyboard.TargetProperty')} without FillBehavior=\"Stop\" — "
                    f"it would hold its end value and outrank the Setter that carries the state")

    return problems


def check_mode_exits(tree: ET.ElementTree, path: str) -> list[str]:
    """
    Reports mode-conditioned triggers that animate on the way out without owning the state.

    Such a trigger runs its ExitActions when the *mode* changes, not only when the state
    ends. That is fine when the trigger also holds the Setters for that state — it is then
    walking towards a value it is itself removing — and wrong otherwise.
    """
    problems: list[str] = []

    for trigger in tree.iter():
        if local(trigger.tag) != "MultiTrigger":
            continue

        conditions = [n for n in trigger.iter() if local(n.tag) == "Condition"]
        if MODE_PROPERTY not in [c.get("Property") for c in conditions]:
            continue

        owns_state = any(local(child.tag) == "Setter" for child in trigger)

        for section in trigger:
            if local(section.tag) != "MultiTrigger.ExitActions" or owns_state:
                continue

            if any(local(action.tag) == "BeginStoryboard" for action in section):
                states = [c.get("Property") for c in conditions if c.get("Property") != MODE_PROPERTY]
                problems.append(
                    f"{path}: trigger on {', '.join(states) or '(mode only)'} begins a storyboard "
                    f"when its conditions stop matching, but carries no Setter of its own — "
                    f"a mode change would run it and undo a state it does not own")

    return problems


def check_file(path: str, shared: dict[str, set[str]]) -> list[str]:
    """Reports every BeginStoryboard whose targets the enclosing template lacks."""
    tree = ET.parse(path)
    problems: list[str] = check_mode_exits(tree, path) + check_fill_behaviour(tree, path)

    # Walking templates rather than BeginStoryboards gives each one its name scope.
    for template in tree.iter():
        if local(template.tag) not in ("ControlTemplate", "DataTemplate"):
            continue

        available = declared_names(template)
        transforms = transform_names(template)

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

                # Only shared storyboards are affected; an inline one is read along with the
                # template, so WPF keeps the transform it names mutable.
                for name in sorted(wanted & transforms):
                    problems.append(
                        f"{path}: shared storyboard '{key}' animates the transform '{name}' — "
                        f"WPF freezes a template transform no inline storyboard names, so this "
                        f"animation would be discarded in silence")
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
          f"no shared storyboard holds a value,\n"
          f"and no mode-conditioned trigger undoes a state it does not own")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
