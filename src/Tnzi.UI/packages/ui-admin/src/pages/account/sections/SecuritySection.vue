<template>
  <TUserCenterSection :title="t('nav.security')">
    <!-- ── Change password ── -->
    <div class="t-uc-group">
      <div class="t-uc-group-title">
        <span class="t-uc-group-title__label">
          {{ t('security.password.title') }}
          <THint type="help" :content="t('security.password.hint')" />
        </span>
      </div>
      <NForm label-placement="top" :show-feedback="false">
        <!-- Compact: current / new / confirm on one row; button on the next row. -->
        <div class="t-uc-pw-grid">
          <NFormItem :label="t('security.password.current')" required>
            <NInput
              v-model:value="pwForm.currentPassword"
              type="password"
              show-password-on="click"
              :placeholder="t('security.password.currentPlaceholder')"
            />
          </NFormItem>
          <NFormItem :label="t('security.password.new')" required>
            <NInput
              v-model:value="pwForm.newPassword"
              type="password"
              show-password-on="click"
              :placeholder="t('security.password.newPlaceholder')"
            />
          </NFormItem>
          <NFormItem :label="t('security.password.confirm')" required>
            <NInput
              v-model:value="pwForm.confirm"
              type="password"
              show-password-on="click"
              :placeholder="t('security.password.confirmPlaceholder')"
            />
          </NFormItem>
        </div>
      </NForm>
      <div class="t-uc-actions t-uc-actions--end">
        <NButton
          type="primary"
          size="small"
          :loading="changingPassword"
          :disabled="!canChangePassword"
          @click="submitChangePassword"
        >
          {{ t('security.password.submit') }}
        </NButton>
      </div>
    </div>

    <NDivider />

    <!-- ── Two-factor authentication ──
         Per-method model: SMS / email / authenticator are each independently
         enabled or disabled (backend per-method flags). The header Switch is the
         master gate: turning it OFF *suspends* 2FA (login stops challenging) but
         KEEPS the configured methods + TOTP key, so turning it back ON restores
         the exact same setup - no re-scan / re-setup. Each enabled method can be
         starred as the preferred (login-default) one. Which methods appear follows
         system config (supportedTypes folds in the runtime EnableSms/EnableEmail). -->
    <div class="t-uc-group">
      <div class="t-uc-group-title">
        <span class="t-uc-group-title__label">
          {{ t('security.twoFactor.title') }}
          <THint type="help" :content="t('security.twoFactor.masterHint')" />
        </span>
        <NSwitch :value="showMethods" :loading="masterLoading" @update:value="onToggleMaster" />
      </div>

      <!-- Suspended: master off but methods saved → reassure they're preserved. -->
      <p v-if="isSuspended" class="t-uc-hint">
        {{ t('security.twoFactor.suspendedHint')
        }}<template v-if="savedMethodLabels.length"> ({{ savedMethodLabels.join(', ') }})</template>
      </p>

      <!-- Turned on but no method active yet → nudge to pick one. -->
      <p v-if="showMethods && !isEnabled" class="t-uc-hint">{{ t('security.twoFactor.setupPrompt') }}</p>

      <!-- Method rows are ALWAYS shown; while 2FA is off (master switch off /
           suspended) they render disabled + dimmed, so the user can see the
           available methods but must turn the master switch on to change them.
           Which rows appear still follows the deployment channels
           (totpAvailable / codeMethods fold in EnableTotp/EnableSms/EnableEmail). -->

      <!-- Authenticator app (TOTP) - set up / remove independently. Hidden when
           the TOTP channel is disabled at the deployment level (EnableTotp). -->
      <div v-if="totpAvailable" class="t-uc-row" :class="{ 't-uc-row--disabled': methodsDisabled }">
        <div>
          <div class="t-uc-row-label">{{ t('security.twoFactor.totp') }}</div>
          <div class="t-uc-hint">{{ t('security.twoFactor.totpHint') }}</div>
        </div>
        <NSpace size="small" align="center">
          <NTooltip v-if="showStar('Totp')">
            <template #trigger>
              <NButton
                text
                size="tiny"
                :disabled="methodsDisabled"
                :loading="busyType === 'Totp'"
                @click="setPreferred('Totp')"
              >
                <TSvgIcon
                  :icon="methodState.Totp.preferred ? 'mdi:star' : 'mdi:star-outline'"
                  :size="16"
                  :style="methodState.Totp.preferred ? 'color: var(--tnzi-warning, #f0a020)' : ''"
                />
              </NButton>
            </template>
            {{ methodState.Totp.preferred ? t('security.twoFactor.preferred') : t('security.twoFactor.setPreferred') }}
          </NTooltip>
          <NTag v-if="isTotpEnabled" type="success" size="small" :bordered="false">
            {{ t('security.twoFactor.enabled') }}
          </NTag>
          <NPopconfirm v-if="isTotpEnabled" :disabled="methodsDisabled" @positive-click="disableTotp">
            <template #trigger>
              <NButton size="tiny" type="warning" ghost :disabled="methodsDisabled" :loading="busyType === 'Totp'">
                {{ t('security.twoFactor.disable') }}
              </NButton>
            </template>
            {{ t('security.twoFactor.confirmDisable') }}
          </NPopconfirm>
          <NButton v-else size="tiny" type="primary" ghost :disabled="methodsDisabled" @click="openTotpSetup">
            {{ t('security.twoFactor.setup') }}
          </NButton>
        </NSpace>
      </div>

      <!-- SMS / email - each a standalone toggle (available = verified address
           + the runtime EnableSms/EnableEmail option). -->
      <div
        v-for="m in codeMethods"
        :key="m.n"
        class="t-uc-row"
        :class="{ 't-uc-row--disabled': methodsDisabled }"
      >
        <div>
          <div class="t-uc-row-label">{{ m.label }}</div>
          <div class="t-uc-hint">{{ methodState[m.n].requiresAddress ? m.needsHint : m.hint }}</div>
        </div>
        <NSpace size="small" align="center">
          <NTooltip v-if="showStar(m.n)">
            <template #trigger>
              <NButton
                text
                size="tiny"
                :disabled="methodsDisabled"
                :loading="busyType === m.n"
                @click="setPreferred(m.n)"
              >
                <TSvgIcon
                  :icon="methodState[m.n].preferred ? 'mdi:star' : 'mdi:star-outline'"
                  :size="16"
                  :style="methodState[m.n].preferred ? 'color: var(--tnzi-warning, #f0a020)' : ''"
                />
              </NButton>
            </template>
            {{ methodState[m.n].preferred ? t('security.twoFactor.preferred') : t('security.twoFactor.setPreferred') }}
          </NTooltip>
          <!-- Disabled when 2FA is off (master), or the channel is on but the
               address isn't verified yet (must verify first). -->
          <NSwitch
            size="small"
            :value="methodState[m.n].enabled"
            :disabled="methodsDisabled || methodState[m.n].requiresAddress"
            :loading="busyType === m.n"
            @update:value="(v: boolean) => toggleMethod(m.n, v)"
          />
        </NSpace>
      </div>
    </div>

    <!-- TOTP setup dialog: secret + QR + verification code. -->
    <TModalShell v-model:show="totpModal.show" :title="t('security.twoFactor.setupTitle')" :width="440">
      <NSpin :show="totpModal.loading">
        <p class="t-uc-hint">{{ t('security.twoFactor.scanHint') }}</p>
        <div v-if="totpModal.uri" class="t-uc-totp-qr">
          <NQrCode :value="totpModal.uri" :size="160" />
        </div>
        <div class="t-uc-row-label" style="margin-top: 12px">{{ t('security.twoFactor.secretLabel') }}</div>
        <div class="t-uc-totp-secret">
          <span>{{ totpModal.secret }}</span>
        </div>
        <NForm label-placement="top" :show-feedback="false" style="margin-top: 12px">
          <NFormItem :label="t('security.twoFactor.verifyCode')" required>
            <NInput v-model:value="totpModal.code" :placeholder="t('security.twoFactor.verifyCodePlaceholder')" />
          </NFormItem>
        </NForm>
      </NSpin>
      <template #footer>
        <NButton size="small" @click="totpModal.show = false">{{ t('changeModal.cancel') }}</NButton>
        <NButton
          size="small"
          type="primary"
          :loading="totpModal.confirming"
          :disabled="!totpModal.code.trim()"
          @click="confirmTotp"
        >
          {{ t('security.twoFactor.confirmEnable') }}
        </NButton>
      </template>
    </TModalShell>
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { NButton, NDivider, NForm, NFormItem, NInput, NPopconfirm, NQrCode, NSpace, NSpin, NSwitch, NTag, NTooltip } from 'naive-ui'
import { THint, TSvgIcon } from '@tnzi/ui'
import { TwoFactorType } from '@tnzi/core/services/identity'
import type { TwoFactorStatusDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import { TModalShell } from '@tnzi/ui'
import { useUserCenterContext } from '../user-center-context'

const ctx = useUserCenterContext()
const t = ctx.t

// ── Password ──
const pwForm = reactive({ currentPassword: '', newPassword: '', confirm: '' })
const changingPassword = ref(false)
const canChangePassword = computed(
  () =>
    pwForm.currentPassword.length > 0 &&
    pwForm.newPassword.length >= 6 &&
    pwForm.newPassword === pwForm.confirm,
)
async function submitChangePassword(): Promise<void> {
  if (!canChangePassword.value) {
    ctx.message.warning(t('security.password.mismatch'))
    return
  }
  changingPassword.value = true
  try {
    await ctx.bridge.me.changePassword({
      currentPassword: pwForm.currentPassword,
      newPassword: pwForm.newPassword,
    })
    pwForm.currentPassword = ''
    pwForm.newPassword = ''
    pwForm.confirm = ''
    ctx.message.success(t('security.password.success'))
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    changingPassword.value = false
  }
}

// ── 2FA status (per-method) ──
const twoFactor = ref<TwoFactorStatusDto | null>(null)
const masterLoading = ref(false)
// Which method row is mid-operation (drives that row's switch/star loading).
const busyType = ref<TfName | null>(null)

async function loadTwoFactor(): Promise<void> {
  try {
    twoFactor.value = await ctx.bridge.me.getTwoFactorStatus()
  } catch {
    /* keep null */
  }
}

// The backend serializes enums as PascalCase names (JsonStringEnumConverter), so
// method types arrive as strings ("Sms"/"Email"/"Totp") - NOT the numeric enum.
// Normalize defensively (tolerate the numeric form too).
type TfName = 'Sms' | 'Email' | 'Totp'
/**
 * The double comparison this used to carry (`v === 'Sms' || v ===
 * TwoFactorType.Sms`) was a workaround: core declared these as NUMERIC enums
 * while the backend serialises them as PascalCase strings, so the enum half was
 * always dead and only the literal ever matched. Core's enum is a string enum
 * now, matching the wire.
 */
function tfName(v: unknown): TfName | null {
  if (v === TwoFactorType.Sms) return 'Sms'
  if (v === TwoFactorType.Email) return 'Email'
  if (v === TwoFactorType.Totp) return 'Totp'
  return null
}
function enumOf(n: TfName): TwoFactorType {
  return n === 'Email' ? TwoFactorType.Email : n === 'Totp' ? TwoFactorType.Totp : TwoFactorType.Sms
}

interface MethodState {
  available: boolean
  enabled: boolean
  preferred: boolean
  /** Channel enabled by config but the user's phone/email isn't verified yet. */
  requiresAddress: boolean
}
/** Per-method state map derived from the status DTO's `methods` + `preferredType`. */
const methodState = computed<Record<TfName, MethodState>>(() => {
  const base: Record<TfName, MethodState> = {
    Sms: { available: false, enabled: false, preferred: false, requiresAddress: false },
    Email: { available: false, enabled: false, preferred: false, requiresAddress: false },
    Totp: { available: false, enabled: false, preferred: false, requiresAddress: false },
  }
  for (const m of twoFactor.value?.methods ?? []) {
    const n = tfName(m.type)
    if (n) {
      base[n] = {
        available: !!m.available,
        enabled: !!m.enabled,
        preferred: !!m.isPreferred,
        requiresAddress: !!m.requiresAddress,
      }
    }
  }
  return base
})

/** Master switch: whether login actually challenges (backend TwoFactorEnabled). */
const isEnabled = computed(() => !!twoFactor.value?.isEnabled)
const isTotpEnabled = computed(() => methodState.value.Totp.enabled)
// Whether to render the authenticator (TOTP) row at all. TOTP is now a runtime
// channel like SMS/email (deployment option EnableTotp): the backend omits it from
// `methods` when the channel is off and the user has none enrolled, so the row is
// hidden for apps that don't want TOTP. It stays visible for an already-enrolled
// user (so they can still remove it) even if the channel was later turned off.
const totpAvailable = computed(() => methodState.value.Totp.available || methodState.value.Totp.enabled)
const enabledCount = computed(
  () => (['Sms', 'Email', 'Totp'] as TfName[]).filter((n) => methodState.value[n].enabled).length,
)
/** At least one method is configured (its per-method flag is on - preserved even
 *  while the master switch is suspended). */
const hasConfigured = computed(() => enabledCount.value > 0)
// SMS / email rows to render. Shown when the method is usable now (`available`)
// OR the channel is on at the deployment level but the user's address isn't
// verified yet (`requiresAddress`) - the latter renders disabled with a hint so
// the option is discoverable instead of silently hidden.
const codeMethods = computed(() =>
  ([
    {
      n: 'Sms' as const,
      label: t('security.twoFactor.smsMethod'),
      hint: t('security.twoFactor.smsMethodHint'),
      needsHint: t('security.twoFactor.smsRequiresAddress'),
    },
    {
      n: 'Email' as const,
      label: t('security.twoFactor.emailMethod'),
      hint: t('security.twoFactor.emailMethodHint'),
      needsHint: t('security.twoFactor.emailRequiresAddress'),
    },
  ]).filter((m) => methodState.value[m.n].available || methodState.value[m.n].requiresAddress),
)
/** Show the "preferred" star for an enabled method only when 2+ methods are on. */
function showStar(n: TfName): boolean {
  return methodState.value[n].enabled && enabledCount.value > 1
}

// The header Switch is the master gate. Its behaviour depends on whether any
// method is already configured:
//   - master ON + no config  → reveal the method options so the user sets one up
//     (`pendingEnable`); the first enabled method turns the master on for real.
//   - master OFF + config      → SUSPEND (keep the config; login stops challenging).
//   - master ON while suspended → RESUME (the saved config takes effect again - no
//     re-setup / re-scan).
const pendingEnable = ref(false)
const showMethods = computed(() => isEnabled.value || pendingEnable.value)
// Method rows are always rendered; they're interactive only while the master
// switch is on (enabled or mid-setup). When off/suspended they show disabled +
// dimmed so the available methods stay visible but can't be changed until the
// user turns 2FA on.
const methodsDisabled = computed(() => !showMethods.value)
// Suspended = configured but master off (and not mid-setup) → show a "saved" hint.
const isSuspended = computed(() => !isEnabled.value && hasConfigured.value && !pendingEnable.value)

/** Localized names of the currently-saved methods (for the suspended hint). */
const savedMethodLabels = computed(() =>
  (['Totp', 'Sms', 'Email'] as TfName[])
    .filter((n) => methodState.value[n].enabled)
    .map((n) =>
      n === 'Totp'
        ? t('security.twoFactor.totp')
        : n === 'Sms'
          ? t('security.twoFactor.smsMethod')
          : t('security.twoFactor.emailMethod'),
    ),
)

function onToggleMaster(v: boolean): void {
  if (v) {
    if (isEnabled.value) return
    if (hasConfigured.value) void resume() // suspended → restore the saved config
    else pendingEnable.value = true // nothing configured → let the user set one up
  } else if (isEnabled.value) {
    void suspend() // active → suspend (KEEP config, just stop challenging)
  } else {
    pendingEnable.value = false
  }
}

// Once a method actually enables 2FA, drop the transient "pending" intent.
watch(isEnabled, (v) => {
  if (v) pendingEnable.value = false
})

/** Enable / disable an SMS or email method independently. */
async function toggleMethod(n: 'Sms' | 'Email', v: boolean): Promise<void> {
  busyType.value = n
  try {
    if (v) {
      await ctx.bridge.me.enableTwoFactor({ type: enumOf(n) })
      ctx.message.success(t('security.twoFactor.enableSuccess'))
    } else {
      await ctx.bridge.me.disableTwoFactorMethod(enumOf(n))
      ctx.message.success(t('security.twoFactor.methodDisabled'))
    }
    await loadTwoFactor()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    busyType.value = null
  }
}

/** Mark a method as the preferred (login-default) one. */
async function setPreferred(n: TfName): Promise<void> {
  if (methodState.value[n].preferred) return
  busyType.value = n
  try {
    await ctx.bridge.me.setPreferredTwoFactor(enumOf(n))
    ctx.message.success(t('security.twoFactor.preferredSet'))
    await loadTwoFactor()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    busyType.value = null
  }
}

/** Suspend 2FA (master OFF) - keeps every configured method for later resume. */
async function suspend(): Promise<void> {
  masterLoading.value = true
  try {
    await ctx.bridge.me.suspendTwoFactor()
    pendingEnable.value = false
    ctx.message.success(t('security.twoFactor.suspendSuccess'))
    await loadTwoFactor()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    masterLoading.value = false
  }
}

/** Resume a suspended 2FA (master ON) - the saved config takes effect again. */
async function resume(): Promise<void> {
  masterLoading.value = true
  try {
    await ctx.bridge.me.resumeTwoFactor()
    ctx.message.success(t('security.twoFactor.resumeSuccess'))
    await loadTwoFactor()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    masterLoading.value = false
  }
}

/** Remove the authenticator app (independent of SMS/email). */
async function disableTotp(): Promise<void> {
  busyType.value = 'Totp'
  try {
    await ctx.bridge.me.disableTotp()
    ctx.message.success(t('security.twoFactor.totpDisableSuccess'))
    await loadTwoFactor()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    busyType.value = null
  }
}

// ── TOTP setup (enable) flow ──
interface TotpModalState {
  show: boolean
  loading: boolean
  confirming: boolean
  secret: string
  uri: string
  code: string
}
const totpModal = reactive<TotpModalState>({
  show: false,
  loading: false,
  confirming: false,
  secret: '',
  uri: '',
  code: '',
})
async function openTotpSetup(): Promise<void> {
  Object.assign(totpModal, { show: true, loading: true, confirming: false, secret: '', uri: '', code: '' })
  try {
    const setup = await ctx.bridge.me.getTotpSetup()
    totpModal.secret = setup.sharedKey
    totpModal.uri = setup.authenticatorUri
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
    totpModal.show = false
  } finally {
    totpModal.loading = false
  }
}
async function confirmTotp(): Promise<void> {
  if (!totpModal.code.trim()) {
    ctx.message.warning(t('security.twoFactor.codeRequired'))
    return
  }
  totpModal.confirming = true
  try {
    await ctx.bridge.me.enableTotp({ verificationCode: totpModal.code.trim() })
    ctx.message.success(t('security.twoFactor.setupSuccess'))
    totpModal.show = false
    await loadTwoFactor()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    totpModal.confirming = false
  }
}

onMounted(() => void loadTwoFactor())
watch(() => ctx.reloadKey.value, () => void loadTwoFactor())
</script>

<style scoped>
.t-uc-totp-qr {
  display: flex;
  justify-content: center;
  padding: 8px 0;
}
</style>
