import type {
  CharacterAttributeDto as GeneratedCharacterAttributeDto,
  CharacterAttributesDto as GeneratedCharacterAttributesDto,
  EquipmentAssignmentDto as GeneratedEquipmentAssignmentDto,
  EquipmentModifierDto as GeneratedEquipmentModifierDto,
  EquipEquipmentRequest as GeneratedEquipEquipmentRequest,
  DeleteApiPlayerAssetsCharactersByCharacterIdEquipmentBySlotData as GeneratedUnequipOperation,
  DeleteApiPlayerAssetsCharactersByCharacterIdEquipmentBySlotErrors as GeneratedUnequipOperationErrors,
  GetApiPlayerAssetsCharactersByCharacterIdAttributesData as GeneratedGetAttributesOperation,
  GetApiPlayerAssetsCharactersByCharacterIdAttributesErrors as GeneratedGetAttributesOperationErrors,
  PlayerAssetsMutationResultDto as GeneratedPlayerAssetsMutationResultDto,
  PutApiPlayerAssetsCharactersByCharacterIdEquipmentBySlotData as GeneratedEquipOperation,
  PutApiPlayerAssetsCharactersByCharacterIdEquipmentBySlotErrors as GeneratedEquipOperationErrors,
} from "./generated/player-assets/types.gen.js";

const strongRevisionEtagPattern = /^"([1-9][0-9]*)"$/;
const canonicalGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const emptyGuid = "00000000-0000-0000-0000-000000000000";
const minimumInt32 = -2_147_483_648;
const maximumInt32 = 2_147_483_647;
const getAttributesErrorStatuses = [
  400, 401, 404, 500,
] as const satisfies readonly (keyof GeneratedGetAttributesOperationErrors)[];
const equipErrorStatuses = [
  400, 401, 404, 409, 422, 500,
] as const satisfies readonly (keyof GeneratedEquipOperationErrors)[];
const unequipErrorStatuses = [
  400, 401, 404, 409, 422, 500,
] as const satisfies readonly (keyof GeneratedUnequipOperationErrors)[];

type RequiredGeneratedFields<T, K extends keyof T> = Readonly<{
  [P in K]-?: NonNullable<T[P]>;
}>;

export type PlayerAssetsCharacterAttribute = RequiredGeneratedFields<
  GeneratedCharacterAttributeDto,
  "baseValue" | "effectiveValue" | "equipmentValue" | "statCode"
>;

export type PlayerAssetsEquipmentModifier = RequiredGeneratedFields<
  GeneratedEquipmentModifierDto,
  "statCode" | "value"
>;

export type PlayerAssetsEquipmentAssignment = RequiredGeneratedFields<
  GeneratedEquipmentAssignmentDto,
  "configurationVersionId" | "configuredItemId" | "ownedItemId" | "slot"
> & {
  readonly modifiers: readonly PlayerAssetsEquipmentModifier[];
};

export type PlayerAssetsAttributes = RequiredGeneratedFields<
  GeneratedCharacterAttributesDto,
  "characterId" | "revision"
> & {
  readonly attributes: readonly PlayerAssetsCharacterAttribute[];
  readonly equipmentAssignments: readonly PlayerAssetsEquipmentAssignment[];
};

export type PlayerAssetsAttributesResult = PlayerAssetsAttributes & {
  readonly etag: string;
};

export type PlayerAssetsMutationResult = RequiredGeneratedFields<
  GeneratedPlayerAssetsMutationResultDto,
  "revision"
> & {
  readonly etag: string;
};

export type PlayerAssetsFetch = (input: string, init: RequestInit) => Promise<Response>;

export interface PlayerAssetsHttpTransportOptions {
  readonly baseUrl: string;
  readonly fetch: PlayerAssetsFetch;
  readonly getAccessToken: () => Promise<string> | string;
}

export interface PlayerAssetsRequest {
  readonly characterId: string;
  readonly signal?: AbortSignal | undefined;
}

export interface PlayerAssetsMutationRequest extends PlayerAssetsRequest {
  readonly expectedEtag: string;
  readonly idempotencyKey: string;
  readonly slot: string;
}

export interface PlayerAssetsEquipRequest extends PlayerAssetsMutationRequest {
  readonly ownedItemId: string;
}

export type PlayerAssetsTransportErrorKind =
  | "authorization"
  | "conflict"
  | "invalid_contract"
  | "invalid_request"
  | "network"
  | "not_found"
  | "server"
  | "token_unavailable"
  | "unexpected_response"
  | "validation";

export type PlayerAssetsErrorDetails = Readonly<Record<string, readonly string[]>>;

interface PlayerAssetsTransportErrorOptions {
  readonly cause?: unknown;
  readonly code?: string;
  readonly details?: PlayerAssetsErrorDetails;
  readonly status?: number;
}

export class PlayerAssetsTransportError extends Error {
  public readonly code: string | undefined;
  public readonly details: PlayerAssetsErrorDetails | undefined;
  public readonly kind: PlayerAssetsTransportErrorKind;
  public readonly status: number | undefined;

  public constructor(
    kind: PlayerAssetsTransportErrorKind,
    message: string,
    options: PlayerAssetsTransportErrorOptions = {},
  ) {
    super(message, options.cause === undefined ? {} : { cause: options.cause });
    this.name = "PlayerAssetsTransportError";
    this.kind = kind;
    this.status = options.status;
    this.code = options.code;
    this.details = options.details;
  }
}

interface DecodedHttpResponse {
  readonly payload: unknown;
  readonly response: Response;
}

export class SwordMasterPlayerAssetsHttpTransport {
  readonly #baseUrl: URL;
  readonly #fetch: PlayerAssetsFetch;
  readonly #getAccessToken: () => Promise<string> | string;

  public constructor(options: PlayerAssetsHttpTransportOptions) {
    if (typeof options.fetch !== "function") {
      throw invalidRequest("A PlayerAssets fetch implementation is required.");
    }
    if (typeof options.getAccessToken !== "function") {
      throw invalidRequest("A PlayerAssets access-token provider is required.");
    }

    this.#baseUrl = parseBaseUrl(options.baseUrl);
    this.#fetch = options.fetch;
    this.#getAccessToken = options.getAccessToken;
  }

  public async getAttributes(request: PlayerAssetsRequest): Promise<PlayerAssetsAttributesResult> {
    request.signal?.throwIfAborted();
    requireGuid(request.characterId, "character identifier");
    const operation = {
      path: { characterId: request.characterId },
      url: "/api/player-assets/characters/{characterId}/attributes",
    } satisfies GeneratedGetAttributesOperation;

    const { payload, response } = await this.#send(
      "GET",
      getAttributesPath(operation),
      request.signal,
      getAttributesErrorStatuses,
    );
    const etag = readResponseEtag(response);
    const data = decodeAttributes(payload, request.characterId);
    assertMatchingRevision(data.revision, etag, response.status);

    return { ...data, etag };
  }

  public async equip(request: PlayerAssetsEquipRequest): Promise<PlayerAssetsMutationResult> {
    request.signal?.throwIfAborted();
    validateMutationRequest(request);
    requireGuid(request.ownedItemId, "owned-item identifier");

    const body: RequiredGeneratedFields<GeneratedEquipEquipmentRequest, "ownedItemId"> = {
      ownedItemId: request.ownedItemId,
    };
    const operation = {
      body,
      headers: {
        "Idempotency-Key": request.idempotencyKey,
        "If-Match": request.expectedEtag,
      },
      path: { characterId: request.characterId, slot: request.slot },
      url: "/api/player-assets/characters/{characterId}/equipment/{slot}",
    } satisfies GeneratedEquipOperation;
    const { payload, response } = await this.#send(
      "PUT",
      equipmentPath(operation),
      request.signal,
      equipErrorStatuses,
      {
        "Content-Type": "application/json",
        ...operation.headers,
      },
      JSON.stringify(operation.body),
    );

    return decodeMutationResult(payload, response);
  }

  public async unequip(request: PlayerAssetsMutationRequest): Promise<PlayerAssetsMutationResult> {
    request.signal?.throwIfAborted();
    validateMutationRequest(request);
    const operation = {
      headers: {
        "Idempotency-Key": request.idempotencyKey,
        "If-Match": request.expectedEtag,
      },
      path: { characterId: request.characterId, slot: request.slot },
      url: "/api/player-assets/characters/{characterId}/equipment/{slot}",
    } satisfies GeneratedUnequipOperation;

    const { payload, response } = await this.#send(
      "DELETE",
      equipmentPath(operation),
      request.signal,
      unequipErrorStatuses,
      operation.headers,
    );

    return decodeMutationResult(payload, response);
  }

  async #send(
    method: "DELETE" | "GET" | "PUT",
    relativePath: string,
    signal?: AbortSignal,
    allowedErrorStatuses: readonly number[] = [],
    additionalHeaders: Readonly<Record<string, string>> = {},
    body?: string,
  ): Promise<DecodedHttpResponse> {
    signal?.throwIfAborted();

    let accessToken: string;
    try {
      accessToken = await awaitWithAbort(this.#getAccessToken(), signal);
    } catch (error) {
      signal?.throwIfAborted();
      throw new PlayerAssetsTransportError(
        "token_unavailable",
        "The SwordMaster player access token could not be acquired.",
        { cause: error },
      );
    }
    signal?.throwIfAborted();
    requireAccessToken(accessToken);

    const headers: Record<string, string> = {
      Accept: "application/json",
      Authorization: `Bearer ${accessToken}`,
      ...additionalHeaders,
    };
    const init: RequestInit = {
      headers,
      method,
      ...(body === undefined ? {} : { body }),
      ...(signal === undefined ? {} : { signal }),
    };

    let response: Response;
    try {
      response = await awaitWithAbort(
        this.#fetch(new URL(relativePath, this.#baseUrl).toString(), init),
        signal,
      );
    } catch (error) {
      signal?.throwIfAborted();
      throw new PlayerAssetsTransportError(
        "network",
        "The PlayerAssets request could not reach the SwordMaster server.",
        { cause: error },
      );
    }
    signal?.throwIfAborted();

    let source: string;
    try {
      source = await awaitWithAbort(response.text(), signal);
    } catch (error) {
      signal?.throwIfAborted();
      throw new PlayerAssetsTransportError(
        "network",
        "The SwordMaster server response could not be read.",
        { cause: error, status: response.status },
      );
    }
    signal?.throwIfAborted();

    let payload: unknown;
    try {
      payload = JSON.parse(source) as unknown;
    } catch (error) {
      throw responseContractError(
        response.status,
        "The SwordMaster server returned invalid JSON.",
        error,
      );
    }

    if (response.status !== 200) {
      throw decodeApiError(response.status, payload, allowedErrorStatuses);
    }

    return { payload, response };
  }
}

function decodeMutationResult(payload: unknown, response: Response): PlayerAssetsMutationResult {
  const data = decodeSuccessData(payload, response.status);
  const revision = requirePositiveSafeInteger(data.revision, "mutation revision", response.status);
  const etag = readResponseEtag(response);
  assertMatchingRevision(revision, etag, response.status);
  return { etag, revision };
}

function decodeAttributes(payload: unknown, expectedCharacterId: string): PlayerAssetsAttributes {
  const data = decodeSuccessData(payload, 200);
  const characterId = requireGuidValue(data.characterId, "response character identifier", 200);
  if (characterId.toLowerCase() !== expectedCharacterId.toLowerCase()) {
    throw invalidContract(
      "The PlayerAssets response character does not match the requested character.",
      200,
    );
  }

  const revision = requirePositiveSafeInteger(data.revision, "attributes revision", 200);
  const attributes = requireArray(data.attributes, "attributes", 200).map((value, index) => {
    const attribute = requireObject(value, `attributes[${index}]`, 200);
    return {
      baseValue: requireInt32(attribute.baseValue, `attributes[${index}].baseValue`, 200),
      effectiveValue: requireInt32(
        attribute.effectiveValue,
        `attributes[${index}].effectiveValue`,
        200,
      ),
      equipmentValue: requireInt32(
        attribute.equipmentValue,
        `attributes[${index}].equipmentValue`,
        200,
      ),
      statCode: requireNonEmptyString(attribute.statCode, `attributes[${index}].statCode`, 200),
    } satisfies PlayerAssetsCharacterAttribute;
  });
  const equipmentAssignments = requireArray(
    data.equipmentAssignments,
    "equipmentAssignments",
    200,
  ).map((value, index) => decodeEquipmentAssignment(value, index));

  return {
    attributes,
    characterId,
    equipmentAssignments,
    revision,
  };
}

function decodeEquipmentAssignment(value: unknown, index: number): PlayerAssetsEquipmentAssignment {
  const assignment = requireObject(value, `equipmentAssignments[${index}]`, 200);
  const modifiers = requireArray(
    assignment.modifiers,
    `equipmentAssignments[${index}].modifiers`,
    200,
  ).map((modifierValue, modifierIndex) => {
    const modifier = requireObject(
      modifierValue,
      `equipmentAssignments[${index}].modifiers[${modifierIndex}]`,
      200,
    );
    return {
      statCode: requireNonEmptyString(
        modifier.statCode,
        `equipmentAssignments[${index}].modifiers[${modifierIndex}].statCode`,
        200,
      ),
      value: requireInt32(
        modifier.value,
        `equipmentAssignments[${index}].modifiers[${modifierIndex}].value`,
        200,
      ),
    } satisfies PlayerAssetsEquipmentModifier;
  });

  return {
    configurationVersionId: requireGuidValue(
      assignment.configurationVersionId,
      `equipmentAssignments[${index}].configurationVersionId`,
      200,
    ),
    configuredItemId: requireNonEmptyString(
      assignment.configuredItemId,
      `equipmentAssignments[${index}].configuredItemId`,
      200,
    ),
    modifiers,
    ownedItemId: requireGuidValue(
      assignment.ownedItemId,
      `equipmentAssignments[${index}].ownedItemId`,
      200,
    ),
    slot: requireNonEmptyString(assignment.slot, `equipmentAssignments[${index}].slot`, 200),
  };
}

function decodeSuccessData(payload: unknown, status: number): Record<string, unknown> {
  const envelope = requireObject(payload, "success envelope", status);
  if (envelope.succeeded !== true || (envelope.error !== undefined && envelope.error !== null)) {
    throw invalidContract(
      "The SwordMaster server returned an invalid PlayerAssets success envelope.",
      status,
    );
  }
  return requireObject(envelope.data, "success data", status);
}

function decodeApiError(
  status: number,
  payload: unknown,
  allowedErrorStatuses: readonly number[],
): PlayerAssetsTransportError {
  let envelope: Record<string, unknown>;
  let error: Record<string, unknown>;
  try {
    envelope = requireObject(payload, "error envelope", status);
    if (envelope.succeeded !== false) {
      throw new Error("The error envelope did not set succeeded to false.");
    }
    error = requireObject(envelope.error, "error", status);
  } catch (cause) {
    return responseContractError(
      status,
      `The SwordMaster server returned status ${status} without a valid PlayerAssets error envelope.`,
      cause,
    );
  }

  const code = readErrorString(error.code);
  const message = readErrorString(error.message);
  if (code === undefined || message === undefined) {
    return responseContractError(
      status,
      `The SwordMaster server returned status ${status} with an invalid PlayerAssets error.`,
    );
  }

  let details: PlayerAssetsErrorDetails | undefined;
  try {
    details = decodeErrorDetails(error.details, status);
  } catch (cause) {
    return responseContractError(
      status,
      `The SwordMaster server returned status ${status} with invalid error details.`,
      cause,
    );
  }

  return new PlayerAssetsTransportError(
    classifyHttpError(status, allowedErrorStatuses.includes(status)),
    message,
    {
      code,
      ...(details === undefined ? {} : { details }),
      status,
    },
  );
}

function decodeErrorDetails(value: unknown, status: number): PlayerAssetsErrorDetails | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  const source = requireObject(value, "error details", status);
  const details: Record<string, readonly string[]> = {};
  for (const [key, messages] of Object.entries(source)) {
    if (!Array.isArray(messages) || !messages.every((message) => typeof message === "string")) {
      throw new Error(`Error detail '${key}' must be an array of strings.`);
    }
    details[key] = [...messages];
  }
  return details;
}

function classifyHttpError(status: number, allowed: boolean): PlayerAssetsTransportErrorKind {
  if (!allowed) {
    return "unexpected_response";
  }
  if (status === 400 || status === 422) {
    return "validation";
  }
  if (status === 401) {
    return "authorization";
  }
  if (status === 404) {
    return "not_found";
  }
  if (status === 409) {
    return "conflict";
  }
  if (status >= 500 && status <= 599) {
    return "server";
  }
  return "unexpected_response";
}

function responseContractError(
  status: number,
  message: string,
  cause?: unknown,
): PlayerAssetsTransportError {
  return new PlayerAssetsTransportError(
    status === 200 ? "invalid_contract" : "unexpected_response",
    message,
    { ...(cause === undefined ? {} : { cause }), status },
  );
}

function readResponseEtag(response: Response): string {
  const etag = response.headers.get("etag");
  if (etag === null) {
    throw invalidContract(
      "The PlayerAssets response is missing its strong revision ETag.",
      response.status,
    );
  }
  parseRevisionEtag(etag, "response ETag", response.status);
  return etag;
}

function assertMatchingRevision(revision: number, etag: string, status: number): void {
  const etagRevision = parseRevisionEtag(etag, "response ETag", status);
  if (etagRevision !== revision) {
    throw invalidContract(
      "The PlayerAssets response ETag does not match its body revision.",
      status,
    );
  }
}

function parseRevisionEtag(value: string, description: string, status?: number): number {
  const revisionText = strongRevisionEtagPattern.exec(value)?.[1];
  const revision = revisionText === undefined ? Number.NaN : Number(revisionText);
  if (!Number.isSafeInteger(revision) || revision <= 0) {
    if (status === undefined) {
      throw invalidRequest(`The ${description} must be a quoted positive safe-integer revision.`);
    }
    throw invalidContract(
      `The ${description} must be a quoted positive safe-integer revision.`,
      status,
    );
  }
  return revision;
}

function validateMutationRequest(request: PlayerAssetsMutationRequest): void {
  requireGuid(request.characterId, "character identifier");
  requirePathSegment(request.slot, "equipment slot");
  parseRevisionEtag(request.expectedEtag, "expected ETag");
  requireCanonicalIdempotencyKey(request.idempotencyKey);
}

function getAttributesPath(operation: GeneratedGetAttributesOperation): string {
  return operation.url
    .replace("{characterId}", encodeURIComponent(operation.path.characterId))
    .replace(/^\//, "");
}

function equipmentPath(operation: GeneratedEquipOperation | GeneratedUnequipOperation): string {
  return operation.url
    .replace("{characterId}", encodeURIComponent(operation.path.characterId))
    .replace("{slot}", encodeURIComponent(operation.path.slot))
    .replace(/^\//, "");
}

async function awaitWithAbort<T>(operation: Promise<T> | T, signal?: AbortSignal): Promise<T> {
  signal?.throwIfAborted();
  if (signal === undefined) {
    return await operation;
  }

  return await new Promise<T>((resolve, reject) => {
    const onAbort = () => {
      signal.removeEventListener("abort", onAbort);
      reject(signal.reason);
    };
    signal.addEventListener("abort", onAbort, { once: true });
    if (signal.aborted) {
      onAbort();
      return;
    }

    void Promise.resolve(operation).then(
      (value) => {
        signal.removeEventListener("abort", onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener("abort", onAbort);
        reject(error);
      },
    );
  });
}

function requireAccessToken(value: string): void {
  if (typeof value !== "string" || value.length === 0 || /\s/.test(value)) {
    throw new PlayerAssetsTransportError(
      "authorization",
      "The SwordMaster player access token is missing or invalid.",
    );
  }
}

function requireCanonicalIdempotencyKey(value: string): void {
  if (!canonicalGuidPattern.test(value) || value === emptyGuid) {
    throw invalidRequest(
      "The idempotency key must be a non-empty lowercase canonical D-format GUID.",
    );
  }
}

function requireGuid(value: string, description: string): void {
  if (typeof value !== "string" || !guidPattern.test(value) || value.toLowerCase() === emptyGuid) {
    throw invalidRequest(`The ${description} must be a non-empty D-format GUID.`);
  }
}

function requirePathSegment(value: string, description: string): void {
  const normalized = typeof value === "string" ? value.trim() : "";
  if (
    normalized.length === 0 ||
    normalized === "." ||
    normalized === ".." ||
    normalized.includes("/") ||
    normalized.includes("\\")
  ) {
    throw invalidRequest(
      `The ${description} must be non-empty and cannot be a URL dot segment or contain a path separator.`,
    );
  }
}

function requireGuidValue(value: unknown, description: string, status: number): string {
  if (typeof value !== "string" || !guidPattern.test(value) || value.toLowerCase() === emptyGuid) {
    throw invalidContract(`The ${description} must be a non-empty D-format GUID.`, status);
  }
  return value;
}

function requireObject(
  value: unknown,
  description: string,
  status: number,
): Record<string, unknown> {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw invalidContract(`The PlayerAssets ${description} must be an object.`, status);
  }
  return value as Record<string, unknown>;
}

function requireArray(value: unknown, description: string, status: number): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw invalidContract(`The PlayerAssets ${description} must be an array.`, status);
  }
  return value;
}

function requireNonEmptyString(value: unknown, description: string, status: number): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw invalidContract(`The PlayerAssets ${description} must be a non-empty string.`, status);
  }
  return value;
}

function requireSafeInteger(value: unknown, description: string, status: number): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw invalidContract(`The PlayerAssets ${description} must be a safe integer.`, status);
  }
  return value;
}

function requireInt32(value: unknown, description: string, status: number): number {
  const integer = requireSafeInteger(value, description, status);
  if (integer < minimumInt32 || integer > maximumInt32) {
    throw invalidContract(`The PlayerAssets ${description} must be a 32-bit integer.`, status);
  }
  return integer;
}

function requirePositiveSafeInteger(value: unknown, description: string, status: number): number {
  const integer = requireSafeInteger(value, description, status);
  if (integer <= 0) {
    throw invalidContract(`The PlayerAssets ${description} must be positive.`, status);
  }
  return integer;
}

function readErrorString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim().length > 0 ? value : undefined;
}

function parseBaseUrl(value: string): URL {
  let url: URL;
  try {
    url = new URL(value);
  } catch (cause) {
    throw invalidRequest("The PlayerAssets base URL is invalid.", cause);
  }
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw invalidRequest("The PlayerAssets base URL must use HTTP or HTTPS.");
  }
  if (
    url.username.length > 0 ||
    url.password.length > 0 ||
    url.search.length > 0 ||
    url.hash.length > 0
  ) {
    throw invalidRequest(
      "The PlayerAssets base URL must not contain credentials, a query, or a fragment.",
    );
  }
  if (!url.pathname.endsWith("/")) {
    url.pathname += "/";
  }
  return url;
}

function invalidRequest(message: string, cause?: unknown): PlayerAssetsTransportError {
  return cause === undefined
    ? new PlayerAssetsTransportError("invalid_request", message)
    : new PlayerAssetsTransportError("invalid_request", message, { cause });
}

function invalidContract(message: string, status: number): PlayerAssetsTransportError {
  return new PlayerAssetsTransportError("invalid_contract", message, { status });
}
