"""Small MCP server with explicit tool and protocol failure paths."""

from mcp import MCPError
from mcp.server.mcpserver import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from mcp.types import INVALID_PARAMS

CATALOG = {"Dune": "available"}

server = MCPServer("tool-error-contract", log_level="CRITICAL")


@server.tool()
def find_book(title: str) -> str:
    """Return a catalog status or an actionable failure for the calling model."""
    if title not in CATALOG:
        raise ToolError(f"No book titled {title!r}. Try Dune.")

    return f"{title} is {CATALOG[title]}."


@server.tool()
def require_catalog_ready() -> str:
    """Reject a request when server state makes a model retry unhelpful."""
    raise MCPError(INVALID_PARAMS, "Catalog is not initialized.")


@server.tool()
def crash_lookup(title: str) -> str:
    """Simulate an unexpected implementation failure for sanitization testing."""
    raise KeyError(f"private_inventory/{title}")
