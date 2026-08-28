import {
  OAuthError,
  OAuthErrorCode,
  requireBearerAuth,
  type AuthInfo,
  type OAuthTokenVerifier,
} from "@modelcontextprotocol/server";
import { pathToFileURL } from "node:url";

const resourceMetadataUrl =
  "https://mcp.example.test/.well-known/oauth-protected-resource";
const requiredScopes = ["tools:read"];
const futureExpiry = 4_102_444_800;

export interface AuthOutcome {
  status: number;
  error: string | null;
  challenge: string | null;
  authInfo: AuthInfo | null;
}

const legacyVerifier: OAuthTokenVerifier = {
  async verifyAccessToken(): Promise<AuthInfo> {
    throw new Error("token is expired or revoked");
  },
};

const migratedVerifier: OAuthTokenVerifier = {
  async verifyAccessToken(token: string): Promise<AuthInfo> {
    if (token === "valid-token") {
      return {
        token,
        clientId: "offline-client",
        scopes: ["tools:read"],
        expiresAt: futureExpiry,
      };
    }

    if (token === "wrong-scope-token") {
      return {
        token,
        clientId: "offline-client",
        scopes: ["profile:read"],
        expiresAt: futureExpiry,
      };
    }

    throw new OAuthError(
      OAuthErrorCode.InvalidToken,
      "token is expired or revoked",
    );
  },
};

export async function inspectBearerAuth(
  token: string,
  verifier: OAuthTokenVerifier,
): Promise<AuthOutcome> {
  const gate = requireBearerAuth({
    verifier,
    requiredScopes,
    resourceMetadataUrl,
  });
  const request = new Request("https://mcp.example.test/mcp", {
    headers: { Authorization: `Bearer ${token}` },
  });
  const result = await gate(request);

  if (!(result instanceof Response)) {
    return {
      status: 200,
      error: null,
      challenge: null,
      authInfo: result,
    };
  }

  const body = (await result.json()) as { error?: string };
  return {
    status: result.status,
    error: body.error ?? null,
    challenge: result.headers.get("www-authenticate"),
    authInfo: null,
  };
}

export async function runVerification(): Promise<string> {
  const checks: string[] = [];
  const verify = (condition: boolean, message: string): void => {
    if (!condition) {
      throw new Error(`FAIL: ${message}`);
    }
    checks.push(message);
  };

  const legacy = await inspectBearerAuth("expired-token", legacyVerifier);
  verify(legacy.status === 500, "generic verifier error maps to HTTP 500");
  verify(legacy.error === "server_error", "generic error becomes server_error");

  const migrated = await inspectBearerAuth("expired-token", migratedVerifier);
  verify(migrated.status === 401, "typed invalid token maps to HTTP 401");
  verify(
    migrated.error === "invalid_token",
    "typed body reports invalid_token",
  );
  verify(
    migrated.challenge?.startsWith("Bearer") === true,
    "401 includes a Bearer challenge",
  );
  verify(
    migrated.challenge?.includes('error="invalid_token"') === true,
    "challenge reports invalid_token",
  );
  verify(
    migrated.challenge?.includes("resource_metadata=") === true,
    "challenge advertises protected-resource metadata",
  );

  const accepted = await inspectBearerAuth("valid-token", migratedVerifier);
  verify(accepted.status === 200, "valid token passes the gate");
  verify(accepted.authInfo !== null, "valid token returns AuthInfo");
  verify(
    accepted.authInfo?.clientId === "offline-client",
    "AuthInfo preserves the client ID",
  );
  verify(
    accepted.authInfo?.scopes.includes("tools:read") === true,
    "AuthInfo contains the required scope",
  );

  const wrongScope = await inspectBearerAuth(
    "wrong-scope-token",
    migratedVerifier,
  );
  verify(wrongScope.status === 403, "missing scope maps to HTTP 403");
  verify(
    wrongScope.error === "insufficient_scope",
    "scope failure reports insufficient_scope",
  );

  return `PASS: ${checks.length}/13 bearer-auth checks`;
}

if (
  process.argv[1] !== undefined &&
  import.meta.url === pathToFileURL(process.argv[1]).href
) {
  console.log(await runVerification());
}
