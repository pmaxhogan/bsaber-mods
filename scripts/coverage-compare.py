"""Coverage ratchet: fail when line coverage drops more than MAX_DROP points vs the base branch.

Usage: coverage-compare.py BASE.xml HEAD.xml MAX_DROP
Both files are Cobertura reports. Only the MaxTweaks.Core package is counted, so test-assembly
lines never inflate the number. A missing base report (first PR that adds tests) passes.
"""

import os
import sys
import xml.etree.ElementTree as ET


def core_line_rate(path):
    root = ET.parse(path).getroot()
    covered = total = 0
    for package in root.iter("package"):
        if not package.get("name", "").startswith("MaxTweaks.Core") or package.get("name", "").endswith("Tests"):
            continue
        # Count class-level <lines> only; method-level <line> entries repeat the same lines.
        for cls in package.iter("class"):
            lines = cls.find("lines")
            for line in lines if lines is not None else []:
                total += 1
                if int(line.get("hits", "0")) > 0:
                    covered += 1
    return 100.0 * covered / total if total else 100.0


def main():
    base_path, head_path, max_drop = sys.argv[1], sys.argv[2], float(sys.argv[3])
    head = core_line_rate(head_path)
    if not os.path.exists(base_path):
        print(f"no base coverage report; head = {head:.2f}% (pass)")
        return 0
    base = core_line_rate(base_path)
    drop = base - head
    print(f"line coverage: base {base:.2f}% -> head {head:.2f}% (drop {drop:.2f} pt, max {max_drop})")
    if drop > max_drop:
        print("::error::coverage dropped more than allowed")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
