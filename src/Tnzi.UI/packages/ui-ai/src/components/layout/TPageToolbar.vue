<script setup lang="ts">
/**
 * @experimental
 * TPageToolbar - the one action bar of a content page.
 *
 * One row. On the left whatever narrows the page (`#start`: `TSegmented` tabs,
 * a filter chip), or nothing. On the right, in this order:
 *
 *   1. the search (`search`, `v-model:query`): an icon that opens in place into
 *      a compact field (`TToolbarSearch`);
 *   2. the page's own controls (`#actions`), if any;
 *   3. the icon actions (`actions`): a filter, a sort, ... each a button, or a
 *      menu when it has `items` (`checked` marks the current choice, `active`
 *      marks the button when something is applied);
 *   4. the primary action (`primary`): a small primary button with a "+" icon;
 *      with `items` the button opens a menu of ways to create, with `split`
 *      too only its chevron does.
 *
 * Controls are the small size (`--tnzi-ai-toolbar-control-size`, 30px). Menus
 * are `TActionMenu`s on the popover layer, above the page, its sticky bars and
 * any dialog.
 *
 * On a phone (below `narrowBreakpoint`) the row stays one line: the start
 * scrolls sideways, two or more icon actions fold into one "more" menu (an
 * action with a menu becomes a submenu; a single action keeps its button), the
 * primary button keeps its icon and drops its label (which stays its
 * accessible name), and an open search takes the row.
 *
 * Every element it renders takes a page's own attributes (`data-*` test
 * hooks): `actions[].attrs` / `items[].attrs`, `primary.attrs` /
 * `primary.menuAttrs`, `searchInputAttrs` / `searchButtonAttrs`, `moreAttrs`.
 * A folded item's id is `<actionId>::<itemId>` (`FOLDED_ID_SEPARATOR`).
 *
 * Put it in `TResourcePage`'s `#toolbar` slot, or anywhere a page needs it.
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import TActionMenu from '../overlay/TActionMenu.vue'
import TToolbarSearch from './TToolbarSearch.vue'
import { useViewportBelow } from '../../headless/useViewportBelow'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import {
  DEFAULT_PRIMARY_ICON,
  foldActions,
  parseFoldedId,
  shouldFoldActions,
  type ElementAttrs,
  type PageToolbarAction,
  type PageToolbarPrimary,
} from './page-toolbar'

const props = withDefaults(
  defineProps<{
    /** Icon actions, in order (filter, sort, ...). */
    actions?: ReadonlyArray<PageToolbarAction>
    /** The primary action, last in the row. */
    primary?: PageToolbarPrimary
    /** Show the collapsible search. */
    search?: boolean
    /** The search query (`v-model:query`). */
    query?: string
    searchLabel?: string
    searchPlaceholder?: string
    /** Width of the open search on a wide screen (number = px). */
    searchWidth?: number | string
    /** Longest query the search takes. */
    searchMaxlength?: number
    /** Attributes for the search field. */
    searchInputAttrs?: ElementAttrs
    /** Attributes for the icon that opens the search. */
    searchButtonAttrs?: ElementAttrs
    /** Attributes for the phone's "more" button. */
    moreAttrs?: ElementAttrs
    /** Viewport width (px) below which the phone layout applies. Default 768. */
    narrowBreakpoint?: number
    /** Force the phone layout (`true`) or the wide one (`false`). */
    narrow?: boolean
  }>(),
  {
    actions: () => [],
    primary: undefined,
    search: false,
    query: '',
    searchLabel: undefined,
    searchPlaceholder: undefined,
    searchWidth: 200,
    searchMaxlength: undefined,
    searchInputAttrs: undefined,
    searchButtonAttrs: undefined,
    moreAttrs: undefined,
    narrowBreakpoint: 768,
    narrow: undefined,
  },
)

const emit = defineEmits<{
  'update:query': [query: string]
  /** An icon action without a menu was chosen. */
  action: [actionId: string]
  /** An item of an icon action's menu was chosen. */
  select: [actionId: string, itemId: string]
  /** The primary button (or, when split, its main part) was pressed. */
  primary: []
  /** An item of the primary action's menu was chosen. */
  'primary-select': [itemId: string]
}>()

defineSlots<{
  start?: (props: { narrow: boolean }) => unknown
  actions?: (props: { narrow: boolean }) => unknown
  primary?: (props: { narrow: boolean }) => unknown
}>()

const t = useAiI18n()

const viewportNarrow = useViewportBelow(() => props.narrowBreakpoint)
const isNarrow = computed(() => props.narrow ?? viewportNarrow.value)

const searchOpen = ref(props.query !== '')
/* On a phone an open search has the row to itself. */
const searchTakesRow = computed(() => props.search && isNarrow.value && searchOpen.value)

const folded = computed(() => shouldFoldActions(isNarrow.value, props.actions.length))
const foldedItems = computed(() => (folded.value ? foldActions(props.actions, t.value.toolbar.active) : []))
const anyActive = computed(() => props.actions.some((action) => action.active))

function onFoldedSelect(id: string): void {
  const { actionId, itemId } = parseFoldedId(id)
  if (itemId === null) emit('action', actionId)
  else emit('select', actionId, itemId)
}

const primaryIcon = computed(() => {
  if (!props.primary) return null
  if (props.primary.loading) return 'lucide:loader-circle'
  return props.primary.icon === undefined ? DEFAULT_PRIMARY_ICON : props.primary.icon
})
/* On a phone the label goes when there is an icon to stand for it. */
const primaryLabelShown = computed(() => !isNarrow.value || !primaryIcon.value)
const primaryHasMenu = computed(() => Boolean(props.primary?.items?.length))
const primaryMenuLabel = computed(() =>
  props.primary ? formatAiMessage(t.value.toolbar.primaryMenu, { label: props.primary.label }) : '',
)
</script>

<template>
  <div
    class="t-page-toolbar"
    :class="{ 't-page-toolbar--narrow': isNarrow, 'is-searching': searchTakesRow }"
  >
    <div v-if="$slots.start && !searchTakesRow" class="t-page-toolbar__start t-ai-scroll">
      <slot name="start" :narrow="isNarrow" />
    </div>

    <div class="t-page-toolbar__tools">
      <TToolbarSearch
        v-if="search"
        v-model:expanded="searchOpen"
        class="t-page-toolbar__search"
        :model-value="query"
        :label="searchLabel"
        :placeholder="searchPlaceholder"
        :width="searchTakesRow ? '100%' : searchWidth"
        :maxlength="searchMaxlength"
        :input-attrs="searchInputAttrs"
        :button-attrs="searchButtonAttrs"
        @update:model-value="emit('update:query', $event)"
      />

      <template v-if="!searchTakesRow">
        <slot name="actions" :narrow="isNarrow" />

        <template v-if="!folded">
          <template v-for="action in actions" :key="action.id">
            <TActionMenu
              v-if="action.items?.length"
              :items="action.items"
              :label="action.label"
              @select="emit('select', action.id, $event)"
            >
              <template #trigger="{ toggle, attrs }">
                <button
                  v-bind="{ ...action.attrs, ...attrs }"
                  type="button"
                  class="t-page-toolbar-icon"
                  :class="{ 'is-active': action.active }"
                  :aria-label="action.label"
                  :title="action.label"
                  :disabled="action.disabled"
                  :data-toolbar-action="action.id"
                  @click="toggle"
                >
                  <Icon :icon="action.icon" aria-hidden="true" />
                </button>
              </template>
            </TActionMenu>
            <button
              v-else
              v-bind="action.attrs"
              type="button"
              class="t-page-toolbar-icon"
              :class="{ 'is-active': action.active }"
              :aria-label="action.label"
              :title="action.label"
              :disabled="action.disabled"
              :data-toolbar-action="action.id"
              @click="emit('action', action.id)"
            >
              <Icon :icon="action.icon" aria-hidden="true" />
            </button>
          </template>
        </template>

        <TActionMenu v-else-if="actions.length" :items="foldedItems" :label="t.toolbar.more" @select="onFoldedSelect">
          <template #trigger="{ toggle, attrs }">
            <button
              v-bind="{ ...moreAttrs, ...attrs }"
              type="button"
              class="t-page-toolbar-icon"
              :class="{ 'is-active': anyActive }"
              :aria-label="t.toolbar.more"
              :title="t.toolbar.more"
              data-toolbar-more
              @click="toggle"
            >
              <Icon icon="lucide:ellipsis" aria-hidden="true" />
            </button>
          </template>
        </TActionMenu>

        <div v-if="primary || $slots.primary" class="t-page-toolbar__primary">
          <slot name="primary" :narrow="isNarrow">
            <template v-if="primary">
              <!-- One button that opens the menu of ways to create. -->
              <TActionMenu
                v-if="primaryHasMenu && !primary.split"
                :items="primary.items!"
                :label="primary.label"
                @select="emit('primary-select', $event)"
              >
                <template #trigger="{ toggle, attrs }">
                  <button
                    v-bind="{ ...primary.attrs, ...attrs }"
                    type="button"
                    class="t-page-toolbar-button t-page-toolbar-button--primary"
                    :aria-label="primaryLabelShown ? undefined : primary.label"
                    :aria-busy="primary.loading || undefined"
                    :disabled="primary.disabled || primary.loading"
                    data-toolbar-primary
                    @click="toggle"
                  >
                    <Icon v-if="primaryIcon" :icon="primaryIcon" :class="{ 't-page-toolbar__spin': primary.loading }" aria-hidden="true" />
                    <span v-if="primaryLabelShown">{{ primary.label }}</span>
                    <Icon icon="lucide:chevron-down" class="t-page-toolbar-chevron" aria-hidden="true" />
                  </button>
                </template>
              </TActionMenu>

              <!-- The button acts; its chevron opens the menu. -->
              <div v-else-if="primaryHasMenu" class="t-page-toolbar-split">
                <button
                  v-bind="primary.attrs"
                  type="button"
                  class="t-page-toolbar-button t-page-toolbar-button--primary"
                  :aria-label="primaryLabelShown ? undefined : primary.label"
                  :aria-busy="primary.loading || undefined"
                  :disabled="primary.disabled || primary.loading"
                  data-toolbar-primary
                  @click="emit('primary')"
                >
                  <Icon v-if="primaryIcon" :icon="primaryIcon" :class="{ 't-page-toolbar__spin': primary.loading }" aria-hidden="true" />
                  <span v-if="primaryLabelShown">{{ primary.label }}</span>
                </button>
                <TActionMenu :items="primary.items!" :label="primary.label" @select="emit('primary-select', $event)">
                  <template #trigger="{ toggle, attrs }">
                    <button
                      v-bind="{ ...primary.menuAttrs, ...attrs }"
                      type="button"
                      class="t-page-toolbar-button t-page-toolbar-button--primary"
                      :aria-label="primaryMenuLabel"
                      :title="primaryMenuLabel"
                      :disabled="primary.disabled"
                      data-toolbar-primary-menu
                      @click="toggle"
                    >
                      <Icon icon="lucide:chevron-down" class="t-page-toolbar-chevron" aria-hidden="true" />
                    </button>
                  </template>
                </TActionMenu>
              </div>

              <button
                v-else
                v-bind="primary.attrs"
                type="button"
                class="t-page-toolbar-button t-page-toolbar-button--primary"
                :aria-label="primaryLabelShown ? undefined : primary.label"
                :aria-busy="primary.loading || undefined"
                :disabled="primary.disabled || primary.loading"
                data-toolbar-primary
                @click="emit('primary')"
              >
                <Icon v-if="primaryIcon" :icon="primaryIcon" :class="{ 't-page-toolbar__spin': primary.loading }" aria-hidden="true" />
                <span v-if="primaryLabelShown">{{ primary.label }}</span>
              </button>
            </template>
          </slot>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.t-page-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
  min-height: var(--tnzi-ai-toolbar-control-size, 30px);
}
.t-page-toolbar__start {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-page-toolbar__tools {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  margin-left: auto;
}
/* A little more room before the primary action than between the icons. */
.t-page-toolbar__primary {
  display: inline-flex;
  margin-left: 4px;
}
.t-page-toolbar__spin {
  animation: t-page-toolbar-spin 0.9s linear infinite;
}
@keyframes t-page-toolbar-spin {
  to {
    transform: rotate(360deg);
  }
}

/* -- A phone: one line. The start scrolls sideways under the fold of the
   tools; 16px field text so the browser does not zoom into the search. -- */
.t-page-toolbar--narrow {
  --tnzi-ai-field-font-size: 16px;
  flex-wrap: nowrap;
}
.t-page-toolbar--narrow .t-page-toolbar__start {
  flex: 1 1 auto;
  position: relative;
  overflow-x: auto;
  scrollbar-width: none;
}
.t-page-toolbar--narrow .t-page-toolbar__start::-webkit-scrollbar {
  display: none;
}
.t-page-toolbar--narrow .t-page-toolbar__tools {
  flex: none;
}
/* An open search on a phone has the row. */
.t-page-toolbar.is-searching .t-page-toolbar__tools {
  flex: 1 1 auto;
  margin-left: 0;
}
.t-page-toolbar.is-searching .t-page-toolbar__search {
  flex: 1 1 auto;
}
@media (prefers-reduced-motion: reduce) {
  .t-page-toolbar__spin {
    animation: none;
  }
}
</style>
