<script setup lang="ts">
/**
 * @experimental
 * TSettingsDialog — two-column modal with section list + content slot.
 *
 * Consumer provides a section list and slots named after each section id.
 * Base component is pure chrome — no persistence, no content assumptions.
 */
import { computed, toRef, watch, onMounted, onBeforeUnmount } from 'vue'
import { Icon } from '@iconify/vue'
import { useSettingsDialog, type SettingsSection } from '@/composables/useSettingsDialog'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    sections: readonly SettingsSection[]
    activeSection?: string
    title?: string
  }>(),
  {
    title: 'Settings',
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  'update:activeSection': [id: string]
}>()

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

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && dialog.open.value) {
    event.preventDefault()
    dialog.hide()
  }
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
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
        <div class="t-settings">
          <button
            type="button"
            class="t-settings__close"
            aria-label="Close"
            @click="dialog.hide()"
          >
            <Icon icon="lucide:x" />
          </button>

          <div class="t-settings__body">
            <nav class="t-settings__nav" aria-label="Settings sections">
              <button
                v-for="section in sections"
                :key="section.id"
                type="button"
                class="t-settings__nav-item"
                :class="{ 't-settings__nav-item--active': section.id === dialog.activeSection.value }"
                @click="dialog.setSection(section.id)"
              >
                <Icon v-if="section.icon" :icon="section.icon" />
                <span>{{ section.label }}</span>
              </button>
            </nav>

            <section class="t-settings__content">
              <h2 class="t-settings__content-title">{{ title }}</h2>
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
.t-settings__header {
  display: none; /* title rendered inside content area instead */
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
  gap: 2px;
  border-right: 1px solid var(--tnzi-ai-divider, #ebebeb);
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
