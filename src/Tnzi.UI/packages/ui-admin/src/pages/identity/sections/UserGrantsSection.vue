<template>
  <TDetailSection :title="t('grants.sectionTitle')" :hint="t('grants.hint')" max-width="none">
    <template v-if="!targetIsSuper" #actions>
      <NInput v-model:value="keyword" size="small" clearable class="ug-search" :placeholder="t('grants.searchPlaceholder')">
        <template #prefix><TSvgIcon icon="mdi:magnify" :size="15" /></template>
      </NInput>
    </template>

    <NSpin :show="loading">
      <!-- A super-admin bypasses every check, so per-user rows would have no
           effect. Say that instead of rendering an editable matrix that
           silently does nothing. -->
      <p v-if="targetIsSuper" class="ug-note">{{ t('grants.superTarget') }}</p>

      <NTabs v-else v-model:value="tab" type="line" size="small" animated>
        <NTabPane name="granted">
          <template #tab>
            {{ t('grants.tabGranted') }}
            <NTag v-if="allowIds.length" size="tiny" round :bordered="false" class="ug-count">{{ allowIds.length }}</NTag>
          </template>
          <TPermissionMatrix
            :modules="modules"
            :functions-by-module="functionsByModule"
            :checked-ids="allowIds"
            :grantable-codes="grantableCodes"
            :keyword="keyword"
            :label-overrides="labelOverrides"
            expand-first
            :translate="tMatrix"
            @update:checked-ids="onAllowChecked"
          />
        </NTabPane>
        <NTabPane name="denied">
          <template #tab>
            {{ t('grants.tabDenied') }}
            <NTag v-if="denyIds.length" size="tiny" type="error" round :bordered="false" class="ug-count">{{ denyIds.length }}</NTag>
          </template>
          <p class="ug-note">{{ t('grants.denyHint') }}</p>
          <TPermissionMatrix
            :modules="modules"
            :functions-by-module="functionsByModule"
            :checked-ids="denyIds"
            :grantable-codes="grantableCodes"
            :keyword="keyword"
            :label-overrides="labelOverrides"
            expand-first
            :translate="tMatrix"
            @update:checked-ids="onDenyChecked"
          />
        </NTabPane>
      </NTabs>
    </NSpin>

    <template v-if="canAssign && !targetIsSuper" #savebar>
      <span v-if="anyDirty" class="ug-dirty">
        {{ t('grants.dirty', { added: allowAdded + denyAdded, removed: allowRemoved + denyRemoved }) }}
      </span>
      <NButton size="small" :disabled="!anyDirty" @click="reset">{{ t('admin.common.reset') }}</NButton>
      <NButton size="small" type="primary" :loading="saving" :disabled="!anyDirty" @click="save">
        {{ t('admin.common.save') }}
      </NButton>
    </template>
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * Direct permission grants for ONE user (backend `UserFunction`).
 *
 * Grants codes to a single user without touching any role. Resolution is the
 * pure-allow union of role grants and direct grants, with denies subtracting;
 * the two tabs are mutually exclusive per function (one row per (user,
 * function) on the backend, allow XOR deny), which the UI mirrors by unticking
 * the opposite tab.
 *
 * Moved out of the Users list overlay: this is a property OF a user, so it
 * belongs on the user's page beside their roles rather than in a drawer opened
 * from a table row.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NInput, NSpin, NTabPane, NTabs, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailSection from '../../../components/detail/TDetailSection.vue'
import TPermissionMatrix from '../../../components/forms/TPermissionMatrix.vue'
import { createAuthorizationBridge } from '../../../services/bridges/authorization-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useAdminAppStore } from '../../../stores/useAdminAppStore'
import { useAdminAuthStore } from '../../../stores/useAdminAuthStore'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import { ZH_SURFACE_LABELS } from '../../authorization/surface-labels'
import type { FunctionModuleDto, ModuleFunctionDto } from '@tnzi/core/services/authorization'

const props = defineProps<{
  userId: string
  /** Role NAMES on the user - used to detect a super-admin target. */
  userRoleNames: string[]
  canAssign: boolean
  t: (key: string, named?: Record<string, unknown>) => string
}>()

const bridge = createAuthorizationBridge({ client: useAdminClient() })
const message = useSafeMessage()

// The role matrix i18n namespace already carries every matrix.* key in both
// locales - reuse it instead of duplicating the strings under identity.users.
const tMatrix = makePageTranslator('authorization.roleFunctions')
const appStore = useAdminAppStore()
const authStore = useAdminAuthStore()
const labelOverrides = computed(() => (appStore.locale === 'zh-cn' ? ZH_SURFACE_LABELS : null))

// Delegation-aware graying (mirrors the backend guard): a non-super grantor may
// only hand out codes from their own set. null = everything grantable.
const grantableCodes = computed<string[] | null>(() =>
  authStore.isSuperUser || authStore.userInfo === null ? null : authStore.userPermissions,
)

const loading = ref(true)
const saving = ref(false)
const keyword = ref('')
const tab = ref<'granted' | 'denied'>('granted')

const modules = ref<FunctionModuleDto[]>([])
const functionsByModule = ref(new Map<string, ModuleFunctionDto[]>())
const superRoleNames = ref<Set<string>>(new Set())

const allowIds = ref<string[]>([])
const allowOriginal = ref<Set<string>>(new Set())
const denyIds = ref<string[]>([])
const denyOriginal = ref<Set<string>>(new Set())

const targetIsSuper = computed(() =>
  props.userRoleNames.some((name) => superRoleNames.value.has(name.toLowerCase())),
)

async function load(): Promise<void> {
  loading.value = true
  try {
    const list = await bridge.functionModules.getAll()
    const next = new Map<string, ModuleFunctionDto[]>()
    await Promise.all(
      list.map(async (m) => {
        try {
          next.set(m.id, await bridge.permissions.getByModule(m.id))
        } catch {
          // One unreadable module must not blank the whole matrix.
          next.set(m.id, [])
        }
      }),
    )
    modules.value = list
    functionsByModule.value = next

    try {
      const names = await bridge.roleFunctions.superAdminRoles()
      superRoleNames.value = new Set(names.map((n) => n.toLowerCase()))
    } catch {
      // Older backends have no such endpoint: render normally, the backend
      // guard still enforces.
      superRoleNames.value = new Set()
    }

    const [ids, denied] = await Promise.all([
      bridge.userFunctions.getAssignedIds(props.userId),
      bridge.userFunctions.getDeniedIds(props.userId),
    ])
    allowIds.value = [...ids]
    allowOriginal.value = new Set(ids)
    denyIds.value = [...denied]
    denyOriginal.value = new Set(denied)
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    loading.value = false
  }
}
void load()

watch(() => props.userId, () => void load())

// Allow and deny are mutually exclusive per function; ticking one side unticks
// the other so the UI can never ask the backend for a contradiction.
function onAllowChecked(ids: string[]): void {
  allowIds.value = ids
  const set = new Set(ids)
  denyIds.value = denyIds.value.filter((id) => !set.has(id))
}
function onDenyChecked(ids: string[]): void {
  denyIds.value = ids
  const set = new Set(ids)
  allowIds.value = allowIds.value.filter((id) => !set.has(id))
}

function added(checked: string[], original: Set<string>): number {
  return checked.filter((id) => !original.has(id)).length
}
function removed(checked: string[], original: Set<string>): number {
  const set = new Set(checked)
  let n = 0
  for (const id of original) if (!set.has(id)) n += 1
  return n
}

const allowAdded = computed(() => added(allowIds.value, allowOriginal.value))
const allowRemoved = computed(() => removed(allowIds.value, allowOriginal.value))
const denyAdded = computed(() => added(denyIds.value, denyOriginal.value))
const denyRemoved = computed(() => removed(denyIds.value, denyOriginal.value))
const anyDirty = computed(
  () => allowAdded.value + allowRemoved.value + denyAdded.value + denyRemoved.value > 0,
)

function reset(): void {
  allowIds.value = [...allowOriginal.value]
  denyIds.value = [...denyOriginal.value]
}

async function save(): Promise<void> {
  saving.value = true
  try {
    // Save the deny set FIRST so a grant→deny move never passes through a
    // transient state where the code is granted and the deny is not yet stored.
    if (denyAdded.value + denyRemoved.value > 0) {
      await bridge.userFunctions.setDeniedForUser(props.userId, denyIds.value)
      denyOriginal.value = new Set(denyIds.value)
    }
    if (allowAdded.value + allowRemoved.value > 0) {
      await bridge.userFunctions.setForUser(props.userId, allowIds.value)
      allowOriginal.value = new Set(allowIds.value)
    }
    message.success(props.t('grants.success'))
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.ug-search {
  width: 240px;
  max-width: 100%;
}
.ug-note {
  margin: 0 0 10px;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--tnzi-base-text-muted);
}
.ug-count {
  margin-left: 6px;
}
.ug-dirty {
  margin-right: auto;
  font-size: 12.5px;
  color: var(--tnzi-warning);
}
</style>
