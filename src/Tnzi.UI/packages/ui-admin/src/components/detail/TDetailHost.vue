<template>
  <!-- page mode: the host IS the route page; render the layout bare. -->
  <TDetailLayout
    v-if="state.mode.value === 'page'"
    :layout="layout"
    :sections="sections"
    :title="resolvedTitle"
    :icon="icon"
    :back="back"
    :content-max-width="contentMaxWidth"
    :active-section="state.activeSection.value"
    :translate="translate"
    @update:active-section="state.setSection"
  >
    <template v-if="$slots.title" #title><slot name="title" :data="state.data.value" /></template>
    <template v-if="$slots.actions" #actions><slot name="actions" :data="state.data.value" :action="state.action.value" /></template>
    <template v-if="$slots.extra" #extra><slot name="extra" :data="state.data.value" /></template>
    <template v-if="$slots['nav-header']" #nav-header><slot name="nav-header" /></template>
    <template v-if="$slots.footer" #footer><slot name="footer" :submit="submit" :close="state.close" /></template>
    <template #default="{ section, sectionIcon }">
      <slot :data="state.data.value" :action="state.action.value" :section="section" :section-icon="sectionIcon" />
    </template>
  </TDetailLayout>

  <!-- drawer/modal: the overlay owns the title + close + footer actions, so the
       in-layout header is suppressed and the identity moves to the shell's own
       `#header`. `#actions` stays page-mode only - an overlay's actions belong
       in its footer, which `#footer` already owns. -->

  <!-- drawer mode -->
  <TDrawerShell
    v-else-if="state.mode.value === 'drawer'"
    :show="state.visible.value"
    :width="width"
    :title="resolvedTitle"
    @update:show="(v: boolean) => { if (!v) state.close() }"
  >
    <template v-if="hasOverlayHeader" #header><slot name="title" :data="rendered.formData.value"><span class="t-detail-host__header"><TSvgIcon v-if="icon" :icon="icon" :size="18" class="t-detail-host__header-icon" /><span class="t-detail-host__header-title">{{ resolvedTitle }}</span></span></slot></template>
    <TDetailLayout
      :layout="layout"
      :sections="sections"
      :active-section="state.activeSection.value"
      :translate="translate"
      :show-header="false"
      @update:active-section="state.setSection"
    >
      <template #default="{ section, sectionIcon }">
        <slot :data="rendered.formData.value" :action="rendered.mode.value" :section="section" :section-icon="sectionIcon" />
      </template>
    </TDetailLayout>
    <template v-if="footer" #footer>
      <slot name="footer" :submit="submit" :close="state.close">
        <NButton @click="state.close">{{ t('admin.common.cancel') }}</NButton>
        <NButton v-if="rendered.mode.value !== 'view'" type="primary" @click="submit">{{ t('admin.common.confirm') }}</NButton>
      </slot>
    </template>
  </TDrawerShell>

  <!-- modal mode (default) -->
  <TModalShell
    v-else
    :show="state.visible.value"
    :title="resolvedTitle"
    :width="width"
    :content-max-height-vh="contentMaxHeightVh"
    @update:show="(v: boolean) => { if (!v) state.close() }"
  >
    <template v-if="hasOverlayHeader" #header><slot name="title" :data="rendered.formData.value"><span class="t-detail-host__header"><TSvgIcon v-if="icon" :icon="icon" :size="18" class="t-detail-host__header-icon" /><span class="t-detail-host__header-title">{{ resolvedTitle }}</span></span></slot></template>
    <TDetailLayout
      :layout="layout"
      :sections="sections"
      :active-section="state.activeSection.value"
      :translate="translate"
      :show-header="false"
      @update:active-section="state.setSection"
    >
      <template #default="{ section, sectionIcon }">
        <slot :data="rendered.formData.value" :action="rendered.mode.value" :section="section" :section-icon="sectionIcon" />
      </template>
    </TDetailLayout>
    <template v-if="footer" #footer>
      <slot name="footer" :submit="submit" :close="state.close">
        <div class="t-detail-host__footer">
          <NButton @click="state.close">{{ t('admin.common.cancel') }}</NButton>
          <NButton v-if="rendered.mode.value !== 'view'" type="primary" @click="submit">{{ t('admin.common.confirm') }}</NButton>
        </div>
      </slot>
    </template>
  </TModalShell>
</template>

<script setup lang="ts" generic="T">
import { computed, useSlots } from 'vue'
import { NButton } from 'naive-ui'
import { TModalShell, TDrawerShell, TSvgIcon } from '@tnzi/ui'
import { provideFormHost } from '@tnzi/ui/headless'
import TDetailLayout from './TDetailLayout.vue'
import { useRetainedFormState } from '../../headless/useFormModal'
import type { UseDetailReturn, DetailSection, DetailLayout } from '../../headless/useDetail'

export interface TDetailHostProps<T> {
  state: UseDetailReturn<T>
  title?: string
  width?: number
  layout?: DetailLayout
  sections?: DetailSection[]
  translate?: (key: string) => string
  /**
   * Render the modal/drawer footer (Cancel/Confirm or the `#footer` slot). Set
   * `false` for a footer-less management panel (e.g. a documents drawer) whose
   * own controls live in the body and whose close affordance is the X button.
   * (Page mode renders the footer only when a `#footer` slot is supplied.)
   */
  footer?: boolean
  /**
   * Glyph beside the title, in EVERY mode: page mode forwards it to the
   * in-layout `TPageHeader`, overlay modes compose it into the shell's
   * `#header`. Ignored when a `#title` slot is supplied - the slot owns the
   * whole identity, which is exactly how `TPageHeader` treats its own
   * `#title`, so the rule does not change with the mode either.
   */
  icon?: string
  /**
   * Page-mode back affordance: `true` → `router.back()`, a string → push that
   * path, `{ fallback }` → smart back (in-app history, else push `fallback` -
   * preferred for a drilled-into detail), `false` → no back button (a top-level
   * page reached from the menu). Default `true`. Ignored in modal/drawer mode.
   */
  back?: boolean | string | { fallback?: string }
  /** Page-mode content max-width (forwarded to `TDetailLayout`). */
  contentMaxWidth?: number | string
  /**
   * Modal-mode cap on the BODY's height, in vh (forwarded to `TModalShell`).
   * Raise it for a content-heavy overlay: the body is the only thing that
   * scrolls, so leaving the cap low forces a taller panel to make the whole
   * modal scroll instead - which is the one thing the fixed body height exists
   * to prevent.
   *
   * Left `undefined` so `TModalShell` supplies the default (65) - repeating the
   * number here would give it two owners that drift apart. Not forwarded to
   * `TDrawerShell`: a drawer is full-height by construction, so there is no
   * equivalent cap to set.
   */
  contentMaxHeightVh?: number
}

const props = withDefaults(defineProps<TDetailHostProps<T>>(), {
  title: undefined,
  width: 560,
  layout: 'plain',
  sections: () => [],
  translate: undefined,
  footer: true,
  icon: undefined,
  back: true,
  contentMaxWidth: undefined,
  contentMaxHeightVh: undefined,
})

defineSlots<{
  default?: (props: { data: T | null; action: string | null; section: string | null; sectionIcon?: string }) => unknown
  /**
   * Rich identity (avatar + name + status tag) in place of the plain title, in
   * EVERY mode: page mode forwards it to `TDetailLayout`'s `#title`, overlay
   * modes to the shell's `#header`. It REPLACES the `icon` prop rather than
   * sitting beside it - same rule `TPageHeader` applies to its own `#title`.
   */
  title?: (props: { data: T | null }) => unknown
  actions?: (props: { data: T | null; action: string | null }) => unknown
  /** Page-mode only: the record's secondary particulars on their own row under
   *  the title bar (forwarded to `TDetailLayout`'s `#extra` - see its doc for why
   *  a meta line belongs here rather than in `#title`). */
  extra?: (props: { data: T | null }) => unknown
  /** Page-mode only: rendered inside the side-layout nav card, above the menu (forwarded to TDetailLayout). */
  'nav-header'?: () => unknown
  footer?: (props: { submit: () => Promise<void>; close: () => void }) => unknown
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

// What the OVERLAY branches render. `close()` clears the detail's action + record
// in the tick it hides the overlay, but NModal / NDrawer keep the body mounted
// until their leave transition ends - so the drawer/modal paint the last pair the
// state held while open and keep it until the body is gone. See
// `useRetainedFormState`. Page mode keeps reading the live refs: it has no leave
// transition, and a page-mode detail that never opens the form never sets
// `visible`, which is what the retained pair is gated on.
const rendered = useRetainedFormState(props.state.form)

// The host owns Confirm in every mode, so it owns the form host: the
// `TSchemaForm`s in its slot register here and are validated before
// `state.submit` runs. The `#footer` slot receives this gated call, not the
// raw `state.submit`, so a page-supplied footer gets the same block.
const formHost = provideFormHost()
async function submit(): Promise<void> {
  if (!(await formHost.validate())) return
  await props.state.submit()
}

const slots = useSlots()

/**
 * Whether the overlay branches supply the shell a `#header` at all.
 *
 * Deliberately NOT unconditional: `TModalShell` withholds naive's `title` prop
 * the moment a `#header` slot exists (the prop would otherwise win and drop the
 * slot silently), so always sending one would re-route every existing overlay's
 * title through our own markup for no gain. With neither an `icon` nor a
 * `#title` slot the shells keep painting the plain string exactly as before.
 */
const hasOverlayHeader = computed(() => Boolean(slots.title) || Boolean(props.icon))

const resolvedTitle = computed(() => {
  if (props.title) return props.title
  const a = rendered.mode.value
  return a ? t(`admin.crud.${a}Title`) : ''
})
</script>

<style scoped>
.t-detail-host__footer { display: flex; justify-content: flex-end; gap: 8px; }
/* The overlay identity row. Scoped works here - `#header` is a real slot, so
   Vue stamps THIS component's scope id on the vnodes even though naive
   teleports the modal to <body>. (Contrast `TDetailLayout`'s tab label, which
   naive renders from a function passed as a PROP and therefore lands outside
   our scope - that one has to live in polish.css.)
   Mirrors `TPageHeader`'s identity row: 8px gap, primary glyph. */
.t-detail-host__header { display: flex; align-items: center; gap: 8px; min-width: 0; }
.t-detail-host__header-icon { color: var(--tnzi-primary); flex-shrink: 0; }
.t-detail-host__header-title { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
