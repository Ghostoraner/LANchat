#!/usr/bin/env python3
"""
strip_comments.py — вырезает комментарии из исходников проекта.

Исключает служебные директории и указанные файлы (publish.sh, publish.ps1).
"""

import sys
import os

# Папки, которые полностью пропускаем
SKIP_DIRS = {
    "bin", "obj", "node_modules", "target", ".git", 
    "dist", "build", "gen", "Received", ".vs", ".idea"
}

# Конкретные файлы, которые НЕЛЬЗЯ трогать
SKIP_FILES = {
    "publish.sh",
    "publish.ps1",
    "strip_comments.py",
    "install.ps1",
    "install.sh"
}


def collapse_blank_lines(text: str) -> str:
    """Убирает цепочки из более чем 2 пустых строк подряд."""
    lines = text.split("\n")
    cleaned = []
    blank_run = 0
    for line in lines:
        if line.strip() == "":
            blank_run += 1
            if blank_run > 2:
                continue
        else:
            blank_run = 0
        cleaned.append(line)
    return "\n".join(cleaned)


def strip_c_style(text: str, is_rust: bool = False) -> str:
    """Убирает // и /* */ из C#/Rust/JS/CSS с защитой строк и кавычек."""
    out = []
    i, n = 0, len(text)
    in_str = None      # '"', "'", '`'
    verbatim = False   # C# @"..." или $@"/@$"

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if in_str:
            out.append(c)
            if c == "\\" and not verbatim and in_str != "`":
                if i + 1 < n:
                    out.append(text[i + 1])
                    i += 2
                    continue
            elif c == in_str:
                if verbatim and in_str == '"' and nxt == '"':
                    out.append(nxt)
                    i += 2
                    continue
                in_str = None
                verbatim = False
            i += 1
            continue

        # Rust: обработка лайфтаймов ('a, '_, 'static)
        if c == "'" and is_rust:
            j = i + 1
            if j < n and text[j] == "\\":
                k = j + 1
                if k < n and text[k] == "u" and k + 1 < n and text[k + 1] == "{":
                    end_brace = text.find("}", k)
                    k = end_brace + 1 if end_brace != -1 else k + 1
                else:
                    k += 1
                if k < n and text[k] == "'":
                    out.append(text[i:k + 1])
                    i = k + 1
                    continue
            elif j < n and j + 1 < n and text[j + 1] == "'":
                out.append(text[i:j + 2])
                i = j + 2
                continue
            out.append(c)
            i += 1
            continue

        # Открытие строки / char-литерала
        if c in ('"', "'", '`'):
            if c == '"' and len(out) > 0:
                tail = "".join(out[-2:]) if len(out) >= 2 else out[-1]
                verbatim = "@" in tail
            in_str = c
            out.append(c)
            i += 1
            continue

        # Однострочные комментарии //
        if c == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue

        # Блочные комментарии /* */
        if c == "/" and nxt == "*":
            if is_rust:
                depth = 1
                i += 2
                while i + 1 < n and depth > 0:
                    if text[i] == "/" and text[i + 1] == "*":
                        depth += 1
                        i += 2
                    elif text[i] == "*" and text[i + 1] == "/":
                        depth -= 1
                        i += 2
                    else:
                        i += 1
                continue
            else:
                i += 2
                while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                    i += 1
                i += 2
                continue

        out.append(c)
        i += 1

    return collapse_blank_lines("".join(out))


def strip_shell(text: str) -> str:
    """Убирает # комментарии из Shell/Bash."""
    out = []
    i, n = 0, len(text)
    in_str = None
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\" and in_str != "'":
                if i + 1 < n:
                    out.append(text[i + 1])
                    i += 2
                    continue
            elif c == in_str:
                in_str = None
            i += 1
            continue

        if c in ('"', "'"):
            in_str = c
            out.append(c)
            i += 1
            continue

        if c == "#":
            if i == 0 and n > 1 and text[1] == "!":
                out.append(c)
                i += 1
                continue
            while i < n and text[i] != "\n":
                i += 1
            continue

        out.append(c)
        i += 1
    return collapse_blank_lines("".join(out))


def strip_powershell(text: str) -> str:
    """Убирает # и <# #> из PowerShell."""
    out = []
    i, n = 0, len(text)
    in_str = None
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if in_str:
            out.append(c)
            if c == "`":
                if i + 1 < n:
                    out.append(text[i + 1])
                    i += 2
                    continue
            elif c == in_str:
                in_str = None
            i += 1
            continue

        if c in ('"', "'"):
            in_str = c
            out.append(c)
            i += 1
            continue

        if c == "<" and nxt == "#":
            i += 2
            while i + 1 < n and not (text[i] == "#" and text[i + 1] == ">"):
                i += 1
            i += 2
            continue

        if c == "#":
            while i < n and text[i] != "\n":
                i += 1
            continue

        out.append(c)
        i += 1
    return collapse_blank_lines("".join(out))


def strip_html_xml(text: str) -> str:
    """Убирает <!-- --> из HTML, XAML, AXAML, XML, CSPROJ."""
    out = []
    i, n = 0, len(text)
    while i < n:
        if text[i:i + 4] == "<!--":
            end = text.find("-->", i + 4)
            if end == -1:
                break
            i = end + 3
            continue
        out.append(text[i])
        i += 1
    return collapse_blank_lines("".join(out))


def process_file(path: str, dry_run: bool) -> bool:
    # Игнорируем заблокированные файлы
    if os.path.basename(path) in SKIP_FILES:
        return False

    ext = os.path.splitext(path)[1].lower()
    try:
        with open(path, "r", encoding="utf-8") as f:
            original = f.read()
    except (UnicodeDecodeError, OSError):
        return False

    if ext in {".cs", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".less"}:
        result = strip_c_style(original, is_rust=False)
    elif ext == ".rs":
        result = strip_c_style(original, is_rust=True)
    elif ext in {".html", ".axaml", ".xaml", ".csproj", ".xml"}:
        result = strip_html_xml(original)
    elif ext in {".sh", ".bash"}:
        result = strip_shell(original)
    elif ext in {".ps1", ".psm1"}:
        result = strip_powershell(original)
    else:
        return False

    if result != original:
        print(f"{'[dry-run] ' if dry_run else ''}изменён: {path}")
        if not dry_run:
            with open(path, "w", encoding="utf-8") as f:
                f.write(result)
        return True
    return False


def main():
    args = sys.argv[1:]
    dry_run = "--dry-run" in args
    targets = [a for a in args if not a.startswith("--")]

    changed = 0
    if targets:
        for t in targets:
            if process_file(t, dry_run):
                changed += 1
    else:
        for root, dirs, files in os.walk("."):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for name in files:
                path = os.path.join(root, name)
                if process_file(path, dry_run):
                    changed += 1

    print(f"\n{'Будет изменено' if dry_run else 'Изменено'} файлов: {changed}")
    if not dry_run and changed:
        print("Готово. Для отмены: git restore .")


if __name__ == "__main__":
    main()