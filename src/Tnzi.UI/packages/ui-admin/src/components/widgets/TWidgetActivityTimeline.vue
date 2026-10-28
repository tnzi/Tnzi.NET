<script setup lang="ts">
/**
 * `TWidgetActivityTimeline` - widget wrapping TProjectTimeline.
 *
 * Hybrid data model:
 *   - **Props mode** - consumer passes `items: TimelineItem[]` (e.g. a
 *     curated "what's new" list) and the widget renders them as-is.
 *   - **Auto-fetch mode** - consumer omits `items` (default in
 *     `defaultWorkbenchWidgets()`) and the widget pulls the most recent
 *     audit log entries via `audit-bridge.logs.fetch(...)`, translating
 *     each entry into a TimelineItem. The Audit module ships with every
 *     `HostingModule` app, so this gives the bundled Workbench a
 *     genuinely live activity feed instead of canned i18n placeholders.
 */
import { formatDateOnly } from '@tnzi/core'
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, ref } from 'vue'
import TProjectTimeline from '../dashboard/TProjectTimeline.vue'
import type { TimelineItem } from '../dashboard/TProjectTimeline.vue'
import { useAdminClient } from '../../plugin/client'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useWidgetData } from '../../headless/useWidgetData'
import { useAdminLocale } from '../../headless/useAdminLocale'
import { maybeTranslate, translateChromeKey } from '../../i18n/translate'
import { auditResultTone } from '../../utils/audit-result-tone'

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

/** An audit row as a timeline item, keeping its timestamp so the relative time follows the locale. */
interface AuditEntry {
  item: TimelineItem
  at?: string
}

const dynamicItems = ref<AuditEntry[]>([])
const isAuto = computed(() => props.items.length === 0)
const locales = useAdminLocale()
const displayItems = computed<TimelineItem[]>(() =>
  props.items.length
    ? props.items
    : dynamicItems.value.map(({ item, at }) => ({ ...item, time: formatRelative(at, locales.intlTag.value) })),
)

const client = useAdminClient()
const auditBridge = createAuditBridge({ client })

/**
 * "now" / "5m ago" / "3h ago" / "2d ago" in the admin's language (the active
 * locale's `Intl` tag), not English literals.
 */
function formatRelative(iso: string | undefined, intlTag: string): string | undefined {
  if (!iso) return undefined
  const t = Date.parse(iso)
  if (Number.isNaN(t)) return iso
  const minutes = Math.floor((Date.now() - t) / 60000)
  const rtf = new Intl.RelativeTimeFormat(intlTag, { numeric: 'auto', style: 'narrow' })
  if (minutes < 1) return rtf.format(0, 'second')
  if (minutes < 60) return rtf.format(-minutes, 'minute')
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return rtf.format(-hours, 'hour')
  const days = Math.floor(hours / 24)
  if (days < 30) return rtf.format(-days, 'day')
  // Beyond a month a relative label stops carrying information; fall back to
  // the shared date formatter so it matches every other date in the admin.
  return formatDateOnly(t)
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
      const action = String(r.functionName ?? r.action ?? EMPTY_DASH)
      return {
        item: {
          key: String(r.id ?? `audit-${index}`),
          title: `${user} · ${action}`,
          description: r.module ? String(r.module) : undefined,
          tone: auditResultTone(r.resultType),
        } as TimelineItem,
        at: (r.startTime ?? r.creationTime) as string | undefined,
      }
    })
  } catch {
    // Audit module not loaded or permission denied - leave empty so the
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
  return props.emptyText ?? translateChromeKey('admin.common.empty', 'No activity yet')
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
