import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('@tnzi/core/services/ai', () => ({
  streamChat: async () => ({ text: 'done' }),
  ChatStreamRequestError: class extends Error {},
}));

const { useChatThreads } = await import('../../src/headless/useChatThreads');

type Row = { id: string; title: string; lastActivityTime: string; creationTime?: string; archivedTime?: string | null };

/** A tiny server: the list honours `status` and `keyword` so the tests can see what the hook asked for. */
function server(rows: Row[]) {
  let data = rows.map((r) => ({ ...r }));
  const threadApi = {
    getList: vi.fn(async (query: { status?: string; keyword?: string; pageIndex: number; pageSize: number }) => {
      const items = data.filter((r) => {
        if (query.status === 'Active' && r.archivedTime) return false;
        if (query.status === 'Archived' && !r.archivedTime) return false;
        return !query.keyword || r.title.toLowerCase().includes(query.keyword.toLowerCase());
      });
      return { succeeded: true, data: { items, totalCount: items.length } };
    }),
    getMessages: vi.fn(async () => ({ succeeded: true, data: { messages: [] } })),
    delete: vi.fn(async (id: string) => {
      data = data.filter((r) => r.id !== id);
      return { succeeded: true };
    }),
    updateTitle: vi.fn(async (id: string, body: { title: string }) => {
      data = data.map((r) => (r.id === id ? { ...r, title: body.title } : r));
      return { succeeded: true };
    }),
    archive: vi.fn(async (id: string) => {
      data = data.map((r) => (r.id === id ? { ...r, archivedTime: '2026-10-21T00:00:00Z' } : r));
      return { succeeded: true };
    }),
    unarchive: vi.fn(async (id: string) => {
      data = data.map((r) => (r.id === id ? { ...r, archivedTime: null } : r));
      return { succeeded: true };
    }),
  };
  return threadApi;
}

function deps(threadApi: ReturnType<typeof server>, extra: Record<string, unknown> = {}) {
  return {
    http: { getAccessToken: () => 'tok', refreshAccessToken: vi.fn(), reportUnauthorized: vi.fn() },
    chatApi: { getChatStreamUrl: () => '/s', getActiveRuns: vi.fn(async () => ({ succeeded: true, data: [] })), cancelRun: vi.fn() },
    threadApi,
    ...extra,
  } as never;
}

const ROWS: Row[] = [
  { id: 'a', title: 'Alpha', lastActivityTime: '2026-10-21T10:00:00Z', creationTime: '2026-10-01T10:00:00Z' },
  { id: 'b', title: 'Beta', lastActivityTime: '2026-10-20T10:00:00Z' },
  { id: 'z', title: 'Zeta', lastActivityTime: '2026-10-19T10:00:00Z', archivedTime: '2026-10-19T11:00:00Z' },
];

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

beforeEach(() => localStorage.clear());

describe('useChatThreads recents', () => {
  it('asks for active conversations by default and maps the archive and creation dates', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();

    expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Active', sort: 'LastActivity' }));
    expect(chat.threads.value.map((t) => t.id)).toEqual(['a', 'b']);
    expect(chat.threads.value[0]).toMatchObject({ createdAt: '2026-10-01T10:00:00Z' });
    expect(chat.threads.value[0]).not.toHaveProperty('archived');
  });

  it('a filter or order change reloads the list and is remembered; grouping only is remembered', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    api.getList.mockClear();

    await chat.recents.setPreferences({ status: 'all', sort: 'name', lastActivity: '7d' });
    expect(api.getList).toHaveBeenCalledTimes(1);
    const query = api.getList.mock.calls[0]![0] as Record<string, unknown>;
    expect(query).toMatchObject({ status: 'All', sort: 'Title', pageIndex: 1 });
    expect(typeof query.startTime).toBe('string');

    await chat.recents.setPreferences({ groupBy: 'date' });
    expect(api.getList).toHaveBeenCalledTimes(1);

    const again = useChatThreads(deps(server(ROWS)));
    expect(again.recents.preferences.value).toEqual({ status: 'all', lastActivity: '7d', sort: 'name', groupBy: 'date' });
  });

  describe('listAgentIds', () => {
    type Agented = Row & { agentId: string };
    const AGENT_ROWS: Agented[] = [
      { id: 'c1', title: 'Chat one', lastActivityTime: '2026-10-21T10:00:00Z', agentId: 'chat' },
      { id: 'c2', title: 'Chat two', lastActivityTime: '2026-10-20T10:00:00Z', agentId: 'chat' },
      { id: 'k1', title: 'Code one', lastActivityTime: '2026-10-19T10:00:00Z', agentId: 'code' },
    ];

    /** The server honours `agentIds` and pages before answering, as the real one does. */
    function scopedServer() {
      const api = server(AGENT_ROWS);
      api.getList.mockImplementation((async (query: { agentIds?: string[]; keyword?: string; pageIndex: number; pageSize: number }) => {
        const all = AGENT_ROWS.filter((r) => !query.agentIds?.length || query.agentIds.includes(r.agentId))
          .filter((r) => !query.keyword || r.title.toLowerCase().includes(query.keyword.toLowerCase()));
        const start = (query.pageIndex - 1) * query.pageSize;
        return { succeeded: true, data: { items: all.slice(start, start + query.pageSize), totalCount: all.length } };
      }) as never);
      return api;
    }

    it('sends no agentIds by default', async () => {
      const api = scopedServer();
      const chat = useChatThreads(deps(api));
      await chat.loadThreads();
      expect(api.getList.mock.calls[0]![0]).not.toHaveProperty('agentIds');
      expect(chat.threads.value.map((t) => t.id)).toEqual(['c1', 'c2', 'k1']);
    });

    it('limits the sidebar list, its search and its paging to the agents', async () => {
      const api = scopedServer();
      const chat = useChatThreads(deps(api, { listAgentIds: () => ['chat'], pageSize: 1 }));
      await chat.loadThreads();
      expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ agentIds: ['chat'] }));
      expect(chat.threads.value.map((t) => t.id)).toEqual(['c1']);
      expect(chat.hasMoreThreads.value).toBe(true);

      await chat.loadMoreThreads();
      expect(chat.threads.value.map((t) => t.id)).toEqual(['c1', 'c2']);
      expect(chat.hasMoreThreads.value).toBe(false);
      expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ agentIds: ['chat'], pageIndex: 2 }));

      await chat.searchThreads('one');
      expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ agentIds: ['chat'], keyword: 'one' }));
      expect(chat.threads.value.map((t) => t.id)).toEqual(['c1']);
    });

    it('accepts one id as a string and a set of ids, without duplicates', async () => {
      const api = scopedServer();
      const one = useChatThreads(deps(api, { listAgentIds: () => 'code' }));
      await one.loadThreads();
      expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ agentIds: ['code'] }));
      expect(one.threads.value.map((t) => t.id)).toEqual(['k1']);

      const many = useChatThreads(deps(api, { listAgentIds: () => ['chat', 'code', 'chat'] }));
      await many.loadThreads();
      expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ agentIds: ['chat', 'code'] }));
    });

    it('limits the Recents page too', async () => {
      const api = scopedServer();
      const chat = useChatThreads(deps(api, { listAgentIds: () => ['code'] }));
      await chat.recents.page.load();
      expect(api.getList).toHaveBeenLastCalledWith(expect.objectContaining({ agentIds: ['code'] }));
      expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['k1']);
      await chat.recents.page.search('chat');
      expect(chat.recents.page.threads.value).toEqual([]);
    });

    it('reloads the lists when a reactive scope changes', async () => {
      const { ref } = await import('vue');
      const scope = ref<string[]>(['chat']);
      const api = scopedServer();
      const chat = useChatThreads(deps(api, { listAgentIds: () => scope.value }));
      await chat.loadThreads();
      await chat.recents.page.load();
      api.getList.mockClear();

      scope.value = ['code'];
      await flush();
      expect(api.getList).toHaveBeenCalledTimes(2);
      expect(chat.threads.value.map((t) => t.id)).toEqual(['k1']);
      expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['k1']);
    });
  });

  it('does not persist with a null storage key', async () => {
    const chat = useChatThreads(deps(server(ROWS), { recentsStorageKey: null }));
    await chat.recents.setPreferences({ groupBy: 'date' });
    expect(chat.recents.preferences.value.groupBy).toBe('date');
    expect(localStorage.length).toBe(0);
  });

  it('archiving takes the row off the Active list and the page, and adds it to an Archived page', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await chat.recents.page.load();

    expect(await chat.recents.archive('a')).toBe(true);
    expect(api.archive).toHaveBeenCalledWith('a');
    expect(chat.threads.value.map((t) => t.id)).toEqual(['b']);
    expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['b']);

    await chat.recents.page.setStatus('archived');
    expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['a', 'z']);

    expect(await chat.recents.unarchive('z')).toBe(true);
    expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['a']);
    await flush();
    /* The Active sidebar list did not hold `z`: it reloads to place it. */
    expect(chat.threads.value.map((t) => t.id)).toEqual(['b', 'z']);
  });

  it('on an All list archiving keeps the row and marks it', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.recents.setPreferences({ status: 'all' });

    await chat.recents.archive('b');
    expect(chat.threads.value.find((t) => t.id === 'b')).toMatchObject({ archived: true });
  });

  it('a refused archive changes nothing and is reported', async () => {
    const api = server(ROWS);
    api.archive.mockResolvedValueOnce({ succeeded: false, code: 403, message: 'no' } as never);
    const onError = vi.fn();
    const chat = useChatThreads(deps(api, { onError }));
    await chat.loadThreads();

    expect(await chat.recents.archive('a')).toBe(false);
    expect(chat.threads.value.map((t) => t.id)).toEqual(['a', 'b']);
    expect(onError).toHaveBeenCalledWith('no', expect.objectContaining({ operation: 'archive-thread', status: 403 }));
  });

  it('renames in the sidebar and on the page, trimmed; a blank title is not sent', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await chat.recents.page.load();

    expect(await chat.recents.rename('b', '  Renamed  ')).toBe(true);
    expect(api.updateTitle).toHaveBeenCalledWith('b', { title: 'Renamed' });
    expect(chat.threads.value.find((t) => t.id === 'b')!.title).toBe('Renamed');
    expect(chat.recents.page.threads.value.find((t) => t.id === 'b')!.title).toBe('Renamed');

    expect(await chat.recents.rename('b', '   ')).toBe(false);
    expect(api.updateTitle).toHaveBeenCalledTimes(1);
  });

  it('a delete leaves the page too', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await chat.recents.page.load();

    await chat.deleteThread('a');
    expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['b']);
  });

  it('the page searches on its own, newest activity first', async () => {
    const api = server(ROWS);
    const chat = useChatThreads(deps(api));
    await chat.loadThreads();
    await chat.recents.page.search('alp');

    expect(api.getList).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'alp', status: 'Active', sort: 'LastActivity' }),
    );
    expect(chat.recents.page.threads.value.map((t) => t.id)).toEqual(['a']);
    expect(chat.recents.page.loaded.value).toBe(true);
    /* The sidebar search is untouched. */
    expect(chat.threadKeyword.value).toBe('');
    expect(chat.threads.value).toHaveLength(2);
  });
});
