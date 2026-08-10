<template>
  <TCrudPage
    :search-fields="destructionSearchFields"
    :state="crud"
    :all-columns="destructionColumns"
    :title="t('title')"
    :translate="t"
    :show-create="false"
    :show-batch="false"
    :detail-width="640"
  >
    <template #toolbarRight>
      <NButton size="small" secondary :loading="verifying" @click="verifyChain">
        <template #icon><TSvgIcon icon="mdi:link-variant" :size="14" /></template>
        {{ t('actions.verify') }}
      </NButton>
      <NButton
        v-if="canExecute"
        size="small"
        type="warning"
        secondary
        :loading="running"
        @click="confirmRun"
      >
        <template #icon><TSvgIcon icon="mdi:play-circle-outline" :size="14" /></template>
        {{ t('actions.run') }}
      </NButton>
    </template>

    <template #detail="{ data }">
      <div v-if="data">
        <TDescriptions :items="detailItems(data as DataDestructionDto)" />
        <p class="destruction-detail__note">{{ t('detail.digestNote') }}</p>
      </div>
    </template>
  </TCrudPage>

  <TModalShell v-model:show="showRunConfirm" :title="t('run.confirmTitle')" size="small">
    <p class="destruction-confirm__body">{{ t('run.confirmBody') }}</p>
    <template #footer>
      <NButton size="small" @click="showRunConfirm = false">{{ t('run.cancel') }}</NButton>
      <NButton size="small" type="warning" :loading="running" @click="runNow">
        {{ t('run.confirm') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton } from 'naive-ui'
import { formatDateTime } from '@tnzi/core'
import { EMPTY_DASH, TDescriptions, TModalShell, TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useSafeMessage } from '../_shared/safe-message'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { makePageTranslator } from '../../i18n/translate'
import { useAdminClient } from '../../plugin/client'
import { createAuditBridge, type DataDestructionDto } from '../../services/bridges/audit-bridge'
import { destructionColumns, destructionSearchFields } from './destruction-config'

const bridge = createAuditBridge({ client: useAdminClient() })
const message = useSafeMessage()
const t = makePageTranslator('audit.destruction')

// Running a cycle permanently deletes data, so the button only exists for
// holders of the execute code - viewing certificates is a far weaker right.
const canExecute = computed(() => useAdminAuthStore().hasPermission('audit.destruction.execute'))

const crud = useCrudPage<DataDestructionDto>({
  pageId: 'audit.destruction',
  columns: destructionColumns,
  rowKey: (r) => String(r.id ?? ''),
  permission: 'audit.destruction',
  // The bridge flattens `filters` onto the request body; only isDryRun needs
  // help - the select carries it as a string because FormSchemaItem options
  // are string | number.
  fetchData: (query) =>
    bridge.destruction.fetch({
      ...query,
      filters: normaliseFilters(query.filters),
    }),
})

const verifying = ref(false)
const running = ref(false)
const showRunConfirm = ref(false)

async function verifyChain() {
  verifying.value = true
  try {
    await bridge.destruction.verify()
    message.success(t('verify.ok'))
  } catch (error) {
    // The backend names the first broken sequence; that detail is the whole
    // point of verifying, so pass it through instead of a generic message.
    message.error(error instanceof Error ? error.message : t('verify.failed'))
  } finally {
    verifying.value = false
  }
}

function confirmRun() {
  showRunConfirm.value = true
}

/**
 * Trigger one cycle.
 *
 * Whether this actually deletes anything depends on the backend's DryRun
 * setting - the confirmation copy says so rather than promising either
 * outcome, because the page cannot see that switch.
 */
async function runNow() {
  running.value = true
  try {
    const result = await bridge.destruction.run()
    message.success(
      t('run.done', { destroyed: result.totalDestroyed, held: result.totalHeld }),
    )
    const failed = result.policies.filter((p) => p.error)
    for (const policy of failed) {
      message.error(`${policy.policyName}: ${policy.error}`)
    }
    showRunConfirm.value = false
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : t('run.failed'))
  } finally {
    running.value = false
  }
}

/** Turn the dry-run select's string value back into the boolean the API expects. */
function normaliseFilters(filters: Record<string, unknown>): Record<string, unknown> {
  const { isDryRun, ...rest } = filters
  if (isDryRun === undefined || isDryRun === null || isDryRun === '') return rest
  return { ...rest, isDryRun: isDryRun === 'true' || isDryRun === true }
}

function detailItems(row: DataDestructionDto) {
  return [
    { label: t('columns.creationTime'), value: formatDateTime(row.creationTime) },
    { label: t('columns.policyName'), value: row.policyName },
    { label: t('columns.entityType'), value: row.entityType },
    { label: t('detail.cutoff'), value: formatDateTime(row.cutoff) },
    { label: t('columns.destroyedCount'), value: String(row.destroyedCount) },
    { label: t('columns.heldCount'), value: String(row.heldCount) },
    { label: t('columns.mode'), value: row.mode },
    { label: t('columns.isDryRun'), value: row.isDryRun ? t('yes') : t('no') },
    { label: t('detail.encryptionKeyId'), value: row.encryptionKeyId || EMPTY_DASH },
    {
      label: t('detail.isKeyDestroyed'),
      value: row.encryptionKeyId ? (row.isKeyDestroyed ? t('yes') : t('no')) : EMPTY_DASH,
    },
    { label: t('detail.identifierDigest'), value: row.identifierDigest },
    { label: t('columns.sequence'), value: String(row.sequence) },
    { label: t('detail.hash'), value: row.hash },
  ]
}
</script>

<style scoped>
.destruction-detail__note {
  margin: 12px 0 0;
  font-size: 12px;
  color: var(--t-text-color-3, #999);
  line-height: 1.6;
}

.destruction-confirm__body {
  margin: 0;
  line-height: 1.7;
}
</style>
