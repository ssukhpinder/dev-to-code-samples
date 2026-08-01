import assert from "node:assert/strict";
import test from "node:test";

import { InMemoryResponseCacheStore } from "@modelcontextprotocol/client";

import { connectClient, createCatalogEndpoint } from "../src/catalog.js";

test("a shared store without partitions reproduces the private-result leak", async () => {
  const cache = new InMemoryResponseCacheStore();
  const aliceEndpoint = createCatalogEndpoint("alice-invoices");
  const bobEndpoint = createCatalogEndpoint("bob-orders");
  const alice = await connectClient(aliceEndpoint, cache);
  const bob = await connectClient(bobEndpoint, cache);

  try {
    const aliceTools = await alice.listTools();
    const bobTools = await bob.listTools();

    assert.deepEqual(
      aliceTools.tools.map((tool) => tool.name),
      ["alice-invoices"],
    );
    assert.deepEqual(
      bobTools.tools.map((tool) => tool.name),
      ["alice-invoices"],
      "Bob received Alice's private cached result",
    );
    assert.equal(aliceEndpoint.toolsListRequests(), 1);
    assert.equal(
      bobEndpoint.toolsListRequests(),
      0,
      "Bob's tools/list request was satisfied from the unsafe shared partition",
    );
  } finally {
    await Promise.all([
      alice.close(),
      bob.close(),
      aliceEndpoint.handler.close(),
      bobEndpoint.handler.close(),
    ]);
  }
});

test("cachePartition isolates private results by authorization principal", async () => {
  const cache = new InMemoryResponseCacheStore();
  const aliceEndpoint = createCatalogEndpoint("alice-invoices");
  const bobEndpoint = createCatalogEndpoint("bob-orders");
  const alice = await connectClient(aliceEndpoint, cache, "subject:alice");
  const bob = await connectClient(bobEndpoint, cache, "subject:bob");

  try {
    const aliceTools = await alice.listTools();
    const bobTools = await bob.listTools();

    assert.deepEqual(
      aliceTools.tools.map((tool) => tool.name),
      ["alice-invoices"],
    );
    assert.deepEqual(
      bobTools.tools.map((tool) => tool.name),
      ["bob-orders"],
    );
    assert.equal(aliceEndpoint.toolsListRequests(), 1);
    assert.equal(bobEndpoint.toolsListRequests(), 1);
  } finally {
    await Promise.all([
      alice.close(),
      bob.close(),
      aliceEndpoint.handler.close(),
      bobEndpoint.handler.close(),
    ]);
  }
});
