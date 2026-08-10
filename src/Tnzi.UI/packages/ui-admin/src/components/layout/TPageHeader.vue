<template>
  <header
    class="t-page-header"
    :class="{
      't-page-header--bordered': bordered,
      't-page-header--surface': surface,
      't-page-header--inline-actions': effectiveInlineActions,
    }"
  >
    <div class="t-page-header__bar">
      <!-- Column 1. Back is CHROME, not identity - it leaves the page rather
           than describing it - so it gets its own column and never contributes
           to the width that sizes the title. Keeping it out of `#title` also
           means a rich custom title (avatar + name + tags) cannot swallow it. -->
      <button
        v-if="showBack"
        type="button"
        class="t-page-header__back"
        :aria-label="t('admin.common.back')"
        @click="onBack"
      >
        <TSvgIcon icon="mdi:arrow-left" :size="18" />
      </button>

      <!-- Column 2. The identity AND its subtitle share one column, which is
           what makes `#extra` line up with the title BY CONSTRUCTION - there is
           no offset to re-apply. It is also the only flexible column, so a long
           title / badge row / subtitle wraps inside it and can never push the
           actions anywhere. -->
      <div class="t-page-header__main">
        <div class="t-page-header__left">
          <slot name="title">
            <TSvgIcon v-if="resolvedIcon" :icon="resolvedIcon" :size="20" class="t-page-header__icon" />
            <span class="t-page-header__title">{{ resolvedTitle }}</span>
            <NPopover v-if="resolvedHelp" trigger="hover" placement="bottom-start">
              <template #trigger>
                <button type="button" class="t-page-header__help" :aria-label="resolvedHelpTitle">
                  <TSvgIcon icon="mdi:information-outline" :size="16" />
                </button>
              </template>
              <div class="t-page-header__help-content">
                <div v-if="resolvedHelpTitle" class="t-page-header__help-title">{{ resolvedHelpTitle }}</div>
                <div class="t-page-header__help-body">{{ resolvedHelp }}</div>
              </div>
            </NPopover>
          </slot>
          <!-- Phone disclosure for `#extra`. The subtitle is the first thing
               worth spending header height on when height is scarce, but it
               must not just vanish - a visible chevron says there is more. -->
          <button
            v-if="extraCollapsible"
            type="button"
            class="t-page-header__extra-toggle"
            :class="{ 'is-open': extraOpen }"
            :aria-expanded="extraOpen"
            aria-controls="t-page-header-extra"
            :aria-label="t(extraOpen ? 'admin.common.hideDetails' : 'admin.common.showDetails')"
            @click="extraOpen = !extraOpen"
          >
            <TSvgIcon icon="mdi:chevron-down" :size="16" />
          </button>
        </div>
        <div v-if="showExtra" id="t-page-header-extra" class="t-page-header__extra"><slot name="extra" /></div>
      </div>

      <!-- Column 3. A declarative `actions` list lets the framework own the
           collapse: on phones every action moves into the "More" menu, so the
           buttons stop claiming a whole row of a header whose height is
           subtracted from the readable area. A `#actions` slot still wins - it
           may hold things that are not buttons (TListShell's search cluster). -->
      <div v-if="$slots.actions || hasDeclarativeActions" class="t-page-header__actions">
        <slot name="actions">
          <TRowActions
            :row="noRow"
            :actions="actions"
            :max-inline="effectiveMaxInline"
            :translate="translate"
          />
        </slot>
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed, ref, useSlots } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'
import { NPopover } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TRowActions from '../crud/TRowActions.vue'
import { useBreakpoint } from '../../headless/useBreakpoint'
import type { RowAction } from '../../headless/row-actions'
import { DEFAULT_ROUTE_ICONS } from '../../router/route-icons'
import { maybeTranslateKey } from '../../i18n/translate'
import { runBack } from './back-target'

interface Props {
  title?: string
  icon?: string
  help?: string
  helpTitle?: string
  /**
   * Show a back affordance.
   *  - `true`   → `router.back()` (browser history).
   *  - `string` → `router.push(path)` (a static parent).
   *  - `{ fallback }` → SMART back: in-app history when present (restores the
   *    origin WITH its `?section=…` deep-link), else push `fallback`. Preferred
   *    for a drilled-into detail page. See {@link BackTarget}.
   */
  back?: boolean | string | { fallback?: string }
  bordered?: boolean
  /** Render the header as a white surface card (bg + radius + soft shadow). */
  surface?: boolean
  /**
   * Keep the title and the `#actions` slot on the SAME row on phones instead
   * of stacking them. Use when the actions are a compact icon cluster (e.g.
   * the list-page search 🔍/Advanced toggles) that fits beside the title.
   * The default (false) stacks actions below the title on narrow screens so
   * wide button groups don't crowd the title.
   */
  inlineActions?: boolean
  /**
   * Declarative header actions - the same `RowAction` vocabulary the operation
   * column uses (C5), minus the row. Prefer this over a `#actions` slot of hand
   * written buttons: it lets the framework collapse them, which is what stops a
   * two-button header from spending a whole row on a phone.
   *
   * Collapse rule: `maxInlineActions` inline when there is room, and **all of
   * them in the "More" menu below 768px**. The slot still wins when both are
   * supplied, so nothing that renders non-buttons there has to change.
   */
  actions?: RowAction<void>[]
  /** Inline action slots before the tail collapses into "More" (default 2).
   *  Ignored on phones, where everything collapses. */
  maxInlineActions?: number
  /**
   * Collapse `#extra` behind a chevron on phones (default true). The subtitle
   * is secondary by definition, and this header sits OUTSIDE the scroll
   * container - every row it takes is taken from the content for good. Set
   * `false` when the subtitle is load-bearing enough to always cost that room.
   */
  extraCollapse?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  icon: undefined,
  help: undefined,
  helpTitle: undefined,
  back: undefined,
  bordered: false,
  surface: false,
  inlineActions: false,
  actions: undefined,
  maxInlineActions: 2,
  extraCollapse: true,
  translate: undefined,
})

const slots = useSlots()
const { isSm } = useBreakpoint()

/** `TRowActions` is row-scoped; a header has no row. `void` keeps consumer
 *  handlers writable as `() => save()` instead of taking an unused argument. */
const noRow: void = undefined

const hasDeclarativeActions = computed(() => (props.actions?.length ?? 0) > 0)
/** Phones put every action in the menu; wider screens keep `maxInlineActions`. */
const effectiveMaxInline = computed(() => (isSm.value ? 0 : props.maxInlineActions))
/** A collapsed action set IS a compact icon cluster, which is exactly what
 *  `inlineActions` means - so it reuses that flag rather than adding a second
 *  modifier, and the phone rule that gives actions a full-width row keeps
 *  applying only to the slot-based clusters that still need one. */
const effectiveInlineActions = computed(
  () => props.inlineActions || (hasDeclarativeActions.value && !slots.actions && isSm.value),
)

const extraOpen = ref(false)
const extraCollapsible = computed(() => props.extraCollapse && isSm.value && !!slots.extra)
const showExtra = computed(() => !!slots.extra && (!extraCollapsible.value || extraOpen.value))

defineSlots<{
  title?: () => unknown
  actions?: () => unknown
  extra?: () => unknown
}>()

// useRoute/useRouter return undefined when no router is installed (e.g. unit
// tests that don't mount a router) - guard every access.
const route = useRoute() as RouteLocationNormalizedLoaded | undefined
const router = useRouter() as Router | undefined

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

const resolvedTitle = computed(() => {
  if (props.title) return maybeTranslateKey(props.translate, props.title, props.title)
  const metaTitle = route?.meta?.title as string | undefined
  return metaTitle ? maybeTranslateKey(props.translate, metaTitle, metaTitle) : ''
})

const resolvedIcon = computed<string | undefined>(() => {
  if (props.icon) return props.icon
  const metaIcon = route?.meta?.icon as string | undefined
  if (metaIcon) return metaIcon
  const name = route?.name as string | undefined
  return name ? DEFAULT_ROUTE_ICONS[name] : undefined
})

const resolvedHelp = computed(() => (props.help ? maybeTranslateKey(props.translate, props.help, props.help) : ''))
const resolvedHelpTitle = computed(() =>
  props.helpTitle ? maybeTranslateKey(props.translate, props.helpTitle, props.helpTitle) : t('admin.common.tip') || 'Tip',
)

const showBack = computed(() => props.back !== undefined && props.back !== false)
function onBack(): void {
  runBack(props.back, router)
}
</script>

<style scoped>
.t-page-header { display: flex; flex-direction: column; gap: 8px; flex-shrink: 0; }
.t-page-header--bordered { border-bottom: 1px solid var(--tnzi-border); padding-bottom: 12px; }
.t-page-header--surface {
  /* Own the page-header container surface: the theme drawer's "Page header"
     background writes `--tnzi-admin-page-header-bg`; falls back to the base
     container color so nothing shifts until a color is picked. The adaptive
     foreground (dark bar → light title) is driven by the root tone attribute
     in polish.css (`[data-tnzi-ph-tone]`). */
  background: var(--tnzi-admin-page-header-bg, var(--tnzi-container-bg, #fff));
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  padding: 12px 16px;
}
/* Three columns - [back] [identity + subtitle] [actions] - vertically centred
   against each other. `nowrap` here plus a non-shrinking actions column plus a
   centre column that MAY shrink past its own content is what makes "the centre
   can never displace the actions" a structural property rather than a hope
   about how long the title happens to be. */
.t-page-header__bar {
  display: flex; align-items: center; flex-wrap: nowrap; gap: 16px; min-height: 32px;
}
/* The centre column: identity row + `#extra` row stacked, sharing one column.
   `min-width: 0` is the load-bearing part - without it the automatic minimum
   size pins this column at its content's min-content width, the line overflows,
   and the actions get shoved out of the header. With it the pressure is absorbed
   here and resolves as wrapping / ellipsis INSIDE the column. */
.t-page-header__main {
  flex: 1 1 auto; min-width: 0;
  display: flex; flex-direction: column; gap: 4px;
}
/* Identity row. `nowrap` here on purpose: this header sits OUTSIDE the section's
   scroll container, so every row it grows by is subtracted from the readable
   area for good. On a roomy width an ellipsised title costs nothing and a
   second row costs ~32px, so the trade goes to ellipsis; `min-width: 0` is what
   lets the title give way instead of the badges wrapping. Phones flip this (see
   the media query) - there the title matters more than the row count. */
.t-page-header__left { display: flex; align-items: center; flex-wrap: nowrap; gap: 8px; min-width: 0; }
/* Phone disclosure for the subtitle. Sits at the end of the identity row so it
   reads as "there is more about this record", not as an action. */
.t-page-header__extra-toggle {
  display: inline-flex; align-items: center; justify-content: center;
  width: 22px; height: 22px; border-radius: 50%; border: none; background: transparent;
  color: var(--tnzi-base-text-muted, #888); cursor: pointer; padding: 0; flex-shrink: 0;
  transition: color 0.15s ease, background 0.15s ease, transform 0.15s ease;
}
.t-page-header__extra-toggle:hover { color: var(--tnzi-primary); background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08); }
.t-page-header__extra-toggle.is-open { transform: rotate(180deg); }
.t-page-header__icon { color: var(--tnzi-primary); flex-shrink: 0; }
.t-page-header__title {
  font-size: 18px; font-weight: 600; color: var(--tnzi-base-text);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.t-page-header__back {
  display: inline-flex; align-items: center; justify-content: center;
  width: 28px; height: 28px; border-radius: var(--tnzi-admin-radius, 6px);
  border: none; background: transparent; color: var(--tnzi-base-text-muted, #888);
  cursor: pointer; padding: 0; flex-shrink: 0; transition: background 0.15s ease, color 0.15s ease;
}
.t-page-header__back:hover { color: var(--tnzi-primary); background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08); }
.t-page-header__help {
  display: inline-flex; align-items: center; justify-content: center;
  width: 22px; height: 22px; border-radius: 50%; border: none; background: transparent;
  color: var(--tnzi-base-text-muted, #888); cursor: pointer; padding: 0;
  transition: color 0.15s ease, background 0.15s ease;
}
.t-page-header__help:hover { color: var(--tnzi-primary); background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08); }
.t-page-header__help-content { max-width: 320px; }
.t-page-header__help-title { font-weight: 600; margin-bottom: 6px; color: var(--tnzi-base-text); }
.t-page-header__help-body { font-size: 13px; line-height: 1.5; color: var(--tnzi-base-text-muted, #888); }
.t-page-header__actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: flex-end; flex-shrink: 0; }
/* The subtitle row. It carries NO indent: it sits in the same column as the
   identity above it, so it lines up with the title by construction. */
.t-page-header__extra { min-width: 0; }
/* Phone. Two changes, and only here:
   1. Trim the title so it doesn't dominate the narrow bar.
   2. Let the actions drop onto their own line under the identity block - UNLESS
      the consumer opted into inline actions (a compact icon cluster that fits
      beside the title). The back control stays on the first line beside the
      identity, so it centres against the title instead of floating between the
      title and the buttons.
   767px, not 640px: `inlineActions` is driven by `useBreakpoint().isSm` (< 768)
   at every call site, so a 640px cut-off left a 641-767px band where the header
   called itself "not inline" yet never stacked. */
@media (max-width: 767px) {
  .t-page-header__title { font-size: 16px; }
  /* Top-align the side columns. The identity block is routinely 2-3 rows tall
     here, and centring puts the back arrow beside the badges or the subtitle
     rather than beside the title it takes you back from. (Consuming apps were
     patching this with `align-items: flex-start !important` on the identity row;
     the framework owns it now.) */
  .t-page-header__bar { align-items: flex-start; }
  /* Let the identity wrap instead of ellipsising: at this width the name is
     worth more than the row, which is the opposite of the wide-screen trade. */
  .t-page-header__left { flex-wrap: wrap; }
  /* A wide slot-based action cluster drops to its own row. `--inline-actions`
     opts out, and it is also set automatically once declarative actions have
     collapsed into the "More" menu - a single icon button belongs beside the
     title, not on a row of its own. */
  .t-page-header:not(.t-page-header--inline-actions) .t-page-header__bar { flex-wrap: wrap; }
  .t-page-header:not(.t-page-header--inline-actions) .t-page-header__actions {
    flex-basis: 100%;
    justify-content: flex-start;
  }
  /* Zero basis, not `auto`: with an auto basis the centre column's max-content
     width does not fit beside the back control, so the bar wraps and the back
     arrow takes an entire row to itself above the title. */
  .t-page-header:not(.t-page-header--inline-actions) .t-page-header__main { flex-basis: 0; }
}
</style>
