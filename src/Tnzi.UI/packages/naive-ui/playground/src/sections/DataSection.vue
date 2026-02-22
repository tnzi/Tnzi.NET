<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Data Display</h1>
      <p class="section-desc">Tables and lists for displaying structured data with sorting, pagination, and actions.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Data Table</h2>
      <PreviewBox :full-width="true">
        <TDataTable
          :data="demoTableData"
          :columns="demoTableColumns"
          :actions="demoTableActions"
          :pagination="{ pageIndex: 1, pageSize: 5, total: demoTableData.length }"
          striped
          bordered
          @action="handleAction"
        />
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Data List</h2>
      <PreviewBox :full-width="true">
        <TDataList :data="demoUsers" bordered @item-click="handleItemClick">
          <template #default="{ item }">
            <n-space justify="space-between" align="center" style="width: 100%">
              <n-space vertical :size="2">
                <n-text strong>{{ item.name }}</n-text>
                <n-text depth="3">{{ item.email }} - {{ item.role }}</n-text>
              </n-space>
              <n-tag :type="item.status === 'active' ? 'success' : 'default'" size="small">
                {{ item.status }}
              </n-tag>
            </n-space>
          </template>
        </TDataList>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useMessage } from 'naive-ui';
import {
  demoTableData,
  demoTableColumns,
  demoTableActions,
  demoUsers,
} from '@tnzi/core/playground';
import { TDataTable, TDataList, createNaiveMessageAdapter } from '@tnzi/naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const msgAdapter = createNaiveMessageAdapter(message);

function handleAction(actionKey: string, row: any) {
  msgAdapter.info(`Action "${actionKey}" on: ${row.name}`);
}

function handleItemClick(item: any) {
  msgAdapter.info(`Clicked: ${item.name}`);
}
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}
</style>
