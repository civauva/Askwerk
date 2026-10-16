# SPDX-License-Identifier: Apache-2.0
"""Builds the askwerk wheel with the standard library only (no build package): python build_wheel.py <out-dir>."""
import base64
import hashlib
import os
import sys
import tomllib
import zipfile

here = os.path.dirname(os.path.abspath(__file__))
project = tomllib.load(open(os.path.join(here, "pyproject.toml"), "rb"))["project"]
name, version = project["name"], project["version"]
dist_info = f"{name}-{version}.dist-info"
out = sys.argv[1]
readme = open(os.path.join(here, "..", "README.md"), encoding="utf-8").read()

files = {}
for file in sorted(os.listdir(os.path.join(here, "askwerk"))):
    if file.endswith(".py") or file == "py.typed":
        files[f"askwerk/{file}"] = open(os.path.join(here, "askwerk", file), "rb").read()
files[f"{dist_info}/METADATA"] = (
    f"Metadata-Version: 2.4\nName: {name}\nVersion: {version}\nSummary: {project['description']}\n"
    f"License-Expression: {project['license']}\nRequires-Python: {project['requires-python']}\n"
    f"Description-Content-Type: text/markdown\n\n{readme}"
).encode()
files[f"{dist_info}/WHEEL"] = b"Wheel-Version: 1.0\nGenerator: askwerk build_wheel.py\nRoot-Is-Purelib: true\nTag: py3-none-any\n"


def digest(data: bytes) -> str:
    return "sha256=" + base64.urlsafe_b64encode(hashlib.sha256(data).digest()).rstrip(b"=").decode()


record = "".join(f"{path},{digest(data)},{len(data)}\n" for path, data in files.items()) + f"{dist_info}/RECORD,,\n"
target = os.path.join(out, f"{name}-{version}-py3-none-any.whl")
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as wheel:
    for path, data in files.items():
        wheel.writestr(path, data)
    wheel.writestr(f"{dist_info}/RECORD", record)
print(target)
