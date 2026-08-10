<template>
  <!--
    TYPE FIXTURE - compiled by `pnpm typecheck`, never mounted, never rendered.

    It stands in for a consuming app's page: a DTO that is an `interface` with
    REQUIRED fields (not the all-optional `type FooRow = { … }` alias the
    built-in configs use as a workaround), columns and row actions declared
    against that DTO, and a tab label that is a render function.

    Everything below must compile with NO cast and NO `@ts-nocheck`. Before
    2026-08-09 each of the three marked spots was an error; the guard against
    them silently regressing is this file being in `tsconfig.json`'s `include`.
    See `__tests__/types/README.md` for the mutation that must turn it red.
  -->
  <div>
    <TCrudPage
      :state="crud"
      :all-columns="columns"
      :row-actions="rowActions"
      title="Matters"
    />
    <!-- Backward compatibility: the ~60 built-in configs declare a plain
         `ColumnDef[]` next to a DTO-typed `useCrudPage`. That must keep
         compiling - this is a widening, not a rename. -->
    <TCrudPage :state="legacyCrud" :all-columns="looseColumns" title="Matters (legacy)" />
    <TTabsPage :sections="sections" title="Matters" />
  </div>
</template>

<script setup lang="ts">
import { h } from 'vue'
import { useRouter } from 'vue-router'
import { NBadge } from 'naive-ui'
import TCrudPage from '../../src/components/crud/TCrudPage.vue'
import TTabsPage from '../../src/components/layout/TTabsPage.vue'
import type { TabSection } from '../../src/components/layout/TTabsPage.vue'
import { useCrudPage } from '../../src/headless/useCrudPage'
import type { ColumnDef } from '../../src/headless/useColumnSettings'
import { editAction, deleteAction, type RowAction } from '../../src/headless/row-actions'

/** An interface with required fields - the shape that used to be rejected.
 *  Deliberately NOT the all-optional row alias the framework's own configs
 *  adopted to work around the contravariance. */
interface MatterSummaryDto {
  id: string
  matterNumber: string
  title: string
  openedOn: string
  clientId: string
}

const router = useRouter()

const crud = useCrudPage<MatterSummaryDto, string>({
  pageId: 'matters',
  columns: [],
  rowKey: (r) => r.id,
  fetchData: () =>
    Promise.resolve({
      items: [] as MatterSummaryDto[],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    }),
  updateData: (id, data) => Promise.resolve({ ...(data as MatterSummaryDto), id }),
  deleteData: () => Promise.resolve(),
})

// (1) Columns typed against the DTO. `row` is the DTO inside `render`, so a
//     typo in a field name is an error here rather than a blank cell.
const columns: ColumnDef<MatterSummaryDto>[] = [
  { key: 'matterNumber', title: 'No.', width: 120 },
  { key: 'title', title: 'Title', minWidth: 220, render: (row) => h('span', row.title) },
  { key: 'openedOn', title: 'Opened', width: 140, render: (row) => row.openedOn },
]

// (2) `onClick` returning something other than `void | Promise<void>`:
//     vue-router's `Promise<NavigationFailure | undefined>`, and the
//     assignment-expression shorthand.
let lastOpened: MatterSummaryDto | null = null
const rowActions: RowAction<MatterSummaryDto>[] = [
  editAction(crud),
  { key: 'open', label: 'Open', onClick: (row) => router.push(`/matters/${row.id}`) },
  { key: 'select', label: 'Select', onClick: (row) => (lastOpened = row) },
  { key: 'client', label: 'Client', onClick: (row) => router.push({ name: 'clients.detail', params: { id: row.clientId } }) },
  deleteAction(crud),
]
void lastOpened

// Backward-compat half: loose `ColumnDef[]` (the default `Record<string,
// unknown>` row) handed to a DTO-typed page, exactly as the built-in configs
// do it. Widening the boundary must not have broken this.
const looseColumns: ColumnDef[] = [
  { key: 'matterNumber', title: 'No.', width: 120 },
  { key: 'title', title: 'Title', minWidth: 220, render: (row) => String(row.title ?? '') },
]
const legacyCrud = useCrudPage<MatterSummaryDto>({
  pageId: 'matters.legacy',
  columns: looseColumns,
  rowKey: (r) => r.id,
  fetchData: () =>
    Promise.resolve({
      items: [] as MatterSummaryDto[],
      totalCount: 0,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    }),
})

// (3) A tab label that is a render function, alongside plain-string labels.
const sections: TabSection[] = [
  { name: 'overview', label: 'Overview' },
  { name: 'files', label: () => h(NBadge, { value: 3 }, { default: () => 'Files' }) },
  { name: 'billing', label: h('span', 'Billing') },
]
</script>
