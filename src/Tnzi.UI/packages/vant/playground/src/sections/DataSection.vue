<script setup lang="ts">
import { ref } from 'vue';
import { showToast } from 'vant';
import {
  demoTableData,
  demoTableColumns,
  demoTableActions,
} from '@tnzi/core/playground';

// TDataTable
const onTableAction = (action: string, row: unknown) => {
  console.log('Table action:', action, row);
  showToast(`Action: ${action}`);
};

// TDataList
const listQuery = ref({ page: 1, pageSize: 10 });
const listLoadState = ref({ loading: false, noMore: true });

const onListItemClick = (item: unknown) => {
  console.log('List item click:', item);
  showToast('Item clicked');
};
</script>

<template>
  <div class="data-section">
    <!-- TDataTable -->
    <van-cell-group inset title="TDataTable">
      <div class="component-wrapper">
        <TDataTable
          :data="demoTableData"
          :columns="demoTableColumns"
          :actions="demoTableActions"
          @action="onTableAction"
        />
      </div>
    </van-cell-group>

    <!-- TDataList -->
    <van-cell-group inset title="TDataList">
      <div class="component-wrapper">
        <TDataList
          :items="demoTableData"
          :query="listQuery"
          :load-state="listLoadState"
          @item-click="onListItemClick"
        >
          <template #item="{ item }">
            <van-cell
              :title="item.name"
              :label="`${item.category} - $${item.price}`"
              :value="item.status"
            />
          </template>
        </TDataList>
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.data-section {
  padding-bottom: 8px;
}

.component-wrapper {
  padding: 4px 0;
}
</style>
