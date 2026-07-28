import type { ParseError } from 'jsonc-parser';

import { parse, printParseErrorCode } from 'jsonc-parser';

export interface JsonSyntaxResult {
  column: null | number;
  line: null | number;
  message: string;
  valid: boolean;
}

export interface JsonReview {
  afterLines: string[];
  beforeLines: string[];
  changedAfter: [number, number] | null;
  changedBefore: [number, number] | null;
}

export function validateJsonSyntax(content: string): JsonSyntaxResult {
  const errors: ParseError[] = [];
  parse(content, errors, {
    allowEmptyContent: false,
    allowTrailingComma: false,
    disallowComments: true,
  });
  const error = errors[0];
  if (!error) {
    return { column: null, line: null, message: '', valid: true };
  }

  const prefix = content.slice(0, error.offset);
  const lines = prefix.split(/\r?\n/);
  return {
    column: (lines.at(-1)?.length ?? 0) + 1,
    line: lines.length,
    message: printParseErrorCode(error.error),
    valid: false,
  };
}

export function formatJson(content: string): string {
  return `${JSON.stringify(JSON.parse(content), null, 2)}\n`;
}

function displayJson(content: null | string): string[] {
  if (content === null || content.trim() === '') {
    return [];
  }

  try {
    return formatJson(content).trimEnd().split('\n');
  } catch {
    return content.split(/\r?\n/);
  }
}

export function buildJsonReview(
  before: null | string,
  after: string,
): JsonReview {
  const beforeLines = displayJson(before);
  const afterLines = displayJson(after);
  const comparableLength = Math.min(beforeLines.length, afterLines.length);
  let prefix = 0;
  while (
    prefix < comparableLength &&
    beforeLines[prefix] === afterLines[prefix]
  ) {
    prefix++;
  }

  let suffix = 0;
  while (
    suffix < comparableLength - prefix &&
    beforeLines[beforeLines.length - 1 - suffix] ===
      afterLines[afterLines.length - 1 - suffix]
  ) {
    suffix++;
  }

  const changedBeforeEnd = beforeLines.length - suffix;
  const changedAfterEnd = afterLines.length - suffix;
  const unchanged =
    prefix === beforeLines.length && prefix === afterLines.length;

  return {
    afterLines,
    beforeLines,
    changedAfter: unchanged ? null : [prefix, changedAfterEnd],
    changedBefore: unchanged ? null : [prefix, changedBeforeEnd],
  };
}

export function isLineChanged(
  range: [number, number] | null,
  index: number,
): boolean {
  return range !== null && index >= range[0] && index < range[1];
}
