<template>
  <NSpin :show="loading">
    <TDetailBlock
      class="t-sip"
      icon="mdi:ip-network-outline"
      :title="t('detail.security.ipAllowList.title')"
      :hint="t('detail.security.ipAllowList.hint')"
    >
      <template #titleExtra>
        <NTag v-if="policy" size="small" round :bordered="false" :type="policy.ipAllowListEnabled ? 'success' : 'default'">
          {{ policy.ipAllowListEnabled ? t('detail.security.ipAllowList.on') : t('detail.security.ipAllowList.off') }}
        </NTag>
      </template>
      <template v-if="canManage" #actions>
        <NButton size="small" tertiary :disabled="!dirty" @click="resetForm">
          {{ t('admin.common.reset') }}
        </NButton>
        <NButton size="small" type="primary" :disabled="!dirty" :loading="saving" @click="save">
          <template #icon><TSvgIcon icon="mdi:content-save-outline" :size="15" /></template>
          {{ t('admin.common.save') }}
        </NButton>
      </template>

      <NAlert
        v-if="policy && policy.exemptedByRoles.length"
        type="warning"
        :bordered="false"
        class="t-sip__alert"
        :title="t('detail.security.ipAllowList.exemptTitle')"
      >
        {{ t('detail.security.ipAllowList.exemptBody', { roles: policy.exemptedByRoles.join(', ') }) }}
      </NAlert>

      <div v-if="policy" class="t-sip__form">
        <div class="t-sip__switch">
          <NSwitch v-model:value="form.enabled" :disabled="!canManage" size="small" />
          <span>{{ t('detail.security.ipAllowList.enable') }}</span>
        </div>
        <NInput
          v-model:value="form.allowedIps"
          type="textarea"
          class="t-sip__text"
          :autosize="{ minRows: 4, maxRows: 14 }"
          :readonly="!canManage"
          :placeholder="t('detail.security.ipAllowList.placeholder')"
          spellcheck="false"
        />
        <div class="t-sip__foot">
          <span class="t-sip__format">{{ t('detail.security.ipAllowList.format') }}</span>
          <NButton
            v-if="canManage && policy.callerIpAddress && !containsCallerIp"
            size="tiny"
            tertiary
            @click="addCallerIp"
          >
            <template #icon><TSvgIcon icon="mdi:plus" :size="13" /></template>
            {{ t('detail.security.ipAllowList.addMine', { ip: policy.callerIpAddress }) }}
          </NButton>
        </div>
        <div v-if="parsedEntries.length" class="t-sip__entries">
          <NTag v-for="e in parsedEntries" :key="e" size="small" :bordered="false">{{ e }}</NTag>
        </div>
      </div>
    </TDetailBlock>
  </NSpin>
</template>

<script setup lang="ts">
/**
 * `TSignInPolicyPanel` - one account's sign-in IP allow-list, administered by
 * someone else.
 *
 * When the list is on, the account can only obtain tokens (refresh included)
 * from an address on it; a denied attempt looks exactly like a wrong password
 * to the caller, and the real reason is in the login log. A role the
 * deployment exempts makes the list inert for that account, and the panel
 * says so out loud instead of letting the list look enforced.
 *
 * The text is saved verbatim (newline / comma / semicolon separated, `#`
 * comments); the chips underneath preview the entries a save would store and
 * the server names every malformed one in its 400. Reads ride `user.view`,
 * writes ride `user.security`, which the panel reads itself so no host can
 * gate them on the wrong code.
 *
 * Hostable with nothing but a `userId`: strings come from the
 * `identity.users` namespace of the admin locale bundle. It draws no section
 * chrome, so it sits inside a host's own section next to the host's blocks.
 */
import { computed, reactive, ref, watch } from 'vue'
import { NAlert, NButton, NInput, NSpin, NSwitch, NTag } from 'naive-ui'
import { TSvgIcon, useSafeMessage } from '@tnzi/ui'
import type { UserSignInPolicyDto } from '@tnzi/core/services/identity'
import TDetailBlock from '../detail/TDetailBlock.vue'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { interpolate, translatePageKey } from '../../i18n/translate'

const props = defineProps<{
  /** The account whose allow-list is shown. */
  userId: string
}>()

const emit = defineEmits<{
  /** After a successful save, with the stored policy. */
  updated: [policy: UserSignInPolicyDto]
}>()

defineOptions({ name: 'TSignInPolicyPanel' })

const t = (key: string, params?: Record<string, unknown>): string =>
  interpolate(translatePageKey('identity.users', key), params)
const message = useSafeMessage()
const { can } = usePermissionGuard()
// A separate grant from user.update: someone allowed to change a phone number
// is not thereby allowed to decide where a login may come from.
const canManage = computed(() => can('user.security'))

const bridge = createIdentityBridge({ client: useAdminClient() })

const loading = ref(true)
const saving = ref(false)
const policy = ref<UserSignInPolicyDto | null>(null)
const form = reactive({ enabled: false, allowedIps: '' })

/**
 * Bumped by every load. Only the latest load's response is applied: with
 * `userId` switched from A to B while A's request is in flight, A's list must
 * not land in B's form, where Save would store it on B.
 */
let loadSeq = 0

async function load(): Promise<void> {
  const seq = ++loadSeq
  loading.value = true
  try {
    const next = await bridge.userSecurity.getSignInPolicy(props.userId)
    if (seq !== loadSeq) return
    policy.value = next
    resetForm()
  } catch (err) {
    if (seq !== loadSeq) return
    policy.value = null
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}
void load()
watch(
  () => props.userId,
  () => {
    // The previous account's list must not stay editable (or savable) while B loads.
    policy.value = null
    resetForm()
    void load()
  },
)

function resetForm(): void {
  form.enabled = policy.value?.ipAllowListEnabled ?? false
  form.allowedIps = policy.value?.allowedIps ?? ''
}

const dirty = computed(() => {
  const p = policy.value
  if (!p) return false
  return form.enabled !== p.ipAllowListEnabled || (form.allowedIps || '') !== (p.allowedIps ?? '')
})

/**
 * Same split the backend applies (newline / comma / semicolon, `#` comments), so
 * the preview chips show exactly the entries a save would store. Validity is
 * the server's call: it names every malformed entry in the 400.
 */
const parsedEntries = computed(() =>
  (form.allowedIps || '')
    .split(/[\n\r,;]+/)
    .map((s) => s.trim())
    .filter((s) => s && !s.startsWith('#')),
)

const containsCallerIp = computed(() => {
  const ip = policy.value?.callerIpAddress
  return !!ip && parsedEntries.value.includes(ip)
})

function addCallerIp(): void {
  const ip = policy.value?.callerIpAddress
  if (!ip) return
  const text = form.allowedIps.trimEnd()
  form.allowedIps = text ? `${text}\n${ip}` : ip
}

async function save(): Promise<void> {
  const userId = props.userId
  saving.value = true
  try {
    const saved = await bridge.userSecurity.setIpAllowList(userId, {
      enabled: form.enabled,
      allowedIps: form.allowedIps.trim() ? form.allowedIps : null,
    })
    // The panel moved on to another account meanwhile: the save stands for the
    // account it was made on, but its result is not this account's policy.
    if (userId !== props.userId) return
    policy.value = saved
    resetForm()
    message.success(t('detail.security.ipAllowList.saved'))
    emit('updated', policy.value)
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    saving.value = false
  }
}

defineExpose({
  /** Re-fetch the policy (a host's Refresh button). */
  reload: load,
})
</script>

<style scoped>
.t-sip__alert {
  margin-bottom: 14px;
}
.t-sip__form {
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-width: 720px;
}
.t-sip__switch {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-sip__text :deep(textarea) {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace);
  font-size: 12.5px;
}
.t-sip__foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.t-sip__format {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-sip__entries {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
</style>
