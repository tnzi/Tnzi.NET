<template>
  <NBreadcrumb v-if="visibleItems.length > 0" class="t-admin-breadcrumb" :separator="separator">
    <NBreadcrumbItem
      v-for="item in visibleItems"
      :key="item.to ?? item.label"
      :aria-busy="item.loading ? 'true' : undefined"
      @click="emit('itemClick', item)"
    >
      <slot name="icon" :item="item" />
      <template v-if="item.loading">
        <span class="t-admin-breadcrumb__placeholder" aria-hidden="true" />
        <!-- The placeholder is a bar; the crumb still needs a name for a screen
             reader, and the route-derived one is the best available guess until
             the record lands. `sr-only` is absolutely positioned, so it adds no
             flex item to the row. -->
        <span class="sr-only">{{ resolveLabel(item.label) }}</span>
      </template>
      <template v-else>{{ resolveLabel(item.label) }}</template>
    </NBreadcrumbItem>
  </NBreadcrumb>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NBreadcrumb, NBreadcrumbItem } from 'naive-ui'

export interface TAdminBreadcrumbItem {
  label: string
  to?: string
  icon?: string
  hidden?: boolean
  /**
   * The crumb is known to exist but its text is still loading - render a
   * placeholder bar instead of `label`.
   *
   * Used for a detail page's leaf while the record loads. The alternative the
   * breadcrumb used to fall back on is the route-derived title, which on a
   * detail route is inherited from the LIST: the reader sees `Clients / Clients`
   * and watches the second one turn into the person's name. A placeholder makes
   * that a single transition from "loading" to the record, and never shows a
   * name that was never true.
   */
  loading?: boolean
}

interface Props {
  items: TAdminBreadcrumbItem[]
  separator?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  separator: '/',
})

const emit = defineEmits<{
  itemClick: [item: TAdminBreadcrumbItem]
}>()

const visibleItems = computed(() => props.items.filter((i) => !i.hidden))

function resolveLabel(label: string): string {
  return props.translate ? props.translate(label) : label
}
</script>

<style scoped>
.t-admin-breadcrumb {
  display: flex;
  align-items: center;
  font-size: 14px;
  color: var(--tnzi-base-text-muted);
}
/* Lay the crumbs out as flex items rather than inline boxes.
   
   naive stacks its `<li>`s inline, so they align on their BASELINES - and an
   inline-flex box takes its baseline from its first flex item. A crumb whose
   first item is the icon therefore reports the icon's bottom edge as its
   baseline, ~1.75px lower than a crumb that starts with text, and the whole row
   shifts to accommodate it. Measured: with one icon present the row is 27.25px
   tall and the text sits at y=4.75; with no icons it is 25.5px and y=3. So the
   moment a detail page replaced the route-derived trail (icons) with its own
   (none), every label in the breadcrumb jumped up ~2px.
   
   `align-items: center` takes baselines out of it: each crumb is centred in a
   row whose height no longer depends on which crumbs happen to carry a glyph. */
/* `.t-admin-breadcrumb` IS the `.n-breadcrumb` root (the class lands on
   NBreadcrumb itself), so the list is a direct child - a descendant selector
   naming `.n-breadcrumb` again matches nothing. */
.t-admin-breadcrumb :deep(> ul) {
  display: flex;
  align-items: center;
  flex-wrap: nowrap;
}
/* naive-ui's `.n-breadcrumb-item__link` defaults to `display: block`,
   which stacks the slot icon above the label text on two lines. Force
   inline-flex so the icon sits on the same baseline as the label, with
   a small gap (the `mr-4px` on the slotted icon also contributes). */
.t-admin-breadcrumb :deep(.n-breadcrumb-item__link) {
  display: inline-flex;
  align-items: center;
}
/* The held leaf (see `TAdminBreadcrumbItem.loading`). Sized in `em` so it
   tracks the crumb's own font size, and shorter than the line box so it never
   changes the row height a real label would produce. */
.t-admin-breadcrumb__placeholder {
  display: inline-block;
  width: 72px;
  max-width: 30vw;
  height: 0.72em;
  border-radius: 3px;
  background: currentColor;
  opacity: 0.18;
  animation: t-admin-breadcrumb-pulse 1.4s ease-in-out infinite;
}
@keyframes t-admin-breadcrumb-pulse {
  0%, 100% { opacity: 0.13; }
  50% { opacity: 0.26; }
}
@media (prefers-reduced-motion: reduce) {
  .t-admin-breadcrumb__placeholder {
    animation: none;
  }
}
@media (max-width: 640px) {
  .t-admin-breadcrumb {
    font-size: 12px;
  }
}
</style>
