import { createScanner, parseTree, type Node as JsonNode, type ParseError } from "jsonc-parser";
import { isLosslessNumber, parse as parseLosslessJson } from "lossless-json";

import {
  GAME_CONFIG_CANONICALIZATION,
  GAME_CONFIG_FORMAT_VERSION,
  GAME_CONFIG_HASH_ALGORITHM,
  GAME_CONFIG_SCHEMA_VERSION,
  type GameConfigContent,
  type GameConfigRecord,
  type GameConfigSnapshot,
  type GameConfigSnapshotIntegrity,
  type LosslessJsonValue,
  type Sha256Digest,
  type ValidateGameConfigSnapshotOptions,
} from "./contracts.js";
import { GameConfigClientError, asGameConfigClientError } from "./errors.js";
import { NobleSha256, assertSha256Hex } from "./hash.js";

const STRONG_SHA256_ETAG = /^"([0-9a-f]{64})"$/;
const SNAPSHOT_MAXIMUM_BYTE_LENGTH = 25_166_848;
const SNAPSHOT_MAXIMUM_DEPTH = 65;
const SAFE_SLUG = /^[a-z][a-z0-9-]{0,63}$/;
const RECORD_ID = /^[a-z][a-z0-9-]{0,127}$/;
const GUID_D = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const UTC_TIMESTAMP = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d{7})Z$/;
const TOP_LEVEL_FIELDS = [
  "formatVersion",
  "schemaVersion",
  "configurationSet",
  "configurationVersion",
  "createdAtUtc",
  "integrity",
  "content",
] as const;
const INTEGRITY_FIELDS = ["algorithm", "canonicalization", "contentHash"] as const;
const EMPTY_COLLECTION: readonly GameConfigRecord[] = Object.freeze([]);
// jsonc-parser declares SyntaxKind as an ambient const enum, which is not
// importable with verbatimModuleSyntax. Version 3.3.1 pins this token map.
const JSONC_LINE_COMMENT_TRIVIA = 12;
const JSONC_BLOCK_COMMENT_TRIVIA = 13;
const JSONC_LINE_BREAK_TRIVIA = 14;
const JSONC_TRIVIA = 15;
const JSONC_EOF = 17;
const JSONC_OPEN_BRACE = 1;
const JSONC_CLOSE_BRACE = 2;
const JSONC_OPEN_BRACKET = 3;
const JSONC_CLOSE_BRACKET = 4;
const DOTNET_JSON_ESCAPED_ASCII = new Set([0x22, 0x26, 0x27, 0x2b, 0x3c, 0x3e, 0x60]);

class ValidatedGameConfigSnapshot implements GameConfigSnapshot {
  readonly configurationSet: string;
  readonly configurationVersion: string;
  readonly content: GameConfigContent;
  readonly createdAtUtc: string;
  readonly etag: string;
  readonly formatVersion = GAME_CONFIG_FORMAT_VERSION;
  readonly integrity: GameConfigSnapshotIntegrity;
  readonly schemaVersion = GAME_CONFIG_SCHEMA_VERSION;
  readonly snapshotHash: string;

  readonly #recordsByCollection: ReadonlyMap<string, ReadonlyMap<string, GameConfigRecord>>;

  constructor(
    metadata: {
      configurationSet: string;
      configurationVersion: string;
      content: GameConfigContent;
      createdAtUtc: string;
      etag: string;
      integrity: GameConfigSnapshotIntegrity;
      snapshotHash: string;
    },
    recordsByCollection: ReadonlyMap<string, ReadonlyMap<string, GameConfigRecord>>,
  ) {
    this.configurationSet = metadata.configurationSet;
    this.configurationVersion = metadata.configurationVersion;
    this.content = metadata.content;
    this.createdAtUtc = metadata.createdAtUtc;
    this.etag = metadata.etag;
    this.integrity = metadata.integrity;
    this.snapshotHash = metadata.snapshotHash;
    this.#recordsByCollection = recordsByCollection;
    Object.freeze(this);
  }

  getCollection(name: string): readonly GameConfigRecord[] {
    return this.content.collections[name] ?? EMPTY_COLLECTION;
  }

  getRecord(collection: string, id: string): GameConfigRecord | undefined {
    return this.#recordsByCollection.get(collection)?.get(id);
  }
}

export async function validateGameConfigSnapshot(
  options: ValidateGameConfigSnapshotOptions,
): Promise<GameConfigSnapshot> {
  const sha256 = options.sha256 ?? new NobleSha256();
  const snapshotHash = readStrongEtag(options.etag);
  if (options.body.byteLength > SNAPSHOT_MAXIMUM_BYTE_LENGTH) {
    throw invalidContract(`The snapshot cannot exceed ${SNAPSHOT_MAXIMUM_BYTE_LENGTH} bytes.`);
  }
  const actualSnapshotHash = await digestHex(sha256, options.body);
  if (actualSnapshotHash !== snapshotHash) {
    throw new GameConfigClientError(
      "snapshot_hash_mismatch",
      "The complete snapshot hash does not match its strong ETag.",
    );
  }

  const source = decodeStrictUtf8(options.body);
  assertMaximumSourceContainerDepth(source, SNAPSHOT_MAXIMUM_DEPTH);
  const { contentNode, rootNode } = parseSnapshotTree(source);
  assertMaximumContainerDepth(rootNode, SNAPSHOT_MAXIMUM_DEPTH);
  assertNoDuplicateProperties(rootNode);
  const topLevelNodes = readExactObjectFields(rootNode, TOP_LEVEL_FIELDS);
  const integrityNode = topLevelNodes.get("integrity");
  if (!integrityNode) {
    throw invalidContract("Snapshot integrity metadata is required.");
  }
  readExactObjectFields(integrityNode, INTEGRITY_FIELDS);

  let parsed: unknown;
  try {
    parsed = parseLosslessJson(source);
  } catch (error) {
    throw new GameConfigClientError("invalid_json", "The snapshot is not valid lossless JSON.", {
      cause: error,
    });
  }

  const root = requireObject(parsed, "The snapshot root must be an object.");
  assertExactKeys(root, TOP_LEVEL_FIELDS, "snapshot");
  const formatVersion = requireString(root, "formatVersion");
  if (formatVersion !== GAME_CONFIG_FORMAT_VERSION) {
    throw new GameConfigClientError(
      "unsupported_format",
      `Snapshot format '${formatVersion}' is unsupported.`,
    );
  }

  const schemaVersion = requireString(root, "schemaVersion");
  if (schemaVersion !== GAME_CONFIG_SCHEMA_VERSION) {
    throw new GameConfigClientError(
      "unsupported_schema",
      `Game-configuration schema '${schemaVersion}' is unsupported.`,
    );
  }

  const configurationSet = requireString(root, "configurationSet");
  if (!SAFE_SLUG.test(configurationSet)) {
    throw invalidContract("The snapshot configuration set is invalid.");
  }
  if (configurationSet !== options.expectedConfigurationSet) {
    throw new GameConfigClientError(
      "configuration_set_mismatch",
      `Expected configuration set '${options.expectedConfigurationSet}', received '${configurationSet}'.`,
    );
  }

  const configurationVersion = requireString(root, "configurationVersion");
  if (
    !GUID_D.test(configurationVersion) ||
    configurationVersion === "00000000-0000-0000-0000-000000000000"
  ) {
    throw invalidContract("The snapshot configuration version is invalid.");
  }
  if (
    options.expectedConfigurationVersion !== undefined &&
    configurationVersion !== options.expectedConfigurationVersion
  ) {
    throw new GameConfigClientError(
      "configuration_version_mismatch",
      `Expected configuration version '${options.expectedConfigurationVersion}', received '${configurationVersion}'.`,
    );
  }

  const createdAtUtc = requireString(root, "createdAtUtc");
  assertUtcTimestamp(createdAtUtc);

  const integrityObject = requireObject(
    root.integrity,
    "Snapshot integrity metadata must be an object.",
  );
  assertExactKeys(integrityObject, INTEGRITY_FIELDS, "snapshot integrity");
  const algorithm = requireString(integrityObject, "algorithm");
  const canonicalization = requireString(integrityObject, "canonicalization");
  if (
    algorithm !== GAME_CONFIG_HASH_ALGORITHM ||
    canonicalization !== GAME_CONFIG_CANONICALIZATION
  ) {
    throw new GameConfigClientError(
      "unsupported_integrity",
      "The snapshot integrity algorithm or canonicalization profile is unsupported.",
    );
  }

  const expectedContentHash = requireString(integrityObject, "contentHash");
  if (!/^[0-9a-f]{64}$/.test(expectedContentHash)) {
    throw invalidContract("Snapshot contentHash must be lowercase hexadecimal SHA-256.");
  }
  const rawContent = source.slice(contentNode.offset, contentNode.offset + contentNode.length);
  assertCanonicalContent(contentNode, rawContent);
  const actualContentHash = await digestHex(sha256, new TextEncoder().encode(rawContent));
  if (actualContentHash !== expectedContentHash) {
    throw new GameConfigClientError(
      "content_hash_mismatch",
      "The canonical content hash does not match the embedded integrity metadata.",
    );
  }

  const { content, recordsByCollection } = buildContent(root.content);
  return new ValidatedGameConfigSnapshot(
    {
      configurationSet,
      configurationVersion,
      content,
      createdAtUtc,
      etag: options.etag,
      integrity: Object.freeze({
        algorithm: GAME_CONFIG_HASH_ALGORITHM,
        canonicalization: GAME_CONFIG_CANONICALIZATION,
        contentHash: expectedContentHash,
      }),
      snapshotHash,
    },
    recordsByCollection,
  );
}

function assertCanonicalContent(contentNode: JsonNode, rawContent: string): void {
  const scanner = createScanner(rawContent, false);
  for (let token = scanner.scan(); token !== JSONC_EOF; token = scanner.scan()) {
    if (
      token === JSONC_TRIVIA ||
      token === JSONC_LINE_BREAK_TRIVIA ||
      token === JSONC_LINE_COMMENT_TRIVIA ||
      token === JSONC_BLOCK_COMMENT_TRIVIA
    ) {
      throw new GameConfigClientError(
        "canonicalization_mismatch",
        "Canonical snapshot content must not contain JSON trivia.",
      );
    }
  }
  assertCanonicalNode(contentNode, [], rawContent, contentNode.offset);
}

function assertCanonicalNode(
  node: JsonNode,
  path: readonly string[],
  source: string,
  sourceOffset: number,
): void {
  if (node.type === "object") {
    const properties = node.children ?? [];
    const names = properties.map((property) => {
      const nameNode = property.children?.[0];
      const name = nameNode?.value;
      if (!nameNode || nameNode.type !== "string" || typeof name !== "string") {
        throw invalidContract("Snapshot object properties are malformed.");
      }
      assertCanonicalStringNode(nameNode, source, sourceOffset);
      return name;
    });
    const sortedNames = [...names].sort(compareOrdinal);
    if (names.some((name, index) => name !== sortedNames[index])) {
      throw new GameConfigClientError(
        "canonicalization_mismatch",
        "Canonical snapshot object properties must use ordinal name order.",
      );
    }
    for (const property of properties) {
      const name = property.children?.[0]?.value;
      const value = property.children?.[1];
      if (typeof name !== "string" || !value) {
        throw invalidContract("Snapshot object properties are malformed.");
      }
      assertCanonicalNode(value, [...path, name], source, sourceOffset);
    }
    return;
  }

  if (node.type === "string") {
    assertCanonicalStringNode(node, source, sourceOffset);
    return;
  }

  if (node.type !== "array") {
    return;
  }
  const children = node.children ?? [];
  if (path.length === 2 && path[0] === "collections") {
    const ids = children.map((record) => {
      const id = readObjectFields(record).get("id")?.value;
      if (typeof id !== "string") {
        throw invalidContract("Canonical collection records require string IDs.");
      }
      return id;
    });
    const sortedIds = [...ids].sort(compareOrdinal);
    if (ids.some((id, index) => id !== sortedIds[index])) {
      throw new GameConfigClientError(
        "canonicalization_mismatch",
        "Canonical collection records must use ordinal ID order.",
      );
    }
  }
  for (const child of children) {
    assertCanonicalNode(child, path, source, sourceOffset);
  }
}

function assertCanonicalStringNode(node: JsonNode, source: string, sourceOffset: number): void {
  if (typeof node.value !== "string") {
    throw invalidContract("Snapshot string value is malformed.");
  }
  const actual = source.slice(node.offset - sourceOffset, node.offset - sourceOffset + node.length);
  if (actual !== encodeDotNetJsonString(node.value)) {
    throw new GameConfigClientError(
      "canonicalization_mismatch",
      "Canonical snapshot strings must use the producer JSON encoding.",
    );
  }
}

function encodeDotNetJsonString(value: string): string {
  let encoded = '"';
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    switch (code) {
      case 0x08:
        encoded += "\\b";
        break;
      case 0x09:
        encoded += "\\t";
        break;
      case 0x0a:
        encoded += "\\n";
        break;
      case 0x0c:
        encoded += "\\f";
        break;
      case 0x0d:
        encoded += "\\r";
        break;
      case 0x5c:
        encoded += "\\\\";
        break;
      default:
        if (code >= 0xd800 && code <= 0xdbff) {
          const next = value.charCodeAt(index + 1);
          if (next >= 0xdc00 && next <= 0xdfff) {
            encoded += unicodeEscape(code) + unicodeEscape(next);
            index++;
          } else {
            encoded += "\\uFFFD";
          }
        } else if (code >= 0xdc00 && code <= 0xdfff) {
          encoded += "\\uFFFD";
        } else if (code >= 0x20 && code <= 0x7e && !DOTNET_JSON_ESCAPED_ASCII.has(code)) {
          encoded += String.fromCharCode(code);
        } else {
          encoded += unicodeEscape(code);
        }
    }
  }
  return `${encoded}"`;
}

function unicodeEscape(code: number): string {
  return `\\u${code.toString(16).toUpperCase().padStart(4, "0")}`;
}

function readStrongEtag(etag: string): string {
  const match = STRONG_SHA256_ETAG.exec(etag);
  if (!match?.[1]) {
    throw new GameConfigClientError(
      "invalid_etag",
      "A strong quoted lowercase SHA-256 ETag is required.",
    );
  }
  return match[1];
}

async function digestHex(sha256: Sha256Digest, bytes: Uint8Array): Promise<string> {
  let digest: string;
  try {
    digest = await sha256.digestHex(bytes);
  } catch (error) {
    throw asGameConfigClientError(error, "hash_failed", "SHA-256 calculation failed.");
  }
  assertSha256Hex(digest, "SHA-256 digest");
  return digest;
}

function decodeStrictUtf8(body: Uint8Array): string {
  if (body[0] === 0xef && body[1] === 0xbb && body[2] === 0xbf) {
    throw new GameConfigClientError(
      "invalid_utf8",
      "Snapshot UTF-8 must not contain a byte-order mark.",
    );
  }

  let source: string;
  try {
    source = new TextDecoder("utf-8", { fatal: true }).decode(body);
  } catch (error) {
    throw new GameConfigClientError("invalid_utf8", "Snapshot bytes are not valid UTF-8.", {
      cause: error,
    });
  }

  const roundTrip = new TextEncoder().encode(source);
  if (!equalBytes(roundTrip, body)) {
    throw new GameConfigClientError(
      "invalid_utf8",
      "Snapshot UTF-8 does not round-trip to its original bytes.",
    );
  }
  return source;
}

function parseSnapshotTree(source: string): {
  contentNode: JsonNode;
  rootNode: JsonNode;
} {
  const errors: ParseError[] = [];
  const rootNode = parseTree(source, errors, {
    allowTrailingComma: false,
    disallowComments: true,
  });
  if (
    errors.length !== 0 ||
    !rootNode ||
    rootNode.type !== "object" ||
    rootNode.offset !== 0 ||
    rootNode.length !== source.length
  ) {
    throw new GameConfigClientError(
      "invalid_json",
      "The snapshot must be one strict JSON object without trailing data.",
    );
  }

  const fields = readObjectFields(rootNode);
  const contentNode = fields.get("content");
  if (!contentNode) {
    throw invalidContract("Snapshot content is required.");
  }
  return { contentNode, rootNode };
}

function assertMaximumSourceContainerDepth(source: string, maximumDepth: number): void {
  const scanner = createScanner(source, false);
  let depth = 0;
  for (let token = scanner.scan(); token !== JSONC_EOF; token = scanner.scan()) {
    if (token === JSONC_OPEN_BRACE || token === JSONC_OPEN_BRACKET) {
      depth++;
      if (depth > maximumDepth) {
        throw invalidContract(`The snapshot cannot exceed ${maximumDepth} JSON container levels.`);
      }
    } else if ((token === JSONC_CLOSE_BRACE || token === JSONC_CLOSE_BRACKET) && depth > 0) {
      depth--;
    }
  }
}

function assertMaximumContainerDepth(root: JsonNode, maximumDepth: number): void {
  const pending: { readonly depth: number; readonly node: JsonNode }[] = [{ depth: 1, node: root }];
  while (pending.length !== 0) {
    const current = pending.pop()!;
    if (current.depth > maximumDepth) {
      throw invalidContract(`The snapshot cannot exceed ${maximumDepth} JSON container levels.`);
    }

    const children =
      current.node.type === "object"
        ? (current.node.children ?? []).map((property) => property.children?.[1])
        : current.node.type === "array"
          ? (current.node.children ?? [])
          : [];
    for (const child of children) {
      if (!child) {
        throw invalidContract("Snapshot JSON containers are malformed.");
      }
      if (child.type === "object" || child.type === "array") {
        pending.push({ depth: current.depth + 1, node: child });
      }
    }
  }
}

function assertNoDuplicateProperties(node: JsonNode): void {
  if (node.type === "object") {
    const names = new Set<string>();
    for (const property of node.children ?? []) {
      const key = property.children?.[0]?.value;
      const value = property.children?.[1];
      if (typeof key !== "string" || !value) {
        throw invalidContract("Snapshot object properties are malformed.");
      }
      if (!names.add(key)) {
        throw invalidContract(`Snapshot property '${key}' is duplicated.`);
      }
      assertNoDuplicateProperties(value);
    }
  } else if (node.type === "array") {
    for (const child of node.children ?? []) {
      assertNoDuplicateProperties(child);
    }
  }
}

function readExactObjectFields(node: JsonNode, expected: readonly string[]): Map<string, JsonNode> {
  const fields = readObjectFields(node);
  const actual = [...fields.keys()];
  if (actual.length !== expected.length || expected.some((name) => !fields.has(name))) {
    throw invalidContract("Snapshot object fields do not match the contract.");
  }
  return fields;
}

function readObjectFields(node: JsonNode): Map<string, JsonNode> {
  if (node.type !== "object") {
    throw invalidContract("Expected a snapshot JSON object.");
  }
  const fields = new Map<string, JsonNode>();
  for (const property of node.children ?? []) {
    const key = property.children?.[0]?.value;
    const value = property.children?.[1];
    if (typeof key !== "string" || !value) {
      throw invalidContract("Snapshot object properties are malformed.");
    }
    fields.set(key, value);
  }
  return fields;
}

function buildContent(value: unknown): {
  content: GameConfigContent;
  recordsByCollection: ReadonlyMap<string, ReadonlyMap<string, GameConfigRecord>>;
} {
  const contentObject = requireObject(value, "Snapshot content must be an object.");
  assertExactKeys(contentObject, ["collections"], "snapshot content");
  const collectionsObject = requireObject(
    contentObject.collections,
    "Snapshot collections must be an object.",
  );
  const collections: Record<string, readonly GameConfigRecord[]> = Object.create(null) as Record<
    string,
    readonly GameConfigRecord[]
  >;
  const indexes = new Map<string, ReadonlyMap<string, GameConfigRecord>>();
  const references: GameConfigReference[] = [];

  for (const collectionName of Object.keys(collectionsObject).sort(compareOrdinal)) {
    if (!SAFE_SLUG.test(collectionName)) {
      throw invalidContract(`Collection name '${collectionName}' is invalid.`);
    }
    const sourceRecords = collectionsObject[collectionName];
    if (!Array.isArray(sourceRecords)) {
      throw invalidContract(`Collection '${collectionName}' must be a JSON array.`);
    }

    const records: GameConfigRecord[] = [];
    const byId = new Map<string, GameConfigRecord>();
    for (const sourceRecord of sourceRecords) {
      const recordObject = requireObject(
        sourceRecord,
        `Collection '${collectionName}' records must be objects.`,
      );
      const id = recordObject.id;
      if (typeof id !== "string" || !RECORD_ID.test(id)) {
        throw invalidContract(
          `Collection '${collectionName}' contains an invalid record identifier.`,
        );
      }
      if (byId.has(id)) {
        throw new GameConfigClientError(
          "duplicate_record_id",
          `Record '${collectionName}/${id}' is duplicated.`,
        );
      }

      collectReferences(recordObject, references);
      const frozen = freezeJsonObject(recordObject) as GameConfigRecord;
      records.push(frozen);
      byId.set(id, frozen);
    }
    records.sort((left, right) => compareOrdinal(left.id, right.id));
    collections[collectionName] = Object.freeze(records);
    indexes.set(collectionName, byId);
  }

  for (const reference of references) {
    if (!indexes.get(reference.collection)?.has(reference.id)) {
      throw invalidContract(
        `Reference '${reference.collection}/${reference.id}' does not resolve.`,
      );
    }
  }

  return {
    content: Object.freeze({
      collections: Object.freeze(collections),
    }),
    recordsByCollection: indexes,
  };
}

interface GameConfigReference {
  readonly collection: string;
  readonly id: string;
}

function collectReferences(value: unknown, references: GameConfigReference[]): void {
  if (Array.isArray(value)) {
    for (const item of value) {
      collectReferences(item, references);
    }
    return;
  }
  if (!isObject(value)) {
    return;
  }

  if (Object.hasOwn(value, "$ref")) {
    const fields = Object.keys(value);
    const reference = value.$ref;
    if (fields.length !== 1 || typeof reference !== "string") {
      throw invalidContract("A reference must contain exactly one string '$ref' property.");
    }
    const separator = reference.indexOf("/");
    const collection = reference.slice(0, separator);
    const id = reference.slice(separator + 1);
    if (
      separator <= 0 ||
      separator !== reference.lastIndexOf("/") ||
      !SAFE_SLUG.test(collection) ||
      !RECORD_ID.test(id)
    ) {
      throw invalidContract("A reference must use the exact 'collection/id' form.");
    }
    references.push({ collection, id });
    return;
  }

  for (const child of Object.values(value)) {
    collectReferences(child, references);
  }
}

function freezeJsonObject(
  value: Readonly<Record<string, unknown>>,
): Readonly<Record<string, LosslessJsonValue>> {
  const entries = Object.keys(value)
    .sort(compareOrdinal)
    .map((key) => [key, freezeJsonValue(value[key])] as const);
  return Object.freeze(Object.fromEntries(entries));
}

function freezeJsonValue(value: unknown): LosslessJsonValue {
  if (value === null || typeof value === "boolean" || typeof value === "string") {
    return value;
  }
  if (isLosslessNumber(value)) {
    return Object.freeze(value);
  }
  if (Array.isArray(value)) {
    return Object.freeze(value.map((item) => freezeJsonValue(item)));
  }
  if (isObject(value)) {
    return freezeJsonObject(value);
  }
  throw invalidContract("Snapshot content contains an unsupported JSON value.");
}

function requireObject(value: unknown, message: string): Readonly<Record<string, unknown>> {
  if (!isObject(value)) {
    throw invalidContract(message);
  }
  return value;
}

function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return (
    typeof value === "object" && value !== null && !Array.isArray(value) && !isLosslessNumber(value)
  );
}

function requireString(object: Readonly<Record<string, unknown>>, property: string): string {
  const value = object[property];
  if (typeof value !== "string") {
    throw invalidContract(`Snapshot property '${property}' must be a string.`);
  }
  return value;
}

function assertExactKeys(
  object: Readonly<Record<string, unknown>>,
  expected: readonly string[],
  label: string,
): void {
  const actual = Object.keys(object);
  if (
    actual.length !== expected.length ||
    expected.some((property) => !Object.hasOwn(object, property))
  ) {
    throw invalidContract(`${label} fields do not match the supported contract.`);
  }
}

function assertUtcTimestamp(value: string): void {
  const match = UTC_TIMESTAMP.exec(value);
  if (!match) {
    throw invalidContract(
      "Snapshot createdAtUtc must use seven fractional digits and a UTC Z suffix.",
    );
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  const daysInMonth = [31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (
    year < 1 ||
    month < 1 ||
    month > 12 ||
    day < 1 ||
    day > (daysInMonth[month - 1] ?? 0) ||
    hour > 23 ||
    minute > 59 ||
    second > 59
  ) {
    throw invalidContract("Snapshot createdAtUtc is not a valid UTC timestamp.");
  }
}

function isLeapYear(year: number): boolean {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function equalBytes(left: Uint8Array, right: Uint8Array): boolean {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function invalidContract(message: string): GameConfigClientError {
  return new GameConfigClientError("invalid_contract", message);
}
