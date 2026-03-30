<script setup lang="ts" generic="T extends Record<string, unknown>">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IMobileDataListEmits, IMobileDataListProps } from '@tnzi/core/components';
import { normalizePageSize, updatePageQuery } from '@tnzi/core/utils';

const props = withDefaults(defineProps<IMobileDataListProps<T>>(), {
  items: () => [] as T[],
  query: () => ({}),
  loadState: () => ({}),
  trigger: 'hybrid',
  pullToRefresh: true,
  emptyText: '',
});

const emit = defineEmits<IMobileDataListEmits<T>>();
const { t } = useI18n();

const refreshing = ref(false);
const loading = computed(() => !!props.loadState?.loading);
const finished = computed(() => !!props.loadState?.noMore);
const isEmpty = computed(() => props.items.length === 0 && !loading.value);
const emptyText = computed(() => props.emptyText || t('common.noData'));

const resolveKey = (item: T, index: number) => {
  if (typeof props.itemKey === 'function') return props.itemKey(item, index);
  if (typeof props.itemKey === 'string' && item[props.itemKey as keyof T] != null) {
    return String(item[props.itemKey as keyof T]);
  }
  return String(index);
};

const onRefresh = async () => {
  refreshing.value = true;
  emit('pullRefresh');
  emit('refresh');
  const normalizedPageSize = normalizePageSize(
    typeof props.query?.pageSize === 'number' ? props.query.pageSize : undefined
  );
  emit('update:query', {
    ...updatePageQuery(props.query ?? {}, 1, normalizedPageSize),
    cursor: undefined,
  });
  // 延迟关闭 refreshing，让 pull-to-refresh 动画有时间完成
  await new Promise<void>(resolve => setTimeout(resolve, 300));
  refreshing.value = false;
};

const onLoad = () => emit('loadMore');
</script>

<template>
  <section class="t-data-list" :class="props.class" :style="props.style">
    <van-pull-refresh
      v-if="props.pullToRefresh"
      v-model="refreshing"
      :disabled="loading"
      @refresh="onRefresh"
    >
      <van-list :loading="loading" :finished="finished" :finished-text="t('table.noMore')" @load="onLoad">
        <template v-if="isEmpty">
          <div class="empty">{{ emptyText }}</div>
        </template>
        <template v-else>
          <div
            v-for="(item, index) in props.items"
            :key="resolveKey(item, index)"
            class="item"
            @click="emit('itemClick', item, index)"
          >
            <slot name="item" :item="item" :index="index">
              <pre>{{ item }}</pre>
            </slot>
          </div>
        </template>
      </van-list>
    </van-pull-refresh>

    <van-list v-else :loading="loading" :finished="finished" :finished-text="t('table.noMore')" @load="onLoad">
      <template v-if="isEmpty">
        <div class="empty">{{ emptyText }}</div>
      </template>
      <template v-else>
        <div
          v-for="(item, index) in props.items"
          :key="resolveKey(item, index)"
          class="item"
          @click="emit('itemClick', item, index)"
        >
          <slot name="item" :item="item" :index="index">
            <pre>{{ item }}</pre>
          </slot>
        </div>
      </template>
    </van-list>
  </section>
</template>

<style scoped>
.t-data-list {
  background: #fff;
  border-radius: 12px;
  overflow: hidden;
}

.item {
  padding: 12px;
  border-bottom: 1px solid #f1f5f9;
}

.item:last-child {
  border-bottom: 0;
}

.empty {
  padding: 24px 12px;
  text-align: center;
  color: #64748b;
}

pre {
  margin: 0;
  white-space: pre-wrap;
  font-size: 12px;
}
</style>
