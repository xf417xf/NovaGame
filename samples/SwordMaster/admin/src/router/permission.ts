import type { RouteRecordRaw } from 'vue-router';

export function isAccessRouteDenied(
  path: string,
  routes: RouteRecordRaw[],
  accessCodes: string[],
): boolean {
  const route = routes.find((candidate) => candidate.path === path);
  const authority = route?.meta?.authority;
  return (
    Array.isArray(authority) &&
    authority.length > 0 &&
    !accessCodes.some((code) => authority.includes(code))
  );
}
