import { describe, it, expect, vi } from 'vitest';
import { useAiPersonalization } from '../../src/headless/useAiPersonalization';

/** Minimal HttpClient stand-in - the hook only uses get/put via the core factory. */
function makeClient(overrides: Partial<Record<'get' | 'put', unknown>> = {}) {
  return {
    get: vi.fn(async () => ({ succeeded: true, data: null })),
    put: vi.fn(async () => ({ succeeded: true, data: null })),
    ...overrides,
  } as never;
}

const PROFILE = {
  id: 'p1',
  userId: 'u1',
  displayName: 'Ada',
  role: 'Engineer',
  preferredLanguage: 'en',
  content: 'Prefers concise answers.',
  creationTime: '2026-01-01T00:00:00Z',
};

describe('useAiPersonalization', () => {
  it('reports unavailable and no-ops without a client', async () => {
    const p = useAiPersonalization();
    expect(p.available.value).toBe(false);

    await p.load();
    expect(await p.save()).toBe(false);
    expect(p.draft.value.content).toBe('');
  });

  it('loads the profile into the draft', async () => {
    const client = makeClient({ get: vi.fn(async () => ({ succeeded: true, data: PROFILE })) });
    const p = useAiPersonalization({ client });

    await p.load();

    expect(p.available.value).toBe(true);
    expect(p.draft.value.displayName).toBe('Ada');
    expect(p.draft.value.content).toBe('Prefers concise answers.');
    expect(p.dirty.value).toBe(false);
  });

  it('nulls out blanks instead of sending empty strings', async () => {
    /* An empty string is a value: sent as-is it overwrites a stored preference
       with a blank rather than clearing it. */
    const put = vi.fn(async () => ({ succeeded: true, data: PROFILE }));
    const p = useAiPersonalization({ client: makeClient({ put }) });

    p.draft.value = { displayName: '  ', role: '', preferredLanguage: 'zh', content: '' };
    await p.save();

    expect(put).toHaveBeenCalledWith('/user-profile', {
      displayName: null,
      role: null,
      preferredLanguage: 'zh',
      content: null,
    });
  });

  it('keeps the prose the user wrote verbatim', async () => {
    const put = vi.fn(async () => ({ succeeded: true, data: PROFILE }));
    const p = useAiPersonalization({ client: makeClient({ put }) });

    p.draft.value = { ...p.draft.value, content: 'Line one\n\n- a\n- b\n' };
    await p.save();

    expect((put.mock.calls[0] as unknown[])[1]).toMatchObject({
      content: 'Line one\n\n- a\n- b\n',
    });
  });

  it('a read failure leaves what the user typed and raises no error', async () => {
    const client = makeClient({
      get: vi.fn(async () => {
        throw new Error('offline');
      }),
    });
    const p = useAiPersonalization({ client });
    p.draft.value = { ...p.draft.value, content: 'typed before load returned' };

    await p.load();

    expect(p.draft.value.content).toBe('typed before load returned');
    expect(p.error.value).toBeNull();
    expect(p.loading.value).toBe(false);
  });

  it('a write failure surfaces and does not mark the draft saved', async () => {
    /* The inverse of the read rule: reporting success on a rejected save tells
       the user the assistant now knows something it does not. */
    const client = makeClient({
      put: vi.fn(async () => ({ succeeded: false, message: 'Forbidden' })),
    });
    const p = useAiPersonalization({ client });
    p.draft.value = { ...p.draft.value, content: 'new' };

    expect(await p.save()).toBe(false);
    expect(p.error.value).toBe('Forbidden');
    expect(p.dirty.value).toBe(true);
    expect(p.saving.value).toBe(false);
  });

  it('a thrown write also surfaces', async () => {
    const client = makeClient({
      put: vi.fn(async () => {
        throw new Error('boom');
      }),
    });
    const p = useAiPersonalization({ client });
    p.draft.value = { ...p.draft.value, role: 'x' };

    expect(await p.save()).toBe(false);
    expect(p.error.value).toBe('boom');
  });

  it('tracks dirty and reset restores the last saved values', async () => {
    const client = makeClient({ get: vi.fn(async () => ({ succeeded: true, data: PROFILE })) });
    const p = useAiPersonalization({ client });
    await p.load();

    p.draft.value = { ...p.draft.value, role: 'Designer' };
    expect(p.dirty.value).toBe(true);

    p.reset();
    expect(p.draft.value.role).toBe('Engineer');
    expect(p.dirty.value).toBe(false);
  });

  it('a successful save becomes the new baseline', async () => {
    const p = useAiPersonalization({ client: makeClient() });
    p.draft.value = { ...p.draft.value, displayName: 'Ada' };

    expect(await p.save()).toBe(true);
    expect(p.dirty.value).toBe(false);
    expect(p.error.value).toBeNull();
  });
});
