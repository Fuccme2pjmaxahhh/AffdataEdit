"""Build the Windows x64 slow-playback plugin (AffStretch.dll) with Zig's cross-compiler.

pip install ziglang, then: python Native/AffStretch/build.py
"""
from pathlib import Path
import subprocess
import sys

source = Path(__file__).resolve().parent
project = source.parents[1]
third = project / "Native/ThirdParty"
target = project / "Assets/Plugins/AffStretch/AffStretch.dll"
staged = target.with_suffix(".tmp")
subprocess.run([
    sys.executable, "-m", "ziglang", "c++", "-target", "x86_64-windows-gnu", "-std=c++14", "-O2",
    "-Wall", "-Wextra", "-shared",
    "-I", str(third / "signalsmith-stretch/include"), "-I", str(third / "linear/include"),
    str(source / "AffStretch.cpp"), "-o", str(staged),
], check=True)
for leftover in (staged.with_suffix(".lib"), staged.with_suffix(".pdb")):
    leftover.unlink(missing_ok=True)
staged.replace(target)
print(f"Updated plugin: {target}")
