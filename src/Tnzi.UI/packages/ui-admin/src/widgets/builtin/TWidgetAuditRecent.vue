<script setup lang="ts">
/**
 * `TWidgetAuditRecent` - most recent audit log entries.
 *
 * Lists the N most recent operations (default 8). Each row shows the
 * actor, action and a relative time stamp. Pull from
 * `audit-bridge.logs.fetch` with a small page size and rely on the
 * backend default sort (creation desc).
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { ref } from 'vue'
import { NTag } from 'naive-ui'
import { TRelativeTime } from '@tnzi/ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import { createAuditBridge } from '../../services/bridges/audit-bridge'
import { useWidgetData } from '../shell/useWidgetData'
import { translatePageKey } from '../../pages/_shared/translate'

interface Props {
  /** How many rows to pull. Default 8. */
  limit?: number
}

const props = withDefaults(defineProps<Props>(), {
  limit: 8,
})

interface AuditRow {
  id: string
  user: string
  action: string
  resultType: number | string | undefined
  time: string | undefined
}

const rows = ref<AuditRow[]>([])
const bridge = createAuditBridge({ client: useAdminClient() })

useWidgetData(async () => {
  const result = await bridge.logs.fetch({
    pageIndex: 1,
    pageSize: props.limit,
    searchText: '',
    filters: {},
  })
  // A 403/failed envelope can resolve to undefined - render the empty state.
  rows.value = (result?.items ?? []).map((raw) => {
    const r = raw as unknown as Record<string, unknown>
    return {
      id: String(r.id ?? ''),
      user: String(r.userName ?? r.userId ?? translatePageKey('', 'admin.modules.audit.logs.anonymous') ?? 'anonymous'),
      action: String(r.functionName ?? r.action ?? EMPTY_DASH),
      resultType: r.resultType as number | string | undefined,
      time: (r.startTime ?? r.creationTime) as string | undefined,
    }
  })
})

function t(key: string, fallback: string): string {
  return translatePageKey('', key) || fallback
}

function resultTone(value: number | string | undefined): 'success' | 'warning' | 'error' | 'default' {
  if (value === 0 || value === '0' || value === 'success') return 'success'
  if (value === 1 || value === '1' || value === 'failed') return 'error'
  if (value === 2 || value === '2' || value === 'warning') return 'warning'
  return 'default'
}
</script>

<template>
  <div class="t-widget-audit">
    <div v-if="rows.length === 0" class="t-widget-audit__empty">
      <TSvgIcon icon="mdi:inbox-outline" :size="32" />
      <p>{{ t('admin.modules.audit.logs.empty', 'No recent activity') }}</p>
    </div>
    <ul v-else class="t-widget-audit__list">
      <li v-for="row in rows" :key="row.id" class="t-widget-audit__row">
        <NTag size="tiny" :type="resultTone(row.resultType)" round>
          {{ row.action }}
        </NTag>
        <span class="t-widget-audit__user">{{ row.user }}</span>
        <TRelativeTime class="t-widget-audit__time" :value="row.time" />
      </li>
    </ul>
  </div>
</template>

<style scoped>
.t-widget-audit__list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-widget-audit__row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12.5px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--tnzi-divider);
}
.t-widget-audit__row:last-child {
  border-bottom: none;
  padding-bottom: 0;
}
.t-widget-audit__user {
  flex: 1;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-widget-audit__time {
  color: var(--tnzi-base-text-muted, #888);
  font-size: 12px;
  flex-shrink: 0;
}
.t-widget-audit__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 140px;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 13px;
}
.t-widget-audit__empty p {
  margin: 0;
}
</style>
