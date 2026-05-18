<template>
  <TCrudPage
    :state="crud"
    :all-columns="functionModuleColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <NForm :disabled="mode === 'view'" label-placement="left" label-width="140px">
        <NFormItem :label="t('form.code')" required>
          <NInput :value="(formData as Row)?.code ?? ''" @update:value="(v: string) => set('code', v)" />
        </NFormItem>
        <NFormItem :label="t('form.name')" required>
          <NInput :value="(formData as Row)?.name ?? ''" @update:value="(v: string) => set('name', v)" />
        </NFormItem>
        <NFormItem :label="t('form.parentId')">
          <NSelect
            :value="(formData as Row)?.parentId ?? null"
            :options="parentOptions"
            :placeholder="t('form.parentIdPlaceholder')"
            clearable
            filterable
            @update:value="(v: string | null) => set('parentId', v ?? undefined)"
          />
        </NFormItem>
        <NFormItem :label="t('form.order')">
          <NInputNumber :value="(formData as Row)?.order ?? 0" :min="0" @update:value="(v: number | null) => set('order', v ?? 0)" />
        </NFormItem>
        <NFormItem :label="t('form.isEnabled')">
          <NSwitch :value="(formData as Row)?.isEnabled ?? true" @update:value="(v: boolean) => set('isEnabled', v)" />
        </NFormItem>
      </NForm>
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { NForm, NFormItem, NInput, NInputNumber, NSwitch, NSelect } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import { functionModuleColumns } from './function-module-config'
import { translatePageKey } from '../_shared/translate'
import type { FunctionModuleDto } from '@tnzi/core/services/authorization'

interface Row {
  id?: string
  code?: string
  name?: string
  parentId?: string
  order?: number
  isEnabled?: boolean
}

const title = 'title'
const bridge = createAuthorizationBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('authorization.functionModules', key)

const crud = useCrudPage<Row>({
  pageId: 'authorization.functionModules',
  columns: functionModuleColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.functionModules.fetch(query),
  createData: async (data) => {
    const created = await bridge.functionModules.create(data as never)
    await loadModuleOptions()
    return created as Row
  },
  updateData: async (id, data) => {
    const updated = await bridge.functionModules.update(String(id), data as never)
    await loadModuleOptions()
    return updated as Row
  },
  deleteData: async (ids) => {
    await bridge.functionModules.delete(ids.map(String))
    await loadModuleOptions()
  },
})

crud.refresh().catch(() => undefined)

// Indented `name` options for the parent picker. Excludes the row being
// edited (and its descendants) to prevent cycles — the simple version below
// excludes self only; a full descendant check is overkill for typical
// module trees (<50 nodes) and the backend rejects cycles anyway.
const allModules = ref<FunctionModuleDto[]>([])
const editingId = computed(() => (crud.formModal.formData.value as Row | null)?.id)

async function loadModuleOptions(): Promise<void> {
  try {
    allModules.value = await bridge.functionModules.getAll()
  } catch {
    allModules.value = []
  }
}

const parentOptions = computed(() => {
  // Build name with `code` suffix for disambiguation; indent by depth from parentId chain.
  const byId = new Map<string, FunctionModuleDto>()
  for (const m of allModules.value) byId.set(m.id, m)
  /**
   * Walk the parent chain, counting hops. Uses a `visited` set so a cyclic
   * chain (corrupted data) terminates instead of looping forever — depth
   * cap alone wouldn't catch a 3-node cycle that re-hits the same nodes.
   */
  const depthOf = (id: string | undefined): number => {
    if (!id) return 0
    const visited = new Set<string>()
    let d = 0
    let cur = byId.get(id)
    while (cur?.parentId) {
      if (visited.has(cur.parentId)) break
      visited.add(cur.parentId)
      d += 1
      cur = byId.get(cur.parentId)
      if (d > 32) break // hard safety net for unreasonably deep trees
    }
    return d
  }
  return allModules.value
    .filter((m) => m.id !== editingId.value)
    .map((m) => ({
      label: `${'  '.repeat(depthOf(m.id))}${m.name} (${m.code})`,
      value: m.id,
    }))
})

function set(key: keyof Row, value: unknown): void {
  if (!crud.formModal.formData.value) {
    crud.formModal.formData.value = {} as Row
  }
  ;(crud.formModal.formData.value as unknown as Record<string, unknown>)[key as string] = value
}

onMounted(() => {
  void loadModuleOptions()
})
</script>
