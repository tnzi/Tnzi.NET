<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
  >
    <template #form="{ formData, mode }">
      <NForm :disabled="mode === 'view'" label-placement="left" label-width="140px">
        <NFormItem :label="t('form.name')" required>
          <NInput
            :value="(formData as MenuRow)?.name ?? ''"
            @update:value="(v: string) => set('name', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.type')" required>
          <NSelect
            :value="(formData as MenuRow)?.type ?? 1"
            :options="typeOptions"
            @update:value="(v: number) => set('type', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.parentId')">
          <NSelect
            :value="(formData as MenuRow)?.parentId ?? null"
            :options="parentOptions"
            :placeholder="t('form.parentIdPlaceholder')"
            clearable
            filterable
            @update:value="(v: string | null) => set('parentId', v ?? undefined)"
          />
        </NFormItem>
        <NFormItem :label="t('form.path')">
          <NInput
            :value="(formData as MenuRow)?.path ?? ''"
            @update:value="(v: string) => set('path', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.component')">
          <NInput
            :value="(formData as MenuRow)?.component ?? ''"
            @update:value="(v: string) => set('component', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.icon')">
          <NInput
            :value="(formData as MenuRow)?.icon ?? ''"
            placeholder="mdi:home-outline"
            @update:value="(v: string) => set('icon', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.permission')">
          <NInput
            :value="(formData as MenuRow)?.permission ?? ''"
            @update:value="(v: string) => set('permission', v)"
          />
        </NFormItem>
        <NFormItem :label="t('form.sortOrder')">
          <NInputNumber
            :value="(formData as MenuRow)?.sortOrder ?? 0"
            :min="0"
            @update:value="(v: number | null) => set('sortOrder', v ?? 0)"
          />
        </NFormItem>
        <NFormItem :label="t('form.isHidden')">
          <NSwitch
            :value="(formData as MenuRow)?.isHidden ?? false"
            @update:value="(v: boolean) => set('isHidden', v)"
          />
        </NFormItem>
      </NForm>
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="(row as MenuRow)" :state="crud" :translate="t" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import { NForm, NFormItem, NInput, NInputNumber, NSelect, NSwitch } from 'naive-ui'
import { Icon } from '@iconify/vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { MenuRow } from './menu-config'

const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('system.menus', key)

const allMenus = ref<MenuRow[]>([])
const menuById = computed(() => {
  const m = new Map<string, MenuRow>()
  for (const x of allMenus.value) if (x.id) m.set(x.id, x)
  return m
})

async function loadAllMenus(): Promise<void> {
  try {
    // Re-use the existing fetch — backend GetMenus returns the full list so
    // a single high-pageSize call is cheap.
    const result = await bridge.menus.fetch({
      pageIndex: 1,
      pageSize: 500,
      searchText: '',
      filters: {},
      sortField: 'sortOrder',
      sortOrder: 'asc',
    })
    allMenus.value = result.items as MenuRow[]
  } catch {
    allMenus.value = []
  }
}

const editingId = computed(() => (crud.formModal.formData.value as MenuRow | null)?.id)

const parentOptions = computed(() => {
  /** Walk parent chain with visited-set so cyclic data terminates. */
  const depthOf = (id: string | undefined): number => {
    if (!id) return 0
    const visited = new Set<string>()
    let d = 0
    let cur = menuById.value.get(id)
    while (cur?.parentId) {
      if (visited.has(cur.parentId)) break
      visited.add(cur.parentId)
      d += 1
      cur = menuById.value.get(cur.parentId)
      if (d > 32) break
    }
    return d
  }
  return allMenus.value
    .filter((m) => m.id && m.id !== editingId.value)
    .map((m) => ({
      label: `${'  '.repeat(depthOf(m.id))}${m.name ?? '(unnamed)'}`,
      value: m.id!,
    }))
})

// MenuType enum: 0=Directory, 1=Menu, 2=Button.
const typeOptions = [
  { label: 'Directory', value: 0 },
  { label: 'Menu', value: 1 },
  { label: 'Button', value: 2 },
]
function typeLabel(v?: number): string {
  return typeOptions.find((o) => o.value === v)?.label ?? '—'
}

const columns: ColumnDef<MenuRow>[] = [
  { key: 'name', title: 'columns.name', width: 220, fixed: 'left' },
  {
    key: 'icon',
    title: 'columns.icon',
    width: 80,
    render: (row) =>
      row.icon
        ? h('span', { style: 'display: inline-flex; align-items: center; gap: 4px' }, [
            h(Icon, { icon: row.icon, width: 16, height: 16 }),
          ])
        : h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—'),
  },
  {
    key: 'parentName',
    title: 'columns.parentName',
    width: 180,
    render: (row) => {
      if (!row.parentId) return h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—')
      const p = menuById.value.get(row.parentId)
      return p?.name ?? row.parentId
    },
  },
  { key: 'path', title: 'columns.path', width: 220 },
  { key: 'permission', title: 'columns.permission', width: 180 },
  {
    key: 'type',
    title: 'columns.type',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.type ?? 0,
        type: row.type === 0 ? 'info' : row.type === 1 ? 'success' : 'warning',
        label: typeLabel(row.type),
      }),
  },
  { key: 'sortOrder', title: 'columns.sortOrder', width: 80 },
  {
    key: 'isHidden',
    title: 'columns.isHidden',
    width: 110,
    fixed: 'right',
    render: (row) =>
      h(TStatusBadge, {
        value: row.isHidden ?? false,
        mapping: {
          true: { type: 'warning', labelKey: 'admin.modules.system.menus.status.hidden' },
          false: { type: 'success', labelKey: 'admin.modules.system.menus.status.visible' },
        },
      }),
  },
]

const crud = useCrudPage<MenuRow, string>({
  pageId: 'system.menus',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.menus.fetch(query) as never,
  createData: async (data) => {
    const created = await bridge.menus.create(data as never)
    await loadAllMenus()
    return created as MenuRow
  },
  updateData: async (id, data) => {
    const updated = await bridge.menus.update(String(id), data as never)
    await loadAllMenus()
    return updated as MenuRow
  },
  deleteData: async (ids) => {
    await bridge.menus.delete(ids.map(String))
    await loadAllMenus()
  },
})

crud.refresh().catch(() => undefined)

function set(key: keyof MenuRow, value: unknown): void {
  if (!crud.formModal.formData.value) {
    crud.formModal.formData.value = {} as MenuRow
  }
  ;(crud.formModal.formData.value as unknown as Record<string, unknown>)[key as string] = value
}

onMounted(() => {
  void loadAllMenus()
})
</script>
