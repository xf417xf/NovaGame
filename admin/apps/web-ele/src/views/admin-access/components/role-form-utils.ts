import { createAdministratorIdempotencyKey } from './administrator-form-utils';

export const roleNameMaximumLength = 64;
export const rolePermissionLimit = 512;
export const permissionNameMaximumLength = 128;
export const unknownRolePermissionErrorCode =
  'NovaGame.AdminAccess.UnknownPermission';

function hasControlCharacter(value: string): boolean {
  return [...value].some((character) => {
    const codePoint = character.codePointAt(0) ?? -1;
    return codePoint <= 31 || (codePoint >= 127 && codePoint <= 159);
  });
}

export function createRoleIdempotencyKey(): string {
  return createAdministratorIdempotencyKey();
}

export function normalizeRoleName(value: string): null | string {
  try {
    return value.normalize('NFKC').trim();
  } catch {
    return null;
  }
}

export function validateRoleName(value: string): null | string {
  const candidate = normalizeRoleName(value);
  if (
    !candidate ||
    candidate.length > roleNameMaximumLength ||
    hasControlCharacter(candidate)
  ) {
    return `Role name must contain 1 to ${roleNameMaximumLength} non-control characters.`;
  }
  return null;
}

export function validateRolePermissionNames(
  permissionNames: readonly string[],
): null | string {
  if (
    permissionNames.length > rolePermissionLimit ||
    permissionNames.some(
      (permissionName) =>
        !permissionName ||
        permissionName.length > permissionNameMaximumLength ||
        hasControlCharacter(permissionName),
    ) ||
    new Set(permissionNames).size !== permissionNames.length
  ) {
    return `Select up to ${rolePermissionLimit} unique registered permissions.`;
  }
  return null;
}
