import { describe, expect, it, vi } from "vitest";

import {
  GAME_CONFIG_CANONICALIZATION,
  GAME_CONFIG_HASH_ALGORITHM,
  GameConfigClient,
  type GameConfigLoadResult,
  type GameConfigSnapshotCache,
  type GameConfigTransport,
  type GameConfigTransportRequest,
  type GameConfigTransportResponse,
  LosslessNumber,
  MemoryGameConfigSnapshotCache,
  validateGameConfigSnapshot,
  WebCryptoSha256,
} from "../src/index.js";

const encoder = new TextEncoder();
const sha256 = new WebCryptoSha256();
const cacheKey = "default/development";
const configurationSet = "default";
const versionA = "11111111-1111-4111-8111-111111111111";
const versionB = "22222222-2222-4222-8222-222222222222";
const snapshotMaximumByteLength = 25_166_848;
const defaultContent = '{"collections":{"items":[{"id":"entry-a","value":1}]}}';

interface SnapshotFixture {
  readonly body: Uint8Array;
  readonly etag: string;
  readonly snapshotHash: string;
}

interface SnapshotFixtureOptions {
  readonly algorithm?: string;
  readonly canonicalization?: string;
  readonly configurationSet?: string;
  readonly content?: string;
  readonly contentHash?: string;
  readonly createdAtUtc?: string;
  readonly formatVersion?: string;
  readonly schemaVersion?: string;
  readonly version?: string;
}

async function createSnapshot(options: SnapshotFixtureOptions = {}): Promise<SnapshotFixture> {
  const content = options.content ?? defaultContent;
  const contentHash = options.contentHash ?? (await sha256.digestHex(encoder.encode(content)));
  const source = JSON.stringify({
    formatVersion: options.formatVersion ?? "1",
    schemaVersion: options.schemaVersion ?? "1",
    configurationSet: options.configurationSet ?? configurationSet,
    configurationVersion: options.version ?? versionA,
    createdAtUtc: options.createdAtUtc ?? "2026-07-26T01:02:03.1234567Z",
    integrity: {
      algorithm: options.algorithm ?? GAME_CONFIG_HASH_ALGORITHM,
      canonicalization: options.canonicalization ?? GAME_CONFIG_CANONICALIZATION,
      contentHash,
    },
  }).replace(/}$/, `,"content":${content}}`);
  const body = encoder.encode(source);
  const snapshotHash = await sha256.digestHex(body);
  return { body, etag: `"${snapshotHash}"`, snapshotHash };
}

function ok(fixture: SnapshotFixture): GameConfigTransportResponse {
  return { body: fixture.body, etag: fixture.etag, status: 200 };
}

function createClient(
  transport: GameConfigTransport,
  cache: GameConfigSnapshotCache = new MemoryGameConfigSnapshotCache(),
  expectedConfigurationVersion?: string,
): GameConfigClient {
  return new GameConfigClient({
    cache,
    cacheKey,
    configurationSet,
    ...(expectedConfigurationVersion ? { expectedConfigurationVersion } : {}),
    sha256,
    transport,
  });
}

function fixedTransport(
  response: GameConfigTransportResponse | Promise<GameConfigTransportResponse>,
): GameConfigTransport {
  return {
    fetchSnapshot: vi.fn(async () => response),
  };
}

function assertError(
  result: GameConfigLoadResult,
  status: "fallback" | "rejected",
  code: string,
): void {
  expect(result.status).toBe(status);
  if (!("error" in result)) {
    throw new Error("Expected a result carrying a client error.");
  }
  expect(result.error.code).toBe(code);
}

function createNestedContent(additionalArrayDepth: number): string {
  return (
    '{"collections":{"items":[{"id":"entry-a","payload":' +
    "[".repeat(additionalArrayDepth) +
    "null" +
    "]".repeat(additionalArrayDepth) +
    "}]}}"
  );
}

function deferred<T>(): {
  promise: Promise<T>;
  reject(error: unknown): void;
  resolve(value: T): void;
} {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

class RecordingCache extends MemoryGameConfigSnapshotCache {
  writes = 0;
  failWrites = false;
  missReads = false;

  override async read(key: string): Promise<{ body: Uint8Array; snapshotHash: string } | null> {
    return this.missReads ? null : super.read(key);
  }

  override async write(
    key: string,
    snapshot: { body: Uint8Array; snapshotHash: string },
  ): Promise<void> {
    this.writes += 1;
    if (this.failWrites) {
      throw new Error("cache unavailable");
    }
    await super.write(key, snapshot);
  }
}

describe("GameConfigClient", () => {
  it("validates and activates a snapshot with exact non-ASCII content hashing and lossless numbers", async () => {
    const fixture = await createSnapshot({
      content:
        '{"collections":{"items":[{"id":"entry-a","label":"\\u5251","scaled":1.2300,"scientific":1e+09,"value":9007199254740993},{"id":"entry-b","value":2}]}}',
    });
    const client = createClient(fixedTransport(ok(fixture)));

    const result = await client.refresh();

    expect(result.status).toBe("activated");
    expect(client.current?.snapshotHash).toBe(fixture.snapshotHash);
    expect(client.current?.getCollection("items").map((record) => record.id)).toEqual([
      "entry-a",
      "entry-b",
    ]);
    const value = client.current?.getRecord("items", "entry-a")?.value;
    expect(value).toBeInstanceOf(LosslessNumber);
    expect(String(value)).toBe("9007199254740993");
    expect(String(client.current?.getRecord("items", "entry-a")?.scaled)).toBe("1.2300");
    expect(String(client.current?.getRecord("items", "entry-a")?.scientific)).toBe("1e+09");
    expect(client.current?.getRecord("missing", "entry-a")).toBeUndefined();
  });

  it("uses portable Noble SHA-256 when no digest port is supplied", async () => {
    const fixture = await createSnapshot();
    const client = new GameConfigClient({
      cache: new MemoryGameConfigSnapshotCache(),
      cacheKey,
      configurationSet,
      transport: fixedTransport(ok(fixture)),
    });

    expect((await client.refresh()).status).toBe("activated");
  });

  it("rejects a complete snapshot hash mismatch", async () => {
    const fixture = await createSnapshot();
    const changed = Uint8Array.from([...fixture.body, 0x20]);
    const client = createClient(fixedTransport({ body: changed, etag: fixture.etag, status: 200 }));

    const result = await client.refresh();

    assertError(result, "rejected", "snapshot_hash_mismatch");
    expect(client.current).toBeNull();
  });

  it("rejects a content hash mismatch even when the complete snapshot hash is valid", async () => {
    const fixture = await createSnapshot({ contentHash: "0".repeat(64) });
    const result = await createClient(fixedTransport(ok(fixture))).refresh();

    assertError(result, "rejected", "content_hash_mismatch");
  });

  it.each([
    '{"collections": {"items":[]}}',
    '{"collections":{"items":[{"value":1,"id":"entry-a"}]}}',
    '{"collections":{"items":[{"id":"entry-b"},{"id":"entry-a"}]}}',
  ])("rejects noncanonical content: %s", async (content) => {
    const fixture = await createSnapshot({ content });
    const result = await createClient(fixedTransport(ok(fixture))).refresh();

    assertError(result, "rejected", "canonicalization_mismatch");
  });

  it("rejects JSON string representations that differ from the producer encoding", async () => {
    const contents = [
      '{"collections":{"items":[{"id":"entry-a","label":"\u5251"}]}}',
      '{"collections":{"items":[{"id":"entry-a","label":"\\u00e9"}]}}',
      '{"collections":{"items":[{"id":"entry-a","label":"\\u0061"}]}}',
      '{"collections":{"items":[{"id":"entry-a","label":"+"}]}}',
    ];

    for (const content of contents) {
      const fixture = await createSnapshot({ content });
      const result = await createClient(fixedTransport(ok(fixture))).refresh();
      assertError(result, "rejected", "canonicalization_mismatch");
    }
  });

  it("rejects snapshots larger than the producer-consumer byte bound", async () => {
    const body = new Uint8Array(snapshotMaximumByteLength + 1);
    const result = await createClient(
      fixedTransport({ body, etag: `"${"0".repeat(64)}"`, status: 200 }),
    ).refresh();

    assertError(result, "rejected", "invalid_contract");
  });

  it("accepts 65 snapshot levels and rejects deeper content", async () => {
    const atLimit = await createSnapshot({ content: createNestedContent(60) });
    expect((await createClient(fixedTransport(ok(atLimit))).refresh()).status).toBe("activated");

    const tooDeep = await createSnapshot({ content: createNestedContent(61) });
    const result = await createClient(fixedTransport(ok(tooDeep))).refresh();
    assertError(result, "rejected", "invalid_contract");
  });

  it("rejects extreme container depth before recursive JSON parsing", async () => {
    const fixture = await createSnapshot({ content: createNestedContent(10_000) });

    await expect(
      validateGameConfigSnapshot({
        body: fixture.body,
        etag: fixture.etag,
        expectedConfigurationSet: configurationSet,
        sha256,
      }),
    ).rejects.toMatchObject({ code: "invalid_contract" });
  });

  it.each([
    [{ formatVersion: "2" }, "unsupported_format"],
    [{ schemaVersion: "2" }, "unsupported_schema"],
    [{ algorithm: "SHA-512" }, "unsupported_integrity"],
    [{ canonicalization: "NovaGame.GameConfig.CanonicalJson.v2" }, "unsupported_integrity"],
  ] as const)("rejects unsupported snapshot contracts %#", async (overrides, code) => {
    const fixture = await createSnapshot(overrides);
    const result = await createClient(fixedTransport(ok(fixture))).refresh();

    assertError(result, "rejected", code);
  });

  it("strictly rejects invalid UTF-8 after the whole-body hash passes", async () => {
    const body = Uint8Array.from([0xff, 0xfe, 0xfd]);
    const hash = await sha256.digestHex(body);
    const result = await createClient(
      fixedTransport({ body, etag: `"${hash}"`, status: 200 }),
    ).refresh();

    assertError(result, "rejected", "invalid_utf8");
  });

  it.each([['W/"' + "a".repeat(64) + '"'], ['"' + "A".repeat(64) + '"'], ["a".repeat(64)]])(
    "rejects a non-contract ETag: %s",
    async (etag) => {
      const fixture = await createSnapshot();
      const result = await createClient(
        fixedTransport({ body: fixture.body, etag, status: 200 }),
      ).refresh();

      assertError(result, "rejected", "invalid_etag");
    },
  );

  it("rejects configuration set and expected version mismatches", async () => {
    const wrongSet = await createSnapshot({ configurationSet: "alternate" });
    const setResult = await createClient(fixedTransport(ok(wrongSet))).refresh();
    assertError(setResult, "rejected", "configuration_set_mismatch");

    const staleVersion = await createSnapshot({ version: versionA });
    const versionResult = await createClient(
      fixedTransport(ok(staleVersion)),
      new MemoryGameConfigSnapshotCache(),
      versionB,
    ).refresh();
    assertError(versionResult, "rejected", "configuration_version_mismatch");

    const emptyVersion = await createSnapshot({
      version: "00000000-0000-0000-0000-000000000000",
    });
    const emptyVersionResult = await createClient(fixedTransport(ok(emptyVersion))).refresh();
    assertError(emptyVersionResult, "rejected", "invalid_contract");
  });

  it("validates exact references and requires their targets to exist", async () => {
    const valid = await createSnapshot({
      content:
        '{"collections":{"groups":[{"id":"base"}],"items":[{"group":{"$ref":"groups/base"},"id":"entry-a"}]}}',
    });
    expect((await createClient(fixedTransport(ok(valid))).refresh()).status).toBe("activated");

    const invalidContents = [
      '{"collections":{"items":[{"id":"entry-a","target":{"$ref":"items/missing"}}]}}',
      '{"collections":{"items":[{"id":"entry-a","target":{"$ref":"items/entry-a","extra":true}}]}}',
      '{"collections":{"items":[{"id":"entry-a","target":{"$ref":"invalid"}}]}}',
    ];
    for (const content of invalidContents) {
      const fixture = await createSnapshot({ content });
      const result = await createClient(fixedTransport(ok(fixture))).refresh();
      assertError(result, "rejected", "invalid_contract");
    }
  });

  it("rejects duplicate record identifiers while preserving deterministic lookups", async () => {
    const fixture = await createSnapshot({
      content: '{"collections":{"items":[{"id":"same"},{"id":"same"}]}}',
    });
    const result = await createClient(fixedTransport(ok(fixture))).refresh();

    assertError(result, "rejected", "duplicate_record_id");
  });

  it("returns a typed missing-snapshot failure", async () => {
    const result = await createClient(fixedTransport({ status: 404 })).refresh();

    assertError(result, "rejected", "snapshot_missing");
  });

  it("retains no partial state after an interrupted download", async () => {
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(async () => {
        throw new Error("connection closed");
      }),
    };
    const client = createClient(transport);

    const result = await client.refresh();

    assertError(result, "rejected", "transport_failed");
    expect(client.current).toBeNull();
  });

  it("falls back to a validated cached active snapshot after transport failure", async () => {
    const fixture = await createSnapshot();
    const cache = new MemoryGameConfigSnapshotCache();
    await cache.write(cacheKey, {
      body: fixture.body,
      snapshotHash: fixture.snapshotHash,
    });
    const requests: GameConfigTransportRequest[] = [];
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(async (request) => {
        requests.push(request);
        throw new Error("offline");
      }),
    };
    const client = createClient(transport, cache);

    const result = await client.refresh();

    assertError(result, "fallback", "transport_failed");
    expect(client.current?.snapshotHash).toBe(fixture.snapshotHash);
    expect(requests[0]?.ifNoneMatch).toBe(fixture.etag);
  });

  it("restores a validated persisted snapshot without invoking transport", async () => {
    const fixture = await createSnapshot();
    const cache = new MemoryGameConfigSnapshotCache();
    await cache.write(cacheKey, {
      body: fixture.body,
      snapshotHash: fixture.snapshotHash,
    });
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(async () => {
        throw new Error("restore must not use transport");
      }),
    };
    const client = createClient(transport, cache);

    const result = await client.restore();

    expect(result.status).toBe("restored");
    expect(client.current?.snapshotHash).toBe(fixture.snapshotHash);
    expect(transport.fetchSnapshot).not.toHaveBeenCalled();
  });

  it("returns typed restore failures for unavailable cache and cancellation", async () => {
    const failingCache: GameConfigSnapshotCache = {
      read: vi.fn(async () => {
        throw new Error("storage unavailable");
      }),
      write: vi.fn(async () => undefined),
    };
    const client = createClient(fixedTransport({ status: 404 }), failingCache);
    assertError(await client.restore(), "rejected", "cache_read_failed");

    const controller = new AbortController();
    controller.abort();
    assertError(await client.restore(controller.signal), "rejected", "operation_aborted");
  });

  it("confirms a cached snapshot through 304 and rejects 304 without a valid cache", async () => {
    const fixture = await createSnapshot();
    const cache = new MemoryGameConfigSnapshotCache();
    await cache.write(cacheKey, {
      body: fixture.body,
      snapshotHash: fixture.snapshotHash,
    });
    const revalidated = await createClient(
      fixedTransport({ etag: fixture.etag, status: 304 }),
      cache,
    ).refresh();
    expect(revalidated.status).toBe("revalidated");

    const missing = await createClient(
      fixedTransport({ etag: fixture.etag, status: 304 }),
    ).refresh();
    assertError(missing, "rejected", "cache_miss");
  });

  it("does not rewrite or reactivate an already active snapshot with the same hash", async () => {
    const fixture = await createSnapshot();
    const cache = new RecordingCache();
    const client = createClient(fixedTransport(ok(fixture)), cache);

    expect((await client.refresh()).status).toBe("activated");
    expect((await client.refresh()).status).toBe("unchanged");
    expect(cache.writes).toBe(1);
  });

  it("repairs missing persisted storage when the active in-memory hash is unchanged", async () => {
    const fixture = await createSnapshot();
    const cache = new RecordingCache();
    const client = createClient(fixedTransport(ok(fixture)), cache);

    expect((await client.refresh()).status).toBe("activated");
    cache.missReads = true;

    expect((await client.refresh()).status).toBe("unchanged");
    expect(cache.writes).toBe(2);
  });

  it("does not expose a candidate in memory until atomic cache persistence completes", async () => {
    const fixture = await createSnapshot();
    const writeStarted = deferred<void>();
    const allowWrite = deferred<void>();
    const cache: GameConfigSnapshotCache = {
      read: vi.fn(async () => null),
      write: vi.fn(async () => {
        writeStarted.resolve();
        await allowWrite.promise;
      }),
    };
    const client = createClient(fixedTransport(ok(fixture)), cache);

    const refresh = client.refresh();
    await writeStarted.promise;
    expect(client.current).toBeNull();
    allowWrite.resolve();

    expect((await refresh).status).toBe("activated");
    expect(client.current?.snapshotHash).toBe(fixture.snapshotHash);
  });

  it("retains the previous valid state when cache persistence fails", async () => {
    const first = await createSnapshot({ version: versionA });
    const second = await createSnapshot({ version: versionB });
    const responses = [ok(first), ok(second)];
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(async () => responses.shift()!),
    };
    const cache = new RecordingCache();
    const client = createClient(transport, cache);
    expect((await client.refresh()).status).toBe("activated");
    cache.failWrites = true;

    const result = await client.refresh();

    assertError(result, "fallback", "cache_write_failed");
    expect(client.current?.configurationVersion).toBe(versionA);
  });

  it("falls back to the previously validated cache when a new write fails before memory activation", async () => {
    const previous = await createSnapshot({ version: versionA });
    const candidate = await createSnapshot({ version: versionB });
    const cache = new RecordingCache();
    await cache.write(cacheKey, {
      body: previous.body,
      snapshotHash: previous.snapshotHash,
    });
    cache.failWrites = true;
    const client = createClient(fixedTransport(ok(candidate)), cache);

    const result = await client.refresh();

    assertError(result, "fallback", "cache_write_failed");
    expect(client.current?.configurationVersion).toBe(versionA);
  });

  it("rejects an earlier slower response only after a later request commits", async () => {
    const firstFixture = await createSnapshot({ version: versionA });
    const secondFixture = await createSnapshot({ version: versionB });
    const first = deferred<GameConfigTransportResponse>();
    const second = deferred<GameConfigTransportResponse>();
    let call = 0;
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(() => (call++ === 0 ? first.promise : second.promise)),
    };
    const client = createClient(transport);

    const firstRefresh = client.refresh();
    await vi.waitFor(() => expect(call).toBe(1));
    const secondRefresh = client.refresh();
    await vi.waitFor(() => expect(call).toBe(2));
    second.resolve(ok(secondFixture));
    expect((await secondRefresh).status).toBe("activated");
    first.resolve(ok(firstFixture));

    const stale = await firstRefresh;
    assertError(stale, "rejected", "stale_response");
    expect(client.current?.configurationVersion).toBe(versionB);
  });

  it("rejects an earlier response after a later request confirms a cached ETag", async () => {
    const earlierFixture = await createSnapshot({ version: versionA });
    const cachedFixture = await createSnapshot({ version: versionB });
    const cache = new MemoryGameConfigSnapshotCache();
    await cache.write(cacheKey, {
      body: cachedFixture.body,
      snapshotHash: cachedFixture.snapshotHash,
    });
    const earlier = deferred<GameConfigTransportResponse>();
    const revalidation = deferred<GameConfigTransportResponse>();
    let call = 0;
    const client = createClient(
      {
        fetchSnapshot: vi.fn(() => (call++ === 0 ? earlier.promise : revalidation.promise)),
      },
      cache,
    );

    const earlierRefresh = client.refresh();
    await vi.waitFor(() => expect(call).toBe(1));
    const laterRefresh = client.refresh();
    await vi.waitFor(() => expect(call).toBe(2));
    revalidation.resolve({ etag: cachedFixture.etag, status: 304 });
    expect((await laterRefresh).status).toBe("revalidated");
    earlier.resolve(ok(earlierFixture));

    assertError(await earlierRefresh, "rejected", "stale_response");
    expect(client.current?.configurationVersion).toBe(versionB);
  });

  it("allows an earlier valid response when a later request fails", async () => {
    const fixture = await createSnapshot({ version: versionA });
    const first = deferred<GameConfigTransportResponse>();
    const second = deferred<GameConfigTransportResponse>();
    let call = 0;
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(() => (call++ === 0 ? first.promise : second.promise)),
    };
    const client = createClient(transport);

    const firstRefresh = client.refresh();
    await vi.waitFor(() => expect(call).toBe(1));
    const secondRefresh = client.refresh();
    await vi.waitFor(() => expect(call).toBe(2));
    second.reject(new Error("later request failed"));
    assertError(await secondRefresh, "rejected", "transport_failed");
    first.resolve(ok(fixture));

    expect((await firstRefresh).status).toBe("activated");
    expect(client.current?.configurationVersion).toBe(versionA);
  });

  it("does not activate a response completed after its request is aborted", async () => {
    const fixture = await createSnapshot();
    const response = deferred<GameConfigTransportResponse>();
    const transport: GameConfigTransport = {
      fetchSnapshot: vi.fn(async () => response.promise),
    };
    const cache = new RecordingCache();
    const client = createClient(transport, cache);
    const controller = new AbortController();

    const refresh = client.refresh(controller.signal);
    await vi.waitFor(() => expect(transport.fetchSnapshot).toHaveBeenCalledOnce());
    controller.abort();
    response.resolve(ok(fixture));

    assertError(await refresh, "rejected", "operation_aborted");
    expect(client.current).toBeNull();
    expect(cache.writes).toBe(0);
  });

  it("accepts a valid rollback snapshot with an older publication timestamp", async () => {
    const newer = await createSnapshot({
      createdAtUtc: "2026-07-26T02:00:00.0000000Z",
      version: versionB,
    });
    const older = await createSnapshot({
      createdAtUtc: "2026-07-25T02:00:00.0000000Z",
      version: versionA,
    });
    const responses = [ok(newer), ok(older)];
    const client = createClient({
      fetchSnapshot: vi.fn(async () => responses.shift()!),
    });

    expect((await client.refresh()).status).toBe("activated");
    expect((await client.refresh()).status).toBe("activated");
    expect(client.current?.configurationVersion).toBe(versionA);
  });
});
