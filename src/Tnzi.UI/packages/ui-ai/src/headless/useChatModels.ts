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
 */
import { ref, computed, watch, type Ref, type ComputedRef } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import { useChatModelApi } from '@tnzi/core/services/ai';
import type { ChatModelDto } from '@tnzi/core/services/ai';

export interface UseChatModelsOptions {
  client?: HttpClient | null;
  /** The conversation's agent, read reactively (a ref or computed); the list is re-read when it changes. */
  agentId?: () => string | undefined | null;
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
  load: () => Promise<void>;
}

const DEFAULT_STORAGE_KEY = 'tnzi-ui-ai-model';

export function useChatModels(options: UseChatModelsOptions = {}): UseChatModelsReturn {
  const client = options.client ?? null;
  const api = client ? useChatModelApi(client) : null;
  const storagePrefix = options.storageKey ?? DEFAULT_STORAGE_KEY;
  const readAgent = () => options.agentId?.() || null;

  const models = ref<ChatModelDto[]>([]);
  const defaultModel = ref<string | null>(null);
  const picked = ref<string | null>(null);
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

  const isAllowed = (id: string | null | undefined): id is string => !!id && models.value.some((m) => m.id === id);

  const selected = computed<string | null>(() => {
    if (isAllowed(picked.value)) return picked.value;
    if (isAllowed(defaultModel.value)) return defaultModel.value;
    return models.value[0]?.id ?? null;
  });

  async function load(): Promise<void> {
    if (!api) return;
    const mine = ++seq;
    const agentId = readAgent();
    loading.value = true;
    try {
      const result = await api.getModels(agentId);
      if (mine !== seq) return;
      const data = result?.succeeded ? result.data : null;
      models.value = data?.models ?? [];
      defaultModel.value = data?.defaultModel ?? null;
      picked.value = readRemembered(agentId);
    } catch {
      if (mine !== seq) return;
      // No list means the agent's own model, which is what sending without one does.
      models.value = [];
      defaultModel.value = null;
    } finally {
      if (mine === seq) loading.value = false;
    }
  }

  function select(modelId: string): void {
    if (!isAllowed(modelId)) return;
    picked.value = modelId;
    remember(readAgent(), modelId);
  }

  watch(readAgent, () => { void load(); }, { immediate: true });

  return {
    models,
    defaultModel,
    selected,
    canChoose: computed(() => models.value.length > 1),
    loading,
    select,
    load,
  };
}
