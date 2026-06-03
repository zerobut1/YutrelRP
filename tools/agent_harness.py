from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


DEFAULT_UNITY_EXE = Path(
    r"C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe"
)
PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOG_DIR = PROJECT_ROOT / "Logs" / "agent-harness"
SHADER_ROOT = PROJECT_ROOT / "Assets" / "YutrelRP" / "Shader"

LOG_ERROR_PATTERNS = [
    re.compile(pattern, re.IGNORECASE)
    for pattern in (
        r"\berror CS\d+",
        r"Shader error",
        r"Compilation failed",
        r"Scripts have compiler errors",
        r"Build failed",
        r"Unhandled exception",
        r"NullReferenceException",
        r"ArgumentException",
        r"InvalidOperationException",
        r"Assertion failed",
    )
]


def fail(message: str, code: int = 1) -> int:
    print(f"FAIL: {message}", file=sys.stderr)
    return code


def resolve_unity(args: argparse.Namespace) -> Path:
    value = args.unity or os.environ.get("UNITY_EDITOR")
    return Path(value) if value else DEFAULT_UNITY_EXE


def ensure_unity_available(unity: Path) -> int | None:
    if not unity.is_file():
        return fail(
            f"Unity editor was not found: {unity}. "
            "Pass --unity or set UNITY_EDITOR to override it."
        )
    return None


def ensure_project_unlocked(args: argparse.Namespace) -> int | None:
    lockfile = PROJECT_ROOT / "Temp" / "UnityLockfile"
    if lockfile.exists() and not args.allow_locked:
        return fail(
            f"Unity lockfile exists: {lockfile}. "
            "Close the editor or pass --allow-locked if this is intentional."
        )
    return None


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def scan_log(log_path: Path, limit: int = 80) -> list[str]:
    if not log_path.exists():
        return [f"Log file was not created: {log_path}"]

    findings: list[str] = []
    for line in read_text(log_path).splitlines():
        if any(pattern.search(line) for pattern in LOG_ERROR_PATTERNS):
            findings.append(line.strip())
            if len(findings) >= limit:
                findings.append(f"... truncated after {limit} findings")
                break
    return findings


def run_process(command: list[str], log_hint: Path | None = None) -> int:
    print("Running:")
    print(" ".join(f'"{part}"' if " " in part else part for part in command))
    if log_hint:
        print(f"Log: {log_hint}")
    completed = subprocess.run(command, cwd=PROJECT_ROOT)
    return completed.returncode


def run_unity(args: argparse.Namespace, extra_args: list[str], log_name: str) -> int:
    unity = resolve_unity(args)
    if error := ensure_unity_available(unity):
        return error
    if error := ensure_project_unlocked(args):
        return error

    log_dir = Path(args.log_dir).resolve() if args.log_dir else DEFAULT_LOG_DIR
    log_dir.mkdir(parents=True, exist_ok=True)
    log_path = log_dir / log_name

    command = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(PROJECT_ROOT),
        "-logFile",
        str(log_path),
        *extra_args,
    ]

    exit_code = run_process(command, log_path)
    findings = scan_log(log_path)
    if exit_code != 0:
        print_log_findings(findings)
        return fail(f"Unity exited with code {exit_code}. See {log_path}.", exit_code)
    if findings:
        print_log_findings(findings)
        return fail(f"Unity log contains error findings. See {log_path}.")

    print(f"PASS: Unity command completed without log errors. See {log_path}.")
    return 0


def print_log_findings(findings: list[str]) -> None:
    if not findings:
        return
    print("Log findings:", file=sys.stderr)
    for finding in findings:
        print(f"  {finding}", file=sys.stderr)


def command_compile(args: argparse.Namespace) -> int:
    return run_unity(args, ["-quit"], "compile.log")


def parse_test_results(results_path: Path) -> tuple[bool, str]:
    if not results_path.exists():
        return False, f"Test result XML was not created: {results_path}"

    try:
        root = ET.parse(results_path).getroot()
    except ET.ParseError as exc:
        return False, f"Could not parse test result XML: {exc}"

    failed = int(root.attrib.get("failed", root.attrib.get("failures", "0")))
    passed = int(root.attrib.get("passed", "0"))
    skipped = int(root.attrib.get("skipped", "0"))
    total = int(root.attrib.get("total", str(passed + failed + skipped)))
    result = root.attrib.get("result", "")

    failed_cases: list[str] = []
    for case in root.iter("test-case"):
        case_result = case.attrib.get("result", "")
        if case_result in {"Failed", "Error"}:
            name = case.attrib.get("fullname") or case.attrib.get("name") or "<unknown>"
            failed_cases.append(name)

    summary = (
        f"EditMode tests: total={total}, passed={passed}, "
        f"failed={failed}, skipped={skipped}, result={result or '<none>'}"
    )
    if failed_cases:
        summary += "\nFailed cases:\n" + "\n".join(f"  {name}" for name in failed_cases[:50])
        if len(failed_cases) > 50:
            summary += f"\n  ... truncated after 50 of {len(failed_cases)} failed cases"

    return failed == 0 and result != "Failed", summary


def command_test_editmode(args: argparse.Namespace) -> int:
    log_dir = Path(args.log_dir).resolve() if args.log_dir else DEFAULT_LOG_DIR
    log_dir.mkdir(parents=True, exist_ok=True)
    results_path = log_dir / "editmode-results.xml"

    exit_code = run_unity(
        args,
        [
            "-runTests",
            "-testPlatform",
            "EditMode",
            "-testResults",
            str(results_path),
            "-quit",
        ],
        "editmode.log",
    )
    ok, summary = parse_test_results(results_path)
    print(summary)
    if exit_code != 0:
        return exit_code
    if not ok:
        return fail(f"EditMode tests failed. See {results_path}.")
    print(f"PASS: EditMode tests passed. See {results_path}.")
    return 0


def command_shader_format(args: argparse.Namespace) -> int:
    clang_format = shutil.which(args.clang_format)
    if not clang_format:
        return fail(f"Could not find formatter executable: {args.clang_format}")
    if not SHADER_ROOT.is_dir():
        return fail(f"Shader directory was not found: {SHADER_ROOT}")

    shader_files = sorted(
        path
        for pattern in ("*.hlsl", "*.hlsli")
        for path in SHADER_ROOT.rglob(pattern)
    )
    if not shader_files:
        print(f"PASS: No HLSL files found under {SHADER_ROOT}.")
        return 0

    for path in shader_files:
        command = [clang_format, "-i", "--style=file", str(path)]
        completed = subprocess.run(command, cwd=PROJECT_ROOT)
        if completed.returncode != 0:
            return fail(f"clang-format failed for {path}", completed.returncode)

    print(f"PASS: Formatted {len(shader_files)} shader files under {SHADER_ROOT}.")
    return 0


def command_doctor(args: argparse.Namespace) -> int:
    unity = resolve_unity(args)
    checks = [
        ("Project root", PROJECT_ROOT.is_dir(), PROJECT_ROOT),
        ("Unity editor", unity.is_file(), unity),
        ("Shader root", SHADER_ROOT.is_dir(), SHADER_ROOT),
    ]

    lockfile = PROJECT_ROOT / "Temp" / "UnityLockfile"
    has_error = False
    for label, ok, path in checks:
        status = "OK" if ok else "MISSING"
        print(f"{status}: {label}: {path}")
        has_error = has_error or not ok

    if lockfile.exists():
        print(f"WARN: Unity lockfile exists: {lockfile}")
    else:
        print(f"OK: Unity lockfile is absent: {lockfile}")

    if DEFAULT_LOG_DIR.exists():
        print(f"OK: Harness log directory exists: {DEFAULT_LOG_DIR}")
    else:
        print(f"INFO: Harness log directory will be created on demand: {DEFAULT_LOG_DIR}")

    if has_error:
        return fail("Harness doctor found missing required paths.")
    print("PASS: Harness doctor completed.")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Agent harness for the YutrelRP Unity project."
    )
    parser.add_argument(
        "--unity",
        help="Unity editor executable path. Defaults to UNITY_EDITOR or the project AGENTS.md path.",
    )
    parser.add_argument(
        "--log-dir",
        help=f"Directory for harness logs. Defaults to {DEFAULT_LOG_DIR}.",
    )
    parser.add_argument(
        "--allow-locked",
        action="store_true",
        help="Run Unity even when Temp/UnityLockfile exists.",
    )

    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("compile", help="Open the project in Unity batchmode and check compile logs.")
    subparsers.add_parser("doctor", help="Check harness paths without launching Unity.")
    subparsers.add_parser("test-editmode", help="Run Unity EditMode tests and parse the XML result.")

    shader_parser = subparsers.add_parser(
        "shader-format",
        help="Format Assets/YutrelRP/Shader .hlsl and .hlsli files with clang-format.",
    )
    shader_parser.add_argument(
        "--clang-format",
        default="clang-format",
        help="Formatter executable name or path.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "compile":
        return command_compile(args)
    if args.command == "doctor":
        return command_doctor(args)
    if args.command == "test-editmode":
        return command_test_editmode(args)
    if args.command == "shader-format":
        return command_shader_format(args)

    parser.error(f"Unknown command: {args.command}")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
