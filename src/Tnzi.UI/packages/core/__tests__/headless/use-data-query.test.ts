import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { useDataQuery } from '../../src/headless/useDataQuery';
import type { ApiResult, PagedList } from '../../src/types/index';

function createMockFetchFn<T>(data: T[], total?: number) {
  return vi.fn(
    async (_query: Record<string, unknown>, _signal?: AbortSignal): Promise<ApiResult<PagedList<T>>> => ({
      succeeded: true,
      success: true,
      code: 200,
      data: {
        items: data,
        totalCount: total ?? data.length,
        pageIndex: 1,
        pageSize: 10,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false,
      },
    })
  );
}

describe('useDataQuery composable', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('should auto-refresh at specified interval', async () => {
    const fetchFn = createMockFetchFn([{ id: 1 }]);
    const query = useDataQuery({
      fetchFn,
      immediate: false,
      autoRefreshInterval: 5000,
    });

    // Initial state - no fetch yet
    expect(fetchFn).toHaveBeenCalledTimes(0);

    // Advance 5 seconds - should trigger first auto-refresh
    await vi.advanceTimersByTimeAsync(5000);
    expect(fetchFn).toHaveBeenCalledTimes(1);

    // Advance another 5 seconds
    await vi.advanceTimersByTimeAsync(5000);
    expect(fetchFn).toHaveBeenCalledTimes(2);

    query.dispose();
  });

  it('should stop auto-refresh on dispose', async () => {
    const fetchFn = createMockFetchFn([{ id: 1 }]);
    const query = useDataQuery({
      fetchFn,
      immediate: false,
      autoRefreshInterval: 5000,
    });

    // First tick fires
    await vi.advanceTimersByTimeAsync(5000);
    expect(fetchFn).toHaveBeenCalledTimes(1);

    // Dispose and advance - should NOT fire again
    query.dispose();
    await vi.advanceTimersByTimeAsync(15000);
    expect(fetchFn).toHaveBeenCalledTimes(1);
  });

  it('should reset controller state on dispose', async () => {
    const fetchFn = createMockFetchFn([{ id: 1 }, { id: 2 }]);
    const query = useDataQuery({
      fetchFn,
      immediate: false,
    });

    // Manually fetch to populate data
    await query.fetch();
    expect(query.items.length).toBe(2);
    expect(query.status).toBe('success');

    // Dispose should reset
    query.dispose();
    expect(query.items.length).toBe(0);
    expect(query.status).toBe('idle');
  });
});
