import type { RouteRecordRaw } from 'vue-router';

import type { AdminAccessLeafPermission } from '#/features/admin-access/permissions';

import {
  AdminAccessPermission,
  hasAdminAccessPermission,
} from '#/features/admin-access/permissions';

const administratorAccessRouteName = 'AdminAccess';

type AdministratorAccessChildRouteName =
  | 'AdminAccessAdministrators'
  | 'AdminAccessAudit'
  | 'AdminAccessRoles'
  | 'AdminAccessSessions';

const requiredPermissions = {
  AdminAccessAdministrators: AdminAccessPermission.AdministratorsRead,
  AdminAccessAudit: AdminAccessPermission.AuditLogsRead,
  AdminAccessRoles: AdminAccessPermission.RolesRead,
  AdminAccessSessions: AdminAccessPermission.SessionsRead,
} as const satisfies Record<
  AdministratorAccessChildRouteName,
  AdminAccessLeafPermission
>;

const comingSoonComponent = () =>
  import('#/views/_core/fallback/coming-soon.vue');

const routes: RouteRecordRaw[] = [
  {
    children: [
      {
        component: () => import('#/views/admin-access/administrators.vue'),
        meta: { title: 'Administrators' },
        name: 'AdminAccessAdministrators',
        path: '/admin-access/administrators',
      },
      {
        component: comingSoonComponent,
        meta: { title: 'Roles and permissions' },
        name: 'AdminAccessRoles',
        path: '/admin-access/roles',
      },
      {
        component: comingSoonComponent,
        meta: { title: 'Sessions' },
        name: 'AdminAccessSessions',
        path: '/admin-access/sessions',
      },
      {
        component: comingSoonComponent,
        meta: { title: 'Audit history' },
        name: 'AdminAccessAudit',
        path: '/admin-access/audit',
      },
    ],
    meta: {
      icon: 'lucide:shield-check',
      order: -2,
      title: 'Administrator Access',
    },
    name: administratorAccessRouteName,
    path: '/admin-access',
  },
];

function getRequiredPermission(
  route: RouteRecordRaw,
): AdminAccessLeafPermission | undefined {
  if (typeof route.name !== 'string') {
    return undefined;
  }

  return requiredPermissions[route.name as AdministratorAccessChildRouteName];
}

/**
 * Vben's authority arrays use OR semantics. Filter this one group explicitly
 * before generic route generation so each child requires Access and its leaf.
 */
export function filterAdminAccessRoutes(
  candidateRoutes: readonly RouteRecordRaw[],
  accessCodes: readonly string[],
): RouteRecordRaw[] {
  return candidateRoutes.flatMap((route) => {
    if (route.name !== administratorAccessRouteName) {
      return [route];
    }

    const children = (route.children ?? [])
      .filter((child) => {
        const requiredPermission = getRequiredPermission(child);
        return (
          requiredPermission !== undefined &&
          hasAdminAccessPermission(accessCodes, requiredPermission)
        );
      })
      .map((child) => ({
        ...child,
        meta: child.meta ? { ...child.meta } : undefined,
      }));

    const firstChild = children[0];
    if (!firstChild?.path) {
      return [];
    }

    return [
      {
        ...route,
        children,
        meta: route.meta ? { ...route.meta } : undefined,
        redirect: firstChild.path,
      },
    ];
  });
}

export { administratorAccessRouteName };
export default routes;
