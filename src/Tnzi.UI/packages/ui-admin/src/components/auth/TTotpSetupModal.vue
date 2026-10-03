<template>
  <TModalShell :show="show" :title="t('totpSetup.title')" :width="440" @update:show="close">
    <NSpin :show="loading">
      <div class="t-totp">
        <p class="t-totp__hint">{{ t('totpSetup.scanHint') }}</p>
        <div v-if="uri" class="t-totp__qr">
          <NQrCode :value="uri" :size="160" />
        </div>
        <div class="t-totp__label">{{ t('totpSetup.secretLabel') }}</div>
        <div class="t-totp__secret">{{ secret }}</div>
        <NForm label-placement="top" :show-feedback="false" class="t-totp__form" @submit.prevent="confirm">
          <NFormItem :label="t('totpSetup.code')" required>
            <NInput
              v-model:value="code"
              :placeholder="t('totpSetup.codePlaceholder')"
              inputmode="numeric"
              autocomplete="one-time-code"
              @keyup.enter="confirm"
            />
          </NFormItem>
        </NForm>
      </div>
    </NSpin>
    <template #footer>
      <NButton size="small" @click="close(false)">{{ t('totpSetup.cancel') }}</NButton>
      <NButton size="small" type="primary" :loading="confirming" :disabled="!code.trim()" @click="confirm">
        {{ t('totpSetup.confirm') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * `TTotpSetupModal` - enrol an authenticator app: shared key + QR code, then
 * the first code from the app to prove the enrolment took.
 *
 * Bridge-agnostic on purpose. The two calls come in as functions so the same
 * modal serves the self-service panel today and any other enrolment flow
 * later, and so a test drives it without a bridge. The panel that opens it
 * loads it lazily: the QR encoder is the one heavy piece of the two-factor
 * surface and an account enrols an authenticator once, so it has no business
 * on the up-front download.
 *
 * Opening fetches a fresh setup every time. The backend resets the key on
 * each setup call, so a stale secret from an earlier, abandoned attempt would
 * never verify; showing it would only teach the user their app is broken.
 */
import { ref, watch } from 'vue'
import { NButton, NForm, NFormItem, NInput, NQrCode, NSpin } from 'naive-ui'
import { TModalShell, useSafeMessage } from '@tnzi/ui'
import type { TotpSetupDto } from '@tnzi/core/services/identity'
import { translatePageKey } from '../../i18n/translate'

interface Props {
  /** `v-model:show`. */
  show: boolean
  /** Generates the shared key + provisioning URI (`me.getTotpSetup`). */
  fetchSetup: () => Promise<TotpSetupDto>
  /** Verifies the first code and turns the authenticator on (`me.enableTotp`). */
  confirmCode: (code: string) => Promise<void>
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:show': [value: boolean]
  /** The authenticator is enabled; the host reloads its status. */
  enabled: []
}>()

defineOptions({ name: 'TTotpSetupModal' })

const message = useSafeMessage()
const t = (key: string): string => translatePageKey('', `admin.twoFactor.${key}`)

const loading = ref(false)
const confirming = ref(false)
const secret = ref('')
const uri = ref('')
const code = ref('')

function close(value = false): void {
  emit('update:show', value)
}

async function load(): Promise<void> {
  loading.value = true
  secret.value = ''
  uri.value = ''
  code.value = ''
  try {
    const setup = await props.fetchSetup()
    secret.value = setup.sharedKey
    uri.value = setup.authenticatorUri
  } catch (e) {
    // A refused setup (channel switched off, an authenticator already enrolled,
    // a cancelled step-up) has nothing to show; the toast carries the reason.
    message.error(e instanceof Error ? e.message : String(e))
    close(false)
  } finally {
    loading.value = false
  }
}

async function confirm(): Promise<void> {
  const value = code.value.trim()
  if (!value) {
    message.warning(t('totpSetup.codeRequired'))
    return
  }
  confirming.value = true
  try {
    await props.confirmCode(value)
    // The host announces and reloads; a second toast from here would double it.
    emit('enabled')
    close(false)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    confirming.value = false
  }
}

watch(
  () => props.show,
  (open) => {
    if (open) void load()
  },
  { immediate: true },
)
</script>

<style scoped>
.t-totp {
  display: flex;
  flex-direction: column;
  gap: 10px;
  /* Keeps the spinner visible while the key is on its way. */
  min-height: 120px;
}
.t-totp__hint {
  margin: 0;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--tnzi-base-text-muted);
}
.t-totp__qr {
  display: flex;
  justify-content: center;
  padding: 4px 0;
}
.t-totp__label {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-totp__secret {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace);
  font-size: 13px;
  padding: 8px 12px;
  background: var(--tnzi-layout-bg);
  border-radius: var(--tnzi-admin-radius-sm, 3px);
  word-break: break-all;
  user-select: all;
}
.t-totp__form {
  margin-top: 4px;
}
</style>
