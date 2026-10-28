/**
 * useSkillBrowser - Skill browsing, search, and activation
 *
 * Built for `useSkillApi(client)` from `@tnzi/core/services/ai` (the user-facing
 * `/skills` endpoints), and typed against it: pass that object straight in.
 *
 * ## What "active" means here
 *
 * Activating a skill renders its prompt template with the given parameters
 * (`POST /skills/{slug}/activate`) and hands the rendered prompt back; the
 * server keeps no "this skill is on" record. So `isActive` is this browser's
 * own state - the skills activated in this session - and `deactivate` only
 * clears it. There is nothing to undo on the server, and in particular no call
 * to `DELETE /skills/{id}`: that endpoint deletes the skill itself.
 */

import { ref, readonly, type Ref, type DeepReadonly } from 'vue';
import type { ApiResult } from '@tnzi/core/types';
import {
  SkillScope,
  type SkillActivateDto,
  type SkillActivationResultDto,
  type SkillSummaryDto,
} from '@tnzi/core/services/ai';

export interface BrowsableSkill {
  id: string;
  slug: string;
  name: string;
  description?: string;
  icon?: string;
  category?: string;
  /** Activated in this session (see the module note: the server keeps no such state). */
  isActive: boolean;
  /** Not one of the user's own editable skills: system / tenant scope, or a file-backed read-only row. */
  isBuiltIn: boolean;
}

export interface SkillCategory {
  id: string;
  name: string;
  children?: SkillCategory[];
}

/** The slice of `useSkillApi(client)` this composable calls. */
export interface SkillBrowserApi {
  getAvailable: () => Promise<ApiResult<SkillSummaryDto[]>>;
  search: (query: string, maxResults?: number) => Promise<ApiResult<SkillSummaryDto[]>>;
  activate: (slug: string, data?: SkillActivateDto) => Promise<ApiResult<SkillActivationResultDto>>;
}

export interface UseSkillBrowserReturn {
  skills: DeepReadonly<Ref<readonly BrowsableSkill[]>>;
  categories: DeepReadonly<Ref<readonly SkillCategory[]>>;
  isLoading: DeepReadonly<Ref<boolean>>;
  error: DeepReadonly<Ref<string | null>>;
  loadSkills: () => Promise<void>;
  loadCategories: () => Promise<void>;
  search: (query: string) => Promise<void>;
  /**
   * Render the skill's prompt with `params` and mark it active. Resolves to the
   * rendered result, or `null` when the server refused (the reason is in `error`).
   */
  activate: (slug: string, params?: Record<string, string>) => Promise<SkillActivationResultDto | null>;
  /** Clear the active mark. Local only: activation has no server-side state. */
  deactivate: (slug: string) => Promise<void>;
}

export function useSkillBrowser(
  skillApi: SkillBrowserApi,
  categoryApi?: {
    getTree: () => Promise<{ data?: SkillCategory[] }>;
  },
): UseSkillBrowserReturn {
  const skills = ref<readonly BrowsableSkill[]>([]);
  const categories = ref<readonly SkillCategory[]>([]);
  const isLoading = ref(false);
  const error = ref<string | null>(null);
  /** Slugs activated in this session; survives a reload or a search of the list. */
  const activeSlugs = new Set<string>();

  function toBrowsable(row: SkillSummaryDto): BrowsableSkill {
    return {
      id: row.id,
      slug: row.slug,
      name: row.name,
      description: row.description ?? undefined,
      category: row.categoryName ?? undefined,
      isActive: activeSlugs.has(row.slug),
      isBuiltIn: row.isReadOnly === true || row.scope !== SkillScope.User,
    };
  }

  /** Apply a list envelope: a refusal resolves rather than throws, so it is read off the envelope. */
  function applyList(res: ApiResult<SkillSummaryDto[]>, fallback: string): void {
    if (!res.succeeded) {
      error.value = res.message || fallback;
      return;
    }
    skills.value = (res.data ?? []).map(toBrowsable);
  }

  async function loadSkills(): Promise<void> {
    isLoading.value = true;
    error.value = null;
    try {
      applyList(await skillApi.getAvailable(), 'Failed to load skills');
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load skills';
    } finally {
      isLoading.value = false;
    }
  }

  async function loadCategories(): Promise<void> {
    if (!categoryApi) return;
    try {
      const res = await categoryApi.getTree();
      categories.value = res.data ?? [];
    } catch {
      // Categories are optional
    }
  }

  async function searchSkills(query: string): Promise<void> {
    isLoading.value = true;
    error.value = null;
    try {
      applyList(await skillApi.search(query), 'Search failed');
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Search failed';
    } finally {
      isLoading.value = false;
    }
  }

  function markActive(slug: string, isActive: boolean): void {
    if (isActive) activeSlugs.add(slug);
    else activeSlugs.delete(slug);
    skills.value = skills.value.map((s) => (s.slug === slug ? { ...s, isActive } : s));
  }

  async function activate(slug: string, params?: Record<string, string>): Promise<SkillActivationResultDto | null> {
    error.value = null;
    const res = await skillApi.activate(slug, { parameters: params ?? null });
    if (!res.succeeded || !res.data) {
      error.value = res.message || 'Failed to activate the skill';
      return null;
    }
    markActive(slug, true);
    return res.data;
  }

  async function deactivate(slug: string): Promise<void> {
    markActive(slug, false);
  }

  return {
    skills: readonly(skills) as DeepReadonly<Ref<readonly BrowsableSkill[]>>,
    categories: readonly(categories) as DeepReadonly<Ref<readonly SkillCategory[]>>,
    isLoading: readonly(isLoading),
    error: readonly(error),
    loadSkills,
    loadCategories,
    search: searchSkills,
    activate,
    deactivate,
  };
}
