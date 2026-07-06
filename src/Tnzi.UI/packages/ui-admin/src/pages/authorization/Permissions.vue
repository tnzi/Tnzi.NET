<template>
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NButton size="small" @click="loadModules">{{ t('refresh') }}</NButton>
    </template>

    <NCard :bordered="false" class="t-permission-page__card">
      <TMasterDetailLayout :master-width="320">
        <template #master>
          <NInput
            v-model:value="treeFilter"
            :placeholder="t('searchModule')"
            size="small"
            clearable
          />
          <NSpin :show="modulesLoading" class="mt-8px">
            <div v-if="!modules.length && !modulesLoading" class="t-permission-page__empty">
              {{ t('noModules') }}
            </div>
            <NTree
              v-else
              :data="treeData"
              :pattern="treeFilter"
              :selected-keys="selectedModuleId ? [selectedModuleId] : []"
              :default-expand-all="true"
              :show-irrelevant-nodes="false"
              block-line
              class="t-permission-page__naive-tree"
              @update:selected-keys="onSelectModule"
            />
          </NSpin>
        </template>
        <template #detail>
          <section class="t-permission-page__detail">
          <div v-if="!selectedModule" class="t-permission-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <div v-else>
            <header class="t-permission-page__detail-header">
              <div>
                <h3 class="t-permission-page__module-name">{{ selectedModule.name }}</h3>
                <code class="t-permission-page__module-code">{{ selectedModule.code }}</code>
              </div>
              <span class="t-permission-page__count">
                {{ t('permissionCount', { n: permissions.length }) }}
              </span>
            </header>
            <NInput
              v-model:value="permFilter"
              :placeholder="t('searchPermission')"
              size="small"
              clearable
              class="t-permission-page__perm-filter"
            />
            <NSpin :show="permissionsLoading">
              <TResponsiveTable
                :data="filteredPermissions"
                :columns="columns"
                :row-key="(r: PermissionRow) => r.id ?? ''"
                size="small"
                :bordered="false"
              />
            </NSpin>
          </div>
          </section>
        </template>
      </TMasterDetailLayout>
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import type { TreeOption, DataTableColumns } from 'naive-ui'
import {
  NCard, NButton, NTree, NInput, NSpin, NTag,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useSafeMessage } from '../_shared/safeMessage'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import type { FunctionModuleDto, ModuleFunctionDto } from '@tnzi/core/services/authorization'
import { PermissionCategory } from '@tnzi/core/services/authorization'
import TContentPage from '../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../components/layout/TMasterDetailLayout.vue'

type PermissionRow = ModuleFunctionDto

const bridge = createAuthorizationBridge({ client: useAdminClient() })
const t = makePageTranslator('authorization.permissions')

const message = useSafeMessage()

const modules = ref<FunctionModuleDto[]>([])
const permissions = ref<PermissionRow[]>([])
const selectedModuleId = ref<string | null>(null)
const treeFilter = ref('')
const permFilter = ref('')
const modulesLoading = ref(false)
const permissionsLoading = ref(false)

const flatById = computed(() => {
  const m = new Map<string, FunctionModuleDto>()
  for (const x of modules.value) m.set(x.id, x)
  return m
})

const selectedModule = computed(() =>
  selectedModuleId.value ? flatById.value.get(selectedModuleId.value) ?? null : null,
)

const treeData = computed<TreeOption[]>(() => {
  // Build parent → children map.
  const byParent = new Map<string | undefined, FunctionModuleDto[]>()
  for (const m of modules.value) {
    const key = m.parentId ?? undefined
    if (!byParent.has(key)) byParent.set(key, [])
    byParent.get(key)!.push(m)
  }
  // Sort each level by `order` then `name`.
  for (const list of byParent.values()) {
    list.sort((a, b) => (a.order ?? 0) - (b.order ?? 0) || a.name.localeCompare(b.name))
  }
  const adapt = (parentId?: string): TreeOption[] =>
    (byParent.get(parentId) ?? []).map((m) => ({
      key: m.id,
      label: m.name,
      children: byParent.has(m.id) ? adapt(m.id) : undefined,
      suffix: () =>
        h(
          NTag,
          {
            size: 'small',
            bordered: false,
            type: m.isEnabled ? 'success' : 'warning',
            class: 'ml-6px',
          },
          { default: () => m.code },
        ),
    }))
  return adapt(undefined)
})

const filteredPermissions = computed(() => {
  if (!permFilter.value) return permissions.value
  const q = permFilter.value.toLowerCase()
  return permissions.value.filter(
    (p) =>
      p.name?.toLowerCase().includes(q) ||
      p.code?.toLowerCase().includes(q) ||
      p.description?.toLowerCase().includes(q),
  )
})

const columns = computed<DataTableColumns<PermissionRow>>(() => [
  { key: 'name', title: t('columns.name'), minWidth: 200 },
  {
    key: 'code',
    title: t('columns.code'),
    minWidth: 240,
    render: (row) =>
      h('code', { class: 't-permission-page__code' }, row.code ?? ''),
  },
  { key: 'description', title: t('columns.description'), minWidth: 200 },
  {
    key: 'category',
    title: t('columns.category'),
    width: 100,
    align: 'center',
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          bordered: false,
          type: row.category === PermissionCategory.Technical ? 'warning' : 'default',
        },
        {
          default: () =>
            row.category === PermissionCategory.Technical
              ? t('category.technical')
              : t('category.business'),
        },
      ),
  },
  {
    key: 'isEnabled',
    title: t('columns.isEnabled'),
    width: 100,
    align: 'center',
    render: (row) =>
      h(
        NTag,
        {
          size: 'small',
          type: row.isEnabled ? 'success' : 'warning',
          bordered: false,
        },
        { default: () => (row.isEnabled ? t('status.enabled') : t('status.disabled')) },
      ),
  },
])

async function loadModules(): Promise<void> {
  modulesLoading.value = true
  try {
    modules.value = await bridge.functionModules.getAll()
    // Auto-select first root module so the right pane isn't empty on first load.
    if (!selectedModuleId.value && modules.value.length) {
      const firstRoot = modules.value.find((m) => !m.parentId) ?? modules.value[0]
      if (firstRoot) {
        selectedModuleId.value = firstRoot.id
        await loadPermissionsFor(firstRoot.id)
      }
    }
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    modulesLoading.value = false
  }
}

async function loadPermissionsFor(moduleId: string): Promise<void> {
  permissionsLoading.value = true
  try {
    const result = await bridge.permissions.fetch({
      pageIndex: 1,
      pageSize: 200,
      sortField: 'order',
      sortOrder: 'asc' as const,
      searchText: '',
      filters: { moduleId },
    })
    permissions.value = result.items
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    permissions.value = []
  } finally {
    permissionsLoading.value = false
  }
}

function onSelectModule(keys: Array<string | number>): void {
  const key = keys[0] as string | undefined
  selectedModuleId.value = key ?? null
  permFilter.value = ''
  if (key) void loadPermissionsFor(key)
  else permissions.value = []
}

onMounted(() => {
  void loadModules()
})
</script>

<style scoped>
/* Fill-height chain (content-page iron rule): TContentPage scroll="fill"
   → card flex-fills the body → n-card__content flex column → layout grid
   claims the residual height → each pane scrolls internally. The white
   container always reaches the viewport bottom; no dead grey canvas. */
.t-permission-page__card {
  flex: 1 1 auto;
  min-height: 0;
}
.t-permission-page__card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
/* Master/detail grid, responsive stacking and pane scroll come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
.t-permission-page__naive-tree {
  margin-top: 8px;
  max-height: 64vh;
  overflow: auto;
}
.t-permission-page__detail {
  padding: 0 4px;
}
.t-permission-page__placeholder {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 60px 16px;
}
.t-permission-page__detail-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-bottom: 12px;
  gap: 16px;
}
.t-permission-page__module-name {
  margin: 0;
  font-size: 18px;
}
.t-permission-page__module-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-permission-page__count {
  color: var(--tnzi-base-text-muted);
  font-size: 13px;
  white-space: nowrap;
}
.t-permission-page__perm-filter {
  margin-bottom: 12px;
}
.t-permission-page__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  background: var(--tnzi-layout-bg);
  padding: 1px 6px;
  border-radius: 3px;
}
.t-permission-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}
</style>
