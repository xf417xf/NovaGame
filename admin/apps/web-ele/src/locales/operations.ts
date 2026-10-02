import { computed, shallowRef } from 'vue';

import { $t, $te, i18n } from '@vben/locales';

import { toNovaGameApiError } from '#/api/api-result';

import english from './langs/en-US/operations.json';

export type OperationsMessageKey = keyof typeof english;

i18n.global.mergeLocaleMessage('en-US', { operations: english });

/** Use the existing locale system, with English fallback for isolated consumers. */
export function tOperations(
  key: OperationsMessageKey,
  values: Record<string, null | number | string | undefined> = {},
): string {
  const parameters = Object.fromEntries(
    Object.entries(values).map(([name, value]) => [name, value ?? '']),
  );
  return $t(`operations.${key}`, parameters, {
    locale: $te(`operations.${key}`) ? i18n.global.locale.value : 'en-US',
  });
}

/** Defer feedback translation so switching language preserves the current form. */
export function useOperationsMessage() {
  const source = shallowRef<(() => string) | string>('');
  return computed<string, (() => string) | string>({
    get: () =>
      typeof source.value === 'function' ? source.value() : source.value,
    set: (value) => {
      source.value = value;
    },
  });
}

/** Translate known framework codes only; keep unknown diagnostics intact. */
export function operationsErrorMessage(error: unknown): string {
  const apiError = toNovaGameApiError(error);
  return operationsCodeMessage(apiError.code, apiError.message);
}

export function operationsCodeMessage(code: string, fallback: string): string {
  const key = `framework.errors.${code.replaceAll('.', '_')}`;
  return $te(key) ? $t(key) : fallback;
}

export function operationsSyntaxMessage(code: string): string {
  const key = `framework.jsonSyntax.${code}`;
  return $te(key) ? $t(key) : code;
}

export function operationsPermissionText(
  code: null | string | undefined,
  field: 'description' | 'title',
  fallback: string,
): string {
  const key = `framework.permissions.${code?.replaceAll('.', '_')}.${field}`;
  return code && $te(key) ? $t(key) : fallback;
}
