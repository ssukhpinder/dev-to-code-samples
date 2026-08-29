import assert from "node:assert/strict";
import test from "node:test";

import {
  runLegacyHarness,
  runModernHarness,
  runVerification,
  strictModernHandlerRejectsLegacyClient,
} from "../src/in-process-era-harness.js";

test("the linked pair and fetch harness exercise different eras", async () => {
  const legacy = await runLegacyHarness();
  const modern = await runModernHarness();

  assert.equal(legacy.era, "legacy");
  assert.equal(modern.era, "modern");
  assert.deepEqual(legacy.toolNames, ["normalize-ticket"]);
  assert.deepEqual(modern.toolNames, ["normalize-ticket"]);
  assert.equal(legacy.normalizedTicket, "INCIDENT-42");
  assert.equal(modern.normalizedTicket, "INCIDENT-42");
  assert.equal(modern.sawSessionId, false);
  assert.ok(modern.fetchCalls > 0);
});

test("a strict modern handler rejects the default legacy client", async () => {
  assert.equal(await strictModernHandlerRejectsLegacyClient(), true);
});

test("the standalone verifier covers the full contract", async () => {
  assert.equal(
    await runVerification(),
    "PASS: 10/10 in-process MCP era checks",
  );
});
