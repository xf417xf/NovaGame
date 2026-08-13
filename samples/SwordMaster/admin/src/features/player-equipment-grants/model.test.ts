import { describe, expect, it, vi } from 'vitest';

import {
  PlayerEquipmentGrantInteractionError,
  SwordMasterPlayerEquipmentGrantInteractionModel,
} from './model';
import type {
  PlayerEquipmentGrantInteractionModelOptions,
  PlayerEquipmentGrantTransport,
} from './model';
import type {
  PlayerEquipmentGrantHistoryItem,
  PlayerEquipmentGrantInteractionState,
  PlayerEquipmentGrantMutationSnapshot,
  PlayerEquipmentGrantPageSnapshot,
} from './state';

const playerA = '11111111-1111-1111-1111-111111111111';
const playerB = '22222222-2222-2222-2222-222222222222';
const grantId = '33333333-3333-3333-3333-333333333333';
const ownedItemId = '44444444-4444-4444-4444-444444444444';
const configurationVersionId = '55555555-5555-5555-5555-555555555555';
const administratorId = '66666666-6666-6666-6666-666666666666';
const firstKey = '77777777-7777-7777-7777-777777777777';
const secondKey = '88888888-8888-8888-8888-888888888888';

interface Deferred<T> {
  promise: Promise<T>;
  reject: (reason?: unknown) => void;
  resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let reject!: (reason?: unknown) => void;
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    reject = rejectPromise;
    resolve = resolvePromise;
  });
  return { promise, reject, resolve };
}

function createTransport(): {
  getPage: ReturnType<typeof vi.fn<PlayerEquipmentGrantTransport['getPage']>>;
  grant: ReturnType<typeof vi.fn<PlayerEquipmentGrantTransport['grant']>>;
  rollback: ReturnType<typeof vi.fn<PlayerEquipmentGrantTransport['rollback']>>;
} {
  return {
    getPage: vi.fn<PlayerEquipmentGrantTransport['getPage']>(),
    grant: vi.fn<PlayerEquipmentGrantTransport['grant']>(),
    rollback: vi.fn<PlayerEquipmentGrantTransport['rollback']>(),
  };
}

function createModel(
  transport: PlayerEquipmentGrantTransport,
  createIdempotencyKey: () => string = () => firstKey,
) {
  return new SwordMasterPlayerEquipmentGrantInteractionModel({
    createIdempotencyKey,
    transport,
  } satisfies PlayerEquipmentGrantInteractionModelOptions);
}

function historyItem(
  overrides: Partial<PlayerEquipmentGrantHistoryItem> = {},
): PlayerEquipmentGrantHistoryItem {
  return {
    configurationVersionId,
    configuredItemId: 'starter-sword',
    grantId,
    grantReason: 'Support correction',
    grantResultingRevision: 7,
    grantedAtUtc: '2026-08-12T00:00:00Z',
    grantedByAdministratorId: administratorId,
    ownedItemId,
    playerId: playerA,
    rollbackReason: null,
    rollbackResultingRevision: null,
    rolledBackAtUtc: null,
    rolledBackByAdministratorId: null,
    status: 1,
    ...overrides,
  };
}

function pageSnapshot(
  playerId: string = playerA,
  revision: number = 7,
  etag: string = `"${revision}"`,
  overrides: Partial<PlayerEquipmentGrantPageSnapshot['data']> = {},
): PlayerEquipmentGrantPageSnapshot {
  return {
    data: {
      items: [historyItem({ playerId })],
      page: 1,
      pageSize: 20,
      playerId,
      revision,
      totalCount: 1,
      ...overrides,
    },
    etag,
  };
}

function mutationSnapshot(
  revision: number = 8,
  etag: string = `"${revision}"`,
): PlayerEquipmentGrantMutationSnapshot {
  return {
    data: {
      configurationVersionId,
      configuredItemId: 'starter-sword',
      grantId,
      grantReason: 'Support correction',
      grantedAtUtc: '2026-08-12T00:00:00Z',
      grantedByAdministratorId: administratorId,
      ownedItemId,
      playerId: playerA,
      resultingRevision: revision,
      rollbackReason: null,
      rolledBackAtUtc: null,
      rolledBackByAdministratorId: null,
      status: 1,
    },
    etag,
  };
}

function apiError(
  status: number,
  code: string = `Status.${status}`,
  details?: Record<string, string[]>,
) {
  return Object.assign(new Error(`Request failed with status ${status}.`), {
    code,
    details,
    status,
  });
}

function failedState(state: PlayerEquipmentGrantInteractionState) {
  expect(state.phase).toBe('failed');
  return state as Extract<
    PlayerEquipmentGrantInteractionState,
    { phase: 'failed' }
  >;
}

describe('SwordMaster player equipment grant interaction model', () => {
  it('publishes an immutable, command-capable empty exact-player state', async () => {
    const transport = createTransport();
    transport.getPage.mockResolvedValue(
      pageSnapshot(playerA, 7, '"7"', { items: [], totalCount: 0 }),
    );
    transport.grant.mockRejectedValue(apiError(422, 'ConfigurationNotFound'));
    const model = createModel(transport);
    const phases: string[] = [];
    const unsubscribe = model.subscribe((state) => phases.push(state.phase));

    const state = await model.load({
      page: 1,
      pageSize: 20,
      playerId: playerA.toUpperCase(),
    });
    unsubscribe();

    expect(transport.getPage).toHaveBeenCalledWith(
      { page: 1, pageSize: 20, playerId: playerA },
      undefined,
    );
    expect(state).toMatchObject({ phase: 'empty', snapshot: { etag: '"7"' } });
    expect(phases).toEqual(['idle', 'loading', 'empty']);
    if (state.phase !== 'empty') throw new Error('Expected empty state.');
    expect(Object.isFrozen(state.snapshot)).toBe(true);
    expect(Object.isFrozen(state.snapshot.data)).toBe(true);
    expect(Object.isFrozen(state.snapshot.data.items)).toBe(true);

    await model.grant({
      configuredItemId: 'starter-sword',
      reason: 'Support correction',
    });
    expect(transport.grant).toHaveBeenCalledOnce();
  });

  it('keeps the newest overlapping lookup when an older success arrives later', async () => {
    const transport = createTransport();
    const older = deferred<PlayerEquipmentGrantPageSnapshot>();
    const newer = deferred<PlayerEquipmentGrantPageSnapshot>();
    transport.getPage
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise);
    const model = createModel(transport);

    const oldLoad = model.load({ page: 1, pageSize: 20, playerId: playerA });
    const newLoad = model.load({ page: 1, pageSize: 20, playerId: playerB });
    newer.resolve(pageSnapshot(playerB, 9));
    await newLoad;
    older.resolve(pageSnapshot(playerA, 7));
    await oldLoad;

    expect(model.state).toMatchObject({
      phase: 'ready',
      snapshot: { data: { playerId: playerB, revision: 9 } },
    });

    const failureTransport = createTransport();
    const staleFailure = deferred<PlayerEquipmentGrantPageSnapshot>();
    failureTransport.getPage
      .mockReturnValueOnce(staleFailure.promise)
      .mockResolvedValueOnce(pageSnapshot(playerB, 9));
    const failureModel = createModel(failureTransport);

    const staleLoad = failureModel.load({
      page: 1,
      pageSize: 20,
      playerId: playerA,
    });
    await failureModel.load({ page: 1, pageSize: 20, playerId: playerB });
    staleFailure.reject(apiError(500));
    await staleLoad;

    expect(failureModel.state).toMatchObject({
      phase: 'ready',
      snapshot: { data: { playerId: playerB } },
    });
  });

  it('rejects invalid request shapes and injected keys before transport', async () => {
    const transport = createTransport();
    transport.getPage.mockResolvedValueOnce(pageSnapshot());
    const createKey = vi.fn(() => 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA');
    const model = createModel(transport, createKey);

    const invalidLookup = failedState(
      await model.load({ page: 0, pageSize: 20, playerId: 'not-a-guid' }),
    );
    expect(invalidLookup).toMatchObject({
      failure: { kind: 'validation', operation: 'load' },
      retry: null,
    });
    expect(
      failedState(
        await model.load({ page: 1, pageSize: 101, playerId: playerA }),
      ).failure.kind,
    ).toBe('validation');
    expect(transport.getPage).not.toHaveBeenCalled();

    await model.load({ page: 1, pageSize: 20, playerId: playerA });
    expect(
      failedState(
        await model.grant({ configuredItemId: '  ', reason: 'Correction' }),
      ).failure.kind,
    ).toBe('validation');
    expect(
      failedState(
        await model.rollback({ grantId: 'invalid', reason: 'Correction' }),
      ).failure.kind,
    ).toBe('validation');
    expect(
      failedState(
        await model.grant({
          configuredItemId: 'starter-sword',
          reason: ' Correction ',
        }),
      ).failure.kind,
    ).toBe('validation');
    expect(createKey).not.toHaveBeenCalled();

    const invalidKey = failedState(
      await model.grant({
        configuredItemId: 'starter-sword',
        reason: 'Correction',
      }),
    );
    expect(invalidKey).toMatchObject({
      canStartNewCommand: true,
      failure: { kind: 'validation', operation: 'grant' },
      retry: null,
    });
    expect(createKey).toHaveBeenCalledOnce();
    expect(transport.grant).not.toHaveBeenCalled();
    expect(transport.rollback).not.toHaveBeenCalled();

    const invalidEtagTransport = createTransport();
    invalidEtagTransport.getPage.mockResolvedValueOnce(
      pageSnapshot(playerA, 7, 'W/"7"'),
    );
    const invalidEtagModel = createModel(invalidEtagTransport);
    await invalidEtagModel.load({
      page: 1,
      pageSize: 20,
      playerId: playerA,
    });
    expect(
      failedState(
        await invalidEtagModel.grant({
          configuredItemId: 'starter-sword',
          reason: 'Correction',
        }),
      ).failure.kind,
    ).toBe('invalid_contract');
    expect(invalidEtagTransport.grant).not.toHaveBeenCalled();
  });

  it('classifies a cancelled lookup and retries the same lookup explicitly', async () => {
    const transport = createTransport();
    transport.getPage
      .mockImplementationOnce(
        async (_request, signal) =>
          await new Promise<PlayerEquipmentGrantPageSnapshot>(
            (_resolve, reject) =>
              signal?.addEventListener('abort', () => reject(signal.reason), {
                once: true,
              }),
          ),
      )
      .mockResolvedValueOnce(pageSnapshot());
    const controller = new AbortController();
    const model = createModel(transport);

    const loading = model.load({
      page: 2,
      pageSize: 10,
      playerId: playerA,
      signal: controller.signal,
    });
    controller.abort(new DOMException('cancelled', 'AbortError'));
    const cancelled = failedState(await loading);
    expect(cancelled).toMatchObject({
      failure: { kind: 'cancelled', operation: 'load' },
      retry: 'load',
    });

    await model.retry();
    expect(transport.getPage).toHaveBeenLastCalledWith(
      { page: 2, pageSize: 10, playerId: playerA },
      undefined,
    );
    expect(model.state.phase).toBe('ready');
  });

  it('classifies every stable HTTP and network failure without losing details', async () => {
    const transport = createTransport();
    const cases = [
      [0, 'network'],
      [400, 'validation'],
      [401, 'unauthenticated'],
      [403, 'forbidden'],
      [404, 'not_found'],
      [409, 'conflict'],
      [422, 'validation'],
      [500, 'server'],
      [418, 'unexpected'],
    ] as const;
    const model = createModel(transport);

    for (const [status, kind] of cases) {
      transport.getPage.mockRejectedValueOnce(
        apiError(status, `Failure.${status}`, { reason: ['invalid'] }),
      );
      const state = failedState(
        await model.load({ page: 1, pageSize: 20, playerId: playerA }),
      );
      expect(state.failure).toMatchObject({
        code: `Failure.${status}`,
        details: { reason: ['invalid'] },
        kind,
        status,
      });
      expect(Object.isFrozen(state.failure.details?.reason)).toBe(true);
    }

    transport.getPage.mockRejectedValueOnce(
      apiError(200, 'NovaGame.InvalidResponse'),
    );
    expect(
      failedState(
        await model.load({ page: 1, pageSize: 20, playerId: playerA }),
      ).failure.kind,
    ).toBe('invalid_contract');
  });

  it('sends one exact grant command and rejects a concurrent mutation', async () => {
    const transport = createTransport();
    const pendingGrant = deferred<PlayerEquipmentGrantMutationSnapshot>();
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.grant.mockReturnValueOnce(pendingGrant.promise);
    const createKey = vi.fn(() => firstKey);
    const model = createModel(transport, createKey);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const grant = model.grant({
      configuredItemId: 'starter-sword',
      reason: 'Support correction',
    });
    expect(model.state).toMatchObject({
      attempt: 'initial',
      command: { kind: 'grant' },
      phase: 'mutating',
    });
    await expect(
      model.rollback({ grantId, reason: 'Concurrent correction' }),
    ).rejects.toBeInstanceOf(PlayerEquipmentGrantInteractionError);
    expect(transport.grant).toHaveBeenCalledWith(
      {
        configuredItemId: 'starter-sword',
        idempotencyKey: firstKey,
        ifMatch: '"7"',
        playerId: playerA,
        reason: 'Support correction',
      },
      undefined,
    );
    expect(transport.rollback).not.toHaveBeenCalled();
    expect(createKey).toHaveBeenCalledOnce();

    pendingGrant.resolve(mutationSnapshot());
    await grant;
    expect(model.state.phase).toBe('ready');
  });

  it('rejects a stale authoritative refresh after a confirmed grant and retries only the GET', async () => {
    const transport = createTransport();
    const staleRefresh = deferred<PlayerEquipmentGrantPageSnapshot>();
    transport.getPage
      .mockResolvedValueOnce(
        pageSnapshot(playerA, 7, '"7"', { page: 3, pageSize: 10 }),
      )
      .mockReturnValueOnce(staleRefresh.promise)
      .mockResolvedValueOnce(pageSnapshot(playerA, 8, '"8"', { pageSize: 10 }));
    transport.grant.mockResolvedValue(mutationSnapshot(8, '"7"'));
    const model = createModel(transport);
    await model.load({ page: 3, pageSize: 10, playerId: playerA });

    const granting = model.grant({
      configuredItemId: 'starter-sword',
      reason: 'Support correction',
    });
    await vi.waitFor(() => expect(model.state.phase).toBe('refreshing'));
    expect(model.state).toMatchObject({
      phase: 'refreshing',
      snapshot: { data: { page: 3, revision: 7 } },
    });
    staleRefresh.resolve(
      pageSnapshot(playerA, 7, '"7"', { page: 1, pageSize: 10 }),
    );
    const failed = failedState(await granting);
    expect(failed).toMatchObject({
      failure: { kind: 'invalid_contract', operation: 'refresh' },
      retry: 'refresh',
    });
    await expect(
      model.load({ page: 1, pageSize: 10, playerId: playerA }),
    ).rejects.toBeInstanceOf(PlayerEquipmentGrantInteractionError);

    await model.retry();
    expect(transport.grant).toHaveBeenCalledOnce();
    expect(transport.getPage).toHaveBeenLastCalledWith(
      { page: 1, pageSize: 10, playerId: playerA },
      undefined,
    );
    expect(model.state).toMatchObject({
      phase: 'ready',
      snapshot: { data: { page: 1, revision: 8 } },
    });
  });

  it('does not let a stale pre-mutation lookup overwrite the refreshed result', async () => {
    const transport = createTransport();
    const staleLookup = deferred<PlayerEquipmentGrantPageSnapshot>();
    transport.getPage
      .mockReturnValueOnce(staleLookup.promise)
      .mockResolvedValueOnce(pageSnapshot(playerA, 7))
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.grant.mockResolvedValue(mutationSnapshot());
    const model = createModel(transport);

    const stale = model.load({ page: 2, pageSize: 20, playerId: playerA });
    await model.load({ page: 1, pageSize: 20, playerId: playerA });
    await model.grant({
      configuredItemId: 'starter-sword',
      reason: 'Support correction',
    });
    staleLookup.resolve(pageSnapshot(playerA, 5, '"5"', { page: 2 }));
    await stale;

    expect(model.state).toMatchObject({
      phase: 'ready',
      snapshot: { data: { page: 1, revision: 8 }, etag: '"8"' },
    });
  });

  it('reuses the exact body, ETag, and key after an ambiguous network failure', async () => {
    const transport = createTransport();
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.grant
      .mockRejectedValueOnce(apiError(0, 'NovaGame.NetworkError'))
      .mockResolvedValueOnce(mutationSnapshot());
    const createKey = vi.fn(() => firstKey);
    const model = createModel(transport, createKey);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const failed = failedState(
      await model.grant({
        configuredItemId: 'starter-sword',
        reason: 'Support correction',
      }),
    );
    expect(failed.retry).toBe('exact_mutation');
    await expect(
      model.load({ page: 1, pageSize: 20, playerId: playerA }),
    ).rejects.toBeInstanceOf(PlayerEquipmentGrantInteractionError);
    await model.retry();

    expect(transport.grant).toHaveBeenCalledTimes(2);
    expect(transport.grant.mock.calls[1]?.[0]).toBe(
      transport.grant.mock.calls[0]?.[0],
    );
    expect(createKey).toHaveBeenCalledOnce();
    expect(model.state.phase).toBe('ready');
  });

  it('preserves an exact retry after an in-flight mutation is cancelled', async () => {
    const transport = createTransport();
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.grant
      .mockImplementationOnce(
        async (_request, signal) =>
          await new Promise<PlayerEquipmentGrantMutationSnapshot>(
            (_resolve, reject) =>
              signal?.addEventListener('abort', () => reject(signal.reason), {
                once: true,
              }),
          ),
      )
      .mockResolvedValueOnce(mutationSnapshot());
    const createKey = vi.fn(() => firstKey);
    const controller = new AbortController();
    const model = createModel(transport, createKey);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const granting = model.grant({
      configuredItemId: 'starter-sword',
      reason: 'Support correction',
      signal: controller.signal,
    });
    controller.abort(new DOMException('cancelled', 'AbortError'));
    const cancelled = failedState(await granting);
    expect(cancelled).toMatchObject({
      failure: { kind: 'cancelled', operation: 'grant' },
      retry: 'exact_mutation',
    });

    await model.retry();
    expect(transport.grant.mock.calls[1]?.[0]).toBe(
      transport.grant.mock.calls[0]?.[0],
    );
    expect(createKey).toHaveBeenCalledOnce();
  });

  it('uses a new command key after a definitive validation failure', async () => {
    const transport = createTransport();
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.grant
      .mockRejectedValueOnce(apiError(422, 'ConfigurationNotFound'))
      .mockResolvedValueOnce(mutationSnapshot());
    const createKey = vi
      .fn<() => string>()
      .mockReturnValueOnce(firstKey)
      .mockReturnValueOnce(secondKey);
    const model = createModel(transport, createKey);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const failed = failedState(
      await model.grant({ configuredItemId: 'missing', reason: 'Correction' }),
    );
    expect(failed).toMatchObject({
      canStartNewCommand: true,
      failure: { kind: 'validation' },
      retry: null,
    });
    await model.grant({
      configuredItemId: 'starter-sword',
      reason: 'Corrected request',
    });

    expect(
      transport.grant.mock.calls.map(([request]) => request.idempotencyKey),
    ).toEqual([firstKey, secondKey]);
  });

  it('refreshes a rollback conflict before allowing a new explicit command', async () => {
    const transport = createTransport();
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.rollback
      .mockRejectedValueOnce(
        apiError(
          409,
          'SwordMaster.PlayerAssets.EquipmentGrants.RollbackBlockedItemEquipped',
        ),
      )
      .mockResolvedValueOnce(mutationSnapshot());
    const createKey = vi
      .fn<() => string>()
      .mockReturnValueOnce(firstKey)
      .mockReturnValueOnce(secondKey);
    const model = createModel(transport, createKey);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const conflict = failedState(
      await model.rollback({
        grantId: grantId.toUpperCase(),
        reason: 'First correction',
      }),
    );
    expect(conflict).toMatchObject({
      canStartNewCommand: true,
      failure: { kind: 'conflict', operation: 'rollback' },
      retry: null,
    });
    expect(transport.getPage).toHaveBeenCalledTimes(2);

    await model.rollback({ grantId, reason: 'Retry after player unequipped' });
    expect(
      transport.rollback.mock.calls.map(([request]) => request.idempotencyKey),
    ).toEqual([firstKey, secondKey]);
    expect(transport.rollback.mock.calls[0]?.[0].grantId).toBe(grantId);
    expect(transport.getPage).toHaveBeenCalledTimes(3);
  });

  it('retries only a failed conflict refresh and then restores the original conflict', async () => {
    const transport = createTransport();
    const conflictCode =
      'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict';
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockRejectedValueOnce(apiError(0, 'NovaGame.NetworkError'))
      .mockResolvedValueOnce(pageSnapshot(playerA, 8));
    transport.rollback.mockRejectedValueOnce(apiError(409, conflictCode));
    const model = createModel(transport);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const refreshFailure = failedState(
      await model.rollback({ grantId, reason: 'Correction' }),
    );
    expect(refreshFailure).toMatchObject({
      failure: { kind: 'network', operation: 'refresh' },
      retry: 'refresh',
    });
    await expect(
      model.load({ page: 1, pageSize: 20, playerId: playerA }),
    ).rejects.toBeInstanceOf(PlayerEquipmentGrantInteractionError);
    await model.retry();

    const conflict = failedState(model.state);
    expect(conflict).toMatchObject({
      canStartNewCommand: true,
      failure: { code: conflictCode, kind: 'conflict', operation: 'rollback' },
      retry: null,
      snapshot: { etag: '"8"' },
    });
    expect(transport.rollback).toHaveBeenCalledOnce();
  });

  it('rejects a concurrency refresh that returns the stale opaque ETag', async () => {
    const transport = createTransport();
    const conflictCode =
      'SwordMaster.PlayerAssets.EquipmentGrants.ConcurrencyConflict';
    transport.getPage
      .mockResolvedValueOnce(pageSnapshot())
      .mockResolvedValueOnce(pageSnapshot(playerA, 7, '"7"'))
      .mockResolvedValueOnce(pageSnapshot(playerA, 8, '"8"'));
    transport.grant.mockRejectedValueOnce(apiError(409, conflictCode));
    const model = createModel(transport);
    await model.load({ page: 1, pageSize: 20, playerId: playerA });

    const stale = failedState(
      await model.grant({
        configuredItemId: 'starter-sword',
        reason: 'Correction',
      }),
    );
    expect(stale).toMatchObject({
      failure: { kind: 'invalid_contract', operation: 'refresh' },
      retry: 'refresh',
    });
    await model.retry();

    expect(failedState(model.state)).toMatchObject({
      failure: { code: conflictCode, kind: 'conflict' },
      retry: null,
      snapshot: { etag: '"8"' },
    });
    expect(transport.grant).toHaveBeenCalledOnce();
  });
});
