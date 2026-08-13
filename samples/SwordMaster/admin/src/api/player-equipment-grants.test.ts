import type {
  PlayerEquipmentGrantRawResponse,
  PlayerEquipmentGrantRequestClient,
} from './player-equipment-grants';

import { describe, expect, it, vi } from 'vitest';

const defaultRequestClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock('./request', () => ({ requestClient: defaultRequestClient }));

import { createPlayerEquipmentGrantTransport } from './player-equipment-grants';

const administratorId = '11111111-1111-1111-1111-111111111111';
const playerId = '22222222-2222-4222-8222-abcdefabcdef';
const grantId = '33333333-3333-4333-8333-abcdefabcdef';
const ownedItemId = '44444444-4444-4444-8444-abcdefabcdef';
const configurationVersionId = '55555555-5555-4555-8555-555555555555';
const idempotencyKey = '66666666-6666-4666-8666-abcdefabcdef';

describe('player-equipment-grant administrator transport', () => {
  it('gets exact-player history with a raw response and preserves its ETag', async () => {
    const { client, get } = createClient();
    get.mockResolvedValue(pageResponse(7, '"7"', { page: 2, pageSize: 25 }));
    const controller = new AbortController();
    const routePlayerId = playerId.toUpperCase();

    const result = await createPlayerEquipmentGrantTransport(client).getPage(
      { page: 2, pageSize: 25, playerId: routePlayerId },
      controller.signal,
    );

    expect(result).toEqual({
      data: pageData(7, { page: 2, pageSize: 25 }),
      etag: '"7"',
    });
    expect(get).toHaveBeenCalledWith(
      `/swordmaster/operations/player-assets/${playerId}/equipment-grants`,
      {
        params: { page: 2, pageSize: 25 },
        responseReturn: 'raw',
        signal: controller.signal,
      },
    );
  });

  it('posts one grant with the canonical concurrency and idempotency headers', async () => {
    const { client, post } = createClient();
    post.mockResolvedValue(mutationResponse(8, '"8"'));
    const controller = new AbortController();

    const result = await createPlayerEquipmentGrantTransport(client).grant(
      {
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"7"',
        playerId: playerId.toUpperCase(),
        reason: 'Customer support correction',
      },
      controller.signal,
    );

    expect(result).toEqual({ data: mutationData(8), etag: '"8"' });
    expect(post).toHaveBeenCalledWith(
      `/swordmaster/operations/player-assets/${playerId}/equipment-grants`,
      {
        configuredItemId: 'starter-sword',
        reason: 'Customer support correction',
      },
      {
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'If-Match': '"7"',
        },
        responseReturn: 'raw',
        signal: controller.signal,
      },
    );
  });

  it('posts rollback only to the selected grant route', async () => {
    const { client, post } = createClient();
    const rollbackEvidence = {
      rollbackReason: 'Correction approved by support lead',
      rolledBackAtUtc: '2026-08-12T10:00:00.0000000Z',
      rolledBackByAdministratorId: administratorId,
      status: 2,
    };
    post.mockResolvedValue(mutationResponse(9, '"9"', rollbackEvidence));

    const result = await createPlayerEquipmentGrantTransport(client).rollback({
      grantId: grantId.toUpperCase(),
      idempotencyKey,
      ifMatch: '"8"',
      playerId: playerId.toUpperCase(),
      reason: 'Correction approved by support lead',
    });

    expect(result).toEqual({
      data: mutationData(9, rollbackEvidence),
      etag: '"9"',
    });
    expect(post).toHaveBeenCalledWith(
      `/swordmaster/operations/player-assets/${playerId}/equipment-grants/${grantId}/rollback`,
      { reason: 'Correction approved by support lead' },
      {
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'If-Match': '"8"',
        },
        responseReturn: 'raw',
      },
    );
  });

  it('preserves stable administrator failure code, status, and details', async () => {
    const { client, get } = createClient();
    get.mockRejectedValue(
      responseFailure(
        403,
        'SwordMaster.PlayerAssets.EquipmentGrants.Forbidden',
        'The administrator does not have the required permission.',
        { permission: ['Manage is required.'] },
      ),
    );

    await expect(
      createPlayerEquipmentGrantTransport(client).getPage({
        page: 1,
        pageSize: 20,
        playerId,
      }),
    ).rejects.toMatchObject({
      code: 'SwordMaster.PlayerAssets.EquipmentGrants.Forbidden',
      details: { permission: ['Manage is required.'] },
      status: 403,
    });
  });

  it('preserves distinct concurrency and idempotency conflicts', async () => {
    const { client, post } = createClient();
    post
      .mockRejectedValueOnce(
        responseFailure(
          409,
          'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict',
          'The PlayerAssets revision is stale.',
        ),
      )
      .mockRejectedValueOnce(
        responseFailure(
          409,
          'SwordMaster.PlayerAssets.EquipmentGrants.IdempotencyConflict',
          'The idempotency key was reused for another command.',
        ),
      );
    const transport = createPlayerEquipmentGrantTransport(client);

    await expect(
      transport.grant({
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"7"',
        playerId,
        reason: 'Customer support correction',
      }),
    ).rejects.toMatchObject({
      code: 'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict',
      status: 409,
    });
    await expect(
      transport.rollback({
        grantId,
        idempotencyKey,
        ifMatch: '"8"',
        playerId,
        reason: 'Correction approved by support lead',
      }),
    ).rejects.toMatchObject({
      code: 'SwordMaster.PlayerAssets.EquipmentGrants.IdempotencyConflict',
      status: 409,
    });
  });

  it('rejects invalid exact-player paging and If-Match metadata before sending a request', async () => {
    const { client, get, post } = createClient();
    const transport = createPlayerEquipmentGrantTransport(client);

    for (const invalidPage of [0, 1.5, 2_147_483_648]) {
      await expect(
        transport.getPage({ page: invalidPage, pageSize: 20, playerId }),
      ).rejects.toMatchObject({
        code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
        status: 400,
      });
    }
    for (const invalidPageSize of [0, 100.5, 101]) {
      await expect(
        transport.getPage({ page: 1, pageSize: invalidPageSize, playerId }),
      ).rejects.toMatchObject({
        code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
        status: 400,
      });
    }
    await expect(
      transport.getPage({
        page: 1,
        pageSize: 20,
        playerId: 'not-a-guid',
      }),
    ).rejects.toMatchObject({
      code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
      status: 400,
    });

    for (const ifMatch of ['*', 'W/"7"', '"01"', '"0"', '7', '"7", "8"']) {
      await expect(
        transport.grant({
          configuredItemId: 'starter-sword',
          idempotencyKey,
          ifMatch,
          playerId,
          reason: 'Customer support correction',
        }),
      ).rejects.toMatchObject({
        code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
        status: 400,
      });
    }
    expect(get).not.toHaveBeenCalled();
    expect(post).not.toHaveBeenCalled();
  });

  it('rejects noncanonical identifiers and invalid command bodies before sending a mutation', async () => {
    const { client, post } = createClient();
    const transport = createPlayerEquipmentGrantTransport(client);

    for (const invalidKey of [
      '',
      emptyGuid(),
      idempotencyKey.toUpperCase(),
      '66666666-6666-4666-8666-66666666666g',
    ]) {
      await expect(
        transport.rollback({
          grantId,
          idempotencyKey: invalidKey,
          ifMatch: '"7"',
          playerId,
          reason: 'Correction approved by support lead',
        }),
      ).rejects.toMatchObject({
        code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
        status: 400,
      });
    }
    for (const invalidConfiguredItemId of [
      '',
      ' starter-sword',
      'starter sword',
    ]) {
      await expect(
        transport.grant({
          configuredItemId: invalidConfiguredItemId,
          idempotencyKey,
          ifMatch: '"7"',
          playerId,
          reason: 'Customer support correction',
        }),
      ).rejects.toMatchObject({
        code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
        status: 400,
      });
    }
    for (const invalidReason of ['', ' leading', 'trailing ', 'line\nbreak']) {
      await expect(
        transport.grant({
          configuredItemId: 'starter-sword',
          idempotencyKey,
          ifMatch: '"7"',
          playerId,
          reason: invalidReason,
        }),
      ).rejects.toMatchObject({
        code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
        status: 400,
      });
    }
    await expect(
      transport.rollback({
        grantId: 'not-a-guid',
        idempotencyKey,
        ifMatch: '"7"',
        playerId,
        reason: 'Correction approved by support lead',
      }),
    ).rejects.toMatchObject({
      code: 'SwordMaster.PlayerAssets.EquipmentGrants.InvalidRequest',
      status: 400,
    });
    expect(post).not.toHaveBeenCalled();
  });

  it('rejects missing, weak, combined, and noncanonical response ETags', async () => {
    const { client, get } = createClient();
    get
      .mockResolvedValueOnce(pageResponse(7))
      .mockResolvedValueOnce(pageResponse(7, 'W/"7"'))
      .mockResolvedValueOnce(pageResponse(7, '"7", "8"'))
      .mockResolvedValueOnce(pageResponse(7, '"01"'));
    const transport = createPlayerEquipmentGrantTransport(client);

    for (let index = 0; index < 4; index += 1) {
      await expect(
        transport.getPage({ page: 1, pageSize: 20, playerId }),
      ).rejects.toMatchObject({
        code: 'NovaGame.InvalidResponse',
        status: 200,
      });
    }
  });

  it('preserves a syntactically valid large revision ETag without numeric comparison', async () => {
    const { client, get, post } = createClient();
    const longEtag = '"9223372036854775807"';
    const roundedLong = 9_223_372_036_854_776_000;
    get.mockResolvedValue(pageResponse(roundedLong, longEtag));
    post.mockResolvedValue(mutationResponse(roundedLong, longEtag));
    const transport = createPlayerEquipmentGrantTransport(client);

    await expect(
      transport.getPage({ page: 1, pageSize: 20, playerId }),
    ).resolves.toMatchObject({ etag: longEtag });
    await expect(
      transport.grant({
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"7"',
        playerId,
        reason: 'Customer support correction',
      }),
    ).resolves.toMatchObject({ etag: longEtag });
  });

  it('rejects malformed envelopes and inconsistent history lifecycle evidence', async () => {
    const { client, get } = createClient();
    const nullRawResponse =
      null as unknown as PlayerEquipmentGrantRawResponse<never>;
    const nullHeaders = {
      data: { data: pageData(7), error: null, succeeded: true },
      headers: null,
      status: 200,
    } as unknown as PlayerEquipmentGrantRawResponse<never>;
    const nullEnvelope = rawEnvelope(null, '"7"');
    const stringEnvelope = rawEnvelope('not an API envelope', '"7"');
    const malformedSuccessEnvelope = rawEnvelope(
      {
        data: pageData(7),
        error: { code: 'Unexpected', details: null, message: 'Unexpected.' },
        succeeded: true,
      },
      '"7"',
    );
    const missingItems = pageData(7) as Record<string, unknown>;
    delete missingItems.items;
    const mismatchedPlayer = pageData(7, {
      playerId: '99999999-9999-4999-8999-999999999999',
    });
    const mismatchedPage = pageData(7, { page: 2 });
    const mismatchedPageSize = pageData(7, { pageSize: 25 });
    const invalidItem = pageData(7, {
      items: [grantData({ status: 3 })],
    });
    const activeWithRollbackEvidence = pageData(7, {
      items: [
        grantData({
          rollbackReason: 'Unexpected rollback evidence',
        }),
      ],
    });
    const rolledBackWithoutEvidence = pageData(7, {
      items: [grantData({ status: 2 })],
    });
    get
      .mockResolvedValueOnce(nullRawResponse)
      .mockResolvedValueOnce(nullHeaders)
      .mockResolvedValueOnce(nullEnvelope)
      .mockResolvedValueOnce(stringEnvelope)
      .mockResolvedValueOnce(malformedSuccessEnvelope)
      .mockResolvedValueOnce(rawResponse(missingItems, '"7"'))
      .mockResolvedValueOnce(rawResponse(mismatchedPlayer, '"7"'))
      .mockResolvedValueOnce(rawResponse(mismatchedPage, '"7"'))
      .mockResolvedValueOnce(rawResponse(mismatchedPageSize, '"7"'))
      .mockResolvedValueOnce(rawResponse(invalidItem, '"7"'))
      .mockResolvedValueOnce(rawResponse(activeWithRollbackEvidence, '"7"'))
      .mockResolvedValueOnce(rawResponse(rolledBackWithoutEvidence, '"7"'));
    const transport = createPlayerEquipmentGrantTransport(client);

    for (const status of [0, ...Array<number>(11).fill(200)]) {
      await expect(
        transport.getPage({ page: 1, pageSize: 20, playerId }),
      ).rejects.toMatchObject({
        code: 'NovaGame.InvalidResponse',
        status,
      });
    }
  });

  it('rejects incomplete, mismatched, and inconsistent mutation data', async () => {
    const { client, post } = createClient();
    const missingRevision = mutationData(8) as Record<string, unknown>;
    delete missingRevision.resultingRevision;
    const rollbackEvidence = {
      rollbackReason: 'Correction approved by support lead',
      rolledBackAtUtc: '2026-08-12T10:00:00.0000000Z',
      rolledBackByAdministratorId: administratorId,
      status: 2,
    };
    const otherGrant = mutationData(9, {
      grantId: '99999999-9999-4999-8999-999999999999',
      ...rollbackEvidence,
    });
    const activeWithRollbackEvidence = mutationData(9, {
      rollbackReason: 'Unexpected rollback evidence',
    });
    const rolledBackWithoutEvidence = mutationData(9, { status: 2 });
    const grantReturnedRolledBack = mutationData(9, rollbackEvidence);
    const rollbackReturnedActive = mutationData(9);
    post
      .mockResolvedValueOnce(rawResponse(missingRevision, '"8"'))
      .mockResolvedValueOnce(rawResponse(otherGrant, '"9"'))
      .mockResolvedValueOnce(rawResponse(activeWithRollbackEvidence, '"9"'))
      .mockResolvedValueOnce(rawResponse(rolledBackWithoutEvidence, '"9"'))
      .mockResolvedValueOnce(rawResponse(grantReturnedRolledBack, '"9"'))
      .mockResolvedValueOnce(rawResponse(rollbackReturnedActive, '"9"'));
    const transport = createPlayerEquipmentGrantTransport(client);

    await expect(
      transport.grant({
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"7"',
        playerId,
        reason: 'Customer support correction',
      }),
    ).rejects.toMatchObject({
      code: 'NovaGame.InvalidResponse',
      status: 200,
    });
    await expect(
      transport.rollback({
        grantId,
        idempotencyKey,
        ifMatch: '"8"',
        playerId,
        reason: 'Correction approved by support lead',
      }),
    ).rejects.toMatchObject({
      code: 'NovaGame.InvalidResponse',
      status: 200,
    });
    await expect(
      transport.rollback({
        grantId,
        idempotencyKey,
        ifMatch: '"8"',
        playerId,
        reason: 'Correction approved by support lead',
      }),
    ).rejects.toMatchObject({
      code: 'NovaGame.InvalidResponse',
      status: 200,
    });
    await expect(
      transport.grant({
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"8"',
        playerId,
        reason: 'Customer support correction',
      }),
    ).rejects.toMatchObject({
      code: 'NovaGame.InvalidResponse',
      status: 200,
    });
    await expect(
      transport.grant({
        configuredItemId: 'starter-sword',
        idempotencyKey,
        ifMatch: '"8"',
        playerId,
        reason: 'Customer support correction',
      }),
    ).rejects.toMatchObject({
      code: 'NovaGame.InvalidResponse',
      status: 200,
    });
    await expect(
      transport.rollback({
        grantId,
        idempotencyKey,
        ifMatch: '"8"',
        playerId,
        reason: 'Correction approved by support lead',
      }),
    ).rejects.toMatchObject({
      code: 'NovaGame.InvalidResponse',
      status: 200,
    });
  });

  it('preserves caller cancellation instead of converting it to a transport error', async () => {
    const { client, get } = createClient();
    const preAborted = new AbortController();
    const preAbortReason = new Error('cancel before history request');
    preAborted.abort(preAbortReason);
    const transport = createPlayerEquipmentGrantTransport(client);

    await expect(
      transport.getPage({ page: 1, pageSize: 20, playerId }, preAborted.signal),
    ).rejects.toBe(preAbortReason);
    expect(get).not.toHaveBeenCalled();

    let rejectRequest!: (reason: unknown) => void;
    get.mockImplementationOnce(
      () =>
        new Promise((_, reject) => {
          rejectRequest = reject;
        }),
    );
    const inFlight = new AbortController();
    const inFlightReason = new Error('cancel while history request is pending');
    const pending = transport.getPage(
      { page: 1, pageSize: 20, playerId },
      inFlight.signal,
    );
    inFlight.abort(inFlightReason);
    rejectRequest(new Error('request client canceled'));

    await expect(pending).rejects.toBe(inFlightReason);
  });
});

function createClient() {
  const get = vi.fn();
  const post = vi.fn();
  return {
    client: { get, post } as unknown as PlayerEquipmentGrantRequestClient,
    get,
    post,
  };
}

function pageResponse(
  revision: number,
  etag?: string,
  overrides: Record<string, unknown> = {},
) {
  return rawResponse(pageData(revision, overrides), etag);
}

function mutationResponse(
  revision: number,
  etag: string,
  overrides: Record<string, unknown> = {},
) {
  return rawResponse(mutationData(revision, overrides), etag);
}

function rawResponse<T>(
  data: T,
  etag?: string,
): PlayerEquipmentGrantRawResponse<T> {
  return {
    data: { data, error: null, succeeded: true },
    headers: etag === undefined ? {} : { etag },
    status: 200,
  };
}

function rawEnvelope(
  envelope: unknown,
  etag: string,
): PlayerEquipmentGrantRawResponse<never> {
  return {
    data: envelope as never,
    headers: { etag },
    status: 200,
  };
}

function pageData(revision: number, overrides: Record<string, unknown> = {}) {
  return {
    items: [grantData()],
    page: 1,
    pageSize: 20,
    playerId,
    revision,
    totalCount: 1,
    ...overrides,
  };
}

function grantData(overrides: Record<string, unknown> = {}) {
  return {
    configurationVersionId,
    configuredItemId: 'starter-sword',
    grantId,
    grantReason: 'Customer support correction',
    grantResultingRevision: 7,
    grantedAtUtc: '2026-08-12T09:00:00.0000000Z',
    grantedByAdministratorId: administratorId,
    ownedItemId,
    playerId,
    rollbackReason: null,
    rollbackResultingRevision: null,
    rolledBackAtUtc: null,
    rolledBackByAdministratorId: null,
    status: 1,
    ...overrides,
  };
}

function mutationData(
  resultingRevision: number,
  overrides: Record<string, unknown> = {},
) {
  return {
    configurationVersionId,
    configuredItemId: 'starter-sword',
    grantId,
    grantReason: 'Customer support correction',
    grantedAtUtc: '2026-08-12T09:00:00.0000000Z',
    grantedByAdministratorId: administratorId,
    ownedItemId,
    playerId,
    resultingRevision,
    rollbackReason: null,
    rolledBackAtUtc: null,
    rolledBackByAdministratorId: null,
    status: 1,
    ...overrides,
  };
}

function responseFailure(
  status: number,
  code: string,
  message: string,
  details: null | Record<string, string[]> = null,
) {
  return {
    response: {
      data: {
        error: { code, details, message },
        succeeded: false,
      },
      status,
    },
  };
}

function emptyGuid() {
  return '00000000-0000-0000-0000-000000000000';
}
