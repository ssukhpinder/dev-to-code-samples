"""MCP extensions with one valid method and three invalid registration shapes."""

from collections.abc import Sequence
from typing import Any, Literal

import mcp.types as types
from mcp.server.context import ServerRequestContext
from mcp.server.extension import Extension, MethodBinding
from mcp.server.mcpserver import MCPServer

PROTOCOL_VERSION = "2026-07-28"
EXTENSION_ID = "com.example/catalog"
METHOD = "com.example/catalog.search"


class SearchParams(types.RequestParams):
    """Parameters accepted by the sample vendor method."""

    query: str


class SearchResult(types.Result):
    """Result returned by the sample vendor method."""

    items: list[str]


class SearchRequest(types.Request[SearchParams, Literal["com.example/catalog.search"]]):
    """Typed client request for the sample vendor method."""

    method: Literal["com.example/catalog.search"] = "com.example/catalog.search"
    params: SearchParams


async def search(ctx: ServerRequestContext[Any, Any], params: SearchParams) -> SearchResult:
    """Return deterministic catalog matches without external I/O."""
    del ctx
    return SearchResult(items=[f"{params.query}-{index}" for index in range(2)])


def search_binding(method: str = METHOD) -> MethodBinding:
    """Build a version-pinned extension method binding."""
    return MethodBinding(
        method,
        SearchParams,
        search,
        protocol_versions=frozenset({PROTOCOL_VERSION}),
    )


class CatalogSearch(Extension):
    """Valid extension that owns the catalog search method."""

    identifier = EXTENSION_ID

    def methods(self) -> Sequence[MethodBinding]:
        return [search_binding()]


class ShadowSearch(Extension):
    """Second extension that incorrectly claims the same method."""

    identifier = "com.example/catalog-shadow"

    def methods(self) -> Sequence[MethodBinding]:
        return [search_binding()]


def build_valid_server() -> MCPServer:
    """Construct the valid one-owner extension configuration."""
    return MCPServer("extension-contract", extensions=[CatalogSearch()], log_level="CRITICAL")


def build_duplicate_server() -> MCPServer:
    """Construct a server whose extensions collide on one method."""
    return MCPServer(
        "extension-contract",
        extensions=[CatalogSearch(), ShadowSearch()],
        log_level="CRITICAL",
    )


def build_core_method_binding() -> MethodBinding:
    """Try to claim a method reserved by the MCP specification."""
    return search_binding("tools/list")


def build_unreachable_binding() -> MethodBinding:
    """Try to register a vendor method for no protocol versions."""
    return MethodBinding(
        METHOD,
        SearchParams,
        search,
        protocol_versions=frozenset(),
    )
