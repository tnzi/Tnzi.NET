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

describe('useChatModels efforts', () => {
  const WITH_EFFORTS = [
    { id: 'opus', displayName: 'Opus', efforts: ['low', 'medium', 'high', 'xhigh', 'max'], defaultEffort: 'medium' },
    { id: 'sonnet', displayName: 'Sonnet', efforts: ['low', 'medium', 'high'], defaultEffort: 'high' },
    { id: 'haiku', displayName: 'Haiku', efforts: [] },
  ];
  const load = (agentId = 'a1') =>
    inScope(() => useChatModels({ client: client({ models: WITH_EFFORTS, defaultModel: 'opus' }) as never, agentId: () => agentId }));

  it("starts on the selected model's default effort", async () => {
    const m = load();
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));

    expect(m.efforts.value).toEqual(['low', 'medium', 'high', 'xhigh', 'max']);
    expect(m.effort.value).toBe('medium');
    expect(m.defaultEffort.value).toBe('medium');
    expect(m.canChooseEffort.value).toBe(true);
  });

  it('remembers the pick per model, and a model without that level lands on its own default', async () => {
    const m = load();
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    m.selectEffort('xhigh');
    expect(m.effort.value).toBe('xhigh');

    m.select('sonnet');
    expect(m.effort.value).toBe('high', 'Sonnet has no xhigh: its default');
    m.selectEffort('low');

    m.select('opus');
    expect(m.effort.value).toBe('xhigh', "Opus keeps the user's last pick for Opus");
    m.select('sonnet');
    expect(m.effort.value).toBe('low');
    scope?.stop();

    const again = load();
    await vi.waitFor(() => expect(again.models.value).toHaveLength(3));
    expect(again.selected.value).toBe('sonnet');
    expect(again.effort.value).toBe('low');
    again.select('opus');
    expect(again.effort.value).toBe('xhigh');
  });

  it('a model without efforts carries none and offers no choice', async () => {
    const m = load();
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    m.select('haiku');

    expect(m.effort.value).toBeNull();
    expect(m.canChooseEffort.value).toBe(false);
    m.selectEffort('low');
    expect(m.effort.value).toBeNull();
  });

  it('ignores an effort the model does not offer, and a remembered one it no longer offers', async () => {
    localStorage.setItem('tnzi-ui-ai-model-effort:a1:sonnet', 'max');
    const m = load();
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    m.selectEffort('turbo');
    expect(m.effort.value).toBe('medium');

    m.select('sonnet');
    expect(m.effort.value).toBe('high');
  });

  it('efforts are remembered per agent', async () => {
    const m = load('a1');
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    m.selectEffort('low');
    scope?.stop();

    const other = load('a2');
    await vi.waitFor(() => expect(other.models.value).toHaveLength(3));
    expect(other.effort.value).toBe('medium');
  });
});

describe('useChatModels per conversation and per model', () => {
  const WITH_EFFORTS = [
    { id: 'opus', displayName: 'Opus', efforts: ['low', 'medium', 'high', 'xhigh', 'max'], defaultEffort: 'medium' },
    { id: 'sonnet', displayName: 'Sonnet', efforts: ['low', 'medium', 'high'], defaultEffort: 'high' },
  ];

  it('asks for the conversation it is in, and reads the list again when it changes', async () => {
    const http = client({ models: WITH_EFFORTS, defaultModel: 'opus' });
    const thread = ref<string | null>('t1');
    const m = inScope(() => useChatModels({ client: http as never, agentId: () => 'a1', threadId: () => thread.value }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(2));
    expect(http.get).toHaveBeenLastCalledWith('/chat/models', { params: { agentId: 'a1', threadId: 't1' } });

    thread.value = 't2';
    await nextTick();
    await vi.waitFor(() => expect(http.get).toHaveBeenCalledTimes(2));
    expect(http.get).toHaveBeenLastCalledWith('/chat/models', { params: { agentId: 'a1', threadId: 't2' } });
  });

  it("gives the remembered effort of a model that is not the selected one, else that model's default", async () => {
    const m = inScope(() => useChatModels({ client: client({ models: WITH_EFFORTS, defaultModel: 'opus' }) as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(2));

    expect(m.rememberedEffort('sonnet')).toBe('high');
    m.selectEffort('low', 'sonnet');
    expect(m.rememberedEffort('sonnet')).toBe('low');
    expect(m.selected.value).toBe('opus');
    expect(m.effort.value).toBe('medium', 'picking for Sonnet leaves the selected model alone');

    m.selectEffort('max', 'sonnet');
    expect(m.rememberedEffort('sonnet')).toBe('low', 'Sonnet does not offer max');
    scope?.stop();

    const again = inScope(() => useChatModels({ client: client({ models: WITH_EFFORTS, defaultModel: 'opus' }) as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(again.models.value).toHaveLength(2));
    expect(again.rememberedEffort('sonnet')).toBe('low');
  });

  it('a model outside the list gives what was remembered for it, or null', async () => {
    localStorage.setItem('tnzi-ui-ai-model-effort:a1:kimi-k2', 'on');
    const m = inScope(() => useChatModels({ client: client({ models: WITH_EFFORTS, defaultModel: 'opus' }) as never, agentId: () => 'a1' }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(2));

    expect(m.rememberedEffort('kimi-k2')).toBe('on');
    expect(m.rememberedEffort('gpt-5')).toBeNull();
  });
});

describe('useChatModels with the run selection', () => {
  it('sends the metadata the next turn carries, and reads the list again when its content changes', async () => {
    const http = client({ models: MODELS, defaultModel: 'claude-opus-5-5' });
    const profile = ref('kimi');
    const m = inScope(() => useChatModels({
      client: http as never,
      agentId: () => 'a1',
      metadata: () => ({ cliProfile: profile.value }),
    }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    expect(http.get).toHaveBeenLastCalledWith('/chat/models', { params: { agentId: 'a1', metadata: '{"cliProfile":"kimi"}' } });

    profile.value = 'kimi';
    await nextTick();
    expect(http.get).toHaveBeenCalledTimes(1);

    profile.value = 'codex';
    await vi.waitFor(() => expect(http.get).toHaveBeenCalledTimes(2));
    expect(http.get).toHaveBeenLastCalledWith('/chat/models', { params: { agentId: 'a1', metadata: '{"cliProfile":"codex"}' } });
  });

  it('empty metadata is not sent', async () => {
    const http = client();
    const m = inScope(() => useChatModels({ client: http as never, agentId: () => 'a1', metadata: () => ({}) }));
    await vi.waitFor(() => expect(m.models.value).toHaveLength(3));
    expect(http.get).toHaveBeenCalledWith('/chat/models', { params: { agentId: 'a1' } });
  });
});
