/**
 * The models the signed-in user may pick for the conversation's agent, and the
 * one they picked.
 *
 * Backed by `GET /chat/models` (`Tnzi.AI`'s `DefaultChatModelController`): the
 * models an administrator allowed this user (`AI:Chat:Models` filtered by the
 * user's policy and by the agent's provider), with the default. More than one
 * model is what makes `TChatApp` show its picker; pass `selected` to
 * `useChatThreads({ model })` so every turn carries it.
 *
 * The choice is remembered per agent in localStorage. A remembered model that
 * is no longer allowed falls back to the default rather than being sent: the
 * server would refuse the turn (`AI_MODEL_NOT_ALLOWED`) for a choice the user
 * made weeks ago and cannot see on screen any more.
 *
 * Each model may also offer reasoning efforts (`ChatModelDto.efforts`,
 * runtime-native values). `effort` is the one the next turn carries: the
 * user's last pick for this model (remembered per agent and model), else the
 * model's default; switching to a model that does not offer that pick lands on
 * that model's default. Pass `effort` to `useChatThreads({ effort })`.
 */
import { ref, computed, watch, type Ref, type ComputedRef } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import { useChatModelApi } from '@tnzi/core/services/ai';
import type { ChatModelDto } from '@tnzi/core/services/ai';

export interface UseChatModelsOptions {
  client?: HttpClient | null;
  /** The conversation's agent, read reactively (a ref or computed); the list is re-read when it changes. */
  agentId?: () => string | undefined | null;
  /**
   * The conversation, read reactively; the list is re-read when it changes. Lets an application that says where a
   * conversation really runs (`IChatModelTargetResolver` on the server) list that CLI's models and efforts.
   */
  threadId?: () => string | undefined | null;
  /**
   * The per-turn metadata the page will send with the next turn (`useChatThreads().send(text, files, { metadata })`),
   * read reactively; the list is re-read when its content changes. An application's `IChatModelTargetResolver` answers
   * from it (the user's run selection, a CLI profile picked beside the composer), and the turn is checked against the
   * same metadata, so the efforts shown before the first turn are the ones that turn is checked against. Pass the same
   * object you send.
   */
  metadata?: () => Record<string, unknown> | undefined | null;
  /** localStorage key prefix for the remembered choice; '' to remember nothing. Default `tnzi-ui-ai-model`. */
  storageKey?: string;
}

export interface UseChatModelsReturn {
  /** The models this user may pick. */
  readonly models: Ref<ChatModelDto[]>;
  /** The administrator's default (or null: the agent's own model). */
  readonly defaultModel: Ref<string | null>;
  /** The model the next turn uses: the user's pick, else the default, else the first; null with no models. */
  readonly selected: ComputedRef<string | null>;
  /** More than one model to choose from. */
  readonly canChoose: ComputedRef<boolean>;
  readonly loading: Ref<boolean>;
  /** Pick a model (ignored unless it is one of `models`). */
  select: (modelId: string) => void;
  /** The reasoning efforts the selected model offers (empty: no choice). */
  readonly efforts: ComputedRef<string[]>;
  /** The selected model's default effort, or null. */
  readonly defaultEffort: ComputedRef<string | null>;
  /**
   * The effort the next turn carries: the user's pick for the selected model, else its default; null when the
   * model offers none (or has no default and nothing was picked), and the turn then carries no effort.
   */
  readonly effort: ComputedRef<string | null>;
  /** The selected model offers efforts to choose from. */
  readonly canChooseEffort: ComputedRef<boolean>;
  /**
   * Pick an effort for the selected model, or for `modelId` (ignored unless that model offers it); remembered for that
   * model.
   */
  selectEffort: (effort: string, modelId?: string) => void;
  /**
   * The effort a turn on `modelId` would carry: the user's last pick on that model while the model still offers it,
   * else its default; null when it offers none. For a model shown in place of `selected` (a project's or a session's
   * own model). A model not in `models` gives what was remembered for it, unchecked, or null.
   */
  rememberedEffort: (modelId: string) => string | null;
  load: () => Promise<void>;
}

const DEFAULT_STORAGE_KEY = 'tnzi-ui-ai-model';

export function useChatModels(options: UseChatModelsOptions = {}): UseChatModelsReturn {
  const client = options.client ?? null;
  const api = client ? useChatModelApi(client) : null;
  const storagePrefix = options.storageKey ?? DEFAULT_STORAGE_KEY;
  const readAgent = () => options.agentId?.() || null;
  const readThread = () => options.threadId?.() || null;
  const readMetadata = () => options.metadata?.() ?? null;

  const models = ref<ChatModelDto[]>([]);
  const defaultModel = ref<string | null>(null);
  const picked = ref<string | null>(null);
  /** The user's effort pick per model id (what was remembered plus what was picked on this page). */
  const pickedEfforts = ref<Record<string, string>>({});
  const loading = ref(false);
  let seq = 0;

  const keyFor = (agentId: string | null) => (storagePrefix ? `${storagePrefix}:${agentId ?? ''}` : '');

  function readRemembered(agentId: string | null): string | null {
    const key = keyFor(agentId);
    if (!key || typeof localStorage === 'undefined') return null;
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  function remember(agentId: string | null, modelId: string): void {
    const key = keyFor(agentId);
    if (!key || typeof localStorage === 'undefined') return;
    try {
      localStorage.setItem(key, modelId);
    } catch {
      /* Storage full or blocked: the pick still holds for this page. */
    }
  }

  const effortKeyFor = (agentId: string | null, modelId: string) =>
    (storagePrefix ? `${storagePrefix}-effort:${agentId ?? ''}:${modelId}` : '');

  function readStored(key: string): string | null {
    if (!key || typeof localStorage === 'undefined') return null;
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  function store(key: string, value: string): void {
    if (!key || typeof localStorage === 'undefined') return;
    try {
      localStorage.setItem(key, value);
    } catch {
      /* Storage full or blocked: the pick still holds for this page. */
    }
  }

  function readRememberedEfforts(agentId: string | null, list: ChatModelDto[]): Record<string, string> {
    const remembered: Record<string, string> = {};
    for (const m of list) {
      const value = readStored(effortKeyFor(agentId, m.id));
      if (value) remembered[m.id] = value;
    }
    return remembered;
  }

  const isAllowed = (id: string | null | undefined): id is string => !!id && models.value.some((m) => m.id === id);

  const selected = computed<string | null>(() => {
    if (isAllowed(picked.value)) return picked.value;
    if (isAllowed(defaultModel.value)) return defaultModel.value;
    return models.value[0]?.id ?? null;
  });

  const current = computed(() => models.value.find((m) => m.id === selected.value) ?? null);
  const efforts = computed<string[]>(() => current.value?.efforts ?? []);
  const defaultEffort = computed<string | null>(() => {
    const value = current.value?.defaultEffort ?? null;
    return value && efforts.value.includes(value) ? value : null;
  });
  function effortOn(model: ChatModelDto): string | null {
    const offered = model.efforts ?? [];
    const fallback = model.defaultEffort && offered.includes(model.defaultEffort) ? model.defaultEffort : null;
    const mine = pickedEfforts.value[model.id];
    // A pick the model no longer offers (the administrator narrowed it) falls back to the default.
    return mine && offered.includes(mine) ? mine : fallback;
  }

  const effort = computed<string | null>(() => (current.value ? effortOn(current.value) : null));

  function rememberedEffort(modelId: string): string | null {
    const model = models.value.find((m) => m.id === modelId);
    if (model) return effortOn(model);
    return pickedEfforts.value[modelId] ?? readStored(effortKeyFor(readAgent(), modelId));
  }

  async function load(): Promise<void> {
    if (!api) return;
    const mine = ++seq;
    const agentId = readAgent();
    loading.value = true;
    try {
      const result = await api.getModels(agentId, null, readThread(), readMetadata());
      if (mine !== seq) return;
      const data = result?.succeeded ? result.data : null;
      models.value = data?.models ?? [];
      defaultModel.value = data?.defaultModel ?? null;
      picked.value = readRemembered(agentId);
      pickedEfforts.value = readRememberedEfforts(agentId, models.value);
    } catch {
      if (mine !== seq) return;
      // No list means the agent's own model, which is what sending without one does.
      models.value = [];
      defaultModel.value = null;
      pickedEfforts.value = {};
    } finally {
      if (mine === seq) loading.value = false;
    }
  }

  function select(modelId: string): void {
    if (!isAllowed(modelId)) return;
    picked.value = modelId;
    remember(readAgent(), modelId);
  }

  function selectEffort(value: string, modelId?: string): void {
    const model = modelId ? models.value.find((m) => m.id === modelId) : current.value;
    if (!model || !(model.efforts ?? []).includes(value)) return;
    pickedEfforts.value = { ...pickedEfforts.value, [model.id]: value };
    store(effortKeyFor(readAgent(), model.id), value);
  }

  // The metadata by content: a page that builds a new object per render does not reload the list each time.
  const metadataKey = () => {
    const metadata = readMetadata();
    return metadata && Object.keys(metadata).length > 0 ? JSON.stringify(metadata) : '';
  };
  watch([readAgent, readThread, metadataKey], () => { void load(); }, { immediate: true });

  return {
    models,
    defaultModel,
    selected,
    canChoose: computed(() => models.value.length > 1),
    loading,
    select,
    efforts,
    defaultEffort,
    effort,
    canChooseEffort: computed(() => efforts.value.length > 0),
    selectEffort,
    rememberedEffort,
    load,
  };
}
