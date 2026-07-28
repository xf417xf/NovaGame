import { sha256 } from "@noble/hashes/sha2.js";

import type { Sha256Digest } from "./contracts.js";
import { GameConfigClientError } from "./errors.js";

const LOWERCASE_SHA256 = /^[0-9a-f]{64}$/;

export class NobleSha256 implements Sha256Digest {
  async digestHex(bytes: Uint8Array): Promise<string> {
    return toHex(sha256(bytes));
  }
}

export class WebCryptoSha256 implements Sha256Digest {
  async digestHex(bytes: Uint8Array): Promise<string> {
    if (!globalThis.crypto?.subtle) {
      throw new GameConfigClientError(
        "hash_failed",
        "The Web Crypto SHA-256 implementation is unavailable.",
      );
    }

    const copy = Uint8Array.from(bytes);
    const digest = await globalThis.crypto.subtle.digest("SHA-256", copy);
    return toHex(new Uint8Array(digest));
  }
}

function toHex(bytes: Uint8Array): string {
  return Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("");
}

export function assertSha256Hex(value: string, label: string): void {
  if (!LOWERCASE_SHA256.test(value)) {
    throw new GameConfigClientError(
      "hash_failed",
      `${label} must be a lowercase hexadecimal SHA-256 value.`,
    );
  }
}
