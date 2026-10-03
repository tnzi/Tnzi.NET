<template>
  <TDetailBlock class="t-2fa" icon="mdi:shield-key-outline" :title="t('title')" :hint="t(`${mode}.hint`)">
    <template #titleExtra>
      <NTag v-if="status" size="small" round :bordered="false" :type="stateTone">{{ stateLabel }}</NTag>
    </template>
    <template v-if="hasHeaderActions" #actions>
      <NPopconfirm v-if="showSuspend" @positive-click="suspend">
        <template #trigger>
          <NButton size="small" tertiary type="warning" :loading="busy === 'suspend'">
            <template #icon><TSvgIcon icon="mdi:pause-circle-outline" :size="15" /></template>
            {{ t('suspend') }}
          </NButton>
        </template>
        {{ t(`${mode}.confirmSuspend`) }}
      </NPopconfirm>
      <NButton v-else-if="showResume" size="small" tertiary type="success" :loading="busy === 'resume'" @click="resume">
        <template #icon><TSvgIcon icon="mdi:play-circle-outline" :size="15" /></template>
        {{ t('resume') }}
      </NButton>
      <NPopconfirm v-if="showReset" @positive-click="reset">
        <template #trigger>
          <NButton size="small" tertiary type="error" :loading="busy === 'reset'">
            <template #icon><TSvgIcon icon="mdi:backup-restore" :size="15" /></template>
            {{ t('reset') }}
          </NButton>
        </template>
        {{ t('admin.confirmReset') }}
      </NPopconfirm>
    </template>

    <NSpin :show="loading">
      <div class="t-2fa__body">
        <TEmpty v-if="!loading && !status" :text="t(`${mode}.unavailable`)" size="small" />
        <div v-else-if="status" class="t-2fa__list">
          <TItemCard
            v-for="m in rows"
            :key="m.name"
            :title="m.label"
            :subtitle="m.subtitle"
            :icon="m.icon"
            :icon-tone="m.enabled ? 'success' : 'default'"
            :muted="!m.enabled && !m.available"
            :tags="m.tags"
          >
            <template v-if="canManage" #actions>
              <NButton
                v-if="m.enabled && !m.preferred"
                size="tiny"
                tertiary
                :loading="busy === `preferred:${m.name}`"
                @click="setPreferred(m.name)"
              >
                {{ t('setPreferred') }}
              </NButton>
              <NPopconfirm v-if="m.enabled" @positive-click="disableMethod(m.name)">
                <template #trigger>
                  <NButton size="tiny" tertiary type="error" :loading="busy === `disable:${m.name}`">
                    {{ t('disable') }}
                  </NButton>
                </template>
                {{ m.name === 'Totp' ? t(`${mode}.confirmDisableTotp`) : t(`${mode}.confirmDisable`) }}
              </NPopconfirm>
              <NButton
                v-else-if="m.canEnable"
                size="tiny"
                tertiary
                type="primary"
                :loading="busy === `enable:${m.name}`"
                @click="enableMethod(m.name)"
              >
                {{ m.setUp ? t('setUp') : t('enable') }}
              </NButton>
            </template>
            <!-- The holder's registered keys, as the row's own footer band: a
                 header with the count and the add action, then one line per key
                 (name, synced, date, remove). Only the holder can see or change
                 them (there is no admin endpoint for another account's keys). -->
            <template v-if="m.name === 'Passkey' && ops?.keys && keys.length" #footer>
              <div class="t-2fa__keys">
                <div class="t-2fa__keys-head">
                  <span class="t-2fa__keys-title">{{ t('keys.title') }}</span>
                  <span class="t-2fa__keys-count">{{ keys.length }}</span>
                  <NButton
                    v-if="canManage && canRegisterKey"
                    size="tiny"
                    quaternary
                    type="primary"
                    class="t-2fa__keys-add"
                    :loading="busy === 'key:add'"
                    @click="addKey"
                  >
                    <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
                    {{ t('keys.add') }}
                  </NButton>
                </div>
                <ul class="t-2fa__key-list">
                  <li v-for="k in keys" :key="k.credentialId" class="t-2fa__key">
                    <span class="t-2fa__key-glyph"><TSvgIcon icon="mdi:key-variant" :size="13" /></span>
                    <span class="t-2fa__key-name" :title="k.name || t('keys.unnamed')">{{ k.name || t('keys.unnamed') }}</span>
                    <NTag v-if="k.isBackedUp" size="tiny" round :bordered="false" type="info">{{ t('keys.synced') }}</NTag>
                    <span class="t-2fa__key-meta">{{ t('keys.added', { date: formatDateOnly(k.createdAt, { dateStyle: 'medium' }) }) }}</span>
                    <NPopconfirm v-if="canManage" placement="top-end" @positive-click="removeKey(k.credentialId)">
                      <template #trigger>
                        <NButton
                          size="tiny"
                          quaternary
                          circle
                          class="t-2fa__key-remove"
                          :title="t('keys.remove')"
                          :aria-label="t('keys.remove')"
                          :loading="busy === `key:${k.credentialId}`"
                        >
                          <template #icon><TSvgIcon icon="mdi:trash-can-outline" :size="14" /></template>
                        </NButton>
                      </template>
                      {{ t('keys.confirmRemove') }}
                    </NPopconfirm>
                  </li>
                </ul>
              </div>
            </template>
          </TItemCard>
        </div>
      </div>
    </NSpin>

    <!-- Self-service only: the authenticator is enrolled on the holder's own
         device. Mounted on first use so its QR encoder is fetched then, not
         with the page. -->
    <TTotpSetupModal
      v-if="ops?.totpSetup && ops.totpEnable && totpSetupMounted"
      v-model:show="totpSetupOpen"
      :fetch-setup="ops.totpSetup"
      :confirm-code="ops.totpEnable"
      @enabled="afterWrite('done.totpEnabled')"
    />
    <TStepUpModal v-if="ownPrompt" :prompt="ownPrompt" />
  </TDetailBlock>
</template>

<script setup lang="ts">
/**
 * `TTwoFactorPanel` - an account's second factor as one list of methods.
 *
 * One panel, two subjects, told apart by `mode`:
 *
 * | | `self` | `admin` |
 * |---|---|---|
 * | account | the signed-in one | the one `userId` names |
 * | endpoints | `users/profile/two-factor/*` | `admin/user-security/{id}/*` |
 * | who may write | the holder, always | holders of `user.security`; read-only otherwise |
 * | authenticator app | set up here (QR + first code), disable | disable only |
 * | security key / passkey | set up here (registers the key, then enables it), enable, disable; the registered keys are listed under the row (name, date, remove, add) | enable over the holder's registered keys, disable |
 * | reset (clear everything) | no | yes, for a lost device |
 * | step-up | the writes the backend marks `[RequireStepUp]` go through the bridge's prompt | none |
 *
 * The list is the same in both: every method the deployment offers, each
 * saying whether it is on, usable, or waiting on a verified address, with the
 * one action that applies. There is no master switch to flip before the rows
 * become live - enabling the first method turns two-factor on, "Turn off"
 * suspends it with every method kept, "Turn back on" restores them as they
 * were. That is the backend's own model (`TwoFactorEnabled` is derived from
 * the per-method flags); a switch in front of it only ever hid the rows.
 *
 * Hostable with nothing but `mode` (+ `userId`): strings come from the
 * `admin.twoFactor` block of the admin locale bundle (consumer overrides via
 * `extendLocaleMessages` win), the write grant is read here, and without a
 * `bridge` the panel builds its own on the admin client - in `self` mode with
 * its own step-up prompt and the modal that renders it. The User Center passes
 * its bridge so one prompt serves the whole page.
 */
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { NButton, NPopconfirm, NSpin, NTag } from 'naive-ui'
import { TEmpty, TSvgIcon, useSafeMessage } from '@tnzi/ui'
import { StepUpPromptController, TwoFactorType, isPasskeySupported } from '@tnzi/core/services/identity'
import type { PasskeyCredentialDto, TotpSetupDto, TwoFactorStatusDto } from '@tnzi/core/services/identity'
import { formatDateOnly } from '@tnzi/core/utils'
import TItemCard, { type ItemCardTag } from '../data/TItemCard.vue'
import TDetailBlock from '../detail/TDetailBlock.vue'
import TStepUpModal from './TStepUpModal.vue'
import { createIdentityBridge, type IdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { interpolate, translatePageKey } from '../../i18n/translate'

/** Whose second factor the panel shows. See the component doc for the two columns. */
export type TwoFactorPanelMode = 'self' | 'admin'

interface Props {
  /** `self` (default): the signed-in account. `admin`: the account `userId` names. */
  mode?: TwoFactorPanelMode
  /** Required in `admin` mode; ignored in `self` mode. Missing in `admin` mode renders the "unavailable" state, never the caller's own account. */
  userId?: string
  /**
   * Bridge to call through. Read once at setup. Without it the panel builds
   * one on the admin client; in `self` mode that bridge carries a step-up
   * prompt of its own, rendered by this panel. A host that already runs a
   * prompt (the User Center) passes its bridge so there is one prompt per page.
   */
  bridge?: IdentityBridge
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'self',
  userId: undefined,
  bridge: undefined,
})

const emit = defineEmits<{
  /** After every successful load (initial and reload), with the status. A host
   *  reads it to know which methods the deployment offers here - the User
   *  Center hides its own passkey inventory when the row carries one. */
  loaded: [status: TwoFactorStatusDto]
  /** After every successful write, with the reloaded status. */
  updated: [status: TwoFactorStatusDto]
}>()

defineOptions({ name: 'TTwoFactorPanel' })

const TTotpSetupModal = defineAsyncComponent(() => import('./TTotpSetupModal.vue'))

const t = (key: string, params?: Record<string, unknown>): string =>
  interpolate(translatePageKey('', `admin.twoFactor.${key}`), params)
const message = useSafeMessage()
const { can } = usePermissionGuard()

// Only a self-service panel without a host bridge needs its own prompt: the
// admin endpoints carry no [RequireStepUp], and a host bridge brings its own.
const ownPrompt = !props.bridge && props.mode === 'self' ? new StepUpPromptController({ client: useAdminClient() }) : null
const bridge: IdentityBridge =
  props.bridge ??
  createIdentityBridge({
    client: useAdminClient(),
    stepUp: ownPrompt ? (scope) => ownPrompt.verify(scope) : undefined,
  })

/**
 * The calls behind the panel, resolved from `mode`. Optional members are the
 * capabilities one side lacks: management never enrols an authenticator for
 * someone else, and the holder never clears their own account in one go.
 */
interface TwoFactorOps {
  load(): Promise<TwoFactorStatusDto>
  suspend(): Promise<void>
  resume(): Promise<void>
  /** SMS / email on a verified address. The authenticator goes through `totpSetup` + `totpEnable`. */
  enable(type: TwoFactorType): Promise<void>
  disable(type: TwoFactorType): Promise<void>
  setPreferred(type: TwoFactorType): Promise<void>
  reset?: () => Promise<void>
  totpSetup?: () => Promise<TotpSetupDto>
  totpEnable?: (code: string) => Promise<void>
  /**
   * Register a security key / passkey on this device and turn the method on in
   * one go, the way the authenticator's set-up flow enrols and enables. Resolves
   * `false` when the user closed the system dialog: nothing was registered,
   * nothing to announce.
   */
  passkeyEnrol?: () => Promise<boolean>
  /** The holder's registered keys (self only: no endpoint lists another account's). */
  keys?: {
    list(): Promise<PasskeyCredentialDto[]>
    remove(credentialId: string): Promise<void>
    /** Resolves `false` when the user closed the system dialog. */
    add(): Promise<boolean>
  }
}

const ops = computed<TwoFactorOps | null>(() => {
  if (props.mode === 'admin') {
    const id = props.userId
    if (!id) return null
    const s = bridge.userSecurity
    return {
      load: () => s.getTwoFactorStatus(id),
      suspend: () => s.suspendTwoFactor(id),
      resume: () => s.resumeTwoFactor(id),
      enable: (type) => s.enableTwoFactorMethod(id, type),
      disable: (type) => s.disableTwoFactorMethod(id, type),
      setPreferred: (type) => s.setPreferredTwoFactor(id, type),
      reset: () => s.resetTwoFactor(id),
    }
  }
  const me = bridge.me
  return {
    load: () => me.getTwoFactorStatus(),
    suspend: () => me.suspendTwoFactor(),
    resume: () => me.resumeTwoFactor(),
    enable: async (type) => {
      await me.enableTwoFactor({ type })
    },
    disable: (type) => me.disableTwoFactorMethod(type),
    setPreferred: (type) => me.setPreferredTwoFactor(type),
    totpSetup: () => me.getTotpSetup(),
    totpEnable: (code) => me.enableTotp({ verificationCode: code }),
    passkeyEnrol: async () => {
      // Registration (step-up included) and then the switch, in one go; the
      // credential also signs the holder in without a password.
      const created = await me.registerPasskey()
      if (!created) return false
      await me.enableTwoFactor({ type: TwoFactorType.Passkey })
      return true
    },
    keys: {
      list: () => me.getPasskeys(),
      remove: (id) => me.removePasskey(id),
      add: async () => !!(await me.registerPasskey()),
    },
  }
})

// The holder always decides about their own account. Someone else needs the
// grant that is separate from user.update: being allowed to change a phone
// number is not being allowed to make a login need one check less.
const canManage = computed(() => props.mode === 'self' || can('user.security'))
const canReset = computed(() => !!ops.value?.reset)

const loading = ref(true)
const busy = ref<string | null>(null)
const status = ref<TwoFactorStatusDto | null>(null)

/**
 * Bumped by every load. A response is applied only while its load is still the
 * latest: switching `userId` from A to B while A's request is in flight must
 * not paint A's methods (and A's keys) under B's name, where the next write
 * would act on B from what the operator read as A.
 */
let loadSeq = 0

async function load(): Promise<void> {
  const seq = ++loadSeq
  const o = ops.value
  if (!o) {
    status.value = null
    loading.value = false
    return
  }
  loading.value = true
  try {
    const next = await o.load()
    if (seq !== loadSeq) return
    status.value = next
    await loadKeys(seq)
    if (seq !== loadSeq) return
    emit('loaded', next)
  } catch (e) {
    if (seq !== loadSeq) return
    // The rows cannot be shown without it; the empty state says so and the
    // toast says why (a deployment with two-factor switched off answers 503).
    status.value = null
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}
void load()
watch(ops, () => {
  // Another subject: nothing of the previous one may stay on screen meanwhile.
  status.value = null
  keys.value = []
  void load()
})

// ── The holder's keys ─────────────────────────────────────────────────────────

const keys = ref<PasskeyCredentialDto[]>([])
/** Only where the passkey row exists: listing is refused when the wiring is off. */
const passkeyRowShown = computed(() => !!status.value?.methods.some((m) => tfName(m.type) === 'Passkey'))
const canRegisterKey = computed(() => !!ops.value?.keys && isPasskeySupported())

async function loadKeys(seq = loadSeq): Promise<void> {
  const k = ops.value?.keys
  if (!k || !passkeyRowShown.value) {
    keys.value = []
    return
  }
  try {
    const next = await k.list()
    if (seq === loadSeq) keys.value = next
  } catch (e) {
    if (seq !== loadSeq) return
    // The row still works without the list; the toast says why it is missing.
    keys.value = []
    message.error(e instanceof Error ? e.message : String(e))
  }
}

const removeKey = (credentialId: string) =>
  run(`key:${credentialId}`, () => ops.value!.keys!.remove(credentialId), 'done.keyRemoved')
const addKey = () => run('key:add', () => ops.value!.keys!.add(), 'done.keyAdded')

// ── Rows ──────────────────────────────────────────────────────────────────────

type TfName = 'Sms' | 'Email' | 'Totp' | 'Passkey'
const METHOD_ORDER: TfName[] = ['Totp', 'Passkey', 'Sms', 'Email']
const METHOD_ICON: Record<TfName, string> = {
  Totp: 'mdi:cellphone-key',
  Passkey: 'mdi:key-outline',
  Sms: 'mdi:message-text-outline',
  Email: 'mdi:email-outline',
}

/** The wire carries PascalCase names; core's enum is a string enum matching it. */
function tfName(v: unknown): TfName | null {
  if (v === TwoFactorType.Sms) return 'Sms'
  if (v === TwoFactorType.Email) return 'Email'
  if (v === TwoFactorType.Totp) return 'Totp'
  if (v === TwoFactorType.Passkey) return 'Passkey'
  return null
}
function enumOf(n: TfName): TwoFactorType {
  return TwoFactorType[n]
}

interface MethodRow {
  name: TfName
  label: string
  subtitle: string
  icon: string
  available: boolean
  enabled: boolean
  preferred: boolean
  canEnable: boolean
  /** The enable button reads "Set up": something is created first (an authenticator key, a passkey). */
  setUp: boolean
  tags: ItemCardTag[]
}

const rows = computed<MethodRow[]>(() => {
  const s = status.value
  if (!s) return []
  const preferred = tfName(s.preferredType)
  const totpEnrolHere = !!ops.value?.totpSetup
  // Registering a key needs the holder's own device and a browser with the
  // WebAuthn JSON bridges; without either the row can only point elsewhere.
  const passkeyEnrolHere = !!ops.value?.passkeyEnrol && isPasskeySupported()
  return METHOD_ORDER.flatMap((name) => {
    const m = s.methods.find((x) => tfName(x.type) === name)
    // A channel the deployment has switched off does not appear in `methods`;
    // an authenticator that is enrolled still does. Skip what the backend skipped.
    if (!m) return []
    // The backend's "requires an address" flag on the passkey row means "no key
    // registered yet". On the holder's own device that is one click away, so the
    // row reads like the authenticator's: available, with a Set up button. Only
    // when it cannot be registered here does the row say where to go.
    const keyMissing = name === 'Passkey' && !!m.requiresAddress
    const needsAddress = !!m.requiresAddress && !(keyMissing && passkeyEnrolHere)
    const tags: ItemCardTag[] = []
    if (m.enabled) tags.push({ label: t('tags.enabled'), type: 'success' })
    // Same flag, different missing thing: an address for the code methods, a credential for the key.
    else if (needsAddress) tags.push({ label: t(name === 'Passkey' ? 'tags.needsPasskey' : 'tags.needsAddress'), type: 'warning' })
    else if (m.available || keyMissing) tags.push({ label: t('tags.available'), type: 'default' })
    else tags.push({ label: t('tags.notEnrolled'), type: 'default' })
    if (m.enabled && preferred === name) tags.push({ label: t('tags.preferred'), type: 'info' })
    const setUp = name === 'Totp' || keyMissing
    return [{
      name,
      label: t(`method.${name}`),
      subtitle: needsAddress ? t(`${props.mode}.needsAddress.${name}`) : t(`${props.mode}.methodHint.${name}`),
      icon: METHOD_ICON[name],
      // A key that can be registered right here is available, not greyed out.
      available: m.available || (keyMissing && passkeyEnrolHere),
      enabled: m.enabled,
      preferred: preferred === name,
      // The authenticator is enrolled here (self) or nowhere (admin). A passkey is
      // registered here too (self), or enabled over a key the holder already has
      // (both sides).
      canEnable: !m.enabled && !needsAddress && (name === 'Totp' ? totpEnrolHere : keyMissing ? passkeyEnrolHere : m.available),
      setUp,
      tags,
    }]
  })
})

const anyEnabled = computed(() => rows.value.some((m) => m.enabled))

const showSuspend = computed(() => !!status.value?.isEnabled)
const showResume = computed(() => !!status.value && !status.value.isEnabled && anyEnabled.value)
// A lost device may leave a key with no method on: reset still has work to do.
const showReset = computed(() => canReset.value && !!status.value && (anyEnabled.value || status.value.isTotpEnabled))
const hasHeaderActions = computed(() => canManage.value && (showSuspend.value || showResume.value || showReset.value))

const stateTone = computed<'success' | 'warning' | 'default'>(() => {
  const s = status.value
  if (!s) return 'default'
  if (s.isEnabled) return 'success'
  return anyEnabled.value ? 'warning' : 'default'
})
const stateLabel = computed(() => {
  const s = status.value
  if (!s) return ''
  if (s.isEnabled) return t('stateOn')
  return anyEnabled.value ? t('stateSuspended') : t('stateOff')
})

// ── Writes ────────────────────────────────────────────────────────────────────

async function afterWrite(doneKey: string): Promise<void> {
  message.success(t(doneKey))
  await load()
  if (status.value) emit('updated', status.value)
}

/** `action` resolving `false` means the user backed out (a closed system dialog): nothing to announce or reload. */
async function run(key: string, action: () => Promise<void | boolean>, doneKey: string): Promise<void> {
  busy.value = key
  try {
    if ((await action()) === false) return
    await afterWrite(doneKey)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    busy.value = null
  }
}

const suspend = () => run('suspend', () => ops.value!.suspend(), 'done.suspended')
const resume = () => run('resume', () => ops.value!.resume(), 'done.resumed')
const reset = () => run('reset', () => ops.value!.reset!(), 'done.reset')
const disableMethod = (name: TfName) =>
  run(`disable:${name}`, () => ops.value!.disable(enumOf(name)), 'done.methodDisabled')
const setPreferred = (name: TfName) =>
  run(`preferred:${name}`, () => ops.value!.setPreferred(enumOf(name)), 'done.preferredSet')

const totpSetupMounted = ref(false)
const totpSetupOpen = ref(false)

function enableMethod(name: TfName): void {
  if (name === 'Totp') {
    totpSetupMounted.value = true
    totpSetupOpen.value = true
    return
  }
  const row = rows.value.find((r) => r.name === name)
  if (name === 'Passkey' && row?.setUp && ops.value?.passkeyEnrol) {
    void run('enable:Passkey', () => ops.value!.passkeyEnrol!(), 'done.passkeyEnabled')
    return
  }
  void run(`enable:${name}`, () => ops.value!.enable(enumOf(name)), 'done.methodEnabled')
}

defineExpose({
  /** Re-fetch the status (a host's Refresh button). */
  reload: load,
})
</script>

<style scoped>
/* Keeps the spinner somewhere to sit before the first status lands. */
.t-2fa__body {
  min-height: 48px;
}
.t-2fa__list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
/* The key inventory: an inset panel under the passkey row. A small header
   (label, count, add) over one line per key. Inset tier: it sits inside a card
   and must read as part of it, not as a second card. */
.t-2fa__keys {
  border: var(--tnzi-surface-inset-border);
  border-radius: var(--tnzi-admin-radius, 6px);
  background: var(--tnzi-bg-deep);
  padding: 6px 10px 6px 12px;
}
.t-2fa__keys-head {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 26px;
}
.t-2fa__keys-title {
  font-size: 11.5px;
  font-weight: 600;
  letter-spacing: 0.02em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}
.t-2fa__keys-count {
  font-size: 11px;
  font-weight: 600;
  line-height: 16px;
  min-width: 16px;
  padding: 0 5px;
  border-radius: 8px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
  background: rgb(23 38 60 / 0.08);
}
.t-2fa__keys-add {
  margin-left: auto;
}
.t-2fa__key-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}
.t-2fa__key {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 32px;
  font-size: 13px;
}
.t-2fa__key + .t-2fa__key {
  border-top: 1px solid var(--tnzi-border);
}
.t-2fa__key-glyph {
  width: 22px;
  height: 22px;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  background: rgb(23 38 60 / 0.06);
  color: var(--tnzi-base-text-muted);
}
.t-2fa__key-name {
  font-weight: 500;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-2fa__key-meta {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  margin-left: auto;
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}
/* Quiet until pointed at: a red bin on every line would be the loudest thing
   in the panel. naive sets its colours as inline custom properties, hence the
   !important. */
.t-2fa__key-remove {
  --n-text-color: var(--tnzi-base-text-muted) !important;
  --n-text-color-hover: var(--tnzi-error) !important;
  --n-text-color-pressed: var(--tnzi-error) !important;
  --n-text-color-focus: var(--tnzi-error) !important;
}
@media (max-width: 660px) {
  .t-2fa__key {
    flex-wrap: wrap;
    row-gap: 2px;
    padding: 4px 0;
  }
  .t-2fa__key-meta {
    margin-left: 30px;
    flex-basis: calc(100% - 60px);
  }
  .t-2fa__key-remove {
    margin-left: auto;
  }
}
</style>
