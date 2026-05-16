#!/usr/bin/env python3
"""Idempotent record-extension script for the chapter A.0' slice pattern.

Given a record-type name and a new field with a default value, finds every
literal-construction site for that record across the input files and adds
the new field with the default — only if the field is not already present
in the record body.

Two construction shapes are recognized:

  1. Inline single-line records:
       { Foo = 1; Bar = []; }
     The trailing field's `Bar = [...]` may include nested braces; we look
     for the outermost closing `}` on the same line.

  2. Multi-line records:
       { Foo = 1
         Bar = [ ... possibly multi-line ... ]
       }
     The closing `}` is on its own line (or trails the last field value).
     We use a brace-depth walk from the opening `{` to find the matching
     `}`; the new field gets inserted with indentation matching the prior
     last field.

Anchor patterns identify the record type. Two anchor modes:

  - `--anchor-field "Modules"` looks for `{ Modules = ...` shapes; this
    is how we identify `Catalog` literals when the type is structural.

  - `--anchor-type "RowsetBundle"` looks for `: <type> =` type annotations
    immediately before the `{`. Stricter; lower false-positive rate.

Idempotency: each candidate record body is scanned for the new field
name first; if present, the site is skipped (logged for visibility).
"""

import sys, re, argparse, pathlib


def find_matching_brace(text: str, open_idx: int) -> int:
    """Given index of '{' in text, return index of matching '}' or -1."""
    depth = 0
    i = open_idx
    in_str = False
    str_quote = None
    in_block_comment = False
    while i < len(text):
        c = text[i]
        # Block comment handling
        if in_block_comment:
            if c == '*' and i + 1 < len(text) and text[i + 1] == ')':
                in_block_comment = False
                i += 2
                continue
            i += 1
            continue
        if not in_str and c == '(' and i + 1 < len(text) and text[i + 1] == '*':
            in_block_comment = True
            i += 2
            continue
        # String handling (very simple — single quote " only; F# triple-quote
        # strings would need more work but our records don't typically
        # contain those in field-value positions).
        if in_str:
            if c == '\\' and i + 1 < len(text):
                i += 2
                continue
            if c == str_quote:
                in_str = False
                str_quote = None
            i += 1
            continue
        if c == '"':
            in_str = True
            str_quote = c
            i += 1
            continue
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def record_body_has_field(body: str, field_name: str) -> bool:
    """Naive but sufficient: regex `<name>\s*=`."""
    return re.search(rf"(?<![A-Za-z0-9_]){re.escape(field_name)}\s*=", body) is not None


def detect_indent(body: str) -> str:
    """Find the indent prefix of the LAST field-assignment line in the body."""
    last_indent = None
    last_field_pos = -1
    for m in re.finditer(r"^(\s+)([A-Z][A-Za-z0-9_']*)\s*=", body, re.MULTILINE):
        if m.start() > last_field_pos:
            last_field_pos = m.start()
            last_indent = m.group(1)
    return last_indent or "    "


def detect_field_align(body: str) -> str:
    """Find the spacing between the longest field name and `=`, so the
    inserted field aligns nicely. Returns the spacing string used by the
    last field (e.g., 'Triggers  = []' → '  ')."""
    last_spacing = None
    last_field_pos = -1
    for m in re.finditer(r"^\s+[A-Z][A-Za-z0-9_']*(\s*)=", body, re.MULTILINE):
        if m.start() > last_field_pos:
            last_field_pos = m.start()
            last_spacing = m.group(1) or " "
    return last_spacing or " "


def transform(text: str, anchor_pattern: re.Pattern, field_name: str,
              default_value: str) -> tuple[str, int, int]:
    """Returns (new_text, n_added, n_skipped_idempotent)."""
    out = []
    cursor = 0
    n_added = 0
    n_skipped = 0
    while cursor < len(text):
        m = anchor_pattern.search(text, cursor)
        if not m:
            out.append(text[cursor:])
            break
        # The anchor matches at the opening `{`; capture it.
        open_brace_idx = m.start("openbrace")
        close_brace_idx = find_matching_brace(text, open_brace_idx)
        if close_brace_idx < 0:
            # Couldn't find matching brace; copy through and continue.
            out.append(text[cursor:m.end()])
            cursor = m.end()
            continue
        body = text[open_brace_idx + 1:close_brace_idx]
        # Idempotency check.
        if record_body_has_field(body, field_name):
            out.append(text[cursor:close_brace_idx + 1])
            cursor = close_brace_idx + 1
            n_skipped += 1
            continue
        # Inline vs multi-line detection.
        is_multiline = '\n' in body
        if is_multiline:
            indent = detect_indent(body)
            spacing = detect_field_align(body)
            insertion = f"\n{indent}{field_name}{spacing}= {default_value}"
            # Insert before the closing brace. If the closing-brace line
            # has only whitespace before `}`, we want to insert before
            # the newline that precedes that whitespace.
            new_text = text[cursor:close_brace_idx] + insertion + "\n"
            # Re-add the trailing whitespace before `}`:
            # walk back from close_brace_idx to the newline.
            i = close_brace_idx - 1
            while i >= 0 and text[i] in ' \t':
                i -= 1
            if i >= 0 and text[i] == '\n':
                # Trim the trailing whitespace; we already added '\n'.
                # Reconstruct: text[cursor:i] + insertion + '\n' + close_brace
                # But we need the indentation before `}` too.
                trailing_ws = text[i + 1:close_brace_idx]
                new_text = text[cursor:i] + insertion + "\n" + trailing_ws + "}"
            else:
                new_text = text[cursor:close_brace_idx] + insertion + " }"
            out.append(new_text)
            cursor = close_brace_idx + 1
        else:
            # Inline: convert `{ ... last_field = X }` to
            # `{ ... last_field = X; FieldName = default }`.
            # Insert `; FieldName = default` before the closing `}`.
            insertion = f"; {field_name} = {default_value}"
            # Trim trailing whitespace before `}`.
            i = close_brace_idx - 1
            while i >= 0 and text[i] in ' \t':
                i -= 1
            new_text = text[cursor:i + 1] + insertion + text[i + 1:close_brace_idx + 1]
            out.append(new_text)
            cursor = close_brace_idx + 1
        n_added += 1
    return ("".join(out), n_added, n_skipped)


def build_anchor(anchor_kind: str, anchor_value: str) -> re.Pattern:
    if anchor_kind == "field":
        # `{ <Field> = ...`. Capture the `{` position via named group.
        return re.compile(
            rf"(?P<openbrace>\{{)\s*{re.escape(anchor_value)}\s*=",
            re.MULTILINE)
    if anchor_kind == "type":
        # `: <Type>(?: =)? \s* {`. The type annotation may precede the
        # `=` (let binding) or be an inline argument type.
        return re.compile(
            rf":\s*{re.escape(anchor_value)}(?:\.\w+)?\s*=\s*(?P<openbrace>\{{)",
            re.MULTILINE)
    raise ValueError(f"Unknown anchor kind: {anchor_kind}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--anchor-field", help="Field name that uniquely identifies the record type")
    ap.add_argument("--anchor-type",  help="Type annotation `: <Type>` that precedes the record")
    ap.add_argument("--field", required=True, help="New field name")
    ap.add_argument("--default", required=True, help="Default value expression")
    args = ap.parse_args()

    if not (args.anchor_field or args.anchor_type):
        ap.error("Need --anchor-field or --anchor-type")

    if args.anchor_field:
        anchor = build_anchor("field", args.anchor_field)
    else:
        anchor = build_anchor("type", args.anchor_type)

    total_added = 0
    total_skipped = 0
    for path_str in args.paths:
        p = pathlib.Path(path_str)
        if not p.exists() or p.suffix not in (".fs", ".fsx", ".fsproj"):
            continue
        text = p.read_text()
        new_text, added, skipped = transform(text, anchor, args.field, args.default)
        total_added += added
        total_skipped += skipped
        if new_text != text:
            p.write_text(new_text)
            print(f"FIXED {p}: +{added} sites" + (f" ({skipped} already had {args.field})" if skipped else ""))
        elif skipped:
            print(f"SKIP  {p}: {skipped} site(s) already had {args.field}")
    print(f"---\nTotal: +{total_added} field additions; {total_skipped} idempotent skips.")


if __name__ == "__main__":
    main()
