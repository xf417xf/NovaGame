import { createMemoryHistory, createRouter } from 'vue-router';

import { describe, expect, it } from 'vitest';

import { AdminAccessPermission } from '#/features/admin-access/permissions';

import { refreshAccessibleRoutes } from './access';
import { routes } from './routes';

const deniedSplitRoleRoutes = [
  ['AdminAccessRoles', '/admin-access/roles'],
  ['AdminAccessSessions', '/admin-access/sessions'],
  ['AdminAccessAudit', '/admin-access/audit'],
] as const;

describe('administrator access route reconciliation', () => {
  it('removes stale leaf matchers after a full-to-split permission refresh', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes,
    });

    await refreshAccessibleRoutes(router, Object.values(AdminAccessPermission));
    for (const [name, path] of deniedSplitRoleRoutes) {
      expect(router.hasRoute(name)).toBe(true);
      expect(router.resolve(path).name).toBe(name);
    }

    const refreshedRoutes = await refreshAccessibleRoutes(router, [
      AdminAccessPermission.Access,
      AdminAccessPermission.AdministratorsRead,
    ]);
    if (refreshedRoutes === null) {
      throw new Error('The current route refresh unexpectedly became stale.');
    }

    const { accessibleRoutes } = refreshedRoutes;
    const adminAccessRoute = accessibleRoutes.find(
      (route) => route.name === 'AdminAccess',
    );
    expect(adminAccessRoute?.children?.map((child) => child.name)).toEqual([
      'AdminAccessAdministrators',
    ]);
    expect(router.hasRoute('AdminAccessAdministrators')).toBe(true);

    for (const [name, path] of deniedSplitRoleRoutes) {
      expect(router.hasRoute(name)).toBe(false);
      const resolved = router.resolve(path);
      expect(resolved.name).toBe('FallbackNotFound');
      expect(resolved.path).toBe(path);
      expect(resolved.matched.at(-1)?.name).toBe('FallbackNotFound');
    }

    const rootRoute = router.resolve('/admin-access').matched.at(-1);
    expect(rootRoute?.name).toBe('AdminAccess');
    expect(rootRoute?.redirect).toBe('/admin-access/administrators');
  });
});
