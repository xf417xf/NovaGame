import { describe, expect, it } from 'vitest';

import {
  NovaGameApiError,
  toNovaGameApiError,
  unwrapApiResultBody,
  unwrapApiVoidResultBody,
} from './api-result';

describe('NovaGame API result adapter', () => {
  it('returns typed data from a successful envelope', () => {
    expect(
      unwrapApiResultBody(
        { data: { accessToken: 'token' }, error: null, succeeded: true },
        200,
      ),
    ).toEqual({ accessToken: 'token' });
  });

  it('preserves status, code, and details from a failed envelope', () => {
    expect(() =>
      unwrapApiResultBody(
        {
          error: {
            code: 'NovaGame.AdminAccess.Forbidden',
            details: { permission: ['Permission is required.'] },
            message: 'Forbidden.',
          },
          succeeded: false,
        },
        403,
      ),
    ).toThrowError(
      expect.objectContaining({
        code: 'NovaGame.AdminAccess.Forbidden',
        details: { permission: ['Permission is required.'] },
        status: 403,
      }),
    );
  });

  it('accepts the void success envelope', () => {
    expect(
      unwrapApiVoidResultBody({ error: null, succeeded: true }, 200),
    ).toBeUndefined();
  });

  it('normalizes an Axios-style API error', () => {
    const error = toNovaGameApiError({
      response: {
        data: {
          error: {
            code: 'NovaGame.AdminAccess.InvalidCredentials',
            details: null,
            message: 'Invalid credentials.',
          },
          succeeded: false,
        },
        status: 401,
      },
    });

    expect(error).toBeInstanceOf(NovaGameApiError);
    expect(error).toMatchObject({
      code: 'NovaGame.AdminAccess.InvalidCredentials',
      message: 'Invalid credentials.',
      status: 401,
    });
  });

  it('maps transport failures to a stable local code', () => {
    expect(toNovaGameApiError(new Error('Network Error'))).toMatchObject({
      code: 'NovaGame.NetworkError',
      status: 0,
    });
  });
});
