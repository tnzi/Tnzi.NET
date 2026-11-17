<script setup lang="ts">
/**
 * @experimental
 * TStepUpPrompt - inline re-authentication for `[RequireStepUp]` writes.
 *
 * Renders a `@tnzi/core` `StepUpPromptController` (the one
 * `useAccountSettings` builds and exposes as `stepUp`). The controller owns the
 * state machine and the calls; this is markup. It sits at the top of the
 * settings page that triggered the challenge rather than in a modal: the
 * settings dialog is already an overlay, and the user has to see which row
 * they were acting on while they confirm.
 *
 * Flow: a write answers `IDENTITY_STEP_UP_REQUIRED` -> `withStepUp` calls
 * `prompt.verify(scope)` -> this panel appears -> passkey or code -> the write
 * is replayed once. Cancel leaves the challenge as the page's visible error.
 */
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { DEFAULT_OTP_CODE_LENGTH, type StepUpMethod, type StepUpPromptController } from '@tnzi/core/services/identity'
import { isImeComposing } from '@tnzi/core/utils'
import TButton from '../controls/TButton.vue'
import { useAiI18n, formatAiMessage } from '../../i18n'

const props = defineProps<{
  prompt: StepUpPromptController
}>()

const t = useAiI18n()

// `verify()` settles only through this renderer's choose / submitCode /
// cancel. Closing the settings dialog mid-prompt would otherwise unmount the
// panel with the prompt still open and the calling write (and its `busy`)
// pending until the dialog was reopened; unmounting is a cancel.
onBeforeUnmount(() => {
  if (props.prompt.open) props.prompt.cancel()
})

const code = ref('')
// Digits for the method in progress: 6 for the authenticator app, the
// deployment's `otpCodeLength` (4-8) for an emailed / texted code.
const codeLength = computed(() => props.prompt.codeLength ?? DEFAULT_OTP_CODE_LENGTH)

// Fresh entry whenever the prompt (re)opens or the method changes.
watch(
  () => [props.prompt.open, props.prompt.method] as const,
  () => {
    code.value = ''
  },
)

const methodLabel = computed<Record<StepUpMethod, string>>(() => ({
  passkey: t.value.settings.stepUpMethodPasskey,
  totp: t.value.settings.stepUpMethodTotp,
  sms: t.value.settings.stepUpMethodSms,
  email: t.value.settings.stepUpMethodEmail,
}))

const codeHint = computed(() => {
  const { method, sentTo } = props.prompt
  if (method === 'totp') return t.value.settings.stepUpCodeHintTotp
  if (sentTo) return formatAiMessage(t.value.settings.stepUpCodeSentTo, { to: sentTo })
  return t.value.settings.stepUpCodeSent
})

const canResend = computed(() => props.prompt.method === 'sms' || props.prompt.method === 'email')

// Keyed on `method`, not `stage`: while a typed code is being verified the
// stage is `busy` but the user is still in the code flow, and flipping back to
// the chooser mid-request would read as "it rejected me". `method` is set only
// once a code path is committed to and cleared by `back()`.
const inCodeFlow = computed(() => props.prompt.method !== null)
const verifying = computed(() => inCodeFlow.value && props.prompt.stage === 'busy')

const canSubmit = computed(() => Boolean(code.value.trim()) && !props.prompt.busy)

async function submit(): Promise<void> {
  await props.prompt.submitCode(code.value)
  if (props.prompt.error) code.value = ''
}

/* Enter submits the code, except the Enter that commits an input method's
   composition (Safari sends it after `compositionend`). */
function onCodeEnter(event: KeyboardEvent): void {
  if (isImeComposing(event) || !canSubmit.value) return
  void submit()
}
</script>

<template>
  <section v-if="prompt.open" class="t-step-up" role="region" :aria-label="t.settings.stepUpTitle" :aria-busy="prompt.busy">
    <h3 class="t-step-up__title">{{ t.settings.stepUpTitle }}</h3>
    <p class="t-step-up__intro">{{ t.settings.stepUpIntro }}</p>

    <!-- Stage: choose a method -->
    <div v-if="!inCodeFlow" class="t-step-up__methods">
      <TButton
        v-for="method in prompt.methods"
        :key="method"
        :loading="prompt.stage === 'busy'"
        :disabled="prompt.busy"
        @click="prompt.choose(method)"
      >
        {{ methodLabel[method] }}
      </TButton>
      <p v-if="prompt.stage === 'choose' && !prompt.canVerify" class="t-settings-field__hint">
        {{ t.settings.stepUpNoMethods }}
      </p>
    </div>

    <!-- Stage: enter the code -->
    <div v-else class="t-step-up__code">
      <p class="t-settings-field__hint">{{ codeHint }}</p>
      <input
        v-model="code"
        class="t-input t-input--md t-input--code t-step-up__input"
        type="text"
        inputmode="numeric"
        autocomplete="one-time-code"
        spellcheck="false"
        :maxlength="codeLength"
        :disabled="prompt.busy"
        :aria-invalid="prompt.error ? 'true' : undefined"
        :placeholder="t.settings.stepUpCodePlaceholder"
        :aria-label="t.settings.stepUpCodePlaceholder"
        @keydown.enter="onCodeEnter"
      />
      <div class="t-step-up__links">
        <TButton variant="link" :disabled="prompt.busy" @click="prompt.back()">
          {{ t.settings.stepUpBack }}
        </TButton>
        <TButton v-if="canResend" variant="link" :disabled="prompt.busy" @click="prompt.resendCode()">
          {{ t.settings.stepUpResend }}
        </TButton>
      </div>
    </div>

    <p v-if="prompt.error" class="t-settings-field__error" role="alert">{{ prompt.error }}</p>

    <div class="t-settings-field__actions">
      <TButton :disabled="prompt.stage === 'busy'" @click="prompt.cancel()">
        {{ t.settings.stepUpCancel }}
      </TButton>
      <TButton
        v-if="inCodeFlow"
        variant="primary"
        :loading="verifying"
        :disabled="!canSubmit"
        @click="submit"
      >
        {{ t.settings.stepUpVerify }}
      </TButton>
    </div>
  </section>
</template>

<style scoped>
.t-step-up {
  margin-bottom: 16px;
  padding: 14px 16px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-accent) 40%, transparent);
  border-radius: 10px;
  background: var(--tnzi-ai-accent-soft);
}
.t-step-up__title {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-ai-text);
}
.t-step-up__intro {
  margin: 0 0 12px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary);
}
.t-step-up__methods {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.t-step-up__code {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-step-up__input {
  max-width: 220px;
}
.t-step-up__links {
  display: flex;
  gap: 16px;
}
</style>
