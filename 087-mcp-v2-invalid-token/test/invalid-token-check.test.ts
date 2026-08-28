import assert from "node:assert/strict";
import test from "node:test";

import {
  inspectBearerAuth,
  runVerification,
} from "../src/invalid-token-check.js";

test("v2 bearer-auth migration preserves OAuth status semantics", async () => {
  assert.equal(await runVerification(), "PASS: 13/13 bearer-auth checks");
});

test("a migrated verifier produces a machine-readable 401", async () => {
  const verifier = {
    async verifyAccessToken() {
      const { OAuthError, OAuthErrorCode } =
        await import("@modelcontextprotocol/server");
      throw new OAuthError(OAuthErrorCode.InvalidToken, "fixture rejection");
    },
  };

  const outcome = await inspectBearerAuth("fixture-token", verifier);

  assert.equal(outcome.status, 401);
  assert.equal(outcome.error, "invalid_token");
  assert.match(outcome.challenge ?? "", /^Bearer /);
});
