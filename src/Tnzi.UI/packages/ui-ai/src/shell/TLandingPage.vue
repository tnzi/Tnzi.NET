<script setup lang="ts">
/**
 * @experimental
 * TLandingPage - empty-state hero for chat applications.
 *
 * Renders a serif display headline above a chat composer and a row of
 * suggestion chips. The composer is `TThreadComposer` in its non-sticky
 * layout, so text + voice input, attachments (paperclip / drag-drop /
 * paste-image) and `composerActions` behave identically here and in an active
 * thread. All content is slot-driven so consumers can tailor every region
 * without forking the component.
 *
 * Slots:
 *   - plan       ........ content above the headline (plan badge, trial pill)
 *   - headline   ........ overrides the default serif greeting
 *   - subline    ........ optional subtitle beneath the headline
 *   - composer-left  .... left cluster of the composer toolbar (before built-ins)
 *   - composer-right ..... right cluster of the composer toolbar (before send)
 *   - chips      ........ overrides the default chip row
 *   - footer     ........ free area below everything
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import TThreadComposer from '../components/chat/TThreadComposer.vue'
import type { ComposerAction } from '../components/chat/composer-types'
import { DEFAULT_COMPOSER_ACCEPT } from '../components/chat/composer-types'

export interface LandingChip {
  readonly id?: string
  readonly icon?: string
  readonly label: string
  readonly prompt?: string
}

const props = withDefaults(
  defineProps<{
    /** Composer text (two-way bound). */
    modelValue?: string
    /** Main serif greeting text. Ignored if the `headline` slot is used. */
    greeting?: string
    /** Optional subtitle below the greeting. Ignored if the `subline` slot is used. */
    subline?: string
    /** Composer placeholder. */
    placeholder?: string
    /** Suggestion chips. Ignored if the `chips` slot is used. */
    chips?: readonly LandingChip[]
    /** Composer max width in pixels. */
    maxWidth?: number
    /** Show the send button even when the input is empty. */
    alwaysShowSend?: boolean
    /** Declarative extra toolbar buttons. */
    composerActions?: ReadonlyArray<ComposerAction>
    /** Built-in voice (speech-to-text) mic button. Default true. */
    enableVoice?: boolean
    /** Built-in attachment button + drag/paste. Default false. */
    enableAttachments?: boolean
    /** Accepted file types. */
    accept?: string
    /** Max attachment size in bytes. */
    maxFileSize?: number
    /** Voice recognition language (BCP-47). */
    voiceLang?: string
  }>(),
  {
    modelValue: '',
    greeting: 'What can I do for you?',
    placeholder: 'Assign a task or ask anything',
    chips: () => [],
    maxWidth: 768,
    alwaysShowSend: false,
    composerActions: () => [],
    enableVoice: true,
    enableAttachments: false,
    accept: DEFAULT_COMPOSER_ACCEPT,
    maxFileSize: 10 * 1024 * 1024,
    voiceLang: 'en-US',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  submit: [value: string, files: File[]]
  'chip-click': [chip: LandingChip]
  action: [id: string]
}>()

const text = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})

function onSubmit(value: string, files: File[]): void {
  emit('submit', value, files)
}

function onChipClick(chip: LandingChip): void {
  emit('chip-click', chip)
  if (chip.prompt != null) {
    text.value = chip.prompt
  }
}
</script>

<template>
  <section class="t-landing">
    <div v-if="$slots.plan" class="t-landing__plan">
      <slot name="plan" />
    </div>

    <h1 class="t-landing__headline">
      <slot name="headline">{{ greeting }}</slot>
    </h1>

    <div v-if="subline || $slots.subline" class="t-landing__subline">
      <slot name="subline">{{ subline }}</slot>
    </div>

    <TThreadComposer
      v-model="text"
      class="t-landing__composer"
      :sticky="false"
      :rows="2"
      :max-width="maxWidth"
      :placeholder="placeholder"
      :composer-actions="composerActions"
      :enable-voice="enableVoice"
      :enable-attachments="enableAttachments"
      :accept="accept"
      :max-file-size="maxFileSize"
      :voice-lang="voiceLang"
      :always-show-send="alwaysShowSend"
      @send="onSubmit"
      @action="emit('action', $event)"
    >
      <template v-if="$slots['composer-left']" #left>
        <slot name="composer-left" />
      </template>
      <template v-if="$slots['composer-right']" #right>
        <slot name="composer-right" />
      </template>
    </TThreadComposer>

    <div v-if="$slots.chips || (chips && chips.length > 0)" class="t-landing__chips">
      <slot name="chips">
        <button
          v-for="(chip, i) in chips"
          :key="chip.id ?? chip.label ?? i"
          type="button"
          class="t-landing__suggestion"
          @click="onChipClick(chip)"
        >
          <Icon v-if="chip.icon" :icon="chip.icon" />
          <span>{{ chip.label }}</span>
        </button>
      </slot>
    </div>

    <div v-if="$slots.footer" class="t-landing__footer">
      <slot name="footer" />
    </div>
  </section>
</template>

<style scoped>
.t-landing {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px 80px;
  min-height: 0;
  overflow-y: auto;
}

.t-landing__plan {
  display: inline-flex;
  align-items: center;
  margin-bottom: 22px;
}

.t-landing__headline {
  margin: 0 0 28px;
  padding: 0;
  font-family: var(--tnzi-ai-font-display, 'Libre Baskerville', Georgia, serif);
  font-size: 36px;
  line-height: 1.5;
  font-weight: 400;
  color: var(--tnzi-ai-text, #34322d);
  text-align: center;
  letter-spacing: normal;
}

.t-landing__subline {
  font-size: 16px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-tertiary, rgba(0, 0, 0, 0.48));
  margin: -16px 0 28px;
  text-align: center;
}

/* The composer itself is TThreadComposer; only the landing-specific width
   cap lives here. Its box chrome (surface, radius, focus ring, drag state,
   attachment chips, toolbar) comes from that component so the two placements
   cannot drift apart again. */
.t-landing__composer {
  width: 100%;
}

.t-landing__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: center;
  margin-top: 18px;
  max-width: 768px;
}
.t-landing__suggestion {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 14px;
  border: 1px solid var(--tnzi-ai-border, rgba(0, 0, 0, 0.08));
  background: var(--tnzi-ai-surface, #ffffff);
  color: var(--tnzi-ai-text, #34322d);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
  transition: background 120ms var(--tnzi-ai-easing, ease), border-color 120ms var(--tnzi-ai-easing, ease);
}
.t-landing__suggestion:hover {
  background: var(--tnzi-ai-hover, rgba(0, 0, 0, 0.04));
  border-color: var(--tnzi-ai-accent, #0d9488);
}
.t-landing__suggestion > .iconify {
  color: var(--tnzi-ai-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 15px;
}

.t-landing__footer {
  margin-top: 24px;
  width: 100%;
  max-width: 768px;
  display: flex;
  justify-content: center;
}
</style>
