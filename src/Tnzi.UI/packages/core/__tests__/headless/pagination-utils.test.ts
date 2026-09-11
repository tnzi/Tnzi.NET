import { describe, it, expect } from 'vitest';
import {
  normalizePageSize,
  calculateTotalPages,
  clampPageIndex,
  updatePageQuery,
  initialPaginationState,
} from '../../src/headless/pagination';

describe('normalizePageSize', () => {
  it('falls back for undefined, zero and negative sizes', () => {
    expect(normalizePageSize(undefined)).toBe(10);
    expect(normalizePageSize(0)).toBe(10);
    expect(normalizePageSize(-5, 25)).toBe(25);
  });

  it('floors a fractional size', () => {
    expect(normalizePageSize(12.9)).toBe(12);
  });
});

describe('calculateTotalPages', () => {
  it('rounds up and never reports fewer than one page', () => {
    expect(calculateTotalPages(0, 10)).toBe(1);
    expect(calculateTotalPages(10, 10)).toBe(1);
    expect(calculateTotalPages(11, 10)).toBe(2);
    expect(calculateTotalPages(-3, 10)).toBe(1);
  });

  it('normalises an invalid page size before dividing', () => {
    expect(calculateTotalPages(25, 0)).toBe(3);
  });
});

describe('clampPageIndex', () => {
  it('keeps the index inside [1, totalPages] and floors fractions', () => {
    expect(clampPageIndex(0, 50, 10)).toBe(1);
    expect(clampPageIndex(3.7, 50, 10)).toBe(3);
    expect(clampPageIndex(99, 50, 10)).toBe(5);
    expect(clampPageIndex(2, 0, 10)).toBe(1);
  });
});

describe('updatePageQuery', () => {
  it('copies the query and overrides the paging fields without mutating the source', () => {
    const query = { keyword: 'x', pageIndex: 1, pageSize: 10 };
    const next = updatePageQuery(query, 3, 20);

    expect(next).toEqual({ keyword: 'x', pageIndex: 3, pageSize: 20 });
    expect(query.pageIndex).toBe(1);
  });

  it('starts from an empty object when there is no query yet', () => {
    expect(updatePageQuery(undefined, 2)).toEqual({ pageIndex: 2, pageSize: undefined });
  });
});

describe('initialPaginationState', () => {
  it('is a fresh object each time', () => {
    const a = initialPaginationState();
    const b = initialPaginationState();

    expect(a).toEqual({ pageIndex: 1, pageSize: 10, totalCount: 0, totalPages: 0, hasMore: false });
    expect(a).not.toBe(b);
  });
});
