import { describe, expect, it } from 'vitest';

import {
  buildJsonReview,
  formatJson,
  isLineChanged,
  validateJsonSyntax,
} from './json';

describe('game configuration JSON utilities', () => {
  it('formats valid JSON deterministically for editing', () => {
    expect(formatJson('{"items":[{"id":1}]}')).toBe(
      '{\n  "items": [\n    {\n      "id": 1\n    }\n  ]\n}\n',
    );
  });

  it('reports a useful position for malformed JSON', () => {
    const result = validateJsonSyntax('{\n  "items": [}\n');

    expect(result.valid).toBe(false);
    expect(result.line).toBeGreaterThan(0);
    expect(result.column).toBeGreaterThan(0);
  });

  it('finds the smallest changed middle segment', () => {
    const review = buildJsonReview(
      '{"items":[{"id":1,"name":"Old"}]}',
      '{"items":[{"id":1,"name":"New"}]}',
    );

    expect(review.changedBefore).not.toBeNull();
    expect(review.changedAfter).not.toBeNull();
    expect(
      review.beforeLines.some(
        (line, index) =>
          isLineChanged(review.changedBefore, index) && line.includes('Old'),
      ),
    ).toBe(true);
    expect(
      review.afterLines.some(
        (line, index) =>
          isLineChanged(review.changedAfter, index) && line.includes('New'),
      ),
    ).toBe(true);
  });

  it('treats a new draft as an all-new candidate', () => {
    const review = buildJsonReview(null, '{"items":[]}');

    expect(review.beforeLines).toEqual([]);
    expect(review.changedBefore).toEqual([0, 0]);
    expect(review.changedAfter).toEqual([0, review.afterLines.length]);
  });

  it('returns no changed range for equal canonical content', () => {
    const review = buildJsonReview('{"a":1}', '{\n  "a": 1\n}');

    expect(review.changedBefore).toBeNull();
    expect(review.changedAfter).toBeNull();
  });
});
