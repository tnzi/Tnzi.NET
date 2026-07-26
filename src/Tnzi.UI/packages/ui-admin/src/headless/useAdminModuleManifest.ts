/**
 * `useAdminModuleManifest()` - composable for loading the backend admin
 * manifest (`GET admin/diagnostics/admin-manifest`) and projecting it onto
 * the frontend menu tree.
 *
 * Powers Phase B of the `@tnzi/ui-admin` overhaul: instead of hard-coding
 * which modules to surface, the menu is derived from whichever business
 * modules the backend has loaded - so the same admin app installed against
 * different backends shows different menus automatically.
 *
 * This is an OPT-IN consumer composable (exported from the headless barrel);
 * `defineAdminApp` does not call it - the built-in sidebar derives from the
 * static route table. Pass `basePath` matching your `defineAdminApp` config
 * so generated menu paths line up with the rewritten route table.
 */

import { ref, computed, type Ref, type ComputedRef } from 'vue'
import type { HttpClient } from '@tnzi/core/http'
import {
  fetchAdminManifest,
  type AdminManifest,
  type AdminManifestModule,
  type AdminManifestEntity,
} from '../services/admin-manifest'

export interface AdminMenuNode {
  /** Stable unique key, e.g. `"identity"` or `"identity/users"`. */
  key: string
  /** Display label (consumer typically maps through i18n). */
  label: string
  /** Absolute route path inside the admin app, e.g. `"/admin/identity/users"`. */
  path?: string
  /** i18n key suggestion: `tnzi.admin.modules.{module}.{entity}.title`. */
  i18nKey?: string
  /** Original entity object when this is a leaf. */
  entity?: AdminManifestEntity
  /** Children - modules with multiple entities expose them as children. */
  children?: AdminMenuNode[]
}

export interface UseAdminModuleManifestOptions {
  /** HttpClient used to call the backend. */
  client: HttpClient
  /** Module short names to hide (e.g. `['Payment', 'Audit']`). Case-insensitive. */
  hideModules?: string[]
  /** Module short names to show - when set, anything not listed is hidden. */
  showOnlyModules?: string[]
  /**
   * Skip fetching and use this pre-built manifest. Useful for tests and SSR.
   */
  manifest?: AdminManifest
  /**
   * Route-table prefix the generated menu paths sit under. MUST match the
   * `basePath` passed to `defineAdminApp` (default `'/admin'`); pass `'/'`
   * for a prefix-free route table (the recommended sub-path deployment
   * shape). Never hardcode a deployment prefix into entity routes - this is
   * the single knob.
   */
  basePath?: string
}

export interface UseAdminModuleManifestReturn {
  /** True while the manifest is being fetched. */
  loading: Ref<boolean>
  /** Resolved manifest from the backend, or null when not yet loaded / unavailable. */
  manifest: Ref<AdminManifest | null>
  /** Filtered module list after applying hide/showOnly. */
  modules: ComputedRef<AdminManifestModule[]>
  /** Frontend menu tree derived from `modules`. */
  menuTree: ComputedRef<AdminMenuNode[]>
  /**
   * Re-fetch from backend; safe to call when the user re-logs in or
   * a module is enabled/disabled at runtime.
   */
  refresh: () => Promise<void>
  /**
   * True when the backend endpoint returned a manifest. Consumers can fall
   * back to a static route list when this is false (e.g. for offline / dev).
   */
  isAvailable: ComputedRef<boolean>
}

/**
 * Normalize a name for comparison: lowercase, replace `.` with `-` so
 * `"AI.Skills"` and `"ai.skills"` and `"ai-skills"` all compare equal.
 */
function normalizeName(name: string): string {
  return name.toLowerCase().replace(/\./g, '-')
}

export function useAdminModuleManifest(
  options: UseAdminModuleManifestOptions,
): UseAdminModuleManifestReturn {
  const loading = ref(false)
  const manifest = ref<AdminManifest | null>(options.manifest ?? null)

  const hideSet = new Set((options.hideModules ?? []).map(normalizeName))
  const showOnlySet = options.showOnlyModules
    ? new Set(options.showOnlyModules.map(normalizeName))
    : null

  const modules = computed<AdminManifestModule[]>(() => {
    if (!manifest.value) return []
    return manifest.value.modules.filter((m) => {
      if (!m.isEnabled) return false
      const key = normalizeName(m.name)
      if (showOnlySet && !showOnlySet.has(key)) return false
      if (hideSet.has(key)) return false
      return m.entities.length > 0
    })
  })

  const prefix = normalizePathPrefix(options.basePath)
  const menuTree = computed<AdminMenuNode[]>(() => buildMenuTree(modules.value, prefix))

  async function refresh(): Promise<void> {
    if (options.manifest) {
      // Static manifest mode - nothing to fetch.
      manifest.value = options.manifest
      return
    }
    loading.value = true
    try {
      manifest.value = await fetchAdminManifest(options.client)
    } finally {
      loading.value = false
    }
  }

  const isAvailable = computed(() => manifest.value !== null)

  // Kick off initial load (fire-and-forget; callers can `await refresh()`
  // explicitly when they need to gate UI on the manifest).
  if (!options.manifest) void refresh()

  return { loading, manifest, modules, menuTree, refresh, isAvailable }
}

/**
 * Normalize the menu path prefix the same way `defineAdminApp` normalizes
 * `basePath`: leading slash, no trailing slash, default `'/admin'`. The
 * domain-root sentinel `'/'` collapses to `''` so paths come out as
 * `/identity/users` (no leading `//`).
 */
function normalizePathPrefix(basePath?: string | null): string {
  if (basePath == null) return '/admin'
  let bp = String(basePath).trim()
  if (bp === '') return '/admin'
  if (bp === '/') return ''
  if (!bp.startsWith('/')) bp = '/' + bp
  while (bp.length > 1 && bp.endsWith('/')) bp = bp.slice(0, -1)
  return bp
}

/**
 * Build a 2-level menu tree from the manifest:
 *   Module (label = module name) → Entity (label = entity name)
 *
 * Modules with a single entity collapse into a single leaf at the top level.
 * This keeps menus compact when a module only exposes one admin surface.
 */
function buildMenuTree(modules: AdminManifestModule[], prefix: string): AdminMenuNode[] {
  return modules.map((module) => {
    const moduleKey = normalizeName(module.name)

    if (module.entities.length === 1) {
      const entity = module.entities[0]!
      return {
        key: `${moduleKey}/${entity.name}`,
        label: titleCase(module.name),
        path: `${prefix}/${entity.route.replace(/^admin\//, '')}`,
        i18nKey: `tnzi.admin.modules.${moduleKey}.${slugify(entity.name)}.title`,
        entity,
      }
    }

    return {
      key: moduleKey,
      label: titleCase(module.name),
      i18nKey: `tnzi.admin.modules.${moduleKey}.label`,
      children: module.entities.map((entity) => ({
        key: `${moduleKey}/${entity.name}`,
        label: titleCase(entity.name.split('/').pop() ?? entity.name),
        path: `${prefix}/${entity.route.replace(/^admin\//, '')}`,
        i18nKey: `tnzi.admin.modules.${moduleKey}.${slugify(entity.name)}.title`,
        entity,
      })),
    }
  })
}

function titleCase(input: string): string {
  return input
    .replace(/[._-]/g, ' ')
    .split(/\s+/)
    .filter(Boolean)
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join(' ')
}

function slugify(input: string): string {
  return input.toLowerCase().replace(/[\s/.]+/g, '-')
}
