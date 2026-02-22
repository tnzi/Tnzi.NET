<template>
  <div class="space-y-4">
    <!-- TDataTable -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TDataTable</CardTitle>
        <CardDescription>Data table with sorting, pagination, and row actions.</CardDescription>
      </CardHeader>
      <CardContent>
        <TDataTable
          :data="demoTableData"
          :columns="demoTableColumns"
          :actions="demoTableActions"
          :selectable="true"
          :striped="true"
          :pagination="pagination"
          row-key="id"
          @sort="onSort"
          @action="onAction"
          @row-click="onRowClick"
          @page-change="onPageChange"
        >
          <template #cell-status="{ row }">
            <span
              class="inline-flex rounded-full px-2 py-0.5 text-xs font-medium"
              :class="statusClass(row.status)"
            >
              {{ row.status }}
            </span>
          </template>
          <template #cell-price="{ row }">
            ${{ row.price.toFixed(2) }}
          </template>
        </TDataTable>
      </CardContent>
    </Card>

    <!-- TDataList -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TDataList</CardTitle>
        <CardDescription>Data list with item rendering and pagination.</CardDescription>
      </CardHeader>
      <CardContent>
        <TDataList
          :items="demoTableData"
          item-key="id"
          :pager="listPager"
          @item-click="onItemClick"
          @refresh="onRefresh"
          @page-change="onListPageChange"
        >
          <template #item="{ item }">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium">{{ item.name }}</p>
                <p class="text-xs text-muted-foreground">{{ item.category }} - ${{ item.price.toFixed(2) }}</p>
              </div>
              <span
                class="inline-flex rounded-full px-2 py-0.5 text-xs font-medium"
                :class="statusClass(item.status)"
              >
                {{ item.status }}
              </span>
            </div>
          </template>
        </TDataList>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import {
  Card, CardHeader, CardTitle, CardDescription, CardContent,
  TDataTable, TDataList,
  useShadcnMessage,
} from '@tnzi/shadcn';
import { demoTableData, demoTableColumns, demoTableActions } from '@tnzi/core/playground';

const message = useShadcnMessage();

const pagination = {
  pageIndex: 1,
  pageSize: 5,
  total: demoTableData.length,
  showTotal: true,
};

const listPager = {
  pageIndex: 1,
  pageSize: 5,
  total: demoTableData.length,
  showTotal: true,
};

const statusClass = (status: string) => {
  const map: Record<string, string> = {
    active: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900 dark:text-emerald-300',
    inactive: 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400',
    draft: 'bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-300',
  };
  return map[status] || '';
};

const onSort = (sortBy: string, order: string | undefined) => {
  console.log('[TDataTable] sort:', sortBy, order);
  message.show(`Sort by ${sortBy} ${order || ''}`, 'info');
};

const onAction = (actionKey: string, row: any, index: number) => {
  console.log('[TDataTable] action:', actionKey, row, index);
  message.show(`${actionKey}: ${row.name}`, 'info');
};

const onRowClick = (row: any, index: number) => {
  console.log('[TDataTable] row click:', row, index);
};

const onPageChange = (page: number, pageSize: number) => {
  console.log('[TDataTable] page change:', page, pageSize);
  message.show(`Page ${page}`, 'info');
};

const onItemClick = (item: any, index: number) => {
  console.log('[TDataList] item click:', item, index);
  message.show(`Clicked: ${item.name}`, 'info');
};

const onRefresh = () => {
  console.log('[TDataList] refresh');
  message.show('List refreshed', 'success');
};

const onListPageChange = (page: number, pageSize: number) => {
  console.log('[TDataList] page change:', page, pageSize);
};
</script>
