<script setup lang="ts">
/**
 * `TWidgetActivityTimeline` — widget wrapping TProjectTimeline.
 *
 * Hybrid data model:
 *   - **Props mode** — consumer passes `items: TimelineItem[]` (e.g. a
 *     curated "what's new" list) and the widget renders them as-is.
 *   - **Auto-fetch mode** — consumer omits `items` (default in
 *     `defaultWorkbenchWidgets()`) and the widget pulls the most recent
 *     audit log entries via `audit-bridge.logs.fetch(...)`, translating
 *     each entry into a TimelineItem. The Audit module ships with every
 *     `HostingModule` app, so this gives the bundled Workbench a
 *     genuinely live activity feed instead of canned i18n placeholders.
 */
import { computed, ref } from 'vue'
import TProjectTimeline from '../../components/dashboard/TProjectTimeline.vue'
import type { TimelineItem, TimelineTone } from '../../components/dashboard/TProjectTimeline.vue'
import { useAdminClient } from '../../plugin/client'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { maybeTranslate, translatePageKey } from '../../pages/_shared/translate'

interface Props {
  /**
   * Timeline entries to render. When omitted / empty the widget
   * auto-fetches up to `limit` recent audit log rows.
   */
  items?: TimelineItem[]
  /** How many rows to pull in auto-fetch mode. Default 6. */
  limit?: number
  /** Shown when both props and audit fetch return empty. */
  emptyText?: string
}

const props = withDefaults(defineProps<Props>(), {
  items: () => [],
  limit: 6,
  emptyText: undefined,
})

const dynamicItems = ref<TimelineItem[]>([])
const isAuto = computed(() => props.items.length === 0)
const displayItems = computed<TimelineItem[]>(() => (props.items.length ? props.items : dynamicItems.value))

const client = useAdminClient()
const auditBridge = createAuditBridge({ client })

function resultTone(value: unknown): TimelineTone {
  if (value === 0 || value === '0' || value === 'success') return 'success'
  if (value === 1 || value === '1' || value === 'failed') return 'error'
  if (value === 2 || value === '2' || value === 'warning') return 'warning'
  return 'default'
}

function formatRelative(iso?: string): string | undefined {
  if (!iso) return undefined
  const t = Date.parse(iso)
  if (Number.isNaN(t)) return iso
  const diff = Date.now() - t
  const minutes = Math.floor(diff / 60000)
  if (minutes < 1) return 'now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 30) return `${days}d ago`
  return new Date(t).toLocaleDateString()
}

useWidgetData(async () => {
  if (!isAuto.value) return
  try {
    const result = await auditBridge.logs.fetch({
      pageIndex: 1,
      pageSize: props.limit,
      searchText: '',
      filters: {},
    })
    dynamicItems.value = (result.items ?? []).map((raw, index) => {
      const r = raw as unknown as Record<string, unknown>
      const user = String(r.userName ?? r.userId ?? 'system')
      const action = String(r.functionName ?? r.action ?? '—')
      return {
        key: String(r.id ?? `audit-${index}`),
        title: `${user} · ${action}`,
        description: r.module ? String(r.module) : undefined,
        time: formatRelative((r.startTime ?? r.creationTime) as string | undefined),
        tone: resultTone(r.resultType),
      } as TimelineItem
    })
  } catch {
    // Audit module not loaded or permission denied — leave empty so the
    // template shows the empty-state copy without throwing.
    dynamicItems.value = []
  }
})

// Resolve i18n-key-style fields on each item before forwarding to
// TProjectTimeline. Props-supplied items in the legacy i18n-key format
// still translate; auto-fetched items already carry display strings
// (user + action) so `maybeTranslate` no-ops on them.
const resolvedItems = computed<TimelineItem[]>(() =>
  displayItems.value.map((item) => ({
    ...item,
    title: maybeTranslate(item.title),
    description: item.description ? maybeTranslate(item.description) : item.description,
    time: item.time ? maybeTranslate(item.time) : item.time,
  })),
)

function emptyLabel(): string {
  return props.emptyText ?? translatePageKey('', 'admin.common.empty') ?? 'No activity yet'
}
</script>

<template>
  <TProjectTimeline v-if="resolvedItems.length" :items="resolvedItems" />
  <div v-else class="t-widget-empty">{{ emptyLabel() }}</div>
</template>

<style scoped>
.t-widget-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 120px;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
</style>
