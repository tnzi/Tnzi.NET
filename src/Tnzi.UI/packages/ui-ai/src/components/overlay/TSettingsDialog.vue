<script setup lang="ts">
/**
 * @experimental
 * TSettingsDialog - two-column modal with section list + content slot.
 *
 * Consumer provides a section list and slots named after each section id.
 * Base component is pure chrome - no persistence, no content assumptions.
 *
 * The left rail supports three optional blocks above the list (account
 * identity, search, grouped headings) because a settings surface grows: three
 * sections need none of them, fifteen need all three. Each is off unless asked
 * for, so the simple case stays simple.
 *
 * The pane heading tracks the *selected section*, not the dialog title. Those
 * are different strings ("Account" inside a dialog labelled "Settings") and
 * showing the dialog title in the content pane made every section look
 * identically named.
 */
import { computed, ref, toRef, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { TAvatar, useFocusTrap } from '@tnzi/ui'
import { useSettingsDialog, type SettingsSection } from '../../headless/useSettingsDialog'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'
import { useAiI18n } from '../../i18n/index'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    sections: readonly SettingsSection[]
    activeSection?: string
    /** Accessible name for the dialog, and the pane heading fallback when no
     *  section is selected. */
    title?: string
    /** Show the filter box above the section list. Worth turning on past
     *  roughly half a dozen sections. */
    searchable?: boolean
    /** Show the account identity block at the top of the left rail. */
    showAccount?: boolean
    accountName?: string
    accountSubtitle?: string
    accountAvatar?: string | null
    /** Render the account-switcher affordance beside the account block. */
    switchable?: boolean
  }>(),
  {
    title: 'Settings',
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

const sectionsRef = toRef(() => props.sections as readonly SettingsSection[])
const dialog = useSettingsDialog({ sections: sectionsRef, initialSection: props.activeSection })

watch(
  () => props.modelValue,
  (val) => {
    if (val) dialog.show(props.activeSection)
    else dialog.hide()
  },
  { immediate: true },
)

watch(dialog.open, (val) => {
  if (val !== props.modelValue) emit('update:modelValue', val)
})

watch(dialog.activeSection, (val) => {
  if (val !== props.activeSection) emit('update:activeSection', val)
})

watch(
  () => props.activeSection,
  (val) => {
    if (val && val !== dialog.activeSection.value) dialog.setSection(val)
  },
)

const currentSlotName = computed(() => dialog.activeSection.value)

/* The heading names the section being edited. Falls back to the dialog title
   only when nothing is selected (empty section list, or a filter that matched
   nothing). */
const paneHeading = computed(() => dialog.currentSection.value?.label ?? props.title)

const hasResults = computed(() => dialog.groupedSections.value.length > 0)

const accountDisplayName = computed(() => props.accountName || t.value.account.fallbackName)

/* Reopening should not resume someone else's half-typed filter. */
watch(dialog.open, (isOpen) => {
  if (!isOpen) dialog.query.value = ''
})

const dialogEl = ref<HTMLElement | null>(null)

/* Keeps Tab inside the `aria-modal` dialog, focuses its first control on open,
   restores focus to whatever opened it on close, and owns Escape (which used
   to be a hand-rolled window listener here). */
useFocusTrap(dialogEl, () => dialog.open.value, {
  onEscape: () => dialog.hide(),
})

useBodyScrollLock(() => dialog.open.value)
</script>

<template>
  <Teleport to="body">
    <transition name="t-settings-fade">
      <div
        v-if="dialog.open.value"
        class="t-settings-backdrop"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        @click.self="dialog.hide()"
      >
        <div ref="dialogEl" class="t-settings">
          <button
            type="button"
            class="t-settings__close"
            :aria-label="t.settings.close"
            @click="dialog.hide()"
          >
            <Icon icon="lucide:x" />
          </button>

          <div class="t-settings__body">
            <nav class="t-settings__nav" aria-label="Settings sections">
              <slot name="rail-header">
                <div v-if="showAccount" class="t-settings__account">
                  <TAvatar
                    :src="accountAvatar ?? undefined"
                    :name="accountDisplayName"
                    :size="34"
                    :max-initials="2"
                  />
                  <div class="t-settings__account-text">
                    <span class="t-settings__account-name">{{ accountDisplayName }}</span>
                    <span v-if="accountSubtitle" class="t-settings__account-sub">
                      {{ accountSubtitle }}
                    </span>
                  </div>
                  <button
                    v-if="switchable"
                    type="button"
                    class="t-settings__account-switch"
                    :aria-label="t.account.switchAccount"
                    @click="emit('switch-account')"
                  >
                    <Icon icon="lucide:chevrons-up-down" />
                  </button>
                </div>

                <div v-if="searchable" class="t-settings__search">
                  <Icon icon="lucide:search" class="t-settings__search-icon" />
                  <input
                    v-model="dialog.query.value"
                    type="search"
                    class="t-settings__search-input"
                    :placeholder="t.settings.search"
                    :aria-label="t.settings.searchAria"
                  />
                </div>
              </slot>

              <div class="t-settings__nav-scroll">
                <div
                  v-for="group in dialog.groupedSections.value"
                  :key="group.label"
                  class="t-settings__nav-group"
                >
                  <div v-if="group.label" class="t-settings__nav-group-label">
                    {{ group.label }}
                  </div>
                  <button
                    v-for="section in group.sections"
                    :key="section.id"
                    type="button"
                    class="t-settings__nav-item"
                    :class="{
                      't-settings__nav-item--active': section.id === dialog.activeSection.value,
                    }"
                    @click="dialog.setSection(section.id)"
                  >
                    <Icon v-if="section.icon" :icon="section.icon" />
                    <span>{{ section.label }}</span>
                  </button>
                </div>

                <p v-if="!hasResults" class="t-settings__nav-empty">
                  {{ t.settings.noResults }}
                </p>
              </div>

              <slot name="rail-footer" />
            </nav>

            <section class="t-settings__content">
              <h2 class="t-settings__content-title">{{ paneHeading }}</h2>
              <slot :name="currentSlotName" :section="dialog.currentSection.value" />
            </section>
          </div>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<style scoped>
.t-settings-backdrop {
  position: fixed;
  inset: 0;
  background: var(--tnzi-ai-backdrop, rgba(0, 0, 0, 0.6));
  backdrop-filter: blur(var(--tnzi-ai-backdrop-blur, 4px));
  -webkit-backdrop-filter: blur(var(--tnzi-ai-backdrop-blur, 4px));
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 90;
}
.t-settings {
  position: relative;
  width: min(1139px, 92vw);
  height: min(649px, 86vh);
  background: var(--tnzi-ai-bg, #f8f8f7);
  color: var(--tnzi-ai-text, inherit);
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-radius: var(--tnzi-ai-modal-radius, 20px);
  box-shadow: none;
  display: flex;
  min-height: 0;
  overflow: hidden;
}
.t-settings__body {
  flex: 1;
  display: flex;
  min-height: 0;
  position: relative;
}
.t-settings__close {
  position: absolute;
  top: 16px;
  right: 16px;
  z-index: 5;
  width: 32px;
  height: 32px;
  background: transparent;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  cursor: pointer;
  border-radius: 999px;
  font-size: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 120ms ease, color 120ms ease;
}
.t-settings__close:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text, #000);
}
.t-settings__nav {
  width: 260px;
  flex-shrink: 0;
  padding: 16px 12px 16px 16px;
  display: flex;
  flex-direction: column;
  min-height: 0;
  border-right: 1px solid var(--tnzi-ai-divider, #ebebeb);
}
/* Only the list scrolls: the account block and filter stay put, which is the
   point of having them. `min-height:0` is what actually lets it scroll inside
   a flex column. */
.t-settings__nav-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-settings__nav-group {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-settings__nav-group-label {
  padding: 16px 12px 4px;
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-settings__nav-group:first-child .t-settings__nav-group-label {
  padding-top: 4px;
}
.t-settings__nav-empty {
  margin: 12px;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}

/* -- Account block -- */
.t-settings__account {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 4px 12px;
}
.t-settings__account-text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.t-settings__account-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #000);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-settings__account-sub {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-settings__account-switch {
  flex-shrink: 0;
  width: 26px;
  height: 26px;
  border: none;
  background: transparent;
  border-radius: 6px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  font-size: 15px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.t-settings__account-switch:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  color: var(--tnzi-ai-text, #000);
}

/* -- Filter -- */
.t-settings__search {
  position: relative;
  display: flex;
  align-items: center;
  margin-bottom: 8px;
}
.t-settings__search-icon {
  position: absolute;
  left: 10px;
  font-size: 15px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  pointer-events: none;
}
.t-settings__search-input {
  width: 100%;
  height: 34px;
  padding: 0 10px 0 32px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  border-radius: 8px;
  background: var(--tnzi-ai-surface, #fff);
  color: var(--tnzi-ai-text, #000);
  font-family: inherit;
  font-size: 13px;
  outline: none;
}
.t-settings__search-input:focus {
  border-color: var(--tnzi-ai-accent, #3b82f6);
}
.t-settings__search-input::-webkit-search-cancel-button {
  cursor: pointer;
}

.t-settings__nav-item {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 34px;
  padding: 0 12px;
  border-radius: 8px;
  background: transparent;
  border: none;
  font-family: inherit;
  font-size: 14px;
  text-align: left;
  cursor: pointer;
  color: var(--tnzi-ai-text, #000);
}
.t-settings__nav-item:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
}
.t-settings__nav-item--active {
  background: var(--tnzi-ai-selected, rgba(0, 0, 0, 0.08));
  font-weight: 500;
}
.t-settings__nav-item > .iconify {
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 16px;
}
.t-settings__content {
  flex: 1;
  padding: 32px 40px 32px 36px;
  overflow-y: auto;
  position: relative;
}
.t-settings__content-title {
  font-family: var(--tnzi-ai-font-display, serif);
  font-size: 24px;
  font-weight: 400;
  margin: 0 0 24px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--tnzi-ai-divider, #ebebeb);
  color: var(--tnzi-ai-text, #000);
}
.t-settings-fade-enter-active,
.t-settings-fade-leave-active {
  transition: opacity 150ms ease, backdrop-filter 150ms ease;
}
.t-settings-fade-enter-from,
.t-settings-fade-leave-to { opacity: 0; }
</style>
