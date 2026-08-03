/**
 * "The super admin themes the product for everyone" - client side.
 *
 * Mirrors `@tnzi/ui-admin`'s `useGlobalTheme` deliberately: same load / save /
 * reset / isDirty shape, same fail-safe posture. The two products are separate,
 * the mechanism should not be.
 *
 * ## Fail-safe by design
 *
 * No client, a backend without the appearance endpoints, a network error, a
 * 403 - every one of them degrades to "keep rendering local defaults". The
 * theme is decoration; a deployment that cannot reach the endpoint must still
 * show a usable product rather than a blank screen.
 *
 * The one place that does NOT swallow failures is `save()`: silently reporting
 * success when a 403 came back would tell the operator their change reached
 * every user when it reached nobody.
 */
import { ref, computed, shallowRef, type Ref, type ComputedRef } from 'vue';
import {
  useAppearanceApi,
  useAdminAppearanceApi,
  type GlobalThemeSnapshotDto,
} from '@tnzi/core/services/system';
import type { HttpClient } from '@tnzi/core/http';
import {
  applyAiThemeSnapshot,
  isValidAiThemeSnapshot,
  type AiThemeSnapshot,
} from '../theme/snapshot';

/** The conversational product's scope name. */
export const AI_THEME_SCOPE = 'chat';

export interface UseGlobalAiThemeOptions {
  /** Wired HttpClient. Without one every operation is a no-op. */
  client?: HttpClient | null;
  /** Scope to read / write. Defaults to `'chat'`. */
  scope?: string;
  /**
   * Whether the signed-in user may publish. Drives `canManage` only - the
   * backend's `system.appearance.update` is the real wall.
   */
  canManage?: () => boolean;
  /** Apply the loaded snapshot to the document. Default true. */
  autoApply?: boolean;
}

export interface UseGlobalAiThemeReturn {
  /** The last snapshot loaded from or saved to the server. */
  remote: Ref<AiThemeSnapshot | null>;
  /** Local working copy - what the editor mutates. */
  draft: Ref<AiThemeSnapshot | null>;
  loading: Ref<boolean>;
  saving: Ref<boolean>;
  /** Last failure, user-facing. Cleared on the next attempt. */
  error: Ref<string | null>;
  /** Whether the draft differs from what the server holds. */
  isDirty: ComputedRef<boolean>;
  /** Whether publishing is offered at all. */
  canManage: ComputedRef<boolean>;
  /** Fetch and (by default) apply. Never throws. */
  load: () => Promise<void>;
  /** Publish the draft for every user in this scope. Resolves false on failure. */
  save: () => Promise<boolean>;
  /** Clear the server-side snapshot. Resolves false on failure. */
  reset: () => Promise<boolean>;
  /** Replace the draft (editor binding). */
  setDraft: (snapshot: AiThemeSnapshot | null) => void;
}

function sameSnapshot(a: AiThemeSnapshot | null, b: AiThemeSnapshot | null): boolean {
  // `exportedAt` is stamped at build time, so comparing whole documents would
  // report a difference for two identical themes built a second apart.
  const strip = (s: AiThemeSnapshot | null) =>
    s ? JSON.stringify({ ui: s.ui ?? {}, ai: s.ai ?? {}, mode: s.mode }) : null;
  return strip(a) === strip(b);
}

export function useGlobalAiTheme(options: UseGlobalAiThemeOptions = {}): UseGlobalAiThemeReturn {
  const scope = options.scope ?? AI_THEME_SCOPE;
  const autoApply = options.autoApply !== false;

  const remote = shallowRef<AiThemeSnapshot | null>(null);
  const draft = shallowRef<AiThemeSnapshot | null>(null);
  const loading = ref(false);
  const saving = ref(false);
  const error = ref<string | null>(null);

  const isDirty = computed(() => !sameSnapshot(draft.value, remote.value));
  const canManage = computed(() => Boolean(options.client) && (options.canManage?.() ?? false));

  function readSnapshot(dto: GlobalThemeSnapshotDto | undefined | null): AiThemeSnapshot | null {
    const theme = dto?.theme;
    // A snapshot written by a newer/older client is treated as "unset" rather
    // than applied blind - half-understood tokens would render a broken theme
    // that looks like a product bug.
    return theme && isValidAiThemeSnapshot(theme) ? (theme as AiThemeSnapshot) : null;
  }

  async function load(): Promise<void> {
    if (!options.client) return;
    loading.value = true;
    error.value = null;
    try {
      const api = useAppearanceApi(options.client);
      const res = await api.getTheme(scope);
      const snapshot = readSnapshot(res?.data);
      remote.value = snapshot;
      draft.value = snapshot;
      if (autoApply) applyAiThemeSnapshot(snapshot);
    } catch {
      // Decoration must never take the product down with it.
    } finally {
      loading.value = false;
    }
  }

  async function save(): Promise<boolean> {
    if (!options.client || !draft.value) return false;
    saving.value = true;
    error.value = null;
    try {
      const api = useAdminAppearanceApi(options.client);
      const res = await api.saveTheme(scope, {
        theme: draft.value as unknown as Record<string, unknown>,
      });
      // A failure envelope resolves rather than throws, so "did it save" has to
      // be read off the envelope - not off the absence of an exception.
      if (res && res.succeeded === false) {
        error.value = res.message ?? 'Failed to publish the theme';
        return false;
      }
      remote.value = draft.value;
      if (autoApply) applyAiThemeSnapshot(draft.value);
      return true;
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to publish the theme';
      return false;
    } finally {
      saving.value = false;
    }
  }

  async function reset(): Promise<boolean> {
    if (!options.client) return false;
    saving.value = true;
    error.value = null;
    try {
      const api = useAdminAppearanceApi(options.client);
      const res = await api.resetTheme(scope);
      if (res && res.succeeded === false) {
        error.value = res.message ?? 'Failed to reset the theme';
        return false;
      }
      remote.value = null;
      draft.value = null;
      if (autoApply) applyAiThemeSnapshot(null);
      return true;
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to reset the theme';
      return false;
    } finally {
      saving.value = false;
    }
  }

  function setDraft(snapshot: AiThemeSnapshot | null): void {
    draft.value = snapshot;
  }

  return { remote, draft, loading, saving, error, isDirty, canManage, load, save, reset, setDraft };
}
