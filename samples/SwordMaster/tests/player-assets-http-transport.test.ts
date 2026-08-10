import { describe, expect, it, vi } from "vitest";

import {
  PlayerAssetsTransportError,
  SwordMasterPlayerAssetsHttpTransport,
  type PlayerAssetsFetch,
} from "../src/index.js";

const characterId = "22222222-2222-2222-2222-222222222222";
const ownedItemId = "33333333-3333-3333-3333-333333333333";
const configurationVersionId = "55555555-5555-5555-5555-555555555555";
const idempotencyKey = "44444444-4444-4444-8444-abcdefabcdef";
const accessToken = "player.jwt.token";

describe("SwordMasterPlayerAssetsHttpTransport", () => {
  it("constructs the authenticated attributes request and decodes the typed result", async () => {
    const controller = new AbortController();
    const fetch = vi.fn<PlayerAssetsFetch>(async () =>
      jsonResponse(200, attributesEnvelope(7), '"7"'),
    );
    const getAccessToken = vi.fn(async () => accessToken);
    const transport = createTransport(fetch, getAccessToken);

    const result = await transport.getAttributes({
      characterId,
      signal: controller.signal,
    });

    expect(result).toEqual({
      attributes: [
        {
          baseValue: 10,
          effectiveValue: 13,
          equipmentValue: 3,
          statCode: "attack",
        },
      ],
      characterId,
      equipmentAssignments: [
        {
          configurationVersionId,
          configuredItemId: "starter-sword",
          modifiers: [{ statCode: "attack", value: 3 }],
          ownedItemId,
          slot: "weapon",
        },
      ],
      etag: '"7"',
      revision: 7,
    });
    expect(getAccessToken).toHaveBeenCalledOnce();
    expect(fetch).toHaveBeenCalledOnce();
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe(
      `https://api.example.test/root/api/player-assets/characters/${characterId}/attributes`,
    );
    expect(init).toEqual({
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${accessToken}`,
      },
      method: "GET",
      signal: controller.signal,
    });
  });

  it("constructs the equip request with canonical concurrency and idempotency headers", async () => {
    const fetch = vi.fn<PlayerAssetsFetch>(async () =>
      jsonResponse(200, mutationEnvelope(8), '"8"'),
    );
    const transport = createTransport(fetch);

    const result = await transport.equip({
      characterId,
      expectedEtag: '"7"',
      idempotencyKey,
      ownedItemId,
      slot: "main#hand",
    });

    expect(result).toEqual({ etag: '"8"', revision: 8 });
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe(
      `https://api.example.test/root/api/player-assets/characters/${characterId}/equipment/main%23hand`,
    );
    expect(init).toEqual({
      body: JSON.stringify({ ownedItemId }),
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${accessToken}`,
        "Content-Type": "application/json",
        "Idempotency-Key": idempotencyKey,
        "If-Match": '"7"',
      },
      method: "PUT",
    });
  });

  it("constructs the unequip request without a body or content type", async () => {
    const fetch = vi.fn<PlayerAssetsFetch>(async () =>
      jsonResponse(200, mutationEnvelope(9), '"9"'),
    );
    const transport = createTransport(fetch);

    const result = await transport.unequip({
      characterId,
      expectedEtag: '"8"',
      idempotencyKey,
      slot: "off?hand",
    });

    expect(result).toEqual({ etag: '"9"', revision: 9 });
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe(
      `https://api.example.test/root/api/player-assets/characters/${characterId}/equipment/off%3Fhand`,
    );
    expect(init).toEqual({
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${accessToken}`,
        "Idempotency-Key": idempotencyKey,
        "If-Match": '"8"',
      },
      method: "DELETE",
    });
    expect(init.body).toBeUndefined();
  });

  it("decodes player authorization failures without losing the stable server code", async () => {
    const transport = createTransport(async () =>
      jsonResponse(
        401,
        failureEnvelope(
          "SwordMaster.PlayerAssets.UnauthorizedPlayer",
          "An authenticated SwordMaster player is required.",
        ),
      ),
    );

    const error = await rejectedError(transport.getAttributes({ characterId }));

    expect(error).toMatchObject({
      code: "SwordMaster.PlayerAssets.UnauthorizedPlayer",
      kind: "authorization",
      message: "An authenticated SwordMaster player is required.",
      status: 401,
    });
  });

  it("decodes operation-specific validation failures and rejects undocumented ones", async () => {
    const fetch = vi
      .fn<PlayerAssetsFetch>()
      .mockResolvedValueOnce(
        jsonResponse(
          400,
          failureEnvelope("NovaCore.ValidationError", "The request payload is invalid.", {
            "$.ownedItemId": ["The JSON value could not be converted to System.Guid."],
          }),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          400,
          failureEnvelope(
            "SwordMaster.PlayerAssets.InvalidRequest",
            "If-Match: Exactly one strong revision ETag is required.",
          ),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          422,
          failureEnvelope(
            "SwordMaster.PlayerAssets.SlotMismatch",
            "The item is not compatible with the requested slot.",
            { slot: ["Expected weapon."] },
          ),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          422,
          failureEnvelope(
            "SwordMaster.PlayerAssets.SlotMismatch",
            "Unexpected GET validation response.",
          ),
        ),
      );
    const transport = createTransport(fetch);
    const mutationRequest = {
      characterId,
      expectedEtag: '"7"',
      idempotencyKey,
      ownedItemId,
      slot: "weapon",
    } as const;

    const badRequest = await rejectedError(transport.getAttributes({ characterId }));
    const invalidRequest = await rejectedError(transport.equip(mutationRequest));
    const unprocessable = await rejectedError(transport.equip(mutationRequest));
    const undocumentedGet = await rejectedError(transport.getAttributes({ characterId }));

    expect(badRequest).toMatchObject({
      code: "NovaCore.ValidationError",
      details: { "$.ownedItemId": ["The JSON value could not be converted to System.Guid."] },
      kind: "validation",
      status: 400,
    });
    expect(invalidRequest).toMatchObject({
      code: "SwordMaster.PlayerAssets.InvalidRequest",
      kind: "validation",
      status: 400,
    });
    expect(unprocessable).toMatchObject({
      code: "SwordMaster.PlayerAssets.SlotMismatch",
      details: { slot: ["Expected weapon."] },
      kind: "validation",
      status: 422,
    });
    expect(undocumentedGet).toMatchObject({
      code: "SwordMaster.PlayerAssets.SlotMismatch",
      kind: "unexpected_response",
      status: 422,
    });
  });

  it("preserves concurrency and idempotency conflict codes independently", async () => {
    const fetch = vi
      .fn<PlayerAssetsFetch>()
      .mockResolvedValueOnce(
        jsonResponse(
          409,
          failureEnvelope(
            "SwordMaster.PlayerAssets.ConcurrencyConflict",
            "The PlayerAssets revision is stale.",
          ),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          409,
          failureEnvelope(
            "SwordMaster.PlayerAssets.IdempotencyConflict",
            "The idempotency key was reused for another command.",
          ),
        ),
      );
    const transport = createTransport(fetch);
    const request = {
      characterId,
      expectedEtag: '"7"',
      idempotencyKey,
      ownedItemId,
      slot: "weapon",
    } as const;

    const concurrency = await rejectedError(transport.equip(request));
    const idempotency = await rejectedError(transport.equip(request));

    expect(concurrency).toMatchObject({
      code: "SwordMaster.PlayerAssets.ConcurrencyConflict",
      kind: "conflict",
      status: 409,
    });
    expect(idempotency).toMatchObject({
      code: "SwordMaster.PlayerAssets.IdempotencyConflict",
      kind: "conflict",
      status: 409,
    });
  });

  it("preserves the caller abort reason before and during a request", async () => {
    const preAbort = new AbortController();
    const preAbortReason = new Error("cancel before transport");
    preAbort.abort(preAbortReason);
    const getAccessToken = vi.fn(() => accessToken);
    const fetch = vi.fn<PlayerAssetsFetch>();
    const transport = createTransport(fetch, getAccessToken);

    await expect(transport.getAttributes({ characterId, signal: preAbort.signal })).rejects.toBe(
      preAbortReason,
    );
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetch).not.toHaveBeenCalled();

    const tokenAbort = new AbortController();
    const tokenAbortReason = new Error("cancel while loading token");
    const pendingToken = createTransport(
      fetch,
      async () => await new Promise<string>(() => {}),
    ).getAttributes({ characterId, signal: tokenAbort.signal });
    tokenAbort.abort(tokenAbortReason);
    await expect(pendingToken).rejects.toBe(tokenAbortReason);
    expect(fetch).not.toHaveBeenCalled();

    const inFlight = new AbortController();
    const inFlightReason = new Error("cancel after fetch started");
    fetch.mockImplementationOnce(async () => await new Promise<Response>(() => {}));
    const pending = transport.getAttributes({ characterId, signal: inFlight.signal });
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    expect(fetch.mock.calls[0]?.[1].signal).toBe(inFlight.signal);
    inFlight.abort(inFlightReason);

    await expect(pending).rejects.toBe(inFlightReason);

    const bodyAbort = new AbortController();
    const bodyAbortReason = new Error("cancel while reading response");
    const bodyFetch = vi.fn<PlayerAssetsFetch>(
      async () =>
        ({
          headers: new Headers({ ETag: '"7"' }),
          status: 200,
          text: async () => await new Promise<string>(() => {}),
        }) as unknown as Response,
    );
    const pendingBody = createTransport(bodyFetch).getAttributes({
      characterId,
      signal: bodyAbort.signal,
    });
    await vi.waitFor(() => expect(bodyFetch).toHaveBeenCalledOnce());
    bodyAbort.abort(bodyAbortReason);
    await expect(pendingBody).rejects.toBe(bodyAbortReason);
  });

  it("rejects noncanonical mutation metadata before acquiring a token or fetching", async () => {
    const getAccessToken = vi.fn(() => accessToken);
    const fetch = vi.fn<PlayerAssetsFetch>();
    const transport = createTransport(fetch, getAccessToken);
    const baseRequest = {
      characterId,
      ownedItemId,
      slot: "weapon",
    } as const;

    const weakEtag = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: 'W/"7"',
        idempotencyKey,
      }),
    );
    const uppercaseKey = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey: idempotencyKey.toUpperCase(),
      }),
    );
    const emptyKey = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey: "00000000-0000-0000-0000-000000000000",
      }),
    );
    const currentDirectorySlot = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey,
        slot: ".",
      }),
    );
    const parentDirectorySlot = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey,
        slot: "..",
      }),
    );
    const wrappedCurrentDirectorySlot = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey,
        slot: " . ",
      }),
    );
    const wrappedParentDirectorySlot = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey,
        slot: " .. ",
      }),
    );
    const forwardSlashSlot = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey,
        slot: "main/hand",
      }),
    );
    const backslashSlot = await rejectedError(
      transport.equip({
        ...baseRequest,
        expectedEtag: '"7"',
        idempotencyKey,
        slot: "main\\hand",
      }),
    );

    expect(weakEtag.kind).toBe("invalid_request");
    expect(uppercaseKey.kind).toBe("invalid_request");
    expect(emptyKey.kind).toBe("invalid_request");
    expect(currentDirectorySlot.kind).toBe("invalid_request");
    expect(parentDirectorySlot.kind).toBe("invalid_request");
    expect(wrappedCurrentDirectorySlot.kind).toBe("invalid_request");
    expect(wrappedParentDirectorySlot.kind).toBe("invalid_request");
    expect(forwardSlashSlot.kind).toBe("invalid_request");
    expect(backslashSlot.kind).toBe("invalid_request");
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("rejects missing, weak, combined, unsafe, and revision-mismatched response ETags", async () => {
    const fetch = vi
      .fn<PlayerAssetsFetch>()
      .mockResolvedValueOnce(jsonResponse(200, attributesEnvelope(7)))
      .mockResolvedValueOnce(jsonResponse(200, attributesEnvelope(7), 'W/"7"'))
      .mockResolvedValueOnce(jsonResponse(200, attributesEnvelope(7), '"7", "8"'))
      .mockResolvedValueOnce(jsonResponse(200, attributesEnvelope(7), '"9007199254740992"'))
      .mockResolvedValueOnce(jsonResponse(200, attributesEnvelope(7), '"8"'))
      .mockResolvedValueOnce(jsonResponse(200, mutationEnvelope(8), '"9"'));
    const transport = createTransport(fetch);

    for (let index = 0; index < 5; index += 1) {
      const error = await rejectedError(transport.getAttributes({ characterId }));
      expect(error).toMatchObject({ kind: "invalid_contract", status: 200 });
    }
    const mutationMismatch = await rejectedError(
      transport.equip({
        characterId,
        expectedEtag: '"7"',
        idempotencyKey,
        ownedItemId,
        slot: "weapon",
      }),
    );
    expect(mutationMismatch).toMatchObject({ kind: "invalid_contract", status: 200 });
  });

  it("rejects malformed success envelopes and required DTO fields", async () => {
    const wrongCharacter = "99999999-9999-9999-9999-999999999999";
    const missingAttributes = attributesEnvelope(7);
    delete (missingAttributes.data as { attributes?: unknown }).attributes;
    const fetch = vi
      .fn<PlayerAssetsFetch>()
      .mockResolvedValueOnce(jsonResponse(200, failureEnvelope("Code", "Failure"), '"7"'))
      .mockResolvedValueOnce(jsonResponse(200, missingAttributes, '"7"'))
      .mockResolvedValueOnce(
        jsonResponse(200, attributesEnvelope(7, { characterId: wrongCharacter }), '"7"'),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          200,
          attributesEnvelope(7, {
            attributes: [
              {
                baseValue: 2_147_483_648,
                effectiveValue: 13,
                equipmentValue: 3,
                statCode: "attack",
              },
            ],
          }),
          '"7"',
        ),
      );
    const transport = createTransport(fetch);

    for (let index = 0; index < 4; index += 1) {
      const error = await rejectedError(transport.getAttributes({ characterId }));
      expect(error).toMatchObject({ kind: "invalid_contract", status: 200 });
    }
  });

  it("classifies malformed and undocumented HTTP responses as unexpected", async () => {
    const fetch = vi
      .fn<PlayerAssetsFetch>()
      .mockResolvedValueOnce(
        jsonResponse(
          404,
          failureEnvelope(
            "SwordMaster.PlayerAssets.CharacterNotFound",
            "The character was not found.",
          ),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          500,
          failureEnvelope("SwordMaster.PlayerAssets.InternalError", "The request failed."),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(401, { status: 401, title: "Unauthorized", type: "about:blank" }),
      )
      .mockResolvedValueOnce(
        jsonResponse(418, failureEnvelope("Proxy.Teapot", "Unexpected upstream response.")),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          409,
          failureEnvelope("SwordMaster.PlayerAssets.ConcurrencyConflict", "Conflict.", {
            revision: ["valid"],
          }),
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(401, {
          error: {
            code: "SwordMaster.PlayerAssets.UnauthorizedPlayer",
            details: { authorization: "invalid" },
            message: "Invalid error details.",
          },
          succeeded: false,
        }),
      )
      .mockResolvedValueOnce(new Response("not-json", { status: 500 }));
    const transport = createTransport(fetch);

    const notFound = await rejectedError(transport.getAttributes({ characterId }));
    const server = await rejectedError(transport.getAttributes({ characterId }));
    const problemDetails = await rejectedError(transport.getAttributes({ characterId }));
    const undocumented = await rejectedError(transport.getAttributes({ characterId }));
    const undocumentedConflict = await rejectedError(transport.getAttributes({ characterId }));
    const malformedDetails = await rejectedError(transport.getAttributes({ characterId }));
    const invalidJson = await rejectedError(transport.getAttributes({ characterId }));

    expect(notFound).toMatchObject({
      code: "SwordMaster.PlayerAssets.CharacterNotFound",
      kind: "not_found",
      status: 404,
    });
    expect(server).toMatchObject({
      code: "SwordMaster.PlayerAssets.InternalError",
      kind: "server",
      status: 500,
    });
    expect(problemDetails).toMatchObject({ kind: "unexpected_response", status: 401 });
    expect(undocumented).toMatchObject({
      code: "Proxy.Teapot",
      kind: "unexpected_response",
      status: 418,
    });
    expect(undocumentedConflict).toMatchObject({
      code: "SwordMaster.PlayerAssets.ConcurrencyConflict",
      kind: "unexpected_response",
      status: 409,
    });
    expect(malformedDetails).toMatchObject({ kind: "unexpected_response", status: 401 });
    expect(invalidJson).toMatchObject({ kind: "unexpected_response", status: 500 });
  });

  it("classifies token acquisition, fetch, and response-stream failures without exposing tokens", async () => {
    const tokenFailure = createTransport(
      async () => jsonResponse(500, {}),
      async () => {
        throw new Error("credential store unavailable");
      },
    );
    const fetchFailure = createTransport(async () => {
      throw new Error("socket unavailable");
    });
    const bodyFailure = createTransport(
      async () =>
        ({
          headers: new Headers({ ETag: '"7"' }),
          status: 200,
          text: async () => {
            throw new Error("stream interrupted");
          },
        }) as unknown as Response,
    );
    const missingTokenFetch = vi.fn<PlayerAssetsFetch>();
    const missingToken = createTransport(missingTokenFetch, () => "   ");

    const tokenError = await rejectedError(tokenFailure.getAttributes({ characterId }));
    const networkError = await rejectedError(fetchFailure.getAttributes({ characterId }));
    const bodyError = await rejectedError(bodyFailure.getAttributes({ characterId }));
    const missingTokenError = await rejectedError(missingToken.getAttributes({ characterId }));

    expect(tokenError.kind).toBe("token_unavailable");
    expect(networkError).toMatchObject({ kind: "network", status: undefined });
    expect(bodyError).toMatchObject({ kind: "network", status: 200 });
    expect(missingTokenError).toMatchObject({ kind: "authorization", status: undefined });
    expect(missingTokenFetch).not.toHaveBeenCalled();
    expect(tokenError.message).not.toContain(accessToken);
    expect(networkError.message).not.toContain(accessToken);
    expect(bodyError.message).not.toContain(accessToken);
  });
});

function createTransport(
  fetch: PlayerAssetsFetch,
  getAccessToken: () => Promise<string> | string = () => accessToken,
): SwordMasterPlayerAssetsHttpTransport {
  return new SwordMasterPlayerAssetsHttpTransport({
    baseUrl: "https://api.example.test/root",
    fetch,
    getAccessToken,
  });
}

function attributesEnvelope(
  revision: number,
  overrides: Readonly<Record<string, unknown>> = {},
): Record<string, unknown> & { data: Record<string, unknown> } {
  return {
    data: {
      attributes: [
        {
          baseValue: 10,
          effectiveValue: 13,
          equipmentValue: 3,
          statCode: "attack",
        },
      ],
      characterId,
      equipmentAssignments: [
        {
          configurationVersionId,
          configuredItemId: "starter-sword",
          modifiers: [{ statCode: "attack", value: 3 }],
          ownedItemId,
          slot: "weapon",
        },
      ],
      revision,
      ...overrides,
    },
    succeeded: true,
  };
}

function mutationEnvelope(revision: number): Record<string, unknown> {
  return { data: { revision }, succeeded: true };
}

function failureEnvelope(
  code: string,
  message: string,
  details?: Readonly<Record<string, readonly string[]>>,
): Record<string, unknown> {
  return {
    error: {
      code,
      ...(details === undefined ? {} : { details }),
      message,
    },
    succeeded: false,
  };
}

function jsonResponse(status: number, payload: unknown, etag?: string): Response {
  return new Response(JSON.stringify(payload), {
    headers: {
      "Content-Type": "application/json",
      ...(etag === undefined ? {} : { ETag: etag }),
    },
    status,
  });
}

async function rejectedError(promise: Promise<unknown>): Promise<PlayerAssetsTransportError> {
  const error = await promise.then(
    () => undefined,
    (reason: unknown) => reason,
  );
  expect(error).toBeInstanceOf(PlayerAssetsTransportError);
  return error as PlayerAssetsTransportError;
}
