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
  items?: T[];
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
  /**
   * How the next page is requested.
   *  - `hybrid` (default): infinite scroll plus pull-to-refresh.
   *  - `scroll`: infinite scroll only.
   *  - `pull`: pull-to-refresh only; the list never asks for more on its own.
   *  - `manual`: a "more" button (override it with the `load-more` slot) asks
   *    for the next page; scrolling never does.
   * `pullToRefresh: false` switches the pull gesture off in any mode.
   */
  trigger?: MobileLoadTrigger;
  pullToRefresh?: boolean;
  /** Label of the `manual` mode button. Defaults to the `common.more` message. */
  loadMoreText?: string;
}>(), {
  items: () => [] as T[],
  query: () => ({}),
  loadState: () => ({}),
  trigger: 'hybrid',
  pullToRefresh: true,
  emptyText: '',
  loadMoreText: '',
});

const emit = defineEmits<IDataListEmits<T>>();
const { t } = useI18n();

const refreshing = ref(false);
const loading = computed(() => !!props.loadState?.loading);
const tracksLoading = computed(() => typeof props.loadState?.loading === 'boolean');
const finished = computed(() => !!props.loadState?.noMore);
const isEmpty = computed(() => props.items.length === 0 && !loading.value);
const emptyText = computed(() => props.emptyText || t('common.noData'));
const loadMoreText = computed(() => props.loadMoreText || t('common.more'));

const autoLoad = computed(() => props.trigger === 'scroll' || props.trigger === 'hybrid');
const manualLoad = computed(() => props.trigger === 'manual');
const pullEnabled = computed(
  () => props.pullToRefresh && (props.trigger === 'pull' || props.trigger === 'hybrid'),
);

// A page request that has not come back yet. van-list resets its own loading
// flag to the `loading` prop on every re-render, so with nothing mirrored back
// every scroll event during a pending fetch would emit `loadMore` again and the
// same page would be fetched twice. Cleared when the rows change (the page
// arrived), when the parent's `loading` goes true -> false, or when it reports
// `noMore` or an `error`. A consumer that reports no load state and whose
// fetch fails without touching the rows is the one case nothing releases.
const loadingMore = ref(false);
const listLoading = computed(() => loading.value || loadingMore.value);

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
    if (wasLoading && !isLoading) {
      refreshing.value = false;
      loadingMore.value = false;
    }
  },
);
// The rows, by reference AND by length: the Vant idiom is `list.value.push(
// ...page)` on the same reactive array, which a reference watch never sees.
watch(() => [props.items, props.items.length], () => { loadingMore.value = false; });
watch(finished, (isFinished) => { if (isFinished) loadingMore.value = false; });
watch(() => props.loadState?.error, (error) => { if (error) loadingMore.value = false; });

const onLoad = () => {
  if (loadingMore.value) return;
  loadingMore.value = true;
  emit('loadMore');
};
</script>

<template>
  <section class="t-data-list">
    <van-pull-refresh
      v-model="refreshing"
      :disabled="!pullEnabled || loading"
      @refresh="onRefresh"
    >
      <van-list
        :loading="listLoading"
        :finished="finished"
        :finished-text="t('table.noMore')"
        :disabled="!autoLoad"
        :immediate-check="autoLoad"
        @load="onLoad"
      >
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
      <div v-if="manualLoad && !finished && !isEmpty" class="load-more">
        <slot name="load-more" :load="onLoad" :loading="listLoading">
          <van-button
            class="t-data-list__load-more"
            size="small"
            plain
            type="primary"
            :loading="listLoading"
            @click="onLoad"
          >
            {{ loadMoreText }}
          </van-button>
        </slot>
      </div>
    </van-pull-refresh>
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

.load-more {
  padding: 12px;
  text-align: center;
}

pre {
  margin: 0;
  white-space: pre-wrap;
  font-size: 12px;
}
</style>
