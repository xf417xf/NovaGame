import type { GameConfigClientErrorCode } from "./contracts.js";

export class GameConfigClientError extends Error {
  readonly code: GameConfigClientErrorCode;

  constructor(code: GameConfigClientErrorCode, message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = "GameConfigClientError";
    this.code = code;
  }
}

export function asGameConfigClientError(
  error: unknown,
  code: GameConfigClientErrorCode,
  message: string,
): GameConfigClientError {
  return error instanceof GameConfigClientError
    ? error
    : new GameConfigClientError(code, message, { cause: error });
}
