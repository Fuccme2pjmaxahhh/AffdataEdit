"""Build AffStretch for Windows x64 (Zig) or macOS ARM64 (Xcode).

pip install ziglang, then: python Native/AffStretch/build.py
On macOS: python3 Native/AffStretch/build.py --target macos-arm64
"""
import argparse
from pathlib import Path
import subprocess
import sys

source = Path(__file__).resolve().parent
project = source.parents[1]
third = project / "Native/ThirdParty"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--target", choices=("windows-x64", "macos-arm64"), default="windows-x64")
args = parser.parse_args()
if args.target == "macos-arm64":
    target = project / "Assets/Plugins/AffStretch/macOS/libAffStretch.dylib"
    compiler = ["xcrun", "clang++", "-arch", "arm64", "-dynamiclib", "-mmacosx-version-min=11.0",
                "-Wl,-install_name,@rpath/libAffStretch.dylib"]
else:
    target = project / "Assets/Plugins/AffStretch/AffStretch.dll"
    compiler = [sys.executable, "-m", "ziglang", "c++", "-target", "x86_64-windows-gnu", "-shared"]
target.parent.mkdir(parents=True, exist_ok=True)
staged = target.with_suffix(".tmp")
subprocess.run(compiler + [
    "-std=c++14", "-O2", "-Wall", "-Wextra",
    "-I", str(third / "signalsmith-stretch/include"), "-I", str(third / "linear/include"),
    str(source / "AffStretch.cpp"), "-o", str(staged),
], check=True)
if args.target == "macos-arm64":
    subprocess.run(["codesign", "--force", "--sign", "-", str(staged)], check=True)
for leftover in (staged.with_suffix(".lib"), staged.with_suffix(".pdb")):
    leftover.unlink(missing_ok=True)
staged.replace(target)
print(f"Updated plugin: {target}")
