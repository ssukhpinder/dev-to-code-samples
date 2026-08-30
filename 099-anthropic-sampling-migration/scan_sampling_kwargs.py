"""Find sampling parameters removed from Anthropic Python SDK v1 message methods."""

from __future__ import annotations

import argparse
import ast
from collections.abc import Iterable, Sequence
from dataclasses import dataclass
from pathlib import Path

REMOVED_PARAMETERS = frozenset({"temperature", "top_p", "top_k"})
MESSAGE_METHODS = frozenset(
    {"create", "stream", "parse", "count_tokens", "tool_runner"}
)
IGNORED_DIRECTORIES = frozenset({".git", ".venv", "__pycache__", "venv"})


@dataclass(frozen=True, order=True)
class Finding:
    path: Path
    line: int
    column: int
    parameter: str
    method: str
    source: str

    def render(self) -> str:
        return (
            f"{self.path}:{self.line}:{self.column}: removed parameter "
            f"'{self.parameter}' passed to {self.method} ({self.source})"
        )


def _attribute_chain(node: ast.AST) -> tuple[str, ...]:
    parts: list[str] = []
    current = node
    while isinstance(current, ast.Attribute):
        parts.append(current.attr)
        current = current.value
    if isinstance(current, ast.Name):
        parts.append(current.id)
    return tuple(reversed(parts))


def _message_method(node: ast.AST) -> str | None:
    chain = _attribute_chain(node)
    if "messages" not in chain or not chain:
        return None

    message_index = len(chain) - 1 - chain[::-1].index("messages")
    tail = chain[message_index + 1 :]
    if not tail or "batches" in tail or tail[-1] not in MESSAGE_METHODS:
        return None

    return ".".join(chain[message_index:])


def _dict_keys(node: ast.AST) -> dict[str, tuple[int, int]] | None:
    if not isinstance(node, ast.Dict):
        return None

    keys: dict[str, tuple[int, int]] = {}
    for key in node.keys:
        if key is None:
            return None
        if not isinstance(key, ast.Constant) or not isinstance(key.value, str):
            return None
        keys[key.value] = (key.lineno, key.col_offset + 1)
    return keys


class SamplingVisitor(ast.NodeVisitor):
    def __init__(self, path: Path) -> None:
        self.path = path
        self.findings: list[Finding] = []
        self._scopes: list[dict[str, dict[str, tuple[int, int]] | None]] = [{}]

    def _bind(self, name: str, value: ast.AST) -> None:
        self._scopes[-1][name] = _dict_keys(value)

    def _resolve(self, name: str) -> dict[str, tuple[int, int]] | None:
        for scope in reversed(self._scopes):
            if name in scope:
                return scope[name]
        return None

    @staticmethod
    def _parameter_names(arguments: ast.arguments) -> set[str]:
        names = {
            argument.arg
            for argument in (
                *arguments.posonlyargs,
                *arguments.args,
                *arguments.kwonlyargs,
            )
        }
        if arguments.vararg is not None:
            names.add(arguments.vararg.arg)
        if arguments.kwarg is not None:
            names.add(arguments.kwarg.arg)
        return names

    def visit_Assign(self, node: ast.Assign) -> None:
        self.generic_visit(node.value)
        for target in node.targets:
            if isinstance(target, ast.Name):
                self._bind(target.id, node.value)

    def visit_AnnAssign(self, node: ast.AnnAssign) -> None:
        if node.value is not None:
            self.generic_visit(node.value)
            if isinstance(node.target, ast.Name):
                self._bind(node.target.id, node.value)

    def _visit_scoped_body(
        self, body: Sequence[ast.stmt], bound_names: Iterable[str] = ()
    ) -> None:
        self._scopes.append(dict.fromkeys(bound_names))
        try:
            for statement in body:
                self.visit(statement)
        finally:
            self._scopes.pop()

    def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
        self._visit_scoped_body(node.body, self._parameter_names(node.args))

    def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef) -> None:
        self._visit_scoped_body(node.body, self._parameter_names(node.args))

    def visit_ClassDef(self, node: ast.ClassDef) -> None:
        self._visit_scoped_body(node.body)

    def visit_Lambda(self, node: ast.Lambda) -> None:
        self._scopes.append(dict.fromkeys(self._parameter_names(node.args)))
        try:
            self.visit(node.body)
        finally:
            self._scopes.pop()

    def visit_Call(self, node: ast.Call) -> None:
        method = _message_method(node.func)
        if method is not None:
            for keyword in node.keywords:
                if keyword.arg in REMOVED_PARAMETERS:
                    self.findings.append(
                        Finding(
                            self.path,
                            keyword.value.lineno,
                            keyword.value.col_offset + 1,
                            keyword.arg,
                            method,
                            "keyword",
                        )
                    )
                    continue

                if keyword.arg is not None:
                    continue

                expanded_keys = _dict_keys(keyword.value)
                if expanded_keys is None and isinstance(keyword.value, ast.Name):
                    expanded_keys = self._resolve(keyword.value.id)
                if expanded_keys is None:
                    continue

                for parameter in sorted(REMOVED_PARAMETERS & expanded_keys.keys()):
                    line, column = expanded_keys[parameter]
                    self.findings.append(
                        Finding(
                            self.path,
                            line,
                            column,
                            parameter,
                            method,
                            "expanded dictionary",
                        )
                    )

        self.generic_visit(node)


def scan_source(source: str, path: Path = Path("<memory>")) -> list[Finding]:
    tree = ast.parse(source, filename=str(path))
    visitor = SamplingVisitor(path)
    visitor.visit(tree)
    return sorted(visitor.findings)


def _python_files(paths: Iterable[Path]) -> tuple[list[Path], list[str]]:
    files: set[Path] = set()
    errors: list[str] = []
    for path in paths:
        if not path.exists():
            errors.append(f"{path}: path does not exist")
            continue
        if path.is_file() and path.suffix == ".py":
            files.add(path)
            continue
        if path.is_file():
            errors.append(f"{path}: expected a Python file or directory")
            continue
        if not path.is_dir():
            continue
        for candidate in path.rglob("*.py"):
            if not any(part in IGNORED_DIRECTORIES for part in candidate.parts):
                files.add(candidate)
    if not files and not errors:
        errors.append("no Python files found in the requested paths")
    return sorted(files), errors


def scan_paths(paths: Iterable[Path]) -> tuple[list[Finding], list[str]]:
    findings: list[Finding] = []
    files, errors = _python_files(paths)
    for path in files:
        try:
            findings.extend(scan_source(path.read_text(encoding="utf-8"), path))
        except (OSError, UnicodeError, SyntaxError) as error:
            errors.append(f"{path}: {error}")
    return sorted(findings), errors


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Find temperature, top_p, and top_k passed to Anthropic message methods."
        )
    )
    parser.add_argument("paths", nargs="+", type=Path)
    args = parser.parse_args(argv)

    findings, errors = scan_paths(args.paths)
    for error in errors:
        print(f"ERROR {error}")
    for finding in findings:
        print(f"FAIL {finding.render()}")

    if errors:
        print(f"SCAN FAILED: {len(errors)} file(s) could not be inspected")
        return 2
    if findings:
        print(f"SCAN FAILED: {len(findings)} removed sampling parameter(s) found")
        return 1

    print("SCAN PASSED: no removed Anthropic sampling parameters found")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
