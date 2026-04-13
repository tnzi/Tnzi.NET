<script setup lang="ts" generic="T extends Record<string, any>">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDataQuery, IDataLoadState, IWebPagerConfig } from '@tnzi/core/types/shared-ui';
import { calculateTotalPages, clampPageIndex, updatePageQuery } from '@tnzi/core/utils';
import { Button } from '../primitive/ui';

interface IDataListEmits<T = unknown> {
  'update:query': [query: IDataQuery];
  refresh: [];
  loadMore: [];
  itemClick: [item: T, index: number];
}

interface IWebDataListEmits<T = unknown> extends IDataListEmits<T> {
  pageChange: [pageIndex: number, pageSize: number];
}

const props = withDefaults(defineProps<{
  items: T[];
  query?: IDataQuery;
  loadState?: IDataLoadState;
  itemKey?: string | ((item: T, index: number) => string);
  emptyText?: string;
  class?: string | string[];
  style?: string | Record<string, string | number>;
  mode?: 'table' | 'list';
  pager?: IWebPagerConfig | false;
}>(), {
  items: () => [] as T[],
  query: () => ({}),
  loadState: () => ({}),
  mode: 'list',
  emptyText: '',
});

const emit = defineEmits<IWebDataListEmits<T>>();
const { t } = useI18n();

const pagerConfig = computed(() => {
  const pager = props.pager as IWebPagerConfig | false | undefined;
  return pager ? pager : undefined;
});

const currentPage = ref(pagerConfig.value?.pageIndex ?? 1);
const pageSize = ref(pagerConfig.value?.pageSize ?? 10);

const isLoading = computed(() => !!props.loadState?.loading);
const isEmpty = computed(() => props.items.length === 0 && !isLoading.value);
const emptyText = computed(() => props.emptyText || t('common.noData'));

const total = computed(() => pagerConfig.value?.total ?? props.items.length);
const totalPages = computed(() => calculateTotalPages(total.value, pageSize.value));
const hasPager = computed(() => !!pagerConfig.value);

const goPage = (page: number) => {
  const next = clampPageIndex(page, total.value, pageSize.value);
  currentPage.value = next;
  emit('pageChange', next, pageSize.value);
  emit('update:query', updatePageQuery(props.query ?? {}, next, pageSize.value));
};

const onRefresh = () => emit('refresh');

const resolveKey = (item: T, index: number) => {
  if (typeof props.itemKey === 'function') return props.itemKey(item, index);
  if (typeof props.itemKey === 'string' && item[props.itemKey as keyof T] != null) {
    return String(item[props.itemKey as keyof T]);
  }
  return String(index);
};
</script>

<template>
  <section class="space-y-3 rounded-xl border bg-card p-4 text-card-foreground" :class="props.class" :style="props.style">
    <div class="flex items-center justify-between">
      <slot name="toolbar" :query="props.query" :load-state="props.loadState" />
      <Button variant="default" size="sm" :disabled="isLoading" @click="onRefresh">
        {{ t('table.refresh') }}
      </Button>
    </div>

    <div v-if="isLoading" class="py-6 text-center text-sm text-muted-foreground">
      {{ t('common.loading') }}
    </div>

    <div v-else-if="isEmpty" class="py-6 text-center text-sm text-muted-foreground">
      {{ emptyText }}
    </div>

    <ul v-else class="space-y-2">
      <li
        v-for="(item, index) in props.items"
        :key="resolveKey(item, index)"
        class="cursor-pointer rounded-md border p-3 transition hover:bg-muted/40"
        @click="emit('itemClick', item, index)"
      >
        <slot name="item" :item="item" :index="index">
          <pre class="whitespace-pre-wrap text-xs">{{ item }}</pre>
        </slot>
      </li>
    </ul>

    <footer v-if="hasPager" class="flex items-center justify-between border-t pt-3 text-sm">
      <span class="text-muted-foreground">
        {{ pagerConfig && pagerConfig.showTotal ? t('table.total', { count: total }) : '' }}
      </span>
      <div class="flex items-center gap-2">
        <Button variant="default" size="sm" :disabled="currentPage <= 1" @click="goPage(currentPage - 1)">
          {{ t('common.prev') }}
        </Button>
        <span>{{ currentPage }} / {{ totalPages }}</span>
        <Button variant="default" size="sm" :disabled="currentPage >= totalPages" @click="goPage(currentPage + 1)">
          {{ t('common.next') }}
        </Button>
      </div>
    </footer>
  </section>
</template>

