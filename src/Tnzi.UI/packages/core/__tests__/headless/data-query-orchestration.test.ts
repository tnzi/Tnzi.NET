import { describe, it, expect, vi } from 'vitest';
import { DataQueryController } from '../../src/headless/data-query';
import type { ApiResult, PagedList } from '../../src/types/index';

interface TestItem {
  id: string;
  name: string;
}

const items: TestItem[] = [
  { id: '1', name: 'Alice' },
  { id: '2', name: 'Bob' },
];

function page(list: TestItem[] = items, totalCount = list.length): PagedList<TestItem> {
  return { items: list, totalCount, pageIndex: 1, pageSize: 20 } as PagedList<TestItem>;
}

function envelope(list: TestItem[] = items): ApiResult<PagedList<TestItem>> {
  return { succeeded: true, success: true, code: 200, data: page(list) } as ApiResult<PagedList<TestItem>>;
}

/**
 * These cover the orchestration options added so `@tnzi/ui-admin`'s
 * `useCrudPage` could delegate here instead of re-implementing the same
 * loop. Everything is opt-in: the defaults are asserted in
 * `data-query.test.ts` and did not move.
 */
describe('DataQueryController - result shapes', () => {
  it('accepts a bare PagedList (data layer already unwrapped the envelope)', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page() });
    await q.fetch();
    expect(q.items).toEqual(items);
    expect(q.pagination.totalCount).toBe(2);
    expect(q.status).toBe('success');
  });

  it('still accepts the ApiResult envelope', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => envelope() });
    await q.fetch();
    expect(q.items).toEqual(items);
    expect(q.status).toBe('success');
  });

  it('treats a rejection as a failed fetch without throwing', async () => {
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => {
        throw new Error('network down');
      },
    });
    // Must not reject: call sites are `void fetch()`. Resolves false because
    // nothing was applied.
    await expect(q.fetch()).resolves.toBe(false);
    expect(q.status).toBe('error');
    expect(q.error).toBe('network down');
  });
});

describe('DataQueryController - fetch() return value', () => {
  it('resolves true when this call applied its result', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page() });
    await expect(q.fetch()).resolves.toBe(true);
  });

  it('resolves false for a failed envelope', async () => {
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => ({ succeeded: false, message: 'no' }) as ApiResult<PagedList<TestItem>>,
    });
    await expect(q.fetch()).resolves.toBe(false);
  });

  // The reason this return value exists: a superseded call resolves too, so a
  // caller that announces "the list reloaded" cannot read `isSuccess` after
  // awaiting - by then it may be observing the NEWER request's success.
  it('resolves false for a superseded call even though a newer one succeeded', async () => {
    const resolvers: Array<(v: PagedList<TestItem>) => void> = [];
    const q = new DataQueryController<TestItem>({
      fetchFn: () => new Promise<PagedList<TestItem>>((resolve) => resolvers.push(resolve)),
    });

    const first = q.fetch();
    const second = q.fetch();

    resolvers[1]!(page());
    await expect(second).resolves.toBe(true);

    resolvers[0]!(page());
    await expect(first).resolves.toBe(false);
    // ...while the controller itself is in a success state from the newer call.
    expect(q.isSuccess).toBe(true);
  });
});

describe('DataQueryController - error object', () => {
  it('exposes the original Error, not just its message', async () => {
    const cause = new Error('boom');
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => {
        throw cause;
      },
    });
    await q.fetch();
    expect(q.errorObject).toBe(cause);
    expect(q.error).toBe('boom');
  });

  it('clears the error object on a subsequent success', async () => {
    let fail = true;
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => {
        if (fail) throw new Error('boom');
        return page();
      },
    });
    await q.fetch();
    expect(q.errorObject).not.toBeNull();
    fail = false;
    await q.fetch();
    expect(q.errorObject).toBeNull();
    expect(q.error).toBeNull();
  });

  it('calls onError once with the terminal failure', async () => {
    const onError = vi.fn();
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => {
        throw new Error('nope');
      },
      onError,
    });
    await q.fetch();
    expect(onError).toHaveBeenCalledTimes(1);
    expect(onError.mock.calls[0]![0]).toBeInstanceOf(Error);
  });

  it('does not call onError when a retry eventually succeeds', async () => {
    const onError = vi.fn();
    let calls = 0;
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => {
        calls++;
        if (calls === 1) throw new Error('transient');
        return page();
      },
      retry: { attempts: 2, delayMs: 0 },
      onError,
    });
    await q.fetch();
    expect(q.status).toBe('success');
    expect(onError).not.toHaveBeenCalled();
  });
});

describe('DataQueryController - retry', () => {
  it('does not retry by default', async () => {
    const fetchFn = vi.fn(async () => {
      throw new Error('down');
    });
    const q = new DataQueryController<TestItem>({ fetchFn });
    await q.fetch();
    expect(fetchFn).toHaveBeenCalledTimes(1);
  });

  it('retries the configured number of times, then surfaces the last error', async () => {
    const fetchFn = vi.fn(async () => {
      throw new Error('down');
    });
    const q = new DataQueryController<TestItem>({ fetchFn, retry: { attempts: 3, delayMs: 0 } });
    await q.fetch();
    // 1 initial attempt + 3 retries
    expect(fetchFn).toHaveBeenCalledTimes(4);
    expect(q.status).toBe('error');
    expect(q.error).toBe('down');
  });

  it('stops retrying as soon as one attempt succeeds', async () => {
    let calls = 0;
    const fetchFn = vi.fn(async () => {
      calls++;
      if (calls < 3) throw new Error('transient');
      return page();
    });
    const q = new DataQueryController<TestItem>({ fetchFn, retry: { attempts: 5, delayMs: 0 } });
    await q.fetch();
    expect(fetchFn).toHaveBeenCalledTimes(3);
    expect(q.status).toBe('success');
    expect(q.items).toEqual(items);
  });

  it('also retries a failed envelope, not just a rejection', async () => {
    const fetchFn = vi.fn(async () => ({ succeeded: false, message: 'server said no' }) as ApiResult<PagedList<TestItem>>);
    const q = new DataQueryController<TestItem>({ fetchFn, retry: { attempts: 2, delayMs: 0 } });
    await q.fetch();
    expect(fetchFn).toHaveBeenCalledTimes(3);
    expect(q.error).toBe('server said no');
  });
});

describe('DataQueryController - page/sort behaviour switches', () => {
  // `goTo` clamps against `totalPages`, which is 0 until a response lands - so
  // these must fetch first, otherwise page 3 silently becomes page 1 and the
  // "resets to 1" assertion passes without the reset ever running.
  it('resets to page 1 on sort by default', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page([], 100) });
    await q.fetch();
    await q.changePage(3);
    expect(q.pagination.pageIndex).toBe(3);
    await q.changeSort('name');
    expect(q.pagination.pageIndex).toBe(1);
  });

  it('keeps the current page on sort when resetPageOnSort is false', async () => {
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => page([], 100),
      resetPageOnSort: false,
    });
    await q.fetch();
    await q.changePage(3);
    await q.changeSort('name');
    expect(q.pagination.pageIndex).toBe(3);
  });

  it('clears the selection on paging by default', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page(items, 100) });
    await q.fetch();
    q.selection.select('1');
    expect(q.selection.selectedKeys).toEqual(['1']);
    await q.changePage(2);
    expect(q.selection.selectedKeys).toEqual([]);
  });

  it('keeps the selection across pages when clearSelectionOnPageChange is false', async () => {
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => page(items, 100),
      clearSelectionOnPageChange: false,
    });
    await q.fetch();
    q.selection.select('1');
    await q.changePage(2);
    expect(q.selection.selectedKeys).toEqual(['1']);
  });
});

describe('DataQueryController - page clamping', () => {
  it('clamps a page beyond the known page count by default', async () => {
    // 3 items over a page size of 20 = exactly one page.
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page(items, 3) });
    await q.fetch();
    await q.changePage(2);
    expect(q.pagination.pageIndex).toBe(1);
  });

  // The switch governs what gets REQUESTED. Once a response lands,
  // `updateFromResponse` still pulls the index back inside the real page count -
  // the server is the authority on how many pages exist, and staying on page 2
  // of a one-page result would leave the pager contradicting the rows.
  it('requests the page as asked when clampPageToTotal is false', async () => {
    const fetchFn = vi.fn(async () => page(items, 3));
    const q = new DataQueryController<TestItem>({ fetchFn, clampPageToTotal: false });
    await q.fetch();
    await q.changePage(2);
    expect((fetchFn.mock.calls.at(-1)![0] as Record<string, unknown>)['pageIndex']).toBe(2);
  });

  it('lets a page beyond the total survive until a response says otherwise', async () => {
    // Never resolves: nothing has told us how many pages there are yet.
    const q = new DataQueryController<TestItem>({
      fetchFn: () => new Promise<PagedList<TestItem>>(() => {}),
      clampPageToTotal: false,
    });
    void q.changePage(3);
    expect(q.pagination.pageIndex).toBe(3);
  });

  it('still refuses a page below 1 when clamping is off', async () => {
    const q = new DataQueryController<TestItem>({
      fetchFn: async () => page(items, 3),
      clampPageToTotal: false,
    });
    await q.changePage(0);
    expect(q.pagination.pageIndex).toBe(1);
  });
});

describe('DataQueryController - setSort', () => {
  it('states the target sort outright instead of cycling', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page() });
    await q.setSort('name', 'desc');
    expect(q.sort.sortBy).toBe('name');
    expect(q.sort.sortDirection).toBe('desc');
    // Repeating it is idempotent - unlike changeSort, which would toggle.
    await q.setSort('name', 'desc');
    expect(q.sort.sortDirection).toBe('desc');
  });

  it('clears the sort when the field is null', async () => {
    const q = new DataQueryController<TestItem>({ fetchFn: async () => page() });
    await q.setSort('name', 'asc');
    await q.setSort(null);
    expect(q.sort.sortBy).toBeNull();
  });
});

describe('DataQueryController - buildQuery', () => {
  it('uses the default payload shape when not supplied', async () => {
    const fetchFn = vi.fn(async () => page());
    const q = new DataQueryController<TestItem>({ fetchFn });
    await q.setSort('name', 'desc');
    const sent = fetchFn.mock.calls.at(-1)![0] as Record<string, unknown>;
    expect(sent).toMatchObject({ pageIndex: 1, sortBy: 'name', sortDescending: true });
  });

  it('lets the caller rename fields for a backend with a different contract', async () => {
    const fetchFn = vi.fn(async () => page());
    const q = new DataQueryController<TestItem>({
      fetchFn,
      buildQuery: ({ pagination, sort }) => ({
        page: pagination.pageIndex,
        size: pagination.pageSize,
        orderBy: sort.sortBy,
        descending: sort.sortDirection === 'desc',
      }),
    });
    await q.setSort('name', 'desc');
    const sent = fetchFn.mock.calls.at(-1)![0] as Record<string, unknown>;
    expect(sent).toEqual({ page: 1, size: 20, orderBy: 'name', descending: true });
    expect(sent).not.toHaveProperty('pageIndex');
  });
});

describe('DataQueryController - staleness', () => {
  it('discards a superseded slow response', async () => {
    const resolvers: Array<(v: PagedList<TestItem>) => void> = [];
    const q = new DataQueryController<TestItem>({
      fetchFn: () => new Promise<PagedList<TestItem>>((resolve) => resolvers.push(resolve)),
    });

    const first = q.fetch();
    const second = q.fetch();

    // Resolve the SECOND (current) request first, then the stale first one.
    resolvers[1]!(page([{ id: '9', name: 'Current' }]));
    await second;
    resolvers[0]!(page([{ id: '1', name: 'Stale' }]));
    await first;

    expect(q.items).toEqual([{ id: '9', name: 'Current' }]);
  });
});
