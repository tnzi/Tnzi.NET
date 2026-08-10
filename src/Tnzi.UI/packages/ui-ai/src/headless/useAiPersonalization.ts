/**
 * The signed-in user's AI profile - what the assistant should know about them
 * before the first message of every conversation.
 *
 * Backed by the framework's own `GET/PUT /user-profile` (`Tnzi.AI`'s
 * `DefaultUserProfileController`), which is a **user-facing** route: unlike the
 * agent and memory surfaces, an ordinary signed-in user can read and write
 * their own profile, so this can be a built-in settings page rather than
 * something only an operator sees.
 *
 * Transport lives here rather than in the component for the same reason it does
 * in `useChatThreads`: Critical Rule #8 keeps I/O out of *components*, and a
 * product built against the framework's own AI module should not have to
 * re-derive this wiring. A product on a different backend simply does not pass
 * a client and the page hides itself.
 */
import { ref, computed, type Ref, type ComputedRef } from 'vue';
import type { HttpClient } from '@tnzi/core/http';
import { useUserProfileApi } from '@tnzi/core/services/ai';
import type { UserProfileDto, UpdateUserProfileDto } from '@tnzi/core/services/ai';

export interface UseAiPersonalizationOptions {
  /** Omit or pass null on a deployment without the AI module. */
  client?: HttpClient | null;
}

/** Editable fields, mirrored locally so the form is not bound to server state. */
export interface AiPersonalizationDraft {
  displayName: string;
  role: string;
  preferredLanguage: string;
  content: string;
}

export interface UseAiPersonalizationReturn {
  readonly draft: Ref<AiPersonalizationDraft>;
  readonly loading: Ref<boolean>;
  readonly saving: Ref<boolean>;
  /** Last write error. Reads never set it - see `load`. */
  readonly error: Ref<string | null>;
  /** False when no client was supplied; the page should not render. */
  readonly available: ComputedRef<boolean>;
  readonly dirty: ComputedRef<boolean>;
  load: () => Promise<void>;
  save: () => Promise<boolean>;
  reset: () => void;
}

const EMPTY: AiPersonalizationDraft = {
  displayName: '',
  role: '',
  preferredLanguage: '',
  content: '',
};

function toDraft(dto: UserProfileDto | null): AiPersonalizationDraft {
  if (!dto) return { ...EMPTY };
  return {
    displayName: dto.displayName ?? '',
    role: dto.role ?? '',
    preferredLanguage: dto.preferredLanguage ?? '',
    content: dto.content ?? '',
  };
}

/* Empty means "not set", and the API's fields are nullable, so blanks go up as
   null rather than "". An empty string is a value: it would overwrite a real
   preferred language with a blank one instead of clearing it. */
function toPayload(draft: AiPersonalizationDraft): UpdateUserProfileDto {
  const orNull = (v: string) => (v.trim() === '' ? null : v.trim());
  return {
    displayName: orNull(draft.displayName),
    role: orNull(draft.role),
    preferredLanguage: orNull(draft.preferredLanguage),
    /* Not trimmed: this is prose the user wrote, and trailing structure
       (a blank line before a list) is theirs to keep. */
    content: draft.content === '' ? null : draft.content,
  };
}

export function useAiPersonalization(
  options: UseAiPersonalizationOptions = {},
): UseAiPersonalizationReturn {
  const client = options.client ?? null;
  const api = client ? useUserProfileApi(client) : null;

  const draft = ref<AiPersonalizationDraft>({ ...EMPTY });
  const saved = ref<AiPersonalizationDraft>({ ...EMPTY });
  const loading = ref(false);
  const saving = ref(false);
  const error = ref<string | null>(null);

  const available = computed(() => api !== null);
  const dirty = computed(
    () =>
      draft.value.displayName !== saved.value.displayName ||
      draft.value.role !== saved.value.role ||
      draft.value.preferredLanguage !== saved.value.preferredLanguage ||
      draft.value.content !== saved.value.content,
  );

  /* Fail-safe, like every other read in this package's settings surfaces: an
     older backend without the route, or a network blip, leaves the user on an
     empty form they can still fill in - it does not surface an error banner on
     a page they merely opened. */
  async function load(): Promise<void> {
    if (!api) return;
    loading.value = true;
    try {
      const result = await api.get();
      const next = toDraft(result?.data ?? null);
      draft.value = { ...next };
      saved.value = { ...next };
    } catch {
      /* keep whatever the user already typed */
    } finally {
      loading.value = false;
    }
  }

  /* Writes do NOT swallow: reporting success on a rejected save tells the user
     the assistant now knows something it does not. */
  async function save(): Promise<boolean> {
    if (!api) return false;
    saving.value = true;
    error.value = null;
    try {
      const result = await api.update(toPayload(draft.value));
      if (result && result.succeeded === false) {
        error.value = result.message || 'Could not save';
        return false;
      }
      saved.value = { ...draft.value };
      return true;
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Could not save';
      return false;
    } finally {
      saving.value = false;
    }
  }

  function reset(): void {
    draft.value = { ...saved.value };
    error.value = null;
  }

  return { draft, loading, saving, error, available, dirty, load, save, reset };
}
