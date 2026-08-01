import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { fileURLToPath } from "node:url";
import { Readable } from "node:stream";
import test from "node:test";

const discoverRequest = {
  jsonrpc: "2.0",
  id: "discover-1",
  method: "server/discover",
  params: {
    _meta: {
      "io.modelcontextprotocol/protocolVersion": "2026-07-28",
      "io.modelcontextprotocol/clientInfo": {
        name: "stdio-contract-test",
        version: "1.0.0",
      },
      "io.modelcontextprotocol/clientCapabilities": {},
    },
  },
};

function firstLine(stream: Readable, timeoutMs = 5_000): Promise<string> {
  return new Promise((resolve, reject) => {
    let buffer = "";
    const timeout = setTimeout(() => {
      cleanup();
      reject(new Error("Timed out waiting for a complete line"));
    }, timeoutMs);

    const onData = (chunk: string): void => {
      buffer += chunk;
      const newline = buffer.indexOf("\n");
      if (newline >= 0) {
        const line = buffer.slice(0, newline).replace(/\r$/, "");
        cleanup();
        resolve(line);
      }
    };

    const onEnd = (): void => {
      cleanup();
      reject(new Error("Stream ended before a complete line arrived"));
    };

    const cleanup = (): void => {
      clearTimeout(timeout);
      stream.off("data", onData);
      stream.off("end", onEnd);
    };

    stream.setEncoding("utf8");
    stream.on("data", onData);
    stream.on("end", onEnd);
  });
}

async function probe(serverFile: string): Promise<{
  stdout: string;
  stderr: string;
}> {
  const child = spawn(process.execPath, [serverFile], {
    stdio: ["pipe", "pipe", "pipe"],
    windowsHide: true,
  });

  const stdout = firstLine(child.stdout);
  const stderr = firstLine(child.stderr);

  child.stdin.write(`${JSON.stringify(discoverRequest)}\n`);

  try {
    return {
      stdout: await stdout,
      stderr: await stderr,
    };
  } finally {
    child.stdin.end();
    await Promise.race([
      once(child, "exit"),
      new Promise((resolve) => setTimeout(resolve, 1_000)),
    ]);
    if (child.exitCode === null) {
      child.kill();
    }
  }
}

function builtServer(name: "good-server" | "bad-server"): string {
  return fileURLToPath(new URL(`../src/${name}.js`, import.meta.url));
}

test("console.log corrupts the first stdout frame", async () => {
  const result = await probe(builtServer("bad-server"));

  assert.equal(result.stdout, "Server starting on stdio");
  assert.throws(() => JSON.parse(result.stdout), SyntaxError);
});

test("console.error preserves a valid server/discover response", async () => {
  const result = await probe(builtServer("good-server"));
  const response = JSON.parse(result.stdout) as {
    jsonrpc: string;
    id: string;
    result: { supportedVersions: string[] };
  };
  const diagnostic = JSON.parse(result.stderr) as {
    event: string;
    transport: string;
  };

  assert.equal(response.jsonrpc, "2.0");
  assert.equal(response.id, "discover-1");
  assert.ok(response.result.supportedVersions.includes("2026-07-28"));
  assert.equal(diagnostic.event, "server_ready");
  assert.equal(diagnostic.transport, "stdio");
});
