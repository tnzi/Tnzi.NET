import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import { useChatModels } from '../../src/headless/useChatModels';

const MODELS = [
  { id: 'claude-opus-5-5', displayName: 'Opus 5.5', provider: 'claude' },
  { id: 'claude-sonnet-5-5', displayName: 'Sonnet 5.5', provider: 'claude' },
  { id: 'claude-haiku-5-5', displayName: 'Haiku 5.5', provider: 'claude' },
];

function client(data: unknown = { models: MODELS, defaultModel: 'claude-sonnet-5-5' }) {
  return { get: vi.fn(async () => ({ succeeded: true, data })) };
}

let scope: ReturnType<typeof effectScope> | null = null;
function inScope<T>(fn: () => T): T {
  scope = effectScope();
  return scope.run(fn)!;
}

beforeEach(() => localStorage.clear());
afterEach(() => {
  scope?.stop();
  scope = null;
});

describe('useChatModels', () => {
  it('reads the models for the agent and starts on the default', async () => {
    const http = client();
    const m = inScope(() => useChatModels({ client: http as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));

    expect(http.get).toHaveBeenCalledWith('/chat/models', { params: { agentId: 'a1' } });
    expect(m.selected.value).toBe('claude-sonnet-5-5');
    expect(m.canChoose.value).toBe(true);
  });

  it('remembers the pick per agent', async () => {
    const m = inScope(() => useChatModels({ client: client() as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    m.select('claude-opus-5-5');
    expect(m.selected.value).toBe('claude-opus-5-5');
    scope?.stop();

    const again = inScope(() => useChatModels({ client: client() as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(again.selected.value).toBe('claude-opus-5-5'));
  });

  it('a remembered model that is no longer allowed falls back to the default', async () => {
    localStorage.setItem('tnzi-ui-ai-model:a1', 'claude-opus-5-5');
    const m = inScope(() => useChatModels({
      client: client({ models: MODELS.slice(1), defaultModel: 'claude-haiku-5-5' }) as never,
      agentId: () => 'a1',
    }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(2));
    expect(m.selected.value).toBe('claude-haiku-5-5');
  });

  it('ignores a pick outside the list', async () => {
    const m = inScope(() => useChatModels({ client: client() as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    m.select('gpt-5');
    expect(m.selected.value).toBe('claude-sonnet-5-5');
  });

  it('a single model offers no choice; no models means the agent decides', async () => {
    const one = inScope(() => useChatModels({ client: client({ models: [MODELS[0]], defaultModel: null }) as never }));
    await vi.waitFor(() => expect(one.models.value).toHaveLength(1));
    expect(one.canChoose.value).toBe(false);
    expect(one.selected.value).toBe('claude-opus-5-5');
    scope?.stop();

    const none = inScope(() => useChatModels({ client: client({ models: [], defaultModel: null }) as never }));
    await vi.waitFor(() => expect(none.loading.value).toBe(false));
    expect(none.selected.value).toBeNull();
  });

  it('re-reads when the agent changes', async () => {
    const agent = ref('a1');
    const http = client();
    inScope(() => useChatModels({ client: http as never, agentId: () => agent.value }));
    await vi.waitFor(() => expect(http.get).toHaveBeenCalledTimes(1));
    agent.value = 'a2';
    await nextTick();
    await vi.waitFor(() => expect(http.get).toHaveBeenCalledTimes(2));
    expect(http.get).toHaveBeenLastCalledWith('/chat/models', { params: { agentId: 'a2' } });
  });
});
