<template>
  <!--
    References — file-reference inspector (TContentPage, no back). Enter a file
    ID to list every entity that references it (`references.byFile`). Useful for
    diagnosing why a file can't be deleted (referenceCount > 0) — admins see the
    owning entity type / id / field for each reference.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NInput
        v-model:value="fileId"
        size="small"
        clearable
        :placeholder="t('search.fileId')"
        class="w-280px"
        @keydown.enter="loadReferences"
        @clear="clear"
      >
        <template #prefix><TSvgIcon icon="mdi:file-search-outline" :size="16" /></template>
      </NInput>
      <NButton size="small" type="primary" :loading="loading" :disabled="!fileId.trim()" @click="loadReferences">
        {{ t('search.query') }}
      </NButton>
    </template>

    <NCard size="small" :bordered="false" class="t-table-card" :title="t('list.title')">
      <TResponsiveTable
        :columns="referenceColumns"
        :data="references"
        :loading="loading"
        :row-key="(r: FileReferenceDto) => r.id"
        :pagination="false"
        :flex-height="true"
        size="small"
        :empty-text="queried ? t('list.empty') : t('list.enterFileId')"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NCard, NInput } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildReferenceColumns } from './references-config'
import type { FileReferenceDto } from '@tnzi/core/services/storage'

const t = (key: string) => translatePageKey('storage.references', key)
const message = useSafeMessage()
const bridge = createStorageBridge({ client: useAdminClient() })

const fileId = ref('')
const loading = ref(false)
const queried = ref(false)
const references = ref<FileReferenceDto[]>([])

const referenceColumns = buildReferenceColumns(t)

async function loadReferences(): Promise<void> {
  const id = fileId.value.trim()
  if (!id) return
  loading.value = true
  try {
    references.value = await bridge.references.byFile(id)
    queried.value = true
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    references.value = []
    queried.value = true
  } finally {
    loading.value = false
  }
}

function clear(): void {
  fileId.value = ''
  references.value = []
  queried.value = false
}
</script>
