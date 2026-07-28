import type {
  GameConfigTransport,
  GameConfigTransportRequest,
  GameConfigTransportResponse,
} from "@novagame/game-config-client";

import type { FetchLike, FetchRequestInit } from "./ports.js";

const defaultConfigurationSet = "default";
const strongSnapshotEtagPattern = /^"[a-f0-9]{64}"$/;
const safeSlugPattern = /^[a-z][a-z0-9-]{0,63}$/;

export interface FetchSnapshotTransportOptions {
  readonly baseUrl: string;
  readonly configurationSet?: string;
  readonly environment: string;
  readonly fetch: FetchLike;
}

export class SnapshotTransportError extends Error {
  public readonly status: number | undefined;

  public constructor(message: string, status?: number) {
    super(message);
    this.name = "SnapshotTransportError";
    this.status = status;
  }
}

export class FetchSnapshotTransport implements GameConfigTransport {
  private readonly baseUrl: URL;
  private readonly configurationSet: string;
  private readonly environment: string;
  private readonly fetch: FetchLike;

  public constructor(options: FetchSnapshotTransportOptions) {
    this.baseUrl = parseBaseUrl(options.baseUrl);
    this.configurationSet = requireSlug(
      options.configurationSet ?? defaultConfigurationSet,
      "configuration set",
    );
    this.environment = requireSlug(options.environment, "environment");
    this.fetch = options.fetch;
  }

  public async fetchSnapshot(
    request: GameConfigTransportRequest,
  ): Promise<GameConfigTransportResponse> {
    request.signal?.throwIfAborted();
    if (request.ifNoneMatch !== undefined) {
      assertStrongSnapshotEtag(request.ifNoneMatch, "conditional ETag");
    }

    const url = new URL(
      `api/game-config/environments/${encodeURIComponent(this.environment)}/snapshot`,
      this.baseUrl,
    );
    url.searchParams.set("configurationSet", this.configurationSet);

    const headers: Record<string, string> = {
      Accept: "application/json",
    };
    if (request.ifNoneMatch !== undefined) {
      headers["If-None-Match"] = request.ifNoneMatch;
    }

    const init: FetchRequestInit = {
      headers,
      method: "GET",
      ...(request.signal === undefined ? {} : { signal: request.signal }),
    };
    const response = await this.fetch(url.toString(), init);

    if (response.status === 304) {
      if (request.ifNoneMatch === undefined) {
        throw new SnapshotTransportError(
          "The snapshot endpoint returned 304 without a conditional ETag.",
          response.status,
        );
      }
      const responseEtag = response.headers.get("etag");
      const etag = responseEtag ?? request.ifNoneMatch;
      assertStrongSnapshotEtag(etag, "response ETag");
      return { etag, status: 304 };
    }
    if (response.status === 404) {
      return { status: 404 };
    }
    if (response.status !== 200) {
      throw new SnapshotTransportError(
        `The snapshot endpoint returned unsupported status ${response.status}.`,
        response.status,
      );
    }

    const etag = readRequiredResponseEtag(response.headers.get("etag"), 200);
    const body = new Uint8Array(await response.arrayBuffer());
    if (body.byteLength === 0) {
      throw new SnapshotTransportError(
        "The snapshot endpoint returned an empty document.",
        response.status,
      );
    }

    return { body, etag, status: 200 };
  }
}

function assertStrongSnapshotEtag(etag: string, description: string): void {
  if (!strongSnapshotEtagPattern.test(etag)) {
    throw new SnapshotTransportError(
      `The ${description} must be a quoted lowercase SHA-256 strong ETag.`,
    );
  }
}

function parseBaseUrl(value: string): URL {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new SnapshotTransportError("The snapshot base URL is invalid.");
  }
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new SnapshotTransportError("The snapshot base URL must use HTTP or HTTPS.");
  }
  if (!url.pathname.endsWith("/")) {
    url.pathname += "/";
  }
  return url;
}

function readRequiredResponseEtag(etag: null | string, status: number): string {
  if (etag === null) {
    throw new SnapshotTransportError(
      `The snapshot endpoint returned ${status} without a strong ETag.`,
      status,
    );
  }
  assertStrongSnapshotEtag(etag, "response ETag");
  return etag;
}

function requireSlug(value: string, description: string): string {
  const normalized = value.trim();
  if (!safeSlugPattern.test(normalized)) {
    throw new SnapshotTransportError(
      `The ${description} must be a lowercase safe slug of at most 64 characters.`,
    );
  }
  return normalized;
}
