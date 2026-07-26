<script setup lang="ts" generic="T extends Record<string, unknown>">
import { computed, ref, watch } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDataQuery, IDataLoadState, MobileLoadTrigger } from '@tnzi/core/types/shared-ui';
import { normalizePageSize, updatePageQuery } from '@tnzi/core/headless';

interface IDataListEmits<T = unknown> {
  'update:query': [query: IDataQuery];
  refresh: [];
  loadMore: [];
  itemClick: [item: T, index: number];
}

const props = withDefaults(defineProps<{
  items: T[];
  query?: IDataQuery;
  /**
   * Load state reported by the parent. When `loading` is a boolean, the
   * pull-to-refresh indicator mirrors it and closes on the `true -> false`
   * transition; when it is absent the indicator closes as soon as `refresh`
   * has been emitted, because there is nothing to wait for.
   */
  loadState?: IDataLoadState;
  itemKey?: string | ((item: T, index: number) => string);
  emptyText?: string;
  trigger?: MobileLoadTrigger;
  pullToRefresh?: boolean;
}>(), {
  items: () => [] as T[],
  query: () => ({}),
  loadState: () => ({}),
  trigger: 'hybrid',
  pullToRefresh: true,
  emptyText: '',
});

const emit = defineEmits<IDataListEmits<T>>();
const { t } = useI18n();

const refreshing = ref(false);
const loading = computed(() => !!props.loadState?.loading);
const tracksLoading = computed(() => typeof props.loadState?.loading === 'boolean');
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

const onRefresh = () => {
  emit('refresh');
  const normalizedPageSize = normalizePageSize(
    typeof props.query?.pageSize === 'number' ? props.query.pageSize : undefined
  );
  emit('update:query', {
    ...updatePageQuery(props.query ?? {}, 1, normalizedPageSize),
    cursor: undefined,
  });

  if (!tracksLoading.value) refreshing.value = false;
};

// Close the indicator when the parent's load actually finishes. A fixed timer
// would stop the animation while a slow request is still in flight and leave
// the stale list on screen.
watch(
  () => props.loadState?.loading,
  (isLoading, wasLoading) => {
    if (wasLoading && !isLoading) refreshing.value = false;
  },
);

const onLoad = () => emit('loadMore');
</script>

<template>
  <section class="t-data-list">
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
  background: var(--van-background-2);
  border-radius: 12px;
  overflow: hidden;
}

.item {
  padding: 12px;
  border-bottom: 1px solid var(--van-border-color);
}

.item:last-child {
  border-bottom: 0;
}

.empty {
  padding: 24px 12px;
  text-align: center;
  color: var(--van-text-color-2);
}

pre {
  margin: 0;
  white-space: pre-wrap;
  font-size: 12px;
}
</style>
