<template>
  <NTag
    :type="config.type"
    size="small"
    :bordered="false"
    class="t-source-badge"
    :title="resolvedTitle"
  >
    <template #icon>
      <TSvgIcon :icon="config.icon" :size="12" />
    </template>
    {{ resolvedLabel }}
  </NTag>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NTag } from 'naive-ui'
import TSvgIcon from './TSvgIcon.vue'

/**
 * `TSourceBadge` — visual marker for entity-source provenance.
 *
 * Used by dual-source modules (Skills, Template, Feature, Workspace
 * Agent/Persona, …) where a list row may originate from the database,
 * the file system, embedded resources, code-level definitions, the
 * workspace, or appsettings configuration.
 *
 * The badge encodes the source in three layers:
 *   - colour family (NTag type) — semantic hint (db=info, file=warning…)
 *   - mdi icon — quick visual recognition
 *   - text label — human-readable, i18n-resolved
 *
 * Consumers pass either the canonical English source key (`'database' |
 * 'filesystem' | 'embedded' | 'code' | 'workspace' | 'config'`) or the
 * raw backend enum. Since the backend registers `JsonStringEnumConverter`
 * globally, source enums now serialise as their PascalCase MEMBER NAME
 * (`"FileSystem"`, `"Database"`, `"Plugin"`, ...); the component lower-cases +
 * strips separators so those match the canonical keys. A raw numeric ordinal is
 * still accepted for backward compatibility (see {@link SKILL_SOURCE_NUMERIC}).
 * The `translate` prop resolves `admin.shared.source.<key>` i18n; the fallback
 * uses a built-in English string so the badge keeps reading even on hosts
 * without the i18n keys installed.
 */
export type SourceKind =
  | 'database'
  | 'filesystem'
  | 'embedded'
  | 'plugin'
  | 'managed'
  | 'project'
  | 'code'
  | 'workspace'
  | 'config'

interface Props {
  /**
   * Source key — accepts string (backend convention) or number (enum value
   * serialised as numeric, like ASP.NET Core's default System.Text.Json
   * config). The `numericMap` below pairs raw enum values with their
   * canonical kind so callers don't have to pre-normalise.
   */
  value: string | number | SourceKind | null | undefined
  /** Custom label override (rare — usually let the badge derive it). */
  label?: string
  /** Page-scoped translate helper. */
  translate?: (key: string) => string
}

const props = defineProps<Props>()

// SkillSource enum (Tnzi.AI/Skills/Models/SkillDefinition.cs) — LEGACY numeric
// compatibility. With JsonStringEnumConverter installed the wire now carries the
// member NAME string (handled by the switch below); this ordinal map only kicks
// in for a raw `0..4` from an older payload so the badge keeps rendering.
const SKILL_SOURCE_NUMERIC: Record<number, SourceKind> = {
  0: 'filesystem',
  1: 'database',
  2: 'plugin',
  3: 'managed',
  4: 'project',
}

const normalized = computed<SourceKind>(() => {
  // Legacy numeric path — interpret as SkillSource enum ordinal.
  if (typeof props.value === 'number') {
    return SKILL_SOURCE_NUMERIC[props.value] ?? 'database'
  }
  // Primary path: the PascalCase member name string (JsonStringEnumConverter),
  // lower-cased + separator-stripped so "FileSystem" → "filesystem" etc.
  const raw = String(props.value ?? '').toLowerCase().replace(/[\s_-]/g, '')
  // A bare numeric string (`"1"`) — legacy ordinal interpretation.
  if (/^\d+$/.test(raw)) {
    return SKILL_SOURCE_NUMERIC[Number(raw)] ?? 'database'
  }
  switch (raw) {
    case 'database':
    case 'db':
      return 'database'
    case 'filesystem':
    case 'file':
      return 'filesystem'
    case 'embedded':
    case 'embeddedresource':
    case 'builtin':
      return 'embedded'
    case 'plugin':
      return 'plugin'
    case 'managed':
      return 'managed'
    case 'project':
      return 'project'
    case 'code':
    case 'codelevel':
      return 'code'
    case 'workspace':
      return 'workspace'
    case 'config':
    case 'configuration':
    case 'appsettings':
      return 'config'
    default:
      return 'database'
  }
})

const config = computed(() => {
  switch (normalized.value) {
    case 'database':
      return { type: 'info' as const, icon: 'mdi:database', fallback: 'Database' }
    case 'filesystem':
      return { type: 'warning' as const, icon: 'mdi:file-document-outline', fallback: 'File' }
    case 'embedded':
      return { type: 'success' as const, icon: 'mdi:package-variant-closed', fallback: 'Built-in' }
    case 'plugin':
      return { type: 'warning' as const, icon: 'mdi:puzzle-outline', fallback: 'Plugin' }
    case 'managed':
      return { type: 'warning' as const, icon: 'mdi:folder-cog-outline', fallback: 'Managed' }
    case 'project':
      return { type: 'warning' as const, icon: 'mdi:folder-outline', fallback: 'Project' }
    case 'code':
      return { type: 'success' as const, icon: 'mdi:code-tags', fallback: 'Code' }
    case 'workspace':
      return { type: 'warning' as const, icon: 'mdi:laptop', fallback: 'Workspace' }
    case 'config':
      return { type: 'warning' as const, icon: 'mdi:tune', fallback: 'Config' }
    default:
      return { type: 'info' as const, icon: 'mdi:database', fallback: 'Database' }
  }
})

const resolvedLabel = computed(() => {
  if (props.label) return props.label
  const key = `admin.shared.source.${normalized.value}`
  if (props.translate) {
    const out = props.translate(key)
    if (out && out !== key) return out
  }
  return config.value.fallback
})

const resolvedTitle = computed(() => resolvedLabel.value)
</script>

<style scoped>
.t-source-badge {
  font-size: 12px;
  font-weight: 500;
}
</style>
