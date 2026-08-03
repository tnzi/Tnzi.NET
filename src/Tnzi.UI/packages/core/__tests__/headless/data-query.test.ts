import { describe, it, expect, beforeEach, vi } from 'vitest';
import { DataQueryController } from '../../src/headless/data-query';
import type { ApiResult, PagedList } from '../../src/types/index';

// Helper to create a successful paged response
function pagedResult<T>(items: T[], totalCount: number): ApiResult<PagedList<T>> {
  return {
    succeeded: true,
    success: true,
    code: 200,
    data: { items, totalCount, pageIndex: 1, pageSize: 20 },
  } as ApiResult<PagedList<T>>;
}

function failedResult<T>(message = 'Fetch failed'): ApiResult<PagedList<T>> {
  return {
    succeeded: false,
    success: false,
    code: 500,
    message,
  } as ApiResult<PagedList<T>>;
}

interface TestItem {
  id: string;
  name: string;
}

const items: TestItem[] = [
  { id: '1', name: 'Alice' },
  { id: '2', name: 'Bob' },
  { id: '3', name: 'Charlie' },
];

describe('DataQueryController', () => {
  let fetchFn: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchFn = vi.fn().mockResolvedValue(pagedResult(items, 50));
  });

  // ------------------------------------------
  // Constructor defaults
  // ------------------------------------------

  describe('constructor defaults', () => {
    it('should have idle status initially', () => {
      const query = new DataQueryController({ fetchFn });
      expect(query.status).toBe('idle');
      expect(query.isIdle).toBe(true);
    });

    it('should have empty items', () => {
      const query = new DataQueryController({ fetchFn });
      expect(query.items).toEqual([]);
    });

    it('should have no error', () => {
      const query = new DataQueryController({ fetchFn });
      expect(query.error).toBeNull();
    });

    it('should have empty filter', () => {
      const query = new DataQueryController({ fetchFn });
      expect(query.filter).toEqual({});
    });

    it('should accept defaultFilter', () => {
      const query = new DataQueryController({
        fetchFn,
        defaultFilter: { keyword: 'test' },
      });
      expect(query.filter).toEqual({ keyword: 'test' });
    });

    it('should create sub-controllers', () => {
      const query = new DataQueryController({ fetchFn });
      expect(query.pagination).toBeDefined();
      expect(query.sort).toBeDefined();
      expect(query.selection).toBeDefined();
    });
  });

  // ------------------------------------------
  // Computed getters
  // ------------------------------------------

  describe('computed getters', () => {
    it('isLoading should be true when status is loading', async () => {
      let resolvePromise: (v: ApiResult<PagedList<TestItem>>) => void;
      fetchFn.mockReturnValue(new Promise((r) => { resolvePromise = r; }));

      const query = new DataQueryController<TestItem>({ fetchFn });
      const fetchPromise = query.fetch();
      expect(query.isLoading).toBe(true);

      resolvePromise!(pagedResult(items, 50));
      await fetchPromise;
      expect(query.isLoading).toBe(false);
    });

    it('isSuccess should be true after successful fetch', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.isSuccess).toBe(true);
    });

    it('isError should be true after failed fetch', async () => {
      fetchFn.mockResolvedValue(failedResult());
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.isError).toBe(true);
    });

    it('isEmpty should be true when success with no items', async () => {
      fetchFn.mockResolvedValue(pagedResult([], 0));
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.isEmpty).toBe(true);
    });

    it('hasData should be true when items exist', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.hasData).toBe(true);
    });
  });

  // ------------------------------------------
  // fetch
  // ------------------------------------------

  describe('fetch', () => {
    it('should populate items from response', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.items).toEqual(items);
      expect(query.status).toBe('success');
    });

    it('should update pagination totalCount', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.pagination.totalCount).toBe(50);
    });

    it('should pass query params to fetchFn', async () => {
      const query = new DataQueryController<TestItem>({
        fetchFn,
        defaultFilter: { keyword: 'test' },
      });
      await query.fetch();
      expect(fetchFn).toHaveBeenCalledWith(
        expect.objectContaining({
          pageIndex: 1,
          pageSize: 20,
          keyword: 'test',
        }),
        expect.any(AbortSignal),
      );
    });

    it('should set error on failed response', async () => {
      fetchFn.mockResolvedValue(failedResult('Server error'));
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.error).toBe('Server error');
      expect(query.status).toBe('error');
    });

    it('should set error on exception', async () => {
      fetchFn.mockRejectedValue(new Error('Network error'));
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      expect(query.error).toBe('Network error');
      expect(query.status).toBe('error');
    });

    it('should abort previous request on new fetch', async () => {
      let callCount = 0;
      const signals: AbortSignal[] = [];
      fetchFn.mockImplementation((_q: unknown, signal: AbortSignal) => {
        signals.push(signal);
        callCount++;
        return Promise.resolve(pagedResult(items, 50));
      });

      const query = new DataQueryController<TestItem>({ fetchFn });
      query.fetch(); // first fetch (will be aborted)
      await query.fetch(); // second fetch

      expect(callCount).toBe(2);
      expect(signals[0].aborted).toBe(true);
      expect(signals[1].aborted).toBe(false);
    });

    it('should update selection allKeys from fetched items', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      // _allKeys is private; verify via toggleAll → selectedKeys
      query.selection.toggleAll();
      expect(query.selection.selectedKeys).toEqual(['1', '2', '3']);
    });

    it('should use custom rowKey extractor', async () => {
      const query = new DataQueryController<TestItem>({
        fetchFn,
        rowKey: (item) => item.name,
      });
      await query.fetch();
      query.selection.toggleAll();
      expect(query.selection.selectedKeys).toEqual(['Alice', 'Bob', 'Charlie']);
    });
  });

  // ------------------------------------------
  // Actions
  // ------------------------------------------

  describe('changePage', () => {
    it('should update pagination and refetch', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      fetchFn.mockClear();
      await query.changePage(2);
      expect(query.pagination.pageIndex).toBe(2);
      expect(fetchFn).toHaveBeenCalledTimes(1);
    });

    it('should clear selection', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      query.selection.toggle('1');
      await query.changePage(2);
      expect(query.selection.selectedKeys).toEqual([]);
    });
  });

  describe('changePageSize', () => {
    it('should update pageSize and reset to page 1', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      await query.changePageSize(50);
      expect(query.pagination.pageSize).toBe(50);
      expect(query.pagination.pageIndex).toBe(1);
    });
  });

  describe('changeSort', () => {
    it('should toggle sort and reset to page 1', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      query.pagination.goTo(3);
      await query.changeSort('name');
      expect(query.sort.sortBy).toBe('name');
      expect(query.pagination.pageIndex).toBe(1);
    });
  });

  describe('applyFilter', () => {
    it('should merge filter and reset to page 1', async () => {
      const query = new DataQueryController<TestItem, { keyword?: string; status?: string }>({
        fetchFn,
        defaultFilter: { keyword: 'old' },
      });
      await query.fetch();
      query.pagination.goTo(3);
      await query.applyFilter({ keyword: 'new' });
      expect(query.filter.keyword).toBe('new');
      expect(query.pagination.pageIndex).toBe(1);
    });
  });

  describe('resetFilter', () => {
    it('should reset filter to empty and refetch', async () => {
      const query = new DataQueryController<TestItem, { keyword?: string }>({
        fetchFn,
        defaultFilter: { keyword: 'test' },
      });
      await query.fetch();
      await query.resetFilter();
      expect(query.filter).toEqual({});
    });

    it('should accept new default filter', async () => {
      const query = new DataQueryController<TestItem, { keyword?: string }>({
        fetchFn,
      });
      await query.resetFilter({ keyword: 'fresh' });
      expect(query.filter.keyword).toBe('fresh');
    });
  });

  describe('refresh', () => {
    it('should refetch current page without changing params', async () => {
      const query = new DataQueryController<TestItem>({ fetchFn });
      await query.fetch();
      fetchFn.mockClear();
      await query.refresh();
      expect(fetchFn).toHaveBeenCalledTimes(1);
    });
  });

  describe('reset', () => {
    it('should reset all state to initial', async () => {
      const query = new DataQueryController<TestItem, { keyword?: string }>({
        fetchFn,
        defaultFilter: { keyword: 'test' },
      });
      await query.fetch();
      query.pagination.goTo(3);
      query.sort.toggle('name');

      await query.reset();
      expect(query.status).toBe('idle');
      expect(query.items).toEqual([]);
      expect(query.error).toBeNull();
      expect(query.filter).toEqual({});
      expect(query.pagination.pageIndex).toBe(1);
      expect(query.sort.sortBy).toBeNull();
    });

    it('should cancel in-flight request', async () => {
      let signal: AbortSignal | undefined;
      fetchFn.mockImplementation((_q: unknown, s: AbortSignal) => {
        signal = s;
        return new Promise(() => {}); // never resolves
      });

      const query = new DataQueryController<TestItem>({ fetchFn });
      query.fetch();
      await query.reset();
      expect(signal?.aborted).toBe(true);
    });
  });

  // ------------------------------------------
  // immediate option
  // ------------------------------------------

  describe('immediate', () => {
    it('should auto-fetch on next microtask', async () => {
      const query = new DataQueryController<TestItem>({
        fetchFn,
        immediate: true,
      });
      // Wait for microtask
      await new Promise((r) => setTimeout(r, 10));
      expect(query.items).toEqual(items);
      expect(fetchFn).toHaveBeenCalledTimes(1);
    });
  });
});
