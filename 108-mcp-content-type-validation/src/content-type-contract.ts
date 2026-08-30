import {
  CLIENT_CAPABILITIES_META_KEY,
  PROTOCOL_VERSION_META_KEY,
  createMcpHandler,
  McpServer,
} from "@modelcontextprotocol/server";
import { pathToFileURL } from "node:url";
import * as z from "zod/v4";

const endpoint = new URL("https://contract.invalid/mcp");
const protocolVersion = "2026-07-28";
const expectedCaseCount = 8;

export interface MediaTypeCase {
  label: string;
  contentType?: string;
  expectedStatus: number;
  expectedDispatched: number;
}

export interface MediaTypeResult {
  label: string;
  status: number;
  dispatched: number;
}

export const mediaTypeCases: readonly MediaTypeCase[] = [
  {
    label: "exact application/json",
    contentType: "application/json",
    expectedStatus: 200,
    expectedDispatched: 1,
  },
  {
    label: "JSON with charset",
    contentType: "application/json; charset=utf-8",
    expectedStatus: 200,
    expectedDispatched: 1,
  },
  {
    label: "mixed-case JSON",
    contentType: "Application/Json",
    expectedStatus: 200,
    expectedDispatched: 1,
  },
  {
    label: "JSON with trailing semicolon",
    contentType: "application/json;",
    expectedStatus: 200,
    expectedDispatched: 1,
  },
  {
    label: "substring spoof",
    contentType: "text/plain; a=application/json",
    expectedStatus: 415,
    expectedDispatched: 0,
  },
  {
    label: "JSON prefix subtype",
    contentType: "application/json-seq",
    expectedStatus: 415,
    expectedDispatched: 0,
  },
  {
    label: "missing Content-Type",
    expectedStatus: 415,
    expectedDispatched: 0,
  },
  {
    label: "structured suffix only",
    contentType: "application/problem+json",
    expectedStatus: 415,
    expectedDispatched: 0,
  },
] as const;

function buildRequest(contentType: string | undefined): Request {
  const headers = new Headers({
    Accept: "application/json, text/event-stream",
    "Mcp-Method": "tools/call",
    "Mcp-Name": "echo-media-type",
    "MCP-Protocol-Version": protocolVersion,
  });

  if (contentType !== undefined) {
    headers.set("Content-Type", contentType);
  }

  const body = {
    jsonrpc: "2.0",
    id: 1,
    method: "tools/call",
    params: {
      name: "echo-media-type",
      arguments: { value: "HEADER_OK" },
      _meta: {
        [PROTOCOL_VERSION_META_KEY]: protocolVersion,
        [CLIENT_CAPABILITIES_META_KEY]: {},
      },
    },
  };

  return new Request(endpoint, {
    method: "POST",
    headers,
    body: new TextEncoder().encode(JSON.stringify(body)),
  });
}

export async function runCase(
  testCase: MediaTypeCase,
): Promise<MediaTypeResult> {
  let toolCalls = 0;
  const handler = createMcpHandler(
    () => {
      const server = new McpServer({
        name: "content-type-contract",
        version: "1.0.0",
      });

      server.registerTool(
        "echo-media-type",
        {
          description: "Echo a deterministic value after header validation",
          inputSchema: z.object({ value: z.string() }),
        },
        async ({ value }) => {
          toolCalls += 1;
          return { content: [{ type: "text", text: value }] };
        },
      );

      return server;
    },
    { legacy: "reject", onerror: () => {} },
  );

  try {
    const response = await handler.fetch(buildRequest(testCase.contentType));
    await response.arrayBuffer();
    return {
      label: testCase.label,
      status: response.status,
      dispatched: toolCalls,
    };
  } finally {
    await handler.close();
  }
}

export async function runMatrix(): Promise<MediaTypeResult[]> {
  const results: MediaTypeResult[] = [];
  for (const testCase of mediaTypeCases) {
    results.push(await runCase(testCase));
  }
  return results;
}

export async function runVerification(): Promise<string> {
  if (mediaTypeCases.length !== expectedCaseCount) {
    throw new Error(
      `FAIL: expected ${expectedCaseCount} matrix cases, found ${mediaTypeCases.length}`,
    );
  }

  const results = await runMatrix();
  const lines: string[] = [];

  for (const [index, result] of results.entries()) {
    const expected = mediaTypeCases[index];
    if (expected === undefined) {
      throw new Error(`Unexpected matrix result at index ${index}`);
    }
    if (
      result.status !== expected.expectedStatus ||
      result.dispatched !== expected.expectedDispatched
    ) {
      throw new Error(
        `FAIL: ${result.label}: expected ${expected.expectedStatus}/${expected.expectedDispatched}, got ${result.status}/${result.dispatched}`,
      );
    }
    lines.push(
      `[PASS] ${result.label}: ${result.status}, dispatched=${result.dispatched}`,
    );
  }

  lines.push(
    `PASS: ${results.length}/${expectedCaseCount} Content-Type checks passed`,
  );
  return lines.join("\n");
}

if (
  process.argv[1] !== undefined &&
  import.meta.url === pathToFileURL(process.argv[1]).href
) {
  console.log(await runVerification());
}
