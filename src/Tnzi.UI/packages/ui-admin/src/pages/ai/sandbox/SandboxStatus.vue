<template>
  <!--
    SandboxStatus - surfaces /admin/sandbox/status exposed by
    `Tnzi.AI.Sandbox`. A read-only operations view: an at-a-glance status
    overview (enabled / provider / data root + the three denylist counts)
    followed by the three denylists (commands / file patterns / environment
    variables) the local provider applies to every sandbox-bound tool call.
    Each denylist has its own keyword filter so long lists stay scannable.
    The backend exposes status only - there is no denylist CRUD here.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <template #default>
      <!-- Module not loaded (404 → null status) -->
      <NEmpty
        v-if="!loading && !status"
        class="t-sandbox-page__not-loaded"
        :description="t('notLoaded.title')"
      >
        <template #icon><TSvgIcon icon="mdi:cube-off-outline" :size="40" /></template>
        <template #extra>
          <span class="text-muted text-13px">{{ t('notLoaded.body') }}</span>
        </template>
      </NEmpty>

      <!-- Flex column that fills the TContentPage fill-mode body so the
           denylist row below can claim the residual viewport height. -->
      <div v-else-if="status" class="t-sandbox-page">
        <!-- Disabled banner - sandbox loaded but Enabled=false -->
        <NAlert
          v-if="!status.enabled"
          class="t-sandbox-page__alert"
          :title="t('disabled.title')"
          type="warning"
          :closable="false"
        >
          {{ t('disabled.body') }}
        </NAlert>

        <!-- Status overview: enabled badge + provider + data root + counts -->
        <NCard size="small" :bordered="false" class="t-sandbox-page__overview-card">
          <div class="t-sandbox-page__overview">
            <div class="t-sandbox-page__stat">
              <span class="t-sandbox-page__stat-label">
                <TSvgIcon icon="mdi:power" :size="14" />
                {{ t('fields.enabled') }}
              </span>
              <NTag
                :type="status.enabled ? 'success' : 'default'"
                size="small"
                :bordered="false"
                round
              >
                {{ status.enabled ? t('status.enabled') : t('status.disabled') }}
              </NTag>
            </div>

            <div class="t-sandbox-page__stat">
              <span class="t-sandbox-page__stat-label">
                <TSvgIcon icon="mdi:server" :size="14" />
                {{ t('fields.provider') }}
              </span>
              <NTag :type="status.enabled ? 'info' : 'default'" size="small" :bordered="false">
                {{ status.provider || EMPTY_DASH }}
              </NTag>
            </div>

            <div class="t-sandbox-page__stat t-sandbox-page__stat--wide">
              <span class="t-sandbox-page__stat-label">
                <TSvgIcon icon="mdi:folder-outline" :size="14" />
                {{ t('fields.dataRoot') }}
              </span>
              <code class="t-sandbox-page__path" :title="status.dataRoot">{{ status.dataRoot || EMPTY_DASH }}</code>
            </div>

            <div
              v-for="c in counts"
              :key="c.key"
              class="t-sandbox-page__stat"
            >
              <span class="t-sandbox-page__stat-label">
                <TSvgIcon :icon="c.icon" :size="14" />
                {{ t(`counts.${c.key}`) }}
              </span>
              <NTag :type="c.tagType" size="small" :bordered="false" round>{{ c.count }}</NTag>
            </div>
          </div>
        </NCard>

        <!-- Three denylists, each with its own keyword filter -->
        <div class="t-sandbox-page__denylists">
          <NCard
            v-for="list in denylists"
            :key="list.key"
            :title="t(`sections.${list.key}`)"
            size="small"
            :bordered="false"
          >
            <template #header-extra>
              <NTag :bordered="false" size="small" round>
                <span v-if="queries[list.key]">{{ list.filtered.length }} / </span>{{ list.items.length }}
              </NTag>
            </template>

            <div class="t-sandbox-page__list">
              <NInput
                v-if="list.items.length"
                v-model:value="queries[list.key]"
                size="small"
                clearable
                :placeholder="t('filter.placeholder')"
              >
                <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
              </NInput>

              <div v-if="list.filtered.length" class="t-sandbox-page__chips">
                <NTag
                  v-for="item in list.filtered"
                  :key="item"
                  :type="list.tagType"
                  :bordered="false"
                  size="small"
                  class="font-mono"
                >
                  {{ item }}
                </NTag>
              </div>

              <NEmpty
                v-else-if="list.items.length"
                size="small"
                :description="t('empty.noMatch')"
              />
              <NEmpty
                v-else
                size="small"
                :description="t(`empty.${list.key}`)"
              />
            </div>
          </NCard>
        </div>
      </div>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, onMounted, reactive, ref } from 'vue'
import type { TagProps } from 'naive-ui'
import { NAlert, NButton, NCard, NEmpty, NInput, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../../plugin/client'
import { createSandboxBridge, type SandboxStatusDto } from '../../../services/bridges/sandbox-bridge'
import { makePageTranslator } from '../../_shared/translate'
import TContentPage from '../../../components/layout/TContentPage.vue'

type DenylistKey = 'deniedCommands' | 'deniedPatterns' | 'envBlacklist'

interface DenylistDef {
  key: DenylistKey
  field: keyof Pick<SandboxStatusDto, 'deniedCommands' | 'deniedPatterns' | 'environmentBlacklist'>
  icon: string
  tagType: TagProps['type']
}

const bridge = createSandboxBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.sandbox')

const loading = ref(false)
const status = ref<SandboxStatusDto | null>(null)

// Per-section keyword filter state (one query string per denylist).
const queries = reactive<Record<DenylistKey, string>>({
  deniedCommands: '',
  deniedPatterns: '',
  envBlacklist: '',
})

const denylistDefs: DenylistDef[] = [
  { key: 'deniedCommands', field: 'deniedCommands', icon: 'mdi:cancel', tagType: 'error' },
  { key: 'deniedPatterns', field: 'deniedPatterns', icon: 'mdi:file-cancel-outline', tagType: 'warning' },
  { key: 'envBlacklist', field: 'environmentBlacklist', icon: 'mdi:key-remove', tagType: 'default' },
]

const counts = computed(() =>
  denylistDefs.map((d) => ({
    key: d.key,
    icon: d.icon,
    tagType: d.tagType,
    count: status.value ? status.value[d.field].length : 0,
  })),
)

const denylists = computed(() =>
  denylistDefs.map((d) => {
    const items = status.value ? status.value[d.field] : []
    const q = queries[d.key].trim().toLowerCase()
    const filtered = q ? items.filter((item) => item.toLowerCase().includes(q)) : items
    return { key: d.key, tagType: d.tagType, items, filtered }
  }),
)

async function refresh(): Promise<void> {
  loading.value = true
  try {
    status.value = await bridge.getStatus()
  } catch {
    status.value = null
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })

defineExpose({ refresh, status, loading, queries, denylists })
</script>

<style scoped>
.t-sandbox-page__not-loaded {
  padding: 48px 0;
}
/* Fill chain - TContentPage scroll="fill" gives the body a bounded height;
   this column fills it, the alert/overview keep natural height, and the
   denylist row claims the residual viewport height (C8 fill-height rule). */
.t-sandbox-page {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-sandbox-page__alert,
.t-sandbox-page__overview-card {
  flex-shrink: 0;
}
.t-sandbox-page__overview {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px 24px;
}
.t-sandbox-page__stat--wide {
  grid-column: 1 / -1;
}
.t-sandbox-page__stat {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}
.t-sandbox-page__stat-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 13px;
  flex-shrink: 0;
}
.t-sandbox-page__path {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  word-break: break-all;
  min-width: 0;
}
/* Desktop: the three cards share one full-height grid row; each card's list
   area flex-fills and the chip area scrolls internally. */
.t-sandbox-page__denylists {
  flex: 1 1 auto;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  grid-auto-rows: minmax(0, 1fr);
  gap: 12px;
}
.t-sandbox-page__denylists :deep(.n-card__content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-sandbox-page__list {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-sandbox-page__chips {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-wrap: wrap;
  align-content: flex-start;
  gap: 6px;
  overflow-y: auto;
}
@media (max-width: 900px) {
  .t-sandbox-page__overview { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  /* Stacked cards keep their natural height and the section itself scrolls
     within the fill body - three viewport-split cards would be unusably
     squashed on a phone. Chips fall back to a capped internal scroller. */
  .t-sandbox-page__denylists {
    grid-template-columns: 1fr;
    grid-auto-rows: auto;
    overflow-y: auto;
  }
  .t-sandbox-page__chips {
    flex: 0 0 auto;
    max-height: 280px;
  }
}
@media (max-width: 560px) {
  .t-sandbox-page__overview { grid-template-columns: 1fr; }
}
</style>
