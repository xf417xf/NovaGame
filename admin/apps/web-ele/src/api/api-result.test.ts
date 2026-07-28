import { describe, expect, it } from 'vitest';

import {
  NovaGameApiError,
  toNovaGameApiError,
  unwrapApiResultBody,
  unwrapApiVoidResultBody,
} from './api-result';

describe('novagame API result adapter', () => {
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
            code: 'NovaGame.GameConfig.DraftRevisionConflict',
            details: { expectedRevision: ['The draft changed.'] },
            message: 'The draft revision is stale.',
          },
          succeeded: false,
        },
        409,
      ),
    ).toThrowError(
      expect.objectContaining({
        code: 'NovaGame.GameConfig.DraftRevisionConflict',
        details: { expectedRevision: ['The draft changed.'] },
        status: 409,
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
            code: 'NovaGame.AdminAccess.Forbidden',
            details: null,
            message: 'Forbidden.',
          },
          succeeded: false,
        },
        status: 403,
      },
    });

    expect(error).toBeInstanceOf(NovaGameApiError);
    expect(error).toMatchObject({
      code: 'NovaGame.AdminAccess.Forbidden',
      message: 'Forbidden.',
      status: 403,
    });
  });

  it('maps transport failures to a stable local code', () => {
    expect(toNovaGameApiError(new Error('Network Error'))).toMatchObject({
      code: 'NovaGame.NetworkError',
      status: 0,
    });
  });
});
