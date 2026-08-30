import assert from "node:assert/strict";
import test from "node:test";
import {
  mediaTypeCases,
  runCase,
  runMatrix,
  runVerification,
} from "../src/content-type-contract.js";

test("the complete media-type matrix matches status and dispatch contracts", async () => {
  assert.equal(mediaTypeCases.length, 8);
  const results = await runMatrix();

  assert.deepEqual(
    results,
    mediaTypeCases.map((testCase) => ({
      label: testCase.label,
      status: testCase.expectedStatus,
      dispatched: testCase.expectedDispatched,
    })),
  );
});

test("a substring spoof returns 415 before tool dispatch", async () => {
  const spoof = mediaTypeCases.find(
    (testCase) => testCase.label === "substring spoof",
  );
  assert.ok(spoof);

  const result = await runCase(spoof);

  assert.equal(result.status, 415);
  assert.equal(result.dispatched, 0);
});

test("the standalone verifier reports all eight passing checks", async () => {
  const output = await runVerification();

  assert.match(output, /PASS: 8\/8 Content-Type checks passed$/);
  assert.equal(output.split("\n").length, 9);
});
