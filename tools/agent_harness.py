from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOG_DIR = PROJECT_ROOT / "Logs" / "agent-harness"
SHADER_ROOT = PROJECT_ROOT / "Assets" / "YutrelRP" / "Shader"
CSHARP_LOG_DIR = DEFAULT_LOG_DIR / "csharp-compile"

RUNTIME_PROJECT = PROJECT_ROOT / "Assembly-CSharp.csproj"
EDITOR_PROJECT = PROJECT_ROOT / "Assembly-CSharp-Editor.csproj"
FAST_RUNTIME_PROJECT = PROJECT_ROOT / "AgentFastCompile.Assembly-CSharp.csproj"
FAST_EDITOR_PROJECT = PROJECT_ROOT / "AgentFastCompile.Assembly-CSharp-Editor.csproj"


def fail(message: str, code: int = 1) -> int:
    print(f"FAIL: {message}", file=sys.stderr)
    return code


def is_editor_path(path: Path) -> bool:
    return any(part.lower() == "editor" for part in path.parts)


def discover_runtime_sources() -> list[Path]:
    assets = PROJECT_ROOT / "Assets"
    if not assets.is_dir():
        return []
    return sorted(
        path
        for path in assets.rglob("*.cs")
        if not is_editor_path(path.relative_to(PROJECT_ROOT))
    )


def discover_editor_sources() -> list[Path]:
    assets = PROJECT_ROOT / "Assets"
    if not assets.is_dir():
        return []
    return sorted(
        path
        for path in assets.rglob("*.cs")
        if is_editor_path(path.relative_to(PROJECT_ROOT))
    )


def set_property(root: ET.Element, name: str, value: str) -> None:
    for group in root.findall("PropertyGroup"):
        child = group.find(name)
        if child is not None:
            child.text = value
            return

    group = root.find("PropertyGroup")
    if group is None:
        group = ET.SubElement(root, "PropertyGroup")
    ET.SubElement(group, name).text = value


def remove_items(root: ET.Element, tag: str) -> None:
    for group in root.findall("ItemGroup"):
        for item in list(group):
            if item.tag == tag:
                group.remove(item)


def replace_project_reference(root: ET.Element, source: str, replacement: Path) -> None:
    for item in root.iter("ProjectReference"):
        include = item.attrib.get("Include", "")
        if include.lower() == source.lower():
            item.attrib["Include"] = str(replacement.relative_to(PROJECT_ROOT))


def add_compile_items(root: ET.Element, sources: list[Path]) -> None:
    group = ET.SubElement(root, "ItemGroup")
    for source in sources:
        item = ET.SubElement(group, "Compile")
        item.attrib["Include"] = str(source.relative_to(PROJECT_ROOT))


def prepare_fast_project(
    source_project: Path,
    target_project: Path,
    sources: list[Path],
    obj_name: str,
    project_reference: Path | None = None,
) -> None:
    root = ET.parse(source_project).getroot()

    remove_items(root, "Compile")
    add_compile_items(root, sources)
    if project_reference is not None:
        replace_project_reference(root, "Assembly-CSharp.csproj", project_reference)

    obj_dir = CSHARP_LOG_DIR / "obj" / obj_name
    bin_dir = CSHARP_LOG_DIR / "bin" / "Debug"
    set_property(root, "BaseIntermediateOutputPath", str(obj_dir) + os.sep)
    set_property(root, "IntermediateOutputPath", "$(BaseIntermediateOutputPath)")
    set_property(root, "OutputPath", str(bin_dir) + os.sep)

    ET.indent(root, space="  ")
    target_project.write_text(
        ET.tostring(root, encoding="unicode"),
        encoding="utf-8",
        newline="\n",
    )


def run_logged(command: list[str], timeout: int, log_path: Path) -> int:
    print("Running:")
    print(" ".join(f'"{part}"' if " " in part else part for part in command))
    print(f"Log: {log_path}")
    print(f"Timeout: {timeout}s")

    log_path.parent.mkdir(parents=True, exist_ok=True)
    with log_path.open("w", encoding="utf-8", newline="\n") as log_file:
        try:
            completed = subprocess.run(
                command,
                cwd=PROJECT_ROOT,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout,
            )
        except subprocess.TimeoutExpired as exc:
            output = exc.stdout or ""
            if isinstance(output, bytes):
                output = output.decode("utf-8", errors="replace")
            log_file.write(output)
            if output:
                print(output, end="")
            return fail(f"Command timed out after {timeout}s.", 124)

        log_file.write(completed.stdout)
        print(completed.stdout, end="")
        return completed.returncode


def command_csharp_compile(args: argparse.Namespace) -> int:
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        return fail(f"Could not find dotnet executable: {args.dotnet}")
    if not RUNTIME_PROJECT.exists():
        return fail(f"Missing Unity generated project: {RUNTIME_PROJECT}")
    if not EDITOR_PROJECT.exists():
        return fail(f"Missing Unity generated project: {EDITOR_PROJECT}")

    runtime_sources = discover_runtime_sources()
    editor_sources = discover_editor_sources()
    if not runtime_sources and not editor_sources:
        return fail("No C# sources found under Assets.")

    CSHARP_LOG_DIR.mkdir(parents=True, exist_ok=True)
    for transient in (FAST_RUNTIME_PROJECT, FAST_EDITOR_PROJECT):
        if transient.exists():
            transient.unlink()

    try:
        prepare_fast_project(
            RUNTIME_PROJECT,
            FAST_RUNTIME_PROJECT,
            runtime_sources,
            "Assembly-CSharp",
        )
        prepare_fast_project(
            EDITOR_PROJECT,
            FAST_EDITOR_PROJECT,
            editor_sources,
            "Assembly-CSharp-Editor",
            FAST_RUNTIME_PROJECT,
        )

        projects: list[Path] = []
        if args.assembly in {"all", "runtime"} and runtime_sources:
            projects.append(FAST_RUNTIME_PROJECT)
        if args.assembly in {"all", "editor"} and editor_sources:
            projects.append(FAST_EDITOR_PROJECT)

        for project in projects:
            log_path = CSHARP_LOG_DIR / f"{project.stem}.log"
            code = run_logged(
                [
                    dotnet,
                    "build",
                    str(project),
                    "--nologo",
                    "--verbosity:minimal",
                ],
                args.timeout,
                log_path,
            )
            if code != 0:
                return fail(f"C# fast compile failed for {project.name}. See {log_path}.", code)
    finally:
        for transient in (FAST_RUNTIME_PROJECT, FAST_EDITOR_PROJECT):
            if transient.exists():
                transient.unlink()

    print(
        "PASS: C# fast compile passed "
        f"(runtime sources={len(runtime_sources)}, editor sources={len(editor_sources)})."
    )
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
        completed = subprocess.run(
            [clang_format, "-i", "--style=file", str(path)],
            cwd=PROJECT_ROOT,
        )
        if completed.returncode != 0:
            return fail(f"clang-format failed for {path}", completed.returncode)

    print(f"PASS: Formatted {len(shader_files)} shader files under {SHADER_ROOT}.")
    return 0


def command_doctor(args: argparse.Namespace) -> int:
    checks = [
        ("Project root", PROJECT_ROOT.is_dir(), PROJECT_ROOT),
        ("Runtime csproj", RUNTIME_PROJECT.exists(), RUNTIME_PROJECT),
        ("Editor csproj", EDITOR_PROJECT.exists(), EDITOR_PROJECT),
        ("dotnet", shutil.which(args.dotnet) is not None, args.dotnet),
        ("Shader root", SHADER_ROOT.is_dir(), SHADER_ROOT),
    ]

    has_error = False
    for label, ok, path in checks:
        print(f"{'OK' if ok else 'MISSING'}: {label}: {path}")
        has_error = has_error or not ok

    print(f"INFO: Runtime C# sources: {len(discover_runtime_sources())}")
    print(f"INFO: Editor C# sources: {len(discover_editor_sources())}")
    if has_error:
        return fail("Harness doctor found missing required paths.")
    print("PASS: Harness doctor completed.")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Agent harness for the YutrelRP Unity project."
    )
    parser.add_argument(
        "--log-dir",
        help=argparse.SUPPRESS,
    )

    subparsers = parser.add_subparsers(dest="command", required=True)

    csharp_parser = subparsers.add_parser(
        "csharp-compile",
        help="Fast C# compile using Unity-generated csproj files without launching Unity.",
    )
    csharp_parser.add_argument("--dotnet", default="dotnet", help="dotnet executable name or path.")
    csharp_parser.add_argument(
        "--timeout",
        type=int,
        default=30,
        help="Maximum seconds per dotnet build invocation.",
    )
    csharp_parser.add_argument(
        "--assembly",
        choices=("all", "runtime", "editor"),
        default="all",
        help="Which generated assembly shape to compile.",
    )

    compile_parser = subparsers.add_parser(
        "compile",
        help="Alias for csharp-compile; does not launch Unity.",
    )
    compile_parser.add_argument("--dotnet", default="dotnet", help="dotnet executable name or path.")
    compile_parser.add_argument("--timeout", type=int, default=30)
    compile_parser.add_argument(
        "--assembly",
        choices=("all", "runtime", "editor"),
        default="all",
    )

    doctor_parser = subparsers.add_parser(
        "doctor",
        help="Check harness paths without launching Unity.",
    )
    doctor_parser.add_argument("--dotnet", default="dotnet", help="dotnet executable name or path.")

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

    if args.command in {"compile", "csharp-compile"}:
        return command_csharp_compile(args)
    if args.command == "doctor":
        return command_doctor(args)
    if args.command == "shader-format":
        return command_shader_format(args)

    parser.error(f"Unknown command: {args.command}")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
