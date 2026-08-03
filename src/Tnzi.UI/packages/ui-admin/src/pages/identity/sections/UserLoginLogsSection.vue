<template>
  <TDetailSection :title="t('detail.sections.loginLogs')" max-width="none">
    <template #actions>
      <NButton size="small" tertiary :loading="loading" @click="load">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="15" /></template>
        {{ t('admin.common.reload') }}
      </NButton>
    </template>

    <NSpin :show="loading">
      <TEmpty v-if="!loading && !logs.length" :text="t('detail.loginLogs.empty')" size="small" />
      <template v-else>
        <ol class="ull-feed">
          <li v-for="log in logs" :key="log.id" class="ull-entry" :class="{ 'ull-entry--fail': !log.isSuccess }">
            <span class="ull-dot">
              <TSvgIcon :icon="log.isSuccess ? 'mdi:check' : 'mdi:close'" :size="12" />
            </span>
            <div class="ull-body">
              <div class="ull-head">
                <span class="ull-verdict">
                  {{ log.isSuccess ? t('detail.loginLogs.success') : t('detail.loginLogs.failure') }}
                </span>
                <TRelativeTime class="ull-time" :value="log.loginTime" />
              </div>
              <div class="ull-meta">
                <span><TSvgIcon icon="mdi:ip-network-outline" :size="13" />{{ log.ipAddress || EMPTY_DASH }}</span>
                <span v-if="log.userAgent" :title="log.userAgent" class="ull-agent">
                  <TSvgIcon icon="mdi:web" :size="13" />{{ log.userAgent }}
                </span>
              </div>
              <p v-if="log.failureReason" class="ull-reason">{{ log.failureReason }}</p>
            </div>
          </li>
        </ol>
        <div v-if="total > pageSize" class="ull-pager">
          <NPagination
            :page="page"
            :page-size="pageSize"
            :item-count="total"
            size="small"
            @update:page="onPage"
          />
        </div>
      </template>
    </NSpin>
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * Sign-in history for ONE user, as a time-ordered feed.
 *
 * Login records are events, not rows to compare: what matters is the sequence
 * and whether a run of failures precedes a success. A vertical feed with a
 * pass/fail marker reads that story directly; a five-column table of the same
 * data does not.
 */
import { ref } from 'vue'
import { NButton, NPagination, NSpin } from 'naive-ui'
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TDetailSection from '../../../components/detail/TDetailSection.vue'
import { TEmpty } from '@tnzi/ui'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { createIdentityBridge } from '../../../services/bridges/identity-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safe-message'
import type { LoginLogDto } from '@tnzi/core/services/identity'

const props = defineProps<{
  userId: string
  t: (key: string, named?: Record<string, unknown>) => string
}>()

const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()

const logs = ref<LoginLogDto[]>([])
const loading = ref(true)
const page = ref(1)
const pageSize = 20
const total = ref(0)

async function load(): Promise<void> {
  loading.value = true
  try {
    const res = await bridge.loginLogs.fetch({
      pageIndex: page.value,
      pageSize,
      searchText: '',
      filters: { userId: props.userId },
    })
    logs.value = res.items ?? []
    total.value = res.totalCount ?? 0
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    loading.value = false
  }
}
void load()

function onPage(p: number): void {
  page.value = p
  void load()
}
</script>

<style scoped>
.ull-feed {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}
.ull-entry {
  display: flex;
  gap: 12px;
  padding-bottom: 14px;
  position: relative;
}
/* The connecting rail: drawn on every entry but the last, so the feed reads as
   one sequence rather than a stack of unrelated blocks. */
.ull-entry:not(:last-child)::before {
  content: '';
  position: absolute;
  left: 10px;
  top: 22px;
  bottom: 0;
  width: 1px;
  background: var(--tnzi-border);
}
.ull-dot {
  flex-shrink: 0;
  width: 21px;
  height: 21px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgb(var(--tnzi-success-rgb) / 0.14);
  color: var(--tnzi-success);
  z-index: 1;
}
.ull-entry--fail .ull-dot {
  background: rgb(var(--tnzi-error-rgb) / 0.14);
  color: var(--tnzi-error);
}
.ull-body {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.ull-head {
  display: flex;
  align-items: baseline;
  gap: 10px;
  flex-wrap: wrap;
}
.ull-verdict {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.ull-entry--fail .ull-verdict {
  color: var(--tnzi-error);
}
.ull-time {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.ull-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 3px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
  min-width: 0;
}
.ull-meta > span {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  min-width: 0;
}
.ull-agent {
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ull-reason {
  margin: 2px 0 0;
  font-size: 12px;
  color: var(--tnzi-error);
}
.ull-pager {
  display: flex;
  justify-content: flex-end;
  padding-top: 8px;
}
</style>
