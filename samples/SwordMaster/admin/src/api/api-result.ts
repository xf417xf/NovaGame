import type {
  ApiErrorPayload,
  ApiResult,
  ApiVoidResult,
} from './nova-game-contracts';

export class NovaGameApiError extends Error {
  constructor(
    message: string,
    public readonly code: string,
    public readonly status: number,
    public readonly details: null | Record<string, string[]> = null,
  ) {
    super(message);
    this.name = 'NovaGameApiError';
  }
}

function isObject(value: unknown): value is Record<string, any> {
  return typeof value === 'object' && value !== null;
}

function readApiError(value: unknown): ApiErrorPayload | null {
  if (!isObject(value) || !isObject(value.error)) {
    return null;
  }

  const { code, details, message } = value.error;
  if (typeof code !== 'string' || typeof message !== 'string') {
    return null;
  }

  return {
    code,
    details: isObject(details) ? (details as Record<string, string[]>) : null,
    message,
  };
}

export function toNovaGameApiError(error: unknown): NovaGameApiError {
  if (error instanceof NovaGameApiError) {
    return error;
  }

  const source =
    isObject(error) && isObject(error.response) ? error.response : error;
  const status =
    isObject(source) && typeof source.status === 'number' ? source.status : 0;
  const body = isObject(source) && 'data' in source ? source.data : source;
  const apiError = readApiError(body);

  if (apiError) {
    return new NovaGameApiError(
      apiError.message,
      apiError.code,
      status,
      apiError.details,
    );
  }

  const message =
    isObject(error) && typeof error.message === 'string'
      ? error.message
      : 'The NovaGame API request failed.';
  return new NovaGameApiError(
    message,
    status === 0 ? 'NovaGame.NetworkError' : 'NovaGame.HttpError',
    status,
  );
}

export function unwrapApiResultBody<T>(body: ApiResult<T>, status: number): T {
  if (body.succeeded) {
    return body.data;
  }

  throw new NovaGameApiError(
    body.error.message,
    body.error.code,
    status,
    body.error.details,
  );
}

export function unwrapApiVoidResultBody(
  body: ApiVoidResult,
  status: number,
): void {
  if (body.succeeded) {
    return;
  }

  throw new NovaGameApiError(
    body.error.message,
    body.error.code,
    status,
    body.error.details,
  );
}
