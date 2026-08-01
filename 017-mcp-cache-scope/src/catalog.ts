import {
  Client,
  InMemoryResponseCacheStore,
  StreamableHTTPClientTransport,
} from "@modelcontextprotocol/client";
import {
  createMcpHandler,
  McpServer,
  type McpHttpHandler,
} from "@modelcontextprotocol/server";

const SERVER_INFO = {
  name: "private-tool-catalog",
  version: "1.0.0",
};

const CLIENT_INFO = {
  name: "shared-mcp-gateway",
  version: "1.0.0",
};

export type CatalogEndpoint = {
  handler: McpHttpHandler;
  fetch: typeof globalThis.fetch;
  toolsListRequests: () => number;
};

export function createCatalogEndpoint(toolName: string): CatalogEndpoint {
  let listRequests = 0;

  const handler = createMcpHandler(
    () => {
      const server = new McpServer(SERVER_INFO, {
        cacheHints: {
          "tools/list": {
            ttlMs: 60_000,
            cacheScope: "private",
          },
        },
      });

      server.registerTool(
        toolName,
        {
          description: `Visible only to the ${toolName} authorization context`,
        },
        async () => ({
          content: [{ type: "text", text: toolName }],
        }),
      );

      return server;
    },
    { legacy: "reject" },
  );

  const fetch: typeof globalThis.fetch = async (input, init) => {
    const request = new Request(input, init);
    const body = (await request.clone().json()) as { method?: string };

    if (body.method === "tools/list") {
      listRequests += 1;
    }

    return handler.fetch(request);
  };

  return {
    handler,
    fetch,
    toolsListRequests: () => listRequests,
  };
}

export async function connectClient(
  endpoint: CatalogEndpoint,
  responseCacheStore: InMemoryResponseCacheStore,
  cachePartition?: string,
): Promise<Client> {
  const client = new Client(CLIENT_INFO, {
    responseCacheStore,
    cachePartition,
    versionNegotiation: { mode: { pin: "2026-07-28" } },
  });

  const transport = new StreamableHTTPClientTransport(
    new URL("https://mcp.test/catalog"),
    { fetch: endpoint.fetch },
  );

  await client.connect(transport);
  return client;
}
