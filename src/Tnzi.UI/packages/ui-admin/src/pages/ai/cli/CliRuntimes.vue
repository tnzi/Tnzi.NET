<template>
  <TItemPage
    :state="crud"
    :title="title"
    :translate="t"
    :show-create="false"
    :show-batch="false"
  >
    <template #primary>
      <NButton size="small" type="primary" :loading="probing" @click="probe">
        <template #icon><TSvgIcon icon="mdi:radar" :size="14" /></template>
        {{ t('actions.probe') }}
      </NButton>
    </template>

    <!-- Providers whose protocol has no adapter in this backend version are
         listed honestly rather than hidden: an admin who cannot see them will
         keep asking why "the CLI we installed" never appears. -->
    <template #kpis>
      <NAlert
        v-if="unimplemented.length"
        type="warning"
        :closable="false"
        :title="t('unimplemented.title')"
      >
        {{ t('unimplemented.body') }}
        <strong>{{ unimplementedNames }}</strong>
      </NAlert>
    </template>

    <template #item="{ item }">
      <TItemCard
        :title="item.name"
        icon="mdi:console"
        :icon-tone="statusTone(item.status)"
        :tags="runtimeTags(item)"
        :muted="item.status === CliRuntimeStatus.Disabled"
      >
        <template #meta>
          <div class="cli-rt-meta">
            <span class="cli-rt-meta__item">
              <TSvgIcon icon="mdi:server" :size="13" />{{ item.hostId }}
            </span>
            <span v-if="item.launchHeader" class="cli-rt-meta__item">
              <TSvgIcon icon="mdi:play-outline" :size="13" />
              <code>{{ item.launchHeader }}</code>
            </span>
            <span v-if="item.cliVersion" class="cli-rt-meta__item">
              <TSvgIcon icon="mdi:tag-outline" :size="13" />{{ item.cliVersion }}
            </span>
            <span class="cli-rt-meta__item">
              <TSvgIcon icon="mdi:layers-triple-outline" :size="13" />
              {{ t('columns.maxConcurrentRuns') }} {{ item.maxConcurrentRuns }}
            </span>
            <span class="cli-rt-meta__item">
              <TSvgIcon icon="mdi:heart-pulse" :size="13" />{{ t('columns.lastSeenAt') }}
              <TRelativeTime :value="item.lastSeenAt" />
            </span>
          </div>
          <p class="cli-rt-path" :title="item.executablePath">
            <TSvgIcon icon="mdi:file-code-outline" :size="13" />{{ item.executablePath }}
          </p>
        </template>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :collapse="false" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <template #form="{ formData }">
      <TFormSchemaRenderer
        :schema="formSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
      />
    </template>
  </TItemPage>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import TItemPage from '../../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag } from '../../../components/data/TItemCard.vue'
import TRowActions from '../../../components/crud/TRowActions.vue'
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { useCrudPage } from '../../../headless/useCrudPage'
import { deleteAction, editAction, type RowAction } from '../../../headless/row-actions'
import { useSafeMessage } from '../../_shared/safe-message'
import { makePageTranslator } from '../../../i18n/translate'
import { useAdminClient } from '../../../plugin/client'
import {
  createCliAgentBridge,
  CliRuntimeStatus,
  type CliRuntimeDto,
  type CliProviderOptionDto,
} from '../../../services/bridges/cli-agent-bridge'
import { formSchema, statusBadgeMapping } from './cli-runtime-config'

const bridge = createCliAgentBridge({ client: useAdminClient() })
const message = useSafeMessage()
const t = makePageTranslator('ai.cliRuntimes')
const title = 'CLI Runtimes'

const providers = ref<CliProviderOptionDto[]>([])
const probing = ref(false)

const crud = useCrudPage<CliRuntimeDto>({
  pageId: 'ai.cli-runtimes',
  columns: [],
  rowKey: (r) => String(r.id ?? ''),
  permission: 'ai.cliRuntime',
  // The registry is a bounded per-host list, so it is fetched whole and wrapped
  // in a single page rather than pretending to paginate.
  fetchData: async () => {
    const [items] = await Promise.all([bridge.runtimes.list(), loadProviders()])
    return {
      items,
      totalCount: items.length,
      pageIndex: 1,
      pageSize: Math.max(items.length, 1),
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    }
  },
  updateData: (id, data) =>
    bridge.runtimes.update(String(id), data as Record<string, never>),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.runtimes.remove(String(id))
  },
})

async function loadProviders() {
  providers.value = await bridge.runtimes.providers()
}

/** Enabled providers whose protocol this backend version cannot actually run. */
const unimplemented = computed(() =>
  providers.value.filter((p: CliProviderOptionDto) => p.enabled && !p.implemented),
)

const unimplementedNames = computed(() =>
  unimplemented.value.map((p: CliProviderOptionDto) => p.displayName).join(', '),
)

async function probe() {
  probing.value = true
  try {
    const result = await bridge.runtimes.probe()
    message.success(
      t('probe.done')
        .replace('{found}', String(result.runtimes.length))
        .replace('{missing}', String(result.notFound.length)),
    )
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    probing.value = false
  }
}

function statusTone(status: CliRuntimeStatus) {
  if (status === CliRuntimeStatus.Online) return 'success'
  return status === CliRuntimeStatus.Disabled ? 'default' : 'warning'
}

function runtimeTags(item: CliRuntimeDto): ItemCardTag[] {
  const tags: ItemCardTag[] = [
    {
      label: t(statusBadgeMapping[item.status]?.label ?? 'status.offline'),
      type: (statusBadgeMapping[item.status]?.type ?? 'default') as ItemCardTag['type'],
    },
  ]

  if (item.providerDisplayName) {
    tags.push({ label: item.providerDisplayName, type: 'info' })
  }

  if (item.protocol) {
    tags.push({ label: item.protocol, type: 'default' })
  }

  return tags
}

const rowActions: RowAction<CliRuntimeDto>[] = [editAction(crud), deleteAction(crud)]
</script>

<style scoped>
.cli-rt-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 14px;
  align-items: center;
}

.cli-rt-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.cli-rt-path {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 4px 0 0;
  overflow: hidden;
  font-family: var(--tnzi-font-mono);
  font-size: 12px;
  color: var(--tnzi-text-3);
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
