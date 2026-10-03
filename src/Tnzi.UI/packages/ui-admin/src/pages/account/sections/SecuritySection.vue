<template>
  <TUserCenterSection :title="t('nav.security')">
    <!-- ── Change password ── -->
    <TDetailBlock icon="mdi:lock-outline" :title="t('security.password.title')" :hint="t('security.password.hint')">
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
    </TDetailBlock>

    <NDivider />

    <!-- ── Two-factor authentication ──
         The same list the administration side renders for another account,
         in `self` mode: enrol / disable the authenticator here, set up a
         security key (registers it, then enables it), enable the code methods
         on a verified address, pick the preferred one, turn the whole thing
         off with the methods kept. The bridge is the page's so the
         [RequireStepUp] writes go through the one prompt the shell mounts.
         When the deployment lets a key be the second step, the row carries the
         holder's keys too (name, date, remove, add another), so nothing about
         security keys lives anywhere else on this page. -->
    <TTwoFactorPanel ref="twoFactor" mode="self" :bridge="ctx.bridge" @loaded="onTwoFactorLoaded" @updated="loadPasskeys" />

    <!-- ── Passkeys & security keys, standalone ──
         Only for a deployment that uses passkeys (sign-in without a password,
         step-up) WITHOUT offering them as the second step: then the two-factor
         list has no row to hold the keys and they need a block of their own.
         Also requires a browser that can run the ceremony (capabilities.passkey
         folds in both). Registration is two-legged and driven by @tnzi/core's
         helper - dismissing the system dialog resolves to null and is NOT an
         error. -->
    <template v-if="ctx.capabilities.value.passkey && !passkeysInTwoFactorRow">
      <NDivider />
      <TDetailBlock icon="mdi:key-outline" :title="t('security.passkey.title')" :hint="t('security.passkey.hint')">
        <template #actions>
          <NButton size="small" tertiary type="primary" :loading="passkeyAdding" @click="addPasskey">
            <template #icon><TSvgIcon icon="mdi:plus" :size="15" /></template>
            {{ t('security.passkey.add') }}
          </NButton>
        </template>

        <NSpin :show="passkeysLoading">
          <p v-if="!passkeys.length" class="t-uc-hint">{{ t('security.passkey.empty') }}</p>
          <div v-for="p in passkeys" :key="p.credentialId" class="t-uc-row">
            <div>
              <div class="t-uc-row-label">
                {{ p.name || t('security.passkey.unnamed') }}
                <NTag v-if="p.isBackedUp" size="tiny" :bordered="false" type="info">
                  {{ t('security.passkey.synced') }}
                </NTag>
              </div>
              <div class="t-uc-hint">{{ t('security.passkey.added', { date: formatDateTime(p.createdAt) }) }}</div>
            </div>
            <NPopconfirm @positive-click="removePasskey(p.credentialId)">
              <template #trigger>
                <NButton size="tiny" type="warning" ghost :loading="passkeyBusyId === p.credentialId">
                  {{ t('security.passkey.remove') }}
                </NButton>
              </template>
              {{ t('security.passkey.confirmRemove') }}
            </NPopconfirm>
          </div>
        </NSpin>
      </TDetailBlock>
    </template>
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NDivider, NForm, NFormItem, NInput, NPopconfirm, NSpin, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { TwoFactorType, type PasskeyCredentialDto, type TwoFactorStatusDto } from '@tnzi/core/services/identity'
import { formatDateTime } from '@tnzi/core/utils'
import TUserCenterSection from './TUserCenterSection.vue'
import TTwoFactorPanel from '../../../components/auth/TTwoFactorPanel.vue'
import TDetailBlock from '../../../components/detail/TDetailBlock.vue'
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

// ── Two-factor ──
const twoFactor = ref<InstanceType<typeof TTwoFactorPanel> | null>(null)
// The panel's passkey row (present when the deployment offers a key as the
// second step) holds the key inventory itself; the standalone block below is
// for deployments that use passkeys without that.
const passkeysInTwoFactorRow = ref(false)
function onTwoFactorLoaded(status: TwoFactorStatusDto): void {
  passkeysInTwoFactorRow.value = status.methods.some((m) => m.type === TwoFactorType.Passkey)
}

// ── Passkeys (WebAuthn) ──
const passkeys = ref<PasskeyCredentialDto[]>([])
const passkeysLoading = ref(false)
const passkeyAdding = ref(false)
const passkeyBusyId = ref<string | null>(null)

async function loadPasskeys(): Promise<void> {
  // Not enabled for this deployment (or unsupported browser): don't call an
  // endpoint that would 400, and leave the group unrendered.
  if (!ctx.capabilities.value.passkey) {
    passkeys.value = []
    return
  }
  passkeysLoading.value = true
  try {
    passkeys.value = await ctx.bridge.me.getPasskeys()
  } catch {
    passkeys.value = []
  } finally {
    passkeysLoading.value = false
  }
}

async function addPasskey(): Promise<void> {
  passkeyAdding.value = true
  try {
    const created = await ctx.bridge.me.registerPasskey()
    // null = the user dismissed the system dialog. A normal outcome, not a
    // failure - surfacing it as an error would be lying about what happened.
    if (!created) return
    ctx.message.success(t('security.passkey.addSuccess'))
    await loadPasskeys()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    passkeyAdding.value = false
  }
}

async function removePasskey(credentialId: string): Promise<void> {
  passkeyBusyId.value = credentialId
  try {
    await ctx.bridge.me.removePasskey(credentialId)
    ctx.message.success(t('security.passkey.removeSuccess'))
    await loadPasskeys()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    passkeyBusyId.value = null
  }
}

// The capability probe resolves after mount, so the list has to follow it -
// loading only on mount would leave the group permanently empty on a deployment
// that does have passkeys enabled.
watch(() => ctx.capabilities.value.passkey, (on) => { if (on) void loadPasskeys() }, { immediate: true })

watch(() => ctx.reloadKey.value, () => {
  void twoFactor.value?.reload()
  void loadPasskeys()
})
</script>

