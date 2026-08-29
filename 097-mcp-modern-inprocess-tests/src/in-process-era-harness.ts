import {
  Client,
  InMemoryTransport,
  SdkErrorCode,
  SdkHttpError,
  StreamableHTTPClientTransport,
} from "@modelcontextprotocol/client";
import { createMcpHandler, McpServer } from "@modelcontextprotocol/server";
import { pathToFileURL } from "node:url";
import * as z from "zod/v4";

const endpoint = new URL("http://test.invalid/mcp");
const clientIdentity = { name: "era-test-client", version: "1.0.0" };

export interface HarnessSnapshot {
  era: "legacy" | "modern";
  toolNames: string[];
  normalizedTicket: string;
  fetchCalls: number;
  sawSessionId: boolean;
}

export function createTicketServer(): McpServer {
  const server = new McpServer({
    name: "ticket-normalizer",
    version: "1.0.0",
  });

  server.registerTool(
    "normalize-ticket",
    {
      description: "Normalize a ticket label for deterministic comparisons",
      inputSchema: z.object({ value: z.string().min(1) }),
      outputSchema: z.object({ normalized: z.string() }),
    },
    async ({ value }) => {
      const normalized = value
        .trim()
        .toUpperCase()
        .replace(/[^A-Z0-9]+/g, "-")
        .replace(/^-|-$/g, "");

      return {
        content: [{ type: "text", text: normalized }],
        structuredContent: { normalized },
      };
    },
  );

  return server;
}

function connectedEra(client: Client): "legacy" | "modern" {
  const era = client.getProtocolEra();
  if (era !== "legacy" && era !== "modern") {
    throw new Error("Client did not report a protocol era after connect");
  }
  return era;
}

function readNormalizedTicket(structuredContent: unknown): string {
  if (
    typeof structuredContent !== "object" ||
    structuredContent === null ||
    !("normalized" in structuredContent) ||
    typeof structuredContent.normalized !== "string"
  ) {
    throw new Error("Tool result did not contain normalized structured output");
  }

  return structuredContent.normalized;
}

export async function runLegacyHarness(): Promise<HarnessSnapshot> {
  const [clientTransport, serverTransport] =
    InMemoryTransport.createLinkedPair();
  const server = createTicketServer();
  const client = new Client(clientIdentity);

  try {
    await server.connect(serverTransport);
    await client.connect(clientTransport);

    const { tools } = await client.listTools();
    const result = await client.callTool({
      name: "normalize-ticket",
      arguments: { value: "  incident 42  " },
    });

    return {
      era: connectedEra(client),
      toolNames: tools.map((tool) => tool.name).sort(),
      normalizedTicket: readNormalizedTicket(result.structuredContent),
      fetchCalls: 0,
      sawSessionId: false,
    };
  } finally {
    await client.close();
    await server.close();
  }
}

export async function runModernHarness(): Promise<HarnessSnapshot> {
  const handler = createMcpHandler(createTicketServer, { legacy: "reject" });
  let fetchCalls = 0;
  let sawSessionId = false;

  const injectedFetch: typeof fetch = async (input, init) => {
    fetchCalls += 1;
    const request = new Request(input, init);
    sawSessionId ||= request.headers.has("mcp-session-id");
    return handler.fetch(request);
  };

  const transport = new StreamableHTTPClientTransport(endpoint, {
    fetch: injectedFetch,
  });
  const client = new Client(clientIdentity, {
    versionNegotiation: { mode: { pin: "2026-07-28" } },
  });

  try {
    await client.connect(transport);

    const { tools } = await client.listTools();
    const result = await client.callTool({
      name: "normalize-ticket",
      arguments: { value: "  incident 42  " },
    });

    return {
      era: connectedEra(client),
      toolNames: tools.map((tool) => tool.name).sort(),
      normalizedTicket: readNormalizedTicket(result.structuredContent),
      fetchCalls,
      sawSessionId,
    };
  } finally {
    await client.close();
    await handler.close();
  }
}

export async function strictModernHandlerRejectsLegacyClient(): Promise<boolean> {
  const handler = createMcpHandler(createTicketServer, { legacy: "reject" });
  const transport = new StreamableHTTPClientTransport(endpoint, {
    fetch: (input, init) => handler.fetch(new Request(input, init)),
  });
  const client = new Client(clientIdentity);

  try {
    await client.connect(transport);
    return false;
  } catch (error) {
    return (
      error instanceof SdkHttpError &&
      error.status === 400 &&
      error.code === SdkErrorCode.ClientHttpNotImplemented &&
      error.message.includes("Unsupported protocol version: 2025-11-25") &&
      error.message.includes('"supported":["2026-07-28"]')
    );
  } finally {
    await client.close();
    await handler.close();
  }
}

export async function runVerification(): Promise<string> {
  const checks: string[] = [];
  const verify = (condition: boolean, message: string): void => {
    if (!condition) {
      throw new Error(`FAIL: ${message}`);
    }
    checks.push(message);
  };

  const legacy = await runLegacyHarness();
  const modern = await runModernHarness();

  verify(legacy.era === "legacy", "linked pair reports the legacy era");
  verify(modern.era === "modern", "fetch harness reports the modern era");
  verify(
    legacy.toolNames.join(",") === "normalize-ticket",
    "legacy harness lists the tool",
  );
  verify(
    modern.toolNames.join(",") === "normalize-ticket",
    "modern harness lists the tool",
  );
  verify(
    legacy.normalizedTicket === "INCIDENT-42",
    "legacy harness calls the tool",
  );
  verify(
    modern.normalizedTicket === "INCIDENT-42",
    "modern harness calls the tool",
  );
  verify(
    legacy.normalizedTicket === modern.normalizedTicket,
    "both eras preserve tool behavior",
  );
  verify(modern.fetchCalls > 0, "modern harness uses the injected fetch");
  verify(!modern.sawSessionId, "modern requests carry no session ID");
  verify(
    await strictModernHandlerRejectsLegacyClient(),
    "strict modern handler rejects a default legacy client",
  );

  return `PASS: ${checks.length}/10 in-process MCP era checks`;
}

if (
  process.argv[1] !== undefined &&
  import.meta.url === pathToFileURL(process.argv[1]).href
) {
  console.log(await runVerification());
}
