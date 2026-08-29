#!/usr/bin/env python3
"""Fail the build when backend coverage drops below the agreed floor.

The floor is deliberately below 100 percent. What is left uncovered is code whose
only untested path needs a live third party to exercise it: signing a real Google
ID token, completing a real VAPID Web Push handshake, exchanging a real Firebase
service account for an access token. Those adapters are covered for every branch
we can drive (unconfigured, rejected, gone, transient failure); the remaining
lines are the single call into the vendor SDK. Asserting 100 percent would mean
either deleting those guards or mocking the vendor SDK into meaninglessness.

Usage: check-coverage.py <cobertura.xml> [--min-line 95] [--min-branch 80]
"""

import argparse
import glob
import sys
import xml.etree.ElementTree as ET

PACKAGE_FLOORS = {
    "SplitEverything.Domain": 95.0,
    "SplitEverything.Application": 95.0,
    "SplitEverything.Infrastructure": 95.0,
    "SplitEverything.Api": 90.0,
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", nargs="?", default="backend/TestResults/*/coverage.cobertura.xml")
    parser.add_argument("--min-line", type=float, default=95.0)
    parser.add_argument("--min-branch", type=float, default=80.0)
    args = parser.parse_args()

    matches = sorted(glob.glob(args.report))
    if not matches:
        print(f"No coverage report matched {args.report}", file=sys.stderr)
        return 2

    root = ET.parse(matches[-1]).getroot()
    line_rate = float(root.get("line-rate", 0)) * 100
    branch_rate = float(root.get("branch-rate", 0)) * 100

    failures = []
    print(f"overall   lines {line_rate:5.1f}%  branches {branch_rate:5.1f}%")

    if line_rate < args.min_line:
        failures.append(f"overall line coverage {line_rate:.1f}% is below {args.min_line:.1f}%")
    if branch_rate < args.min_branch:
        failures.append(f"overall branch coverage {branch_rate:.1f}% is below {args.min_branch:.1f}%")

    for package in sorted(root.iter("package"), key=lambda p: p.get("name") or ""):
        name = package.get("name") or ""
        rate = float(package.get("line-rate", 0)) * 100
        floor = PACKAGE_FLOORS.get(name)
        status = "" if floor is None else f"  (floor {floor:.0f}%)"
        print(f"  {name:40s} lines {rate:5.1f}%{status}")
        if floor is not None and rate < floor:
            failures.append(f"{name} line coverage {rate:.1f}% is below {floor:.1f}%")

    if failures:
        print("\nCoverage gate failed:", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1

    print("\nCoverage gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
