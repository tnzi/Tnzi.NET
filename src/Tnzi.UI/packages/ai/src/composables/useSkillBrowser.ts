/**
 * useSkillBrowser — Skill browsing, search, and activation
 */

import { ref, readonly, type Ref, type DeepReadonly } from 'vue';

export interface BrowsableSkill {
  id: string;
  slug: string;
  name: string;
  description?: string;
  icon?: string;
  category?: string;
  isActive: boolean;
  isBuiltIn: boolean;
}

export interface SkillCategory {
  id: string;
  name: string;
  children?: SkillCategory[];
}

export interface UseSkillBrowserReturn {
  skills: DeepReadonly<Ref<readonly BrowsableSkill[]>>;
  categories: DeepReadonly<Ref<readonly SkillCategory[]>>;
  isLoading: DeepReadonly<Ref<boolean>>;
  error: DeepReadonly<Ref<string | null>>;
  loadSkills: () => Promise<void>;
  loadCategories: () => Promise<void>;
  search: (query: string) => Promise<void>;
  activate: (slug: string, params?: Record<string, string>) => Promise<void>;
  deactivate: (slug: string) => Promise<void>;
}

export function useSkillBrowser(
  skillApi: {
    getAvailable: () => Promise<{ data?: BrowsableSkill[] }>;
    search: (query: string, maxResults?: number) => Promise<{ data?: BrowsableSkill[] }>;
    activate: (slug: string, data?: Record<string, string>) => Promise<unknown>;
    delete: (id: string) => Promise<unknown>;
  },
  categoryApi?: {
    getTree: () => Promise<{ data?: SkillCategory[] }>;
  },
): UseSkillBrowserReturn {
  const skills = ref<readonly BrowsableSkill[]>([]);
  const categories = ref<readonly SkillCategory[]>([]);
  const isLoading = ref(false);
  const error = ref<string | null>(null);

  async function loadSkills(): Promise<void> {
    isLoading.value = true;
    error.value = null;
    try {
      const res = await skillApi.getAvailable();
      skills.value = res.data ?? [];
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
    try {
      const res = await skillApi.search(query);
      skills.value = res.data ?? [];
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Search failed';
    } finally {
      isLoading.value = false;
    }
  }

  async function activate(slug: string, params?: Record<string, string>): Promise<void> {
    await skillApi.activate(slug, params);
    skills.value = skills.value.map((s) =>
      s.slug === slug ? { ...s, isActive: true } : s,
    );
  }

  async function deactivate(slug: string): Promise<void> {
    const skill = skills.value.find((s) => s.slug === slug);
    if (skill) {
      await skillApi.delete(skill.id);
      skills.value = skills.value.map((s) =>
        s.slug === slug ? { ...s, isActive: false } : s,
      );
    }
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
