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
import { computed, nextTick, useTemplateRef } from 'vue'
import { Icon } from '@iconify/vue'
import TThreadComposer from './TThreadComposer.vue'
import { useAiI18n } from '../../i18n/index'
import type { ComposerAction } from './composer-types'
import { DEFAULT_COMPOSER_ACCEPT } from './composer-types'
import type { AddAttachmentsResult, RejectedAttachment } from '../../headless/useComposerAttachments'

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
    /** Main serif greeting text. Ignored if the `headline` slot is used.
     *  Defaults to the current locale's. */
    greeting?: string
    /** Optional subtitle below the greeting. Ignored if the `subline` slot is used. */
    subline?: string
    /** Composer placeholder. Defaults to the current locale's. */
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
    /** Refuse to send, e.g. while a previous submit is still being read or
     *  uploaded. The text stays editable. */
    disabled?: boolean
    /**
     * Phone layout: a smaller headline (content in the `headline` slot sized in
     * `em` follows it), a one-line composer and tighter spacing. `TChatApp`
     * sets it below its narrow-screen breakpoint.
     */
    compact?: boolean
  }>(),
  {
    modelValue: '',
    chips: () => [],
    maxWidth: 768,
    alwaysShowSend: false,
    composerActions: () => [],
    enableVoice: true,
    enableAttachments: false,
    accept: DEFAULT_COMPOSER_ACCEPT,
    maxFileSize: 10 * 1024 * 1024,
    voiceLang: 'en-US',
    compact: false,
    disabled: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  submit: [value: string, files: File[]]
  'chip-click': [chip: LandingChip]
  action: [id: string]
  /** Files the composer refused (size or type), from any way they arrived. */
  'attachments-rejected': [rejected: readonly RejectedAttachment[]]
}>()

const composerRef = useTemplateRef<InstanceType<typeof TThreadComposer>>('composerRef')

const t = useAiI18n()
const greetingText = computed(() => props.greeting ?? t.value.landing.greeting)
const placeholderText = computed(() => props.placeholder ?? t.value.landing.placeholder)

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
  /* Focus last, and a tick later, so the value has round-tripped through the
     parent's v-model before the caret is placed at the end of it. Suggestion
     prompts are deliberately half-written ("... plan my day around "), so
     leaving focus on the chip means the user has to click into the box and
     press End before they can continue the sentence. */
  void nextTick(() => composerRef.value?.focus())
}

/** The landing composer's `addAttachments` (see TThreadComposer). Null when
 *  attachments are off. */
function addAttachments(incoming: FileList | readonly File[]): AddAttachmentsResult | null {
  return composerRef.value?.addAttachments(incoming) ?? null
}

defineExpose({ addAttachments, focus: () => composerRef.value?.focus() })
</script>

<template>
  <section class="t-landing t-ai-scroll" :class="{ 't-landing--compact': compact }">
    <div v-if="$slots.plan" class="t-landing__plan">
      <slot name="plan" />
    </div>

    <h1 class="t-landing__headline">
      <slot name="headline">{{ greetingText }}</slot>
    </h1>

    <div v-if="subline || $slots.subline" class="t-landing__subline">
      <slot name="subline">{{ subline }}</slot>
    </div>

    <TThreadComposer
      ref="composerRef"
      v-model="text"
      class="t-landing__composer"
      :sticky="false"
      :rows="2"
      :compact="compact"
      :max-width="maxWidth"
      :placeholder="placeholderText"
      :disabled="disabled"
      :composer-actions="composerActions"
      :enable-voice="enableVoice"
      :enable-attachments="enableAttachments"
      :accept="accept"
      :max-file-size="maxFileSize"
      :voice-lang="voiceLang"
      :always-show-send="alwaysShowSend"
      @send="onSubmit"
      @action="emit('action', $event)"
      @attachments-rejected="emit('attachments-rejected', $event)"
    >
      <template v-if="$slots['composer-notice']" #notice>
        <slot name="composer-notice" />
      </template>
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
  position: relative;
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

/* Compact (phone). At 36px a short greeting took two lines of a 390px
   screen, and anything in the headline slot sized in em grew with it. The
   generous outer padding centred the hero in a desktop window; on a phone it
   only pushed the composer down. */
.t-landing--compact {
  padding: 16px 12px 24px;
}
.t-landing--compact .t-landing__plan { margin-bottom: 12px; }
.t-landing--compact .t-landing__headline {
  font-size: 26px;
  line-height: 1.3;
  margin-bottom: 16px;
}
.t-landing--compact .t-landing__subline {
  font-size: 14px;
  margin: -8px 0 16px;
}
.t-landing--compact .t-landing__chips { margin-top: 12px; }
.t-landing--compact .t-landing__footer { margin-top: 16px; }

.t-landing__footer {
  margin-top: 24px;
  width: 100%;
  max-width: 768px;
  display: flex;
  justify-content: center;
}
</style>
