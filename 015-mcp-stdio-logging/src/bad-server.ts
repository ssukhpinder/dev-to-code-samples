import { McpServer } from "@modelcontextprotocol/server";
import { serveStdio } from "@modelcontextprotocol/server/stdio";

console.log("Server starting on stdio");

serveStdio(
  () =>
    new McpServer({
      name: "stdio-corruption-demo",
      version: "1.0.0",
    }),
  {
    onerror: (error: Error) => console.error(error.message),
  },
);

console.error(
  "Server connected; the earlier stdout line already broke framing",
);
