import { McpServer } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";

serveStdio(
  () =>
    new McpServer({
      name: "stdio-safe-demo",
      version: "1.0.0",
    }),
  {
    onerror: (error: Error) => {
      console.error(
        JSON.stringify({
          level: "error",
          event: "transport_error",
          message: error.message,
        }),
      );
    },
  },
);

console.error(
  JSON.stringify({
    level: "info",
    event: "server_ready",
    transport: "stdio",
  }),
);

process.on("uncaughtException", (error: Error) => {
  console.error(
    JSON.stringify({
      level: "error",
      event: "server_failed",
      message: error.message,
    }),
  );
  process.exitCode = 1;
});
