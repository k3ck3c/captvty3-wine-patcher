
#!/usr/bin/env python3
"""Find Wine prefix duplicates in a SlimToolkit JSON report."""

import argparse
import json
from pathlib import Path


def analyze(args):
    with open(args.report, encoding="utf-8") as f:
        report = json.load(f)

    groups = report["image_report"]["duplicates"]
    pairs = []
    total_bytes = 0

    for group in groups.values():
        paths = sorted(group["files"])
        sources = [
            p for p in paths
            if p.startswith(args.wine_root.rstrip("/") + "/")
        ]
        if not sources:
            continue

        for dest in paths:
            if not dest.startswith(args.prefix.rstrip("/") + "/"):
                continue
            if "/winsxs/" in dest.lower():
                continue

            pairs.append((dest, sources[0]))
            total_bytes += group["file_size"]

    with open(args.output, "w", encoding="utf-8") as f:
        for dest, src in pairs:
            f.write(f"{dest}\t{src}\n")

    print(f"Duplicate files: {len(pairs)}")
    print(f"Potential savings: {total_bytes / 1048576:.2f} MiB")
    print(f"Output: {args.output}")


def apply(args):
    import filecmp

    prefix = Path(args.prefix).resolve()
    wine_root = Path(args.wine_root).resolve()
    checked = errors = 0
    validated = []
    seen = set()

    with open(args.pairs, encoding="utf-8") as f:
        for number, line in enumerate(f, 1):
            try:
                dest, src = line.rstrip("\n").split("\t")
                d, s = Path(dest), Path(src)
                if dest in seen:
                    raise ValueError("duplicate destination")
                seen.add(dest)

                if not d.is_absolute() or not s.is_absolute():
                    raise ValueError("relative path")
                if not d.parent.resolve().is_relative_to(prefix):
                    raise ValueError("destination escapes Wine prefix via symlink")
                if not s.resolve().is_relative_to(wine_root):
                    raise ValueError("source escapes Wine installation via symlink")

                if not d.is_relative_to(prefix):
                    raise ValueError("destination outside Wine prefix")
                if not s.is_relative_to(wine_root):
                    raise ValueError("source outside Wine installation")
                if "/winsxs/" in dest.lower():
                    raise ValueError("winsxs destination")
                if d.is_symlink():
                    if d.readlink() != s:
                        raise ValueError("symlink points to wrong source")
                elif not d.is_file():
                    raise ValueError("missing destination")
                if not s.is_file():
                    raise ValueError("missing file or existing symlink")
                if not filecmp.cmp(d, s, shallow=False):
                    raise ValueError("different contents")
                validated.append((d, s))
                checked += 1
            except (ValueError, OSError) as e:
                print(f"ERROR line {number}: {e}")
                errors += 1

    print(f"Verified: {checked}; Errors: {errors}")
    if errors:
        raise SystemExit(1)
    if not args.execute:
        print("DRY RUN: no files modified")
        return


    import os
    import tempfile

    replaced = 0
    for d, s in validated:
        temporary = None
        try:
            if not d.is_file() or not s.is_file():
                raise ValueError("file changed since validation")
            if not filecmp.cmp(d, s, shallow=False):
                raise ValueError("contents changed since validation")
            if not d.parent.resolve().is_relative_to(prefix):
                raise ValueError("destination escapes Wine prefix")
            if not s.resolve().is_relative_to(wine_root):
                raise ValueError("source escapes Wine installation")

            if d.is_symlink():
                if d.readlink() != s:
                    raise ValueError("symlink changed since validation")
                continue

            fd, name = tempfile.mkstemp(prefix=".wine-dedup-", dir=d.parent)
            os.close(fd)
            temporary = Path(name)
            temporary.unlink()
            os.symlink(str(s), temporary)
            os.replace(temporary, d)
            temporary = None
            replaced += 1
        except (OSError, ValueError) as e:
            print(f"ERROR replacing {d}: {e}")
            raise SystemExit(1)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)

    print(f"Replaced: {replaced}")


parser = argparse.ArgumentParser(
    description="Wine prefix deduplication utility"
)
sub = parser.add_subparsers(dest="command", required=True)

p = sub.add_parser("analyze", help="Generate duplicate file pairs")
p.add_argument("report", help="SlimToolkit JSON report")
p.add_argument("--prefix", default="/home/captvty/.wine")
p.add_argument("--wine-root", default="/opt/wine-staging")
p.add_argument("--output", default="dedup-safe.tsv")
p.set_defaults(func=analyze)

p = sub.add_parser("apply", help="Verify duplicate file pairs (dry-run)")
p.add_argument("pairs", help="Tab-separated destination/source pairs")
p.add_argument("--prefix", default="/home/captvty/.wine")
p.add_argument("--wine-root", default="/opt/wine-staging")
p.add_argument("--execute", action="store_true", help="Enable file replacement")
p.set_defaults(func=apply)

args = parser.parse_args()
args.func(args)
