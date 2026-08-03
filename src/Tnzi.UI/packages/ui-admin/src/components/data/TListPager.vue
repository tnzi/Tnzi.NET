<template>
  <NPagination
    v-bind="config"
    @update:page="(v: number) => emit('update:page', v)"
    @update:page-size="(v: number) => emit('update:pageSize', v)"
  />
</template>

<script setup lang="ts">
/**
 * The admin list footer pager: a "Total N" prefix plus a page-size picker,
 * collapsing to naive's `simple` mode on phones.
 *
 * `TListShell` renders this for every page that rides it. It exists as its own
 * component so a page that does NOT ride the shell - an embedded table, a
 * drawer list, a tab pane with its own layout - gets the same footer instead of
 * reproducing it from a bare `NPagination` (which is how footers drift into
 * `size="small"` here and medium there, with and without the total).
 *
 * Controlled: bind `page` / `pageSize` and handle both update events.
 */
import { computed } from 'vue'
import { NPagination } from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { translatePageKey } from '../../i18n'

const props = withDefaults(
  defineProps<{
    page: number
    pageSize: number
    itemCount: number
    /** Page-size choices. Default `[10, 20, 50, 100]`. */
    pageSizes?: number[]
    /** Hide the size picker even on desktop (fixed-size lists). */
    showSizePicker?: boolean
    /** Override the label resolver; defaults to the admin i18n layer. */
    translate?: (key: string) => string
  }>(),
  {
    pageSizes: () => [10, 20, 50, 100],
    showSizePicker: true,
    translate: undefined,
  },
)

const emit = defineEmits<{
  (e: 'update:page', value: number): void
  (e: 'update:pageSize', value: number): void
}>()

const bp = useBreakpoint()
const t = (key: string) => props.translate?.(key) ?? translatePageKey('', key)

const config = computed(() => {
  const base = {
    page: props.page,
    pageSize: props.pageSize,
    itemCount: props.itemCount,
  }
  // Phones: naive's `simple` mode. A size picker plus a total plus page
  // buttons does not fit, and the one people reach for is the page buttons.
  if (bp.isSm.value) return { ...base, simple: true, showSizePicker: false }
  return {
    ...base,
    showSizePicker: props.showSizePicker,
    pageSizes: props.pageSizes,
    prefix: ({ itemCount }: { itemCount: number | undefined }) =>
      `${t('admin.crud.total')} ${itemCount ?? 0}`,
  }
})
</script>
