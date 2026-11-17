<script setup lang="ts">
/**
 * @experimental
 * TSettingsDialog - a settings dialog: its sections down the left in groups set
 * apart by a gap, the open one beside them under its name; on a phone a
 * full-screen sheet with the sections in a row along the top.
 *
 * Built on `TDialog` (900 x min(740px, 90vh), flush body, full screen below
 * `narrowBreakpoint`), so it shares the dialog's chrome, focus trap, Escape,
 * stacking, page scroll lock, phone back gesture and `beforeClose` with every
 * other dialog of the package. The dialog's title is its accessible name and
 * its header; the open section's name is a heading over the pane.
 *
 * The consumer provides the section list and either a slot named after each
 * section id (each pane is mounted while its section is open and released when
 * the person moves on) or one default slot, given `{ activeSection, section }`,
 * which stays mounted across switches: for panes that keep unsaved input while
 * the person looks at another section (`v-show` per pane) or manage their own
 * mounting. With a default slot the named ones are not rendered. The component
 * keeps no settings of its own. A section may carry a badge:
 * how many things in it wait on the person, shown only when there are any
 * (`badge`, with `badgeLabel` for a screen reader). The page may name the open
 * section itself (`heading`) and put something above that name (`#lead`).
 *
 * Two optional blocks sit above the list, for a settings surface that has grown:
 * the account (`showAccount`, with a switcher button when `switchable`) and a
 * filter box (`searchable`). `#rail-header` replaces both; `#rail-footer` goes
 * under the list.
 */
import { computed, ref, toRef, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { TAvatar } from '@tnzi/ui/components'
import TDialog from './TDialog.vue'
import type { DialogCloseReason } from './dialog-motion'
import { useSettingsDialog, type SettingsSection } from '../../headless/useSettingsDialog'
import { useViewportBelow } from '../../headless/useViewportBelow'
import { useAiI18n } from '../../i18n/index'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    sections: readonly SettingsSection[]
    activeSection?: string
    /** The dialog's title and accessible name. Defaults to the current locale's. */
    title?: string
    /** The name over the open section; the section's own label when left out. */
    heading?: string
    /** Asked before the dialog closes (Escape, backdrop, close button, back); `false` keeps it open. */
    beforeClose?: (reason: DialogCloseReason) => boolean | void | Promise<boolean | void>
    /** Show the filter box above the section list. Worth turning on past roughly half a dozen sections. */
    searchable?: boolean
    /** Show the account at the top of the section list. */
    showAccount?: boolean
    accountName?: string
    accountSubtitle?: string
    accountAvatar?: string | null
    /** Render the account-switcher button beside the account. */
    switchable?: boolean
    /**
     * Viewport width (px) below which the dialog is a full-screen sheet with the
     * sections along the top. Default 768, the same as `TChatApp`'s `sidebarMobileBreakpoint`.
     */
    narrowBreakpoint?: number
    /** Force the sheet (`true`) or the centred dialog (`false`); unset follows `narrowBreakpoint`. */
    narrow?: boolean
    /** On the sheet, back closes the dialog instead of leaving the page. Default true; false when the address keeps it open. */
    closeOnBack?: boolean
    /**
     * The pane's content fills the height under the section name with no
     * padding, and the pane does not scroll as a whole: for panels that scroll
     * their own body and keep an action bar on the dialog's bottom edge (its
     * divider full width). `#lead` and the section name stay at the top.
     * The same word as `TDialog`'s `flush`.
     */
    flush?: boolean
  }>(),
  {
    activeSection: undefined,
    title: undefined,
    heading: undefined,
    beforeClose: undefined,
    closeOnBack: true,
    flush: false,
    narrowBreakpoint: 768,
    narrow: undefined,
    searchable: false,
    showAccount: false,
    accountName: '',
    accountSubtitle: '',
    accountAvatar: null,
    switchable: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  'update:activeSection': [id: string]
  'switch-account': []
}>()

const t = useAiI18n()
const titleText = computed(() => props.title ?? t.value.sidebar.settings)

const sectionsRef = toRef(() => props.sections as readonly SettingsSection[])
const dialog = useSettingsDialog({ sections: sectionsRef, initialSection: props.activeSection })

/* Opening moves to the section asked for; the filter starts empty each time. */
watch(
  () => props.modelValue,
  (open) => {
    if (open) dialog.show(props.activeSection)
    else {
      dialog.open.value = false
      dialog.query.value = ''
    }
  },
  { immediate: true },
)

watch(dialog.activeSection, (id) => {
  if (id !== props.activeSection) emit('update:activeSection', id)
})

watch(
  () => props.activeSection,
  (id) => {
    if (id && id !== dialog.activeSection.value) dialog.setSection(id)
  },
)

const shownHeading = computed(() => props.heading ?? dialog.currentSection.value?.label ?? titleText.value)
const hasResults = computed(() => dialog.groupedSections.value.length > 0)
const accountDisplayName = computed(() => props.accountName || t.value.account.fallbackName)

const viewportNarrow = useViewportBelow(() => props.narrowBreakpoint)
const isNarrow = computed(() => props.narrow ?? viewportNarrow.value)

const dialogRef = ref<{ panel: HTMLElement | null } | null>(null)

/* Focus opens on the panel itself, as `TDialog` does when it has no field:
   not on the filter box or a field of the open section (a phone would raise
   its keyboard over a dialog the person has not looked at yet), and not on an
   entry of the list, whose focus line would light up on open. Tab goes on
   from the panel into the list. */
function initialFocus(): HTMLElement | null {
  return dialogRef.value?.panel ?? null
}

function setOpen(open: boolean): void {
  emit('update:modelValue', open)
}
</script>

<template>
  <TDialog
    ref="dialogRef"
    :model-value="modelValue"
    :title="titleText"
    :width="900"
    height="min(740px, 90vh)"
    flush
    narrow-layout="fullscreen"
    :narrow="isNarrow"
    :close-on-back="closeOnBack"
    :before-close="beforeClose"
    :initial-focus="initialFocus"
    class="t-settings-dialog"
    @update:model-value="setOpen"
  >
    <div class="t-settings" :class="{ 't-settings--sheet': isNarrow, 't-settings--flush': flush }">
      <div class="t-settings__rail">
        <slot name="rail-header">
          <div v-if="showAccount" class="t-settings__account">
            <TAvatar :src="accountAvatar ?? undefined" :name="accountDisplayName" :size="28" :max-initials="2" />
            <div class="t-settings__account-text">
              <span class="t-settings__account-name">{{ accountDisplayName }}</span>
              <span v-if="accountSubtitle" class="t-settings__account-sub">{{ accountSubtitle }}</span>
            </div>
            <button
              v-if="switchable"
              type="button"
              class="t-settings__account-switch"
              :aria-label="t.account.switchAccount"
              @click="emit('switch-account')"
            >
              <Icon icon="lucide:chevrons-up-down" aria-hidden="true" />
            </button>
          </div>

          <div v-if="searchable" class="t-settings__search">
            <Icon icon="lucide:search" class="t-settings__search-icon" aria-hidden="true" />
            <input
              v-model="dialog.query.value"
              type="search"
              class="t-settings__search-input"
              :placeholder="t.settings.search"
              :aria-label="t.settings.searchAria"
            />
          </div>
        </slot>

        <nav class="t-settings__nav t-ai-scroll" :aria-label="t.settings.sections">
          <div v-for="group in dialog.groupedSections.value" :key="group.key" class="t-settings__group">
            <button
              v-for="section in group.sections"
              :key="section.id"
              type="button"
              class="t-settings__item"
              :class="{ 'is-active': section.id === dialog.activeSection.value }"
              :aria-current="section.id === dialog.activeSection.value ? 'page' : undefined"
              :data-section-id="section.id"
              @click="dialog.setSection(section.id)"
            >
              <Icon v-if="section.icon" :icon="section.icon" class="t-settings__item-icon" aria-hidden="true" />
              <span class="t-settings__item-label">{{ section.label }}</span>
              <span v-if="section.badge" class="t-settings__badge" data-section-badge>
                <span aria-hidden="true">{{ section.badge }}</span>
                <span v-if="section.badgeLabel" class="sr-only">{{ section.badgeLabel }}</span>
              </span>
            </button>
          </div>
          <p v-if="!hasResults" class="t-settings__nav-empty">{{ t.settings.noResults }}</p>
        </nav>

        <slot name="rail-footer" />
      </div>

      <section class="t-settings__content t-ai-scroll">
        <slot name="lead" />
        <h3 class="t-settings__heading" data-section-heading>{{ shownHeading }}</h3>
        <div class="t-settings__body">
          <!-- One default slot stays mounted across sections; a named slot per
               section is its own fragment (keyed by its name), rebuilt on each switch. -->
          <slot v-if="$slots.default" :active-section="dialog.activeSection.value" :section="dialog.currentSection.value" />
          <slot v-else :name="dialog.activeSection.value" :section="dialog.currentSection.value" />
        </div>
      </section>
    </div>
  </TDialog>
</template>

<style scoped>
.t-settings {
  display: flex;
  height: 100%;
  min-height: 0;
}
.t-settings__rail {
  display: flex;
  flex-direction: column;
  flex: none;
  width: 186px;
  min-height: 0;
  border-right: 1px solid var(--tnzi-ai-border);
}
.t-settings__nav {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 14px;
  flex: 1;
  min-height: 0;
  padding: 12px 10px;
  overflow-y: auto;
}
.t-settings__group {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-settings__item {
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: 32px;
  padding: 5px 10px;
  box-sizing: border-box;
  border: 0;
  border-radius: 8px;
  background: none;
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 13.5px;
  text-align: left;
  cursor: pointer;
}
.t-settings__item:hover {
  background: var(--tnzi-ai-hover);
}
.t-settings__item.is-active {
  background: var(--tnzi-ai-selected);
  font-weight: 500;
}
.t-settings__item:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
.t-settings__item-icon {
  flex: none;
  font-size: 16px;
  color: var(--tnzi-ai-text-secondary);
}
.t-settings__item-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-settings__badge {
  /* Holds its screen reader words, which would otherwise widen the scrolling box around. */
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  min-width: 18px;
  height: 18px;
  margin-left: auto;
  padding: 0 5px;
  box-sizing: border-box;
  border-radius: 9px;
  background: var(--tnzi-ai-warning);
  color: var(--tnzi-ai-on-status);
  font-size: 11px;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
.t-settings__nav-empty {
  margin: 4px 10px;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
}

/* -- The account, above the list -- */
.t-settings__account {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 10px 4px 12px;
}
.t-settings__account-text {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}
.t-settings__account-name,
.t-settings__account-sub {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-settings__account-name {
  font-size: 13.5px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-settings__account-sub {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-settings__account-switch {
  display: grid;
  place-items: center;
  flex: none;
  width: 24px;
  height: 24px;
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: none;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 14px;
  cursor: pointer;
}
.t-settings__account-switch:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-settings__account-switch:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
}

/* -- The filter, above the list: a 30px field like `.t-input` -- */
.t-settings__search {
  position: relative;
  display: flex;
  align-items: center;
  margin: 12px 10px 0;
}
.t-settings__account + .t-settings__search {
  margin-top: 8px;
}
.t-settings__search-icon {
  position: absolute;
  left: 9px;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
  pointer-events: none;
}
.t-settings__search-input {
  width: 100%;
  height: 30px;
  box-sizing: border-box;
  padding: 0 8px 0 28px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 13px;
  outline: none;
}
.t-settings__search-input::placeholder {
  color: var(--tnzi-ai-text-tertiary);
}
.t-settings__search-input:focus {
  border-color: var(--tnzi-ai-focus-border);
  box-shadow: var(--tnzi-ai-focus-ring);
}

/* -- The pane: the open section's name over its content; the pane scrolls -- */
.t-settings__content {
  position: relative;
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow-y: auto;
  overflow-wrap: break-word;
}
.t-settings__heading {
  flex: none;
  margin: 0;
  padding: 16px 24px 4px;
  font-size: 15px;
  font-weight: 600;
  color: var(--tnzi-ai-text);
}
.t-settings__body {
  padding: 4px 24px 24px;
}

/* -- A phone: a full-screen sheet, the sections in a row along the top -- */
.t-settings--sheet {
  flex-direction: column;
}
.t-settings--sheet .t-settings__rail {
  width: auto;
  border-right: 0;
  border-bottom: 1px solid var(--tnzi-ai-border);
}
.t-settings--sheet .t-settings__nav {
  position: relative;
  flex-direction: row;
  gap: 2px;
  padding: 8px;
  overflow: auto hidden;
  scrollbar-width: none;
}
.t-settings--sheet .t-settings__group {
  flex: none;
  flex-direction: row;
}
.t-settings--sheet .t-settings__item {
  flex: none;
  white-space: nowrap;
}
.t-settings--sheet .t-settings__account {
  padding: 10px 14px 0;
}
.t-settings--sheet .t-settings__search {
  margin: 10px 14px 0;
}
.t-settings--sheet .t-settings__heading {
  padding: 14px 14px 4px;
}
.t-settings--sheet .t-settings__body {
  padding: 4px 14px 24px;
}
/* 16px on a phone, so iOS does not zoom the page when the filter is focused. */
/* -- Flush: the content owns its height, scrolling and spacing. Last, so it
   also wins over the sheet's padding. -- */
.t-settings--flush .t-settings__content {
  overflow: hidden;
}
.t-settings--flush .t-settings__body {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  padding: 0;
}
@media (max-width: 640px) {
  .t-settings__search-input {
    font-size: 16px;
  }
}
</style>
