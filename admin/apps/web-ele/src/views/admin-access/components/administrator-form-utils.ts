import type { NovaGameApiError } from '#/api';

import { toNovaGameApiError } from '#/api';
import { operationsErrorMessage, tOperations } from '#/locales/operations';

export const administratorPasswordMaximumLength = 256;
export const administratorPasswordMinimumLength = 12;
export const administratorRoleLimit = 100;
export const administratorUserNameMaximumLength = 64;
export const administratorUserNameMinimumLength = 3;
export const administratorDisplayNameMaximumLength = 128;

const canonicalGuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export type AdministratorFieldErrors = Record<string, string>;

function normalize(value: string): null | string {
  try {
    return value.normalize('NFKC').trim();
  } catch {
    return null;
  }
}

function hasControlCharacter(value: string): boolean {
  return [...value].some((character) => {
    const codePoint = character.codePointAt(0) ?? -1;
    return codePoint <= 31 || (codePoint >= 127 && codePoint <= 159);
  });
}

export function createAdministratorIdempotencyKey(): string {
  const key = globalThis.crypto?.randomUUID?.();
  if (!key || !canonicalGuidPattern.test(key)) {
    throw new Error(
      tOperations(
        'thisBrowserCannotCreateTheRequiredCommandIdentifierRetryInASupportedBrowser',
      ),
    );
  }
  return key;
}

export function fieldErrorsFrom(error: unknown): AdministratorFieldErrors {
  const apiError = toNovaGameApiError(error);
  return Object.fromEntries(
    Object.entries(apiError.details ?? {}).map(([field, messages]) => [
      field.charAt(0).toLowerCase() + field.slice(1),
      messages[0] ?? operationsErrorMessage(apiError),
    ]),
  );
}

export function isUncertainCommandError(error: NovaGameApiError): boolean {
  return error.status === 0 || error.status >= 500;
}

export function normalizeDisplayName(value: string): null | string {
  return normalize(value);
}

export function validateAdministratorDisplayName(value: string): null | string {
  const candidate = normalize(value);
  if (
    !candidate ||
    candidate.length > administratorDisplayNameMaximumLength ||
    hasControlCharacter(candidate)
  ) {
    return tOperations(
      'displayNameMustContain1ToParameterNonControlCharacters',
      { value0: administratorDisplayNameMaximumLength },
    );
  }
  return null;
}

export function validateAdministratorPassword(value: string): null | string {
  if (
    value.length < administratorPasswordMinimumLength ||
    value.length > administratorPasswordMaximumLength
  ) {
    return tOperations('passwordMustContainParameterToParameterCharacters', {
      value0: administratorPasswordMinimumLength,
      value1: administratorPasswordMaximumLength,
    });
  }
  return null;
}

export function validateAdministratorRoleIds(
  roleIds: readonly string[],
): null | string {
  if (
    roleIds.length > administratorRoleLimit ||
    roleIds.some((roleId) => !roleId) ||
    new Set(roleIds).size !== roleIds.length
  ) {
    return tOperations('selectUpToParameterUniqueRoles', {
      value0: administratorRoleLimit,
    });
  }
  return null;
}

export function validateAdministratorUserName(value: string): null | string {
  const candidate = normalize(value);
  if (
    !candidate ||
    candidate.length < administratorUserNameMinimumLength ||
    candidate.length > administratorUserNameMaximumLength ||
    /\s/.test(candidate) ||
    hasControlCharacter(candidate)
  ) {
    return tOperations(
      'userNameMustContainParameterToParameterNonControlCharactersWithoutWhitespace',
      {
        value0: administratorUserNameMinimumLength,
        value1: administratorUserNameMaximumLength,
      },
    );
  }
  return null;
}
