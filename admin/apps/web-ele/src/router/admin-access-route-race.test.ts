import type { RouteRecordRaw } from 'vue-router';

import type { GenerateMenuAndRoutesOptions } from '@vben/types';

import { createMemoryHistory, createRouter } from 'vue-router';

import { describe, expect, it, vi } from 'vitest';

import { refreshAccessibleRoutes, resetAccessibleRoutes } from './access';
import { routes } from './routes';

const deferredGeneration = vi.hoisted(() => {
  let rejectAfterInjection = false;
  let resolveGeneration: () => void = () => {
    throw new Error('Deferred route generation is unavailable.');
  };
  let resolveStarted: () => void = () => {
    throw new Error('Deferred route generation start is unavailable.');
  };
  let generation: Promise<void>;
  let invocationCount = 0;
  let started: Promise<void>;

  const prepare = (reject = false) => {
    rejectAfterInjection = reject;
    invocationCount = 0;
    generation = new Promise<void>((resolve) => {
      resolveGeneration = resolve;
    });
    started = new Promise<void>((resolve) => {
      resolveStarted = resolve;
    });
  };
  prepare();

  return {
    beginInvocation: () => ++invocationCount,
    prepare,
    release: () => resolveGeneration(),
    shouldRejectAfterInjection: () => rejectAfterInjection,
    signalStarted: () => resolveStarted(),
    waitForFirstGeneration: () => generation,
    waitForFirstStart: () => started,
  };
});

vi.mock('@vben/access', () => ({
  generateAccessible: vi.fn(
    async (_mode: unknown, options: GenerateMenuAndRoutesOptions) => {
      const invocation = deferredGeneration.beginInvocation();
      const route: RouteRecordRaw = {
        component: () => null,
        name:
          invocation === 1
            ? 'StaleAdministratorAccessRoute'
            : 'FreshAdministratorAccessRoute',
        path: invocation === 1 ? '/admin-access/stale' : '/admin-access/fresh',
      };

      if (invocation === 1) {
        deferredGeneration.signalStarted();
        await deferredGeneration.waitForFirstGeneration();
      }

      options.router.addRoute('Root', route);
      if (invocation === 1 && deferredGeneration.shouldRejectAfterInjection()) {
        throw new Error(
          'Deferred route generation failed after route injection.',
        );
      }

      return { accessibleMenus: [], accessibleRoutes: [route] };
    },
  ),
}));

describe('administrator access route refresh races', () => {
  it('removes stale root children before a queued relogin refresh applies', async () => {
    deferredGeneration.prepare();
    const router = createRouter({
      history: createMemoryHistory(),
      routes,
    });

    const staleRefresh = refreshAccessibleRoutes(router, []);
    await deferredGeneration.waitForFirstStart();
    resetAccessibleRoutes(router);
    const freshRefresh = refreshAccessibleRoutes(router, []);
    deferredGeneration.release();

    await expect(staleRefresh).resolves.toBeNull();
    expect(router.hasRoute('StaleAdministratorAccessRoute')).toBe(false);
    await expect(freshRefresh).resolves.toMatchObject({
      accessibleRoutes: [
        {
          name: 'FreshAdministratorAccessRoute',
        },
      ],
    });
    expect(router.hasRoute('FreshAdministratorAccessRoute')).toBe(true);
  });

  it('cleans injected routes when generation fails after logout invalidation', async () => {
    deferredGeneration.prepare(true);
    const router = createRouter({
      history: createMemoryHistory(),
      routes,
    });

    const refresh = refreshAccessibleRoutes(router, []);
    await deferredGeneration.waitForFirstStart();
    resetAccessibleRoutes(router);
    deferredGeneration.release();

    await expect(refresh).rejects.toThrow(
      'Deferred route generation failed after route injection.',
    );
    expect(router.hasRoute('StaleAdministratorAccessRoute')).toBe(false);
  });
});
