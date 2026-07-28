import { describe, expect, it, vi } from "vitest";

import { FetchSnapshotTransport } from "../src/index.js";
import type { FetchRequestInit, FetchResponse } from "../src/index.js";

const snapshotHash = "a".repeat(64);
const snapshotEtag = `"${snapshotHash}"`;

describe("FetchSnapshotTransport", () => {
  it("requests the active snapshot with an exact conditional ETag", async () => {
    const bytes = new TextEncoder().encode('{"version":1}');
    const fetch = vi.fn(
      async (_input: string, _init?: FetchRequestInit): Promise<FetchResponse> =>
        response(200, bytes, snapshotEtag),
    );
    const transport = new FetchSnapshotTransport({
      baseUrl: "https://api.example.test/nested/",
      configurationSet: "live-content",
      environment: "staging-blue",
      fetch,
    });

    const result = await transport.fetchSnapshot({
      ifNoneMatch: snapshotEtag,
    });

    expect(result).toEqual({ body: bytes, etag: snapshotEtag, status: 200 });
    expect(fetch).toHaveBeenCalledOnce();
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe(
      "https://api.example.test/nested/api/game-config/environments/staging-blue/snapshot?configurationSet=live-content",
    );
    expect(init).toEqual({
      headers: {
        Accept: "application/json",
        "If-None-Match": snapshotEtag,
      },
      method: "GET",
    });
  });

  it("maps 304 and 404 without reading a response body", async () => {
    const notModifiedBody = vi.fn(async () => new ArrayBuffer(0));
    const missingBody = vi.fn(async () => new ArrayBuffer(0));
    const fetch = vi
      .fn()
      .mockResolvedValueOnce({
        arrayBuffer: notModifiedBody,
        headers: headers(),
        status: 304,
      } satisfies FetchResponse)
      .mockResolvedValueOnce({
        arrayBuffer: missingBody,
        headers: headers(),
        status: 404,
      } satisfies FetchResponse);
    const transport = new FetchSnapshotTransport({
      baseUrl: "https://api.example.test",
      environment: "development",
      fetch,
    });

    await expect(transport.fetchSnapshot({ ifNoneMatch: snapshotEtag })).resolves.toEqual({
      etag: snapshotEtag,
      status: 304,
    });
    await expect(transport.fetchSnapshot({})).resolves.toEqual({ status: 404 });
    expect(notModifiedBody).not.toHaveBeenCalled();
    expect(missingBody).not.toHaveBeenCalled();
  });

  it("rejects weak, unquoted, missing, and unsupported response ETags or statuses", async () => {
    const body = new TextEncoder().encode("{}");
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(200, body, `W/${snapshotEtag}`))
      .mockResolvedValueOnce(response(200, body, null))
      .mockResolvedValueOnce(response(503, body, snapshotEtag));
    const transport = new FetchSnapshotTransport({
      baseUrl: "https://api.example.test",
      environment: "development",
      fetch,
    });

    await expect(transport.fetchSnapshot({ ifNoneMatch: snapshotHash })).rejects.toThrow(
      "quoted lowercase SHA-256 strong ETag",
    );
    expect(fetch).not.toHaveBeenCalled();
    await expect(transport.fetchSnapshot({})).rejects.toThrow(
      "quoted lowercase SHA-256 strong ETag",
    );
    await expect(transport.fetchSnapshot({})).rejects.toThrow("without a strong ETag");
    await expect(transport.fetchSnapshot({})).rejects.toMatchObject({
      status: 503,
    });
  });

  it("rejects 304 when no conditional ETag was sent", async () => {
    const transport = new FetchSnapshotTransport({
      baseUrl: "https://api.example.test",
      environment: "development",
      fetch: async () => response(304),
    });

    await expect(transport.fetchSnapshot({})).rejects.toMatchObject({
      status: 304,
    });
  });

  it("rejects malformed environment and configuration-set identifiers before fetch", () => {
    const fetch = vi.fn(async () => response(404));

    expect(
      () =>
        new FetchSnapshotTransport({
          baseUrl: "https://api.example.test",
          environment: "staging/blue",
          fetch,
        }),
    ).toThrow("lowercase safe slug");
    expect(
      () =>
        new FetchSnapshotTransport({
          baseUrl: "https://api.example.test",
          configurationSet: "Live",
          environment: "staging",
          fetch,
        }),
    ).toThrow("lowercase safe slug");
    expect(fetch).not.toHaveBeenCalled();
  });
});

function headers(etag: null | string = null) {
  return {
    get(name: string): null | string {
      return name.toLowerCase() === "etag" ? etag : null;
    },
  };
}

function response(
  status: number,
  bytes: Uint8Array = new Uint8Array(),
  etag: null | string = null,
): FetchResponse {
  const body = Uint8Array.from(bytes).buffer;
  return {
    arrayBuffer: async () => body,
    headers: headers(etag),
    status,
  };
}
