<template>
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NButton size="small" @click="loadAll">{{ t('refresh') }}</NButton>
    </template>

    <NCard :bordered="false" class="t-role-func-page__card">
      <TMasterDetailLayout :master-width="240">
        <template #master>
          <div class="t-role-func-page__rail">
          <div class="t-role-func-page__rail-caption">{{ t('rolesCaption') }}</div>
          <NInput
            v-model:value="roleFilter"
            :placeholder="t('searchRole')"
            size="small"
            clearable
          />
          <NSpin :show="rolesLoading" class="mt-8px t-role-func-page__rail-scroll">
            <div v-if="!roles.length && !rolesLoading" class="t-role-func-page__empty">
              {{ t('noRoles') }}
            </div>
            <ul v-else class="t-role-func-page__role-list">
              <li
                v-for="role in filteredRoles"
                :key="role.id"
                class="t-role-func-page__role-item"
                :class="{ 'is-active': role.id === selectedRoleId }"
                @click="selectRole(role.id)"
              >
                <span class="t-role-func-page__role-dot" :class="roleDotClass(role.id)" />
                <span class="t-role-func-page__role-body">
                  <span class="t-role-func-page__role-line">
                    <span class="t-role-func-page__role-name">{{ role.name }}</span>
                    <span
                      v-if="isSuperRole(role)"
                      class="t-role-func-page__role-count is-super"
                      :title="t('super.badgeTip')"
                    >
                      {{ t('super.all') }}
                    </span>
                    <span v-else-if="roleAssignedCount(role.id) !== null" class="t-role-func-page__role-count">
                      {{ roleAssignedCount(role.id) }} / {{ totalFunctionCount }}
                    </span>
                  </span>
                  <code
                    v-if="!isCodeRedundant(role.name, role.code)"
                    class="t-role-func-page__role-code"
                  >{{ role.code }}</code>
                </span>
              </li>
            </ul>
          </NSpin>
          <div class="t-role-func-page__rail-stats">
            {{ t('catalogueStats', { codes: totalFunctionCount, modules: modules.length, surfaces: surfaceCount }) }}
          </div>
          </div>
        </template>
        <template #detail>
          <section class="t-role-func-page__tree">
          <div v-if="!selectedRoleId" class="t-role-func-page__placeholder">
            <!-- Mobile hides the master rail, and the header's role switcher
                 only renders once a role is selected - so without an entry
                 point here a fresh phone visit is a dead end. Surface the
                 role picker as a CTA; desktop keeps the "pick on the left"
                 hint since its rail is visible. -->
            <NButton v-if="isSm" type="primary" @click="rolePickerOpen = true">
              <template #icon><TSvgIcon icon="mdi:account-multiple-outline" :size="16" /></template>
              {{ t('selectRole') }}
            </NButton>
            <template v-else>{{ t('selectRolePrompt') }}</template>
          </div>
          <div v-else>
            <header class="t-role-func-page__tree-header">
              <div class="t-role-func-page__role-summary">
                <div class="t-role-func-page__role-title-line">
                  <h3 class="t-role-func-page__selected-name">{{ selectedRole?.name }}</h3>
                  <NTag
                    v-if="!isCodeRedundant(selectedRole?.name, selectedRole?.code)"
                    size="small"
                    :bordered="false"
                  >{{ selectedRole?.code }}</NTag>
                </div>
                <div v-if="selectedIsSuper" class="t-role-func-page__role-progress-line">
                  <span class="t-role-func-page__assigned-text">
                    {{ t('super.summary', { total: totalFunctionCount }) }}
                  </span>
                  <span class="t-role-func-page__bar">
                    <span class="t-role-func-page__bar-fill is-super" style="width: 100%" />
                  </span>
                  <NTag size="small" type="warning" :bordered="false" round>
                    {{ t('super.badge') }}
                  </NTag>
                </div>
                <div v-else class="t-role-func-page__role-progress-line">
                  <span class="t-role-func-page__assigned-text">
                    {{ t('assignedPrefix') }}
                    <b>{{ checkedFunctionIds.length }}</b> / {{ totalFunctionCount }}
                  </span>
                  <span class="t-role-func-page__bar">
                    <span
                      class="t-role-func-page__bar-fill"
                      :style="{ width: `${totalFunctionCount ? Math.round((checkedFunctionIds.length / totalFunctionCount) * 100) : 0}%` }"
                    />
                  </span>
                  <NTag v-if="isDirty" size="small" type="warning" :bordered="false" round>
                    {{ t('dirtyChip', { added: dirtyAdded, removed: dirtyRemoved }) }}
                  </NTag>
                  <NTag v-else size="small" type="success" :bordered="false" round>
                    {{ t('savedBadge') }}
                  </NTag>
                </div>
              </div>
              <!-- Mobile: the rail is hidden, roles are picked from a bottom sheet. -->
              <NButton v-if="isSm" size="small" secondary type="primary" @click="rolePickerOpen = true">
                {{ selectedRole?.name ?? t('selectRole') }}
                <template #icon><TSvgIcon icon="mdi:chevron-down" :size="14" /></template>
              </NButton>
              <NSpace v-else-if="!selectedIsSuper" :size="8">
                <NButton size="small" :disabled="!isDirty || treeLoading" @click="reset">
                  {{ t('reset') }}
                </NButton>
                <NButton
                  type="primary"
                  size="small"
                  :loading="saving"
                  :disabled="!isDirty"
                  @click="handleSave"
                >
                  {{ t('save') }}
                </NButton>
              </NSpace>
            </header>
            <!-- Super-admin: a bypass role holds the whole catalogue, so the matrix
                 below is shown READ-ONLY (all granted, locked) rather than replaced by
                 an explainer. This banner says why + offers to clear any stale explicit
                 rows (which have no effect for a bypass role). -->
            <div v-if="selectedIsSuper" class="t-role-func-page__super-banner">
              <TSvgIcon icon="mdi:shield-crown-outline" :size="22" class="t-role-func-page__super-banner-icon" />
              <div class="t-role-func-page__super-banner-text">
                <span class="t-role-func-page__super-banner-title">{{ t('super.badge') }}</span>
                <span class="t-role-func-page__super-banner-line">{{ t('super.explain1') }}</span>
              </div>
              <code class="t-role-func-page__super-banner-code">Authorization:SuperAdminRoles</code>
              <div
                v-if="(roleAssignedCount(selectedRoleId!) ?? 0) > 0"
                class="t-role-func-page__super-stale"
              >
                <TSvgIcon icon="mdi:alert-circle-outline" :size="14" />
                {{ t('super.stale', { n: roleAssignedCount(selectedRoleId!) ?? 0 }) }}
                <NButton size="tiny" secondary type="warning" @click="onCleanupSuperRows">
                  {{ t('super.cleanup') }}
                </NButton>
              </div>
            </div>
            <div class="t-role-func-page__toolbar">
              <div class="t-role-func-page__toolbar-btns">
                <!-- Write ops don't apply to a read-only super-admin role. -->
                <template v-if="!selectedIsSuper">
                  <NButton size="small" :disabled="treeLoading" @click="openCompareModal">
                    <template #icon><TSvgIcon icon="mdi:compare-horizontal" :size="14" /></template>
                    {{ t('compare.button') }}
                  </NButton>
                  <NButton size="small" :disabled="treeLoading" @click="openCloneModal">
                    <template #icon><TSvgIcon icon="mdi:content-copy" :size="14" /></template>
                    {{ t('clone.button') }}
                  </NButton>
                  <NButton
                    size="small"
                    type="error"
                    ghost
                    :disabled="!checkedFunctionIds.length || treeLoading"
                    @click="onClearAll"
                  >
                    {{ t('clearAll') }}
                  </NButton>
                  <span class="t-role-func-page__toolbar-divider" />
                </template>
                <NButton size="small" :disabled="treeLoading" @click="toggleExpandAll">
                  <template #icon>
                    <TSvgIcon
                      :icon="allExpanded ? 'mdi:unfold-less-horizontal' : 'mdi:unfold-more-horizontal'"
                      :size="15"
                    />
                  </template>
                  {{ allExpanded ? t('collapseAll') : t('expandAll') }}
                </NButton>
                <span class="t-role-func-page__toolbar-divider" />
                <!-- Legend + gates hint collapsed into a hover popover: it is
                     reference-only info that used to eat two full rows above the
                     matrix. -->
                <NPopover trigger="hover" placement="bottom-start" style="max-width: 340px">
                  <template #trigger>
                    <NButton size="small" quaternary>
                      <template #icon><TSvgIcon icon="mdi:help-circle-outline" :size="15" /></template>
                      {{ t('legend.title') }}
                    </NButton>
                  </template>
                  <div class="t-role-func-page__legend-pop">
                    <div class="t-role-func-page__legend-pop-grid">
                      <span class="t-role-func-page__legend-item">
                        <span class="t-role-func-page__swatch is-on"><TSvgIcon icon="mdi:check" :size="11" /></span>
                        {{ t('legend.granted') }}
                      </span>
                      <span class="t-role-func-page__legend-item">
                        <span class="t-role-func-page__swatch" />
                        {{ t('legend.ungranted') }}
                      </span>
                      <span class="t-role-func-page__legend-item">
                        <span class="t-role-func-page__swatch-dot">·</span>
                        {{ t('legend.na') }}
                      </span>
                      <span class="t-role-func-page__legend-item">
                        <span class="t-role-func-page__swatch is-hatched" />
                        {{ t('legend.disabled') }}
                      </span>
                      <span class="t-role-func-page__legend-item">
                        <span class="t-role-func-page__swatch is-menu" />
                        {{ t('matrix.menuEntry') }}
                      </span>
                      <span class="t-role-func-page__legend-item">
                        <span class="t-role-func-page__swatch is-tech">T</span>
                        {{ t('legend.technical') }}
                      </span>
                    </div>
                    <div class="t-role-func-page__legend-note">
                      <TSvgIcon icon="mdi:information-outline" :size="14" />
                      <span>
                        <b class="t-role-func-page__legend-term">{{ t('legend.viewTerm') }}</b>
                        {{ t('legend.viewRest') }}
                      </span>
                    </div>
                  </div>
                </NPopover>
              </div>
              <NInput
                v-model:value="matrixFilter"
                :placeholder="t('matrix.searchPlaceholder')"
                size="small"
                clearable
                class="t-role-func-page__matrix-search"
              >
                <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
              </NInput>
            </div>
            <NSpin :show="treeLoading">
              <div v-if="!modules.length" class="t-role-func-page__empty">
                {{ t('noFunctions') }}
              </div>
              <TPermissionMatrix
                v-else
                ref="matrixRef"
                :modules="modules"
                :functions-by-module="functionsByModule"
                :checked-ids="checkedFunctionIds"
                :grantable-codes="grantableCodes"
                :keyword="matrixFilter"
                :label-overrides="labelOverrides"
                :readonly="selectedIsSuper"
                expand-first
                :translate="t"
                class="t-role-func-page__matrix"
                @update:checked-ids="onCheckedChange"
              />
            </NSpin>
            <!-- Mobile sticky save bar - the header buttons are hidden below md,
                 so pending edits surface here instead. -->
            <div v-if="isSm && isDirty" class="t-role-func-page__savebar">
              <NTag size="small" type="warning" :bordered="false" round>
                {{ t('dirtyChip', { added: dirtyAdded, removed: dirtyRemoved }) }}
              </NTag>
              <NButton size="small" :disabled="treeLoading" @click="reset">{{ t('reset') }}</NButton>
              <NButton type="primary" size="small" :loading="saving" @click="handleSave">
                {{ t('save') }}
              </NButton>
            </div>
          </div>
          </section>
        </template>
      </TMasterDetailLayout>
    </NCard>

    <!-- Mobile role picker - bottom sheet listing the same roles as the rail. -->
    <TOverlayTheme>
    <NDrawer v-model:show="rolePickerOpen" placement="bottom" height="60%">
      <NDrawerContent :title="t('selectRole')" closable>
        <ul class="t-role-func-page__role-list t-role-func-page__role-list--sheet">
          <li
            v-for="role in roles"
            :key="role.id"
            class="t-role-func-page__role-item"
            :class="{ 'is-active': role.id === selectedRoleId }"
            @click="onPickRole(role.id)"
          >
            <span class="t-role-func-page__role-dot" :class="roleDotClass(role.id)" />
            <span class="t-role-func-page__role-body">
              <span class="t-role-func-page__role-line">
                <span class="t-role-func-page__role-name">{{ role.name }}</span>
                <span v-if="isSuperRole(role)" class="t-role-func-page__role-count is-super">
                  {{ t('super.all') }}
                </span>
                <span v-else-if="roleAssignedCount(role.id) !== null" class="t-role-func-page__role-count">
                  {{ roleAssignedCount(role.id) }} / {{ totalFunctionCount }}
                </span>
              </span>
              <code class="t-role-func-page__role-code">{{ role.code }}</code>
            </span>
          </li>
        </ul>
      </NDrawerContent>
    </NDrawer>
    </TOverlayTheme>

    <!--
      Compare-roles overlay - three-column read-only diff, driven by the shared
      useDetail + TDetailHost renderer (modal mode) so it is deep-linkable via
      `?compare=new` and Back-closeable. Role A is the currently-selected role;
      role B is picked from the catalogue. The result comes from the backend
      (PermissionComparisonDto: onlyInRole1 / onlyInRole2 / shared).
    -->
    <TDetailHost
      :state="compareDetail"
      :title="t('compare.title')"
      :width="1080"
      :translate="t"
    >
      <template #default>
        <div class="t-role-func-page__compare-picker">
          <div class="t-role-func-page__compare-pair">
            <span class="t-role-func-page__compare-label">{{ t('compare.roleA') }}:</span>
            <NTag :bordered="false">{{ selectedRole?.name ?? EMPTY_DASH }}</NTag>
          </div>
          <div class="t-role-func-page__compare-pair">
            <span class="t-role-func-page__compare-label">{{ t('compare.roleB') }}:</span>
            <NSelect
              v-model:value="compare.targetRoleId"
              :options="otherRoleOptions"
              :placeholder="t('compare.pickRole')"
              filterable
              clearable
              size="small"
              class="w-280px"
            />
            <NButton
              type="primary"
              size="small"
              :disabled="!compare.targetRoleId"
              :loading="compare.loading"
              @click="runCompare"
            >
              {{ t('compare.run') }}
            </NButton>
          </div>
        </div>

        <NSpin :show="compare.loading">
          <div v-if="compare.result" class="t-role-func-page__compare-grid">
            <section
              v-for="bucket in compareBuckets"
              :key="bucket.id"
              class="t-role-func-page__compare-bucket"
            >
              <header class="t-role-func-page__compare-bucket-header">
                <h4 class="t-role-func-page__compare-bucket-title">{{ bucket.title }}</h4>
                <NTag size="tiny" :bordered="false" :type="bucket.tone">
                  {{ bucket.rows.length }}
                </NTag>
              </header>
              <div v-if="!bucket.rows.length" class="t-role-func-page__compare-empty">
                {{ t('compare.empty') }}
              </div>
              <ul v-else class="t-role-func-page__compare-list">
                <li v-for="row in bucket.rows" :key="row.id">
                  <div class="t-role-func-page__compare-fn-name">{{ row.name }}</div>
                  <code class="t-role-func-page__compare-fn-code">{{ row.code }}</code>
                  <div v-if="row.moduleCode" class="t-role-func-page__compare-fn-module">
                    {{ row.moduleCode }}
                  </div>
                </li>
              </ul>
            </section>
          </div>
        </NSpin>
      </template>

      <template #footer="{ close }">
        <NButton @click="close">{{ t('compare.close') }}</NButton>
      </template>
    </TDetailHost>

    <!--
      Clone-from-role overlay - pick a source role, call the backend clone
      endpoint, refresh the current role's assignment set. Deep-linkable via
      `?clone=new`. Idempotent on the backend (existing assignments are kept).
    -->
    <TDetailHost
      :state="cloneDetail"
      :title="t('clone.title')"
      :width="480"
      :translate="t"
    >
      <template #default>
        <div class="t-role-func-page__clone-label">{{ t('clone.sourceRole') }}</div>
        <div v-if="cloneSourceRoles.length === 0" class="t-role-func-page__empty">
          {{ t('clone.noSource') }}
        </div>
        <ul v-else class="t-role-func-page__role-list t-role-func-page__role-list--picker">
          <li
            v-for="role in cloneSourceRoles"
            :key="role.id"
            class="t-role-func-page__role-item"
            :class="{ 'is-active': role.id === clone.sourceRoleId }"
            @click="clone.sourceRoleId = role.id"
          >
            <span class="t-role-func-page__role-dot" :class="roleDotClass(role.id)" />
            <span class="t-role-func-page__role-body">
              <span class="t-role-func-page__role-line">
                <span class="t-role-func-page__role-name">{{ role.name }}</span>
                <span v-if="roleAssignedCount(role.id) !== null" class="t-role-func-page__role-count">
                  {{ roleAssignedCount(role.id) }} / {{ totalFunctionCount }}
                </span>
              </span>
              <code class="t-role-func-page__role-code">{{ role.code }}</code>
            </span>
            <TSvgIcon
              v-if="role.id === clone.sourceRoleId"
              icon="mdi:check-circle"
              :size="16"
              class="t-role-func-page__clone-check"
            />
          </li>
        </ul>
        <p class="t-role-func-page__hint">{{ t('clone.hint') }}</p>
      </template>
      <template #footer="{ close }">
        <NButton @click="close">{{ t('compare.close') }}</NButton>
        <NButton
          type="primary"
          :loading="clone.saving"
          :disabled="!clone.sourceRoleId"
          @click="runClone"
        >
          {{ t('clone.confirm') }}
        </NButton>
      </template>
    </TDetailHost>
  </TContentPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, reactive, ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useDialog } from 'naive-ui'
import type { SelectOption } from 'naive-ui'
import {
  NCard,
  NSpace,
  NButton,
  NInput,
  NSpin,
  NTag,
  NPopover,
  NSelect,
  NDrawer,
  NDrawerContent,
} from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { isCodeRedundant } from '../../headless/code-label'
import TPermissionMatrix from '../../components/forms/TPermissionMatrix.vue'
import { ZH_SURFACE_LABELS } from './surface-labels'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { TOverlayTheme } from '../../components/overlay'
import { useDetail } from '../../headless/useDetail'
import { useSafeMessage } from '../_shared/safe-message'
import { createAuthorizationBridge } from '../../services/bridges/authorization-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { makePageTranslator } from '../_shared/translate'
import { TSvgIcon } from '@tnzi/ui'
import type {
  FunctionModuleDto,
  ModuleFunctionDto,
  PermissionComparisonDto,
  FunctionSummaryDto,
} from '@tnzi/core/services/authorization'
import type { RoleDto } from '@tnzi/core/services/identity'
import TContentPage from '../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../components/layout/TMasterDetailLayout.vue'

interface Role { id: string; code: string; name: string; description?: string | null }

const client = useAdminClient()
const authBridge = createAuthorizationBridge({ client })
const idBridge = createIdentityBridge({ client })
const t = makePageTranslator('authorization.roleFunctions')

const message = useSafeMessage()

// Framework confirm dialog for the unsaved-changes role switch. Guarded so a
// bare test mount (no <n-dialog-provider>) falls back to the native confirm
// instead of throwing at setup - the confirmation gate is never skipped.
const dialog = (() => {
  try {
    return useDialog()
  } catch {
    return null
  }
})()

function confirmWarning(title: string, content: string): Promise<boolean> {
  if (!dialog) return Promise.resolve(window.confirm(content))
  return new Promise((resolve) => {
    dialog.warning({
      title,
      content,
      positiveText: t('admin.common.confirm'),
      negativeText: t('admin.common.cancel'),
      onPositiveClick: () => resolve(true),
      onNegativeClick: () => resolve(false),
      onClose: () => resolve(false),
      onMaskClick: () => resolve(false),
    })
  })
}

function confirmSwitchDirty(): Promise<boolean> {
  return confirmWarning(t('confirmSwitchTitle'), t('confirmSwitchDirty'))
}

const roles = ref<Role[]>([])
const modules = ref<FunctionModuleDto[]>([])
/** moduleId → its functions (loaded lazily / cached). */
const functionsByModule = ref(new Map<string, ModuleFunctionDto[]>())

const selectedRoleId = ref<string | null>(null)
const checkedFunctionIds = ref<string[]>([])
/** Snapshot of the server-side assigned set, to compute dirty + reset. */
const originalAssignedIds = ref<Set<string>>(new Set())

const rolesLoading = ref(false)
const treeLoading = ref(false)
const saving = ref(false)
const roleFilter = ref('')
const matrixFilter = ref('')

// Delegation-aware graying: a non-super grantor may only hand out codes from
// their OWN permission set (mirrors the backend GetRoleGrantViolationAsync
// guard). null = everything grantable (super admin / permissions not loaded -
// fail-open like the sidebar; the backend guard is the real wall).
const authStore = useAdminAuthStore()
const grantableCodes = computed<string[] | null>(() =>
  authStore.isSuperUser || authStore.userInfo === null ? null : authStore.userPermissions,
)

const filteredRoles = computed(() => {
  if (!roleFilter.value) return roles.value
  const q = roleFilter.value.toLowerCase()
  return roles.value.filter(
    (r) => r.name.toLowerCase().includes(q) || r.code.toLowerCase().includes(q),
  )
})

const selectedRole = computed(() =>
  roles.value.find((r) => r.id === selectedRoleId.value) ?? null,
)

const totalFunctionCount = computed(() => {
  let n = 0
  for (const list of functionsByModule.value.values()) n += list.length
  return n
})

const isDirty = computed(() => {
  if (originalAssignedIds.value.size !== checkedFunctionIds.value.length) return true
  for (const id of checkedFunctionIds.value) {
    if (!originalAssignedIds.value.has(id)) return true
  }
  return false
})

// Unsaved diff, surfaced as a "+a / -b" chip next to the assigned count so
// the operator sees the pending change size before hitting Save.
const dirtyAdded = computed(
  () => checkedFunctionIds.value.filter((id) => !originalAssignedIds.value.has(id)).length,
)
const dirtyRemoved = computed(() => {
  const checked = new Set(checkedFunctionIds.value)
  let n = 0
  for (const id of originalAssignedIds.value) {
    if (!checked.has(id)) n += 1
  }
  return n
})

// Localized matrix labels: zh users see the sidebar's own wording for every
// framework surface; other locales (and unmapped consumer codes) fall back
// to the backend display names.
const appStore = useAdminAppStore()
const labelOverrides = computed(() => (appStore.locale === 'zh-cn' ? ZH_SURFACE_LABELS : null))

const matrixRef = ref<InstanceType<typeof TPermissionMatrix> | null>(null)

// Mobile (<md): the master rail is hidden; roles are picked from a bottom
// sheet and pending edits surface in a sticky save bar.
const { isSm } = useBreakpoint()
const rolePickerOpen = ref(false)

async function onPickRole(roleId: string): Promise<void> {
  rolePickerOpen.value = false
  await selectRole(roleId)
}

// ── Super-admin roles (read-only in this page) ─────────────────────────────
// Members of `Authorization:SuperAdminRoles` bypass every permission check,
// so explicit RoleFunction rows are meaningless for them: editing here would
// be a trap ("clear all" would not de-privilege anyone). The page renders
// those roles as an explainer instead of the matrix. Best-effort: when the
// endpoint is unavailable (older backend) the set stays empty and the page
// behaves as before.
const superRoleNames = ref<Set<string>>(new Set())

async function loadSuperRoles(): Promise<void> {
  try {
    const names = await authBridge.roleFunctions.superAdminRoles()
    superRoleNames.value = new Set(names.map((n) => n.toLowerCase()))
  } catch {
    superRoleNames.value = new Set()
  }
}

function isSuperRole(role: Role): boolean {
  return superRoleNames.value.has(role.name.toLowerCase())
}

const selectedIsSuper = computed(() =>
  selectedRole.value !== null && isSuperRole(selectedRole.value),
)

// Stale explicit rows on a super role have zero effect - offer a one-click
// cleanup so the dead data doesn't linger and confuse audits.
async function onCleanupSuperRows(): Promise<void> {
  const ok = await confirmWarning(t('super.cleanup'), t('super.cleanupConfirm'))
  if (ok) await handleClear()
}

// Decorative status dot in the role lists: selected = primary, super /
// configured = warning, untouched = grey.
function roleDotClass(roleId: string): string {
  if (roleId === selectedRoleId.value) return 'is-active'
  const role = roles.value.find((r) => r.id === roleId)
  if (role && isSuperRole(role)) return 'is-configured'
  return (assignedCountByRole.value.get(roleId) ?? 0) > 0 ? 'is-configured' : ''
}

// Single expand/collapse toggle (mirrors the design's "Collapse all" control).
// Tracks the last bulk action; individually toggled sections don't need to
// flip the label - the button always applies its stated action to all.
const allExpanded = ref(true)

function toggleExpandAll(): void {
  if (allExpanded.value) matrixRef.value?.collapseAll()
  else matrixRef.value?.expandAll()
  allExpanded.value = !allExpanded.value
}

// Clear-all is destructive: confirm through the framework dialog before running.
async function onClearAll(): Promise<void> {
  const ok = await confirmWarning(t('clearAll'), t('confirmClear'))
  if (ok) await handleClear()
}

// ── Left-rail role stats ───────────────────────────────────────────────────
// Per-role assigned counts give the rail an at-a-glance distribution overview
// (which roles are configured at all). Loaded best-effort in parallel after
// the role list; capped so an unusually large role catalogue doesn't fan out
// hundreds of requests - uncapped roles simply show no count.
const assignedCountByRole = ref(new Map<string, number>())

function roleAssignedCount(roleId: string): number | null {
  return assignedCountByRole.value.get(roleId) ?? null
}

async function loadRoleCounts(): Promise<void> {
  const targets = roles.value.slice(0, 100)
  const entries = await Promise.all(
    targets.map(async (r) => {
      try {
        const ids = await authBridge.roleFunctions.getAssignedIds(r.id)
        return [r.id, ids.length] as const
      } catch {
        return null
      }
    }),
  )
  const next = new Map<string, number>()
  for (const e of entries) {
    if (e) next.set(e[0], e[1])
  }
  assignedCountByRole.value = next
}

/** Distinct surface (code prefix) count for the rail's catalogue summary. */
const ACTION_SUFFIXES = new Set(['view', 'create', 'update', 'delete', 'execute', 'assign'])
const surfaceCount = computed(() => {
  const prefixes = new Set<string>()
  for (const list of functionsByModule.value.values()) {
    for (const fn of list) {
      const idx = fn.code.lastIndexOf('.')
      const seg = idx >= 0 ? fn.code.slice(idx + 1).toLowerCase() : ''
      prefixes.add(idx >= 0 && ACTION_SUFFIXES.has(seg) ? fn.code.slice(0, idx) : fn.code)
    }
  }
  return prefixes.size
})

async function loadAll(): Promise<void> {
  await Promise.all([loadRoles(), loadModulesAndFunctions()])
  if (selectedRoleId.value) {
    // If a role was already selected, re-pull its assignments so the tree
    // reflects any external changes.
    await loadAssignedForRole(selectedRoleId.value)
  } else if (roles.value.length > 0) {
    // Auto-select the first role so the matrix is populated on open - the right
    // pane should never sit blank waiting for a click.
    await selectRole(roles.value[0]!.id)
  }
  // Rail counts + super-role markers are decorative - never block the page.
  void loadRoleCounts().catch(() => undefined)
  void loadSuperRoles().catch(() => undefined)
}

async function loadRoles(): Promise<void> {
  rolesLoading.value = true
  try {
    const result = await idBridge.roles.fetch({
      pageIndex: 1,
      pageSize: 500,
      sortField: 'name',
      sortOrder: 'asc' as const,
      searchText: '',
      filters: {},
    })
    roles.value = result.items
      .filter((r): r is RoleDto & { id: string; name: string } => !!(r.id && r.name))
      .map((r) => ({
        id: r.id,
        // RoleDto exposes normalizedName (uppercased system identifier) rather
        // than a separate `code` - use that as the secondary line under the
        // display name; fall back to the name itself if empty.
        code: r.normalizedName ?? r.name,
        name: r.name,
        description: r.description ?? null,
      }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    roles.value = []
  } finally {
    rolesLoading.value = false
  }
}

async function loadModulesAndFunctions(): Promise<void> {
  treeLoading.value = true
  try {
    modules.value = await authBridge.functionModules.getAll()
    // Load functions for each module in parallel; bounded fan-out keeps it
    // friendly for typical module counts (<50).
    const next = new Map<string, ModuleFunctionDto[]>()
    await Promise.all(
      modules.value.map(async (m) => {
        try {
          next.set(m.id, await authBridge.permissions.getByModule(m.id))
        } catch {
          next.set(m.id, [])
        }
      }),
    )
    functionsByModule.value = next
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    modules.value = []
  } finally {
    treeLoading.value = false
  }
}

async function loadAssignedForRole(roleId: string): Promise<void> {
  treeLoading.value = true
  try {
    const ids = await authBridge.roleFunctions.getAssignedIds(roleId)
    originalAssignedIds.value = new Set(ids)
    checkedFunctionIds.value = [...ids]
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    originalAssignedIds.value = new Set()
    checkedFunctionIds.value = []
  } finally {
    treeLoading.value = false
  }
}

async function selectRole(roleId: string): Promise<void> {
  if (isDirty.value) {
    // Guard against losing unsaved edits when switching roles - a framework
    // warning dialog replaces the native confirm().
    const ok = await confirmSwitchDirty()
    if (!ok) return
  }
  selectedRoleId.value = roleId
  await loadAssignedForRole(roleId)
}

function onCheckedChange(ids: string[]): void {
  // The matrix emits raw function IDs only - no synthetic module keys.
  checkedFunctionIds.value = ids
}

function reset(): void {
  checkedFunctionIds.value = [...originalAssignedIds.value]
}

async function handleSave(): Promise<void> {
  if (!selectedRoleId.value) return
  saving.value = true
  try {
    await authBridge.roleFunctions.setForRole(selectedRoleId.value, checkedFunctionIds.value)
    originalAssignedIds.value = new Set(checkedFunctionIds.value)
    const counts = new Map(assignedCountByRole.value)
    counts.set(selectedRoleId.value, checkedFunctionIds.value.length)
    assignedCountByRole.value = counts
    message.success(t('saveSuccess'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

async function handleClear(): Promise<void> {
  if (!selectedRoleId.value) return
  saving.value = true
  try {
    await authBridge.roleFunctions.clearForRole(selectedRoleId.value)
    originalAssignedIds.value = new Set()
    checkedFunctionIds.value = []
    const counts = new Map(assignedCountByRole.value)
    counts.set(selectedRoleId.value, 0)
    assignedCountByRole.value = counts
    message.success(t('clearSuccess'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    saving.value = false
  }
}

// ─── Compare / Clone roles ────────────────────────────────────────────────
// Both modals share the role catalogue (the same `roles.value` that powers
// the left rail). We expose a "other roles" computed so the source/target
// dropdowns can't accidentally pick the currently-selected role.

// Super roles are excluded from compare/clone pickers: their explicit row
// set (usually empty) is not their effective access, so comparing against
// or copying from them is misleading.
const otherRoleOptions = computed<SelectOption[]>(() =>
  roles.value
    .filter((r) => r.id !== selectedRoleId.value && !isSuperRole(r))
    .map((r) => ({ value: r.id, label: r.name })),
)

/** Clone-source list entries (list-style picker instead of a dropdown). */
const cloneSourceRoles = computed<Role[]>(() =>
  roles.value.filter((r) => r.id !== selectedRoleId.value && !isSuperRole(r)),
)

// Compare / Clone overlays run through the single useDetail + TDetailHost
// renderer (modal mode). They are role-level operations (not per-record), so
// each claims a static URL scope - `?compare=new` / `?clone=new` - which makes
// opening deep-linkable and Back-closeable. The transient picker / result
// state stays local to the page.
const compareDetail = useDetail({ mode: 'modal', url: 'compare' })
const compare = reactive({
  loading: false,
  targetRoleId: null as string | null,
  result: null as PermissionComparisonDto | null,
})

const compareBuckets = computed(() => {
  const r = compare.result
  if (!r) return []
  const roleAName = selectedRole.value?.name ?? EMPTY_DASH
  const roleBName = roles.value.find((x) => x.id === compare.targetRoleId)?.name ?? EMPTY_DASH
  return [
    {
      id: 'onlyIn1',
      title: t('compare.onlyInA', { role: roleAName }),
      tone: 'info' as const,
      rows: r.onlyInRole1 ?? [],
    },
    {
      id: 'onlyIn2',
      title: t('compare.onlyInB', { role: roleBName }),
      tone: 'warning' as const,
      rows: r.onlyInRole2 ?? [],
    },
    {
      id: 'shared',
      title: t('compare.common'),
      tone: 'success' as const,
      rows: r.shared ?? [],
    },
  ] as Array<{
    id: string
    title: string
    tone: 'info' | 'warning' | 'success'
    rows: FunctionSummaryDto[]
  }>
})

function openCompareModal(): void {
  compare.targetRoleId = null
  compare.result = null
  void compareDetail.open('create')
}

async function runCompare(): Promise<void> {
  const target = compare.targetRoleId
  if (!target || !selectedRoleId.value) return
  compare.loading = true
  try {
    compare.result = await authBridge.roleFunctions.compare(
      selectedRoleId.value,
      target,
    )
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    compare.result = null
  } finally {
    compare.loading = false
  }
}

const cloneDetail = useDetail({ mode: 'modal', url: 'clone' })
const clone = reactive({
  saving: false,
  sourceRoleId: null as string | null,
})

function openCloneModal(): void {
  clone.sourceRoleId = null
  void cloneDetail.open('create')
}

async function runClone(): Promise<void> {
  const source = clone.sourceRoleId
  if (!source || !selectedRoleId.value) return
  if (source === selectedRoleId.value) {
    message.warning(t('clone.sameRoleError'))
    return
  }
  clone.saving = true
  try {
    const count = await authBridge.roleFunctions.clone(selectedRoleId.value, source)
    message.success(t('clone.success', { n: count }))
    cloneDetail.close()
    // Refetch assignments so the tree reflects the merged permissions.
    await loadAssignedForRole(selectedRoleId.value)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    clone.saving = false
  }
}

// Pre-select role from `?roleId=` query - used by Roles detail
// drawer's "Open Permission Editor" deep-link. Wait for the role list to
// load before selecting so the active highlight + tree render in one paint.
const route = useRoute()

onMounted(async () => {
  await loadAll()
  // Optional-chain `route`: useRoute() is undefined when the component is
  // mounted without a router (SSR / bare test mount), and reading `.query`
  // off undefined inside this async hook would surface as an unhandled
  // rejection. The deep-link pre-select is simply skipped when there's no route.
  const queryRoleId = typeof route?.query?.roleId === 'string' ? route.query.roleId : null
  if (queryRoleId && roles.value.some((r) => r.id === queryRoleId)) {
    selectedRoleId.value = queryRoleId
    await loadAssignedForRole(queryRoleId)
  }
})
</script>

<style scoped>
/* Fill-height chain (content-page iron rule): TContentPage scroll="fill"
   → card flex-fills the body → n-card__content flex column → layout grid
   claims the residual height → each pane scrolls internally. The white
   container always reaches the viewport bottom; no dead grey canvas. */
.t-role-func-page__card {
  flex: 1 1 auto;
  min-height: 0;
}
.t-role-func-page__card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
/* Master/detail grid, responsive stacking and pane scroll come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
.t-role-func-page__rail {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}
.t-role-func-page__rail-scroll {
  flex: 1;
  min-height: 0;
  overflow: auto;
}
.t-role-func-page__role-list {
  list-style: none;
  padding: 0;
  margin: 8px 0 0;
}
.t-role-func-page__rail-caption {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
  margin-bottom: 8px;
}
.t-role-func-page__role-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 10px;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  margin-bottom: 4px;
  transition: background-color 0.15s;
}
.t-role-func-page__role-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--tnzi-border);
  margin-top: 6px;
  flex-shrink: 0;
}
.t-role-func-page__role-dot.is-configured {
  background: var(--tnzi-warning);
}
.t-role-func-page__role-dot.is-active {
  background: var(--tnzi-primary);
}
.t-role-func-page__role-body {
  flex: 1;
  min-width: 0;
}
.t-role-func-page__role-item:hover {
  background: var(--tnzi-layout-bg);
}
.t-role-func-page__role-item.is-active {
  background: rgb(var(--tnzi-primary-rgb) / 0.12);
  color: var(--tnzi-primary);
}
.t-role-func-page__role-line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.t-role-func-page__role-name {
  font-weight: 500;
  font-size: 14px;
}
.t-role-func-page__role-count {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.t-role-func-page__role-count.is-super {
  color: var(--tnzi-warning);
  font-weight: 600;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.t-role-func-page__role-item.is-active .t-role-func-page__role-count {
  color: var(--tnzi-primary);
  font-weight: 600;
}
.t-role-func-page__role-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__role-item.is-active .t-role-func-page__role-code {
  color: var(--tnzi-primary);
  opacity: 0.7;
}
.t-role-func-page__rail-stats {
  margin-top: 10px;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border);
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__tree {
  padding: 0 4px;
}
.t-role-func-page__placeholder {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 60px 16px;
}
.t-role-func-page__tree-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  gap: 16px;
  flex-wrap: wrap;
}
.t-role-func-page__role-summary {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.t-role-func-page__role-title-line {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-role-func-page__selected-name {
  margin: 0;
  font-size: 18px;
}
.t-role-func-page__role-progress-line {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.t-role-func-page__assigned-text {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__assigned-text b {
  color: var(--tnzi-base-text);
  font-variant-numeric: tabular-nums;
}
.t-role-func-page__bar {
  width: 150px;
  height: 6px;
  border-radius: 3px;
  /* Explicit track grey - layout-bg was invisible on the white card. */
  background: var(--tnzi-border);
  overflow: hidden;
}
.t-role-func-page__bar-fill {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--tnzi-primary);
  transition: width 0.2s ease;
}
.t-role-func-page__bar-fill.is-super {
  background: var(--tnzi-warning);
}
/* Super-admin banner - sits ABOVE the read-only matrix (a bypass role holds
   everything, so the matrix shows all-granted + locked rather than editable). */
.t-role-func-page__super-banner {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 12px;
  padding: 8px 12px;
  margin-bottom: 8px;
  border: 1px solid var(--tnzi-warning);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  background: color-mix(in srgb, var(--tnzi-warning) 8%, transparent);
}
.t-role-func-page__super-banner-icon {
  color: var(--tnzi-warning);
  flex-shrink: 0;
}
.t-role-func-page__super-banner-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-role-func-page__super-banner-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-role-func-page__super-banner-line {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  line-height: 1.4;
}
.t-role-func-page__super-banner-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11.5px;
  background: var(--tnzi-layout-bg);
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
}
.t-role-func-page__super-stale {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
  font-size: 12.5px;
  color: var(--tnzi-warning);
}
/* Clone-source list picker (modal). */
.t-role-func-page__clone-label {
  font-size: 13px;
  font-weight: 500;
  margin-bottom: 6px;
}
.t-role-func-page__role-list--picker {
  margin: 0 0 10px;
  max-height: 300px;
  overflow: auto;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  padding: 4px;
}
.t-role-func-page__role-list--picker .t-role-func-page__role-item {
  align-items: center;
}
.t-role-func-page__clone-check {
  color: var(--tnzi-primary);
  flex-shrink: 0;
}
.t-role-func-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.t-role-func-page__toolbar-btns {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-role-func-page__toolbar-divider {
  width: 1px;
  height: 18px;
  background: var(--tnzi-border);
  flex-shrink: 0;
}
.t-role-func-page__matrix-search {
  max-width: 300px;
  margin-left: auto;
}
/* Legend now lives inside a toolbar hover popover (it used to eat two full
   rows above the matrix). The item / swatch rules below are shared. */
.t-role-func-page__legend-pop {
  max-width: 320px;
}
.t-role-func-page__legend-pop-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px 16px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__legend-item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  white-space: nowrap;
}
/* Sample-style legend swatches mirroring the real matrix cell states. */
.t-role-func-page__swatch {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
  border-radius: 3px;
  border: 1px solid var(--tnzi-border);
  background: var(--tnzi-container-bg);
  color: #fff;
  flex-shrink: 0;
  font-size: 10px;
  font-weight: 600;
}
.t-role-func-page__swatch.is-on {
  background: var(--tnzi-primary);
  border-color: var(--tnzi-primary);
}
.t-role-func-page__swatch.is-hatched {
  background: repeating-linear-gradient(
    45deg,
    transparent,
    transparent 3px,
    var(--tnzi-border) 3px,
    var(--tnzi-border) 4px
  );
}
.t-role-func-page__swatch.is-menu {
  background: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.14);
  border-color: rgb(var(--tnzi-info-rgb, 32 128 240) / 0.4);
}
.t-role-func-page__swatch.is-tech {
  background: rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.16);
  border-color: rgb(var(--tnzi-warning-rgb, 240 160 32) / 0.5);
  color: var(--tnzi-warning);
}
.t-role-func-page__swatch-dot {
  font-size: 15px;
  line-height: 1;
  opacity: 0.6;
}
.t-role-func-page__legend-note {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border);
  line-height: 1.5;
}
.t-role-func-page__legend-term {
  color: var(--tnzi-primary);
  font-weight: 600;
}
.t-role-func-page__savebar {
  position: sticky;
  bottom: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  margin-top: 8px;
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg));
  border-top: 1px solid var(--tnzi-border);
}
.t-role-func-page__savebar > .n-tag {
  margin-right: auto;
}
.t-role-func-page__role-list--sheet {
  max-height: none;
}
/* Desktop: the matrix owns a 64vh internal scroll region (sticky header stays,
   rows scroll). Declared BEFORE the mobile media query so the phone override
   (equal specificity) wins the cascade and lifts the cap. */
.t-role-func-page__matrix {
  max-height: 64vh;
}

/* Mobile (<768px): hide the master rail entirely - roles are picked from the
   header's bottom-sheet selector; the toolbar chips scroll horizontally and
   the search drops to its own full-width row. */
@media (max-width: 767px) {
  .t-role-func-page__card :deep(.t-master-detail__master) {
    display: none;
  }
  .t-role-func-page__toolbar {
    flex-direction: column;
    align-items: stretch;
  }
  .t-role-func-page__toolbar-btns {
    overflow-x: auto;
    flex-wrap: nowrap;
    padding-bottom: 4px;
  }
  .t-role-func-page__toolbar-btns .n-button {
    flex-shrink: 0;
  }
  .t-role-func-page__matrix-search {
    max-width: none;
    margin-left: 0;
  }
  /* Phones scroll the whole page (the content-page fill region), so drop the
     desktop 64vh scroll cap - a capped flex-column list otherwise crushes the
     collapsed module cards to slivers. */
  .t-role-func-page__matrix {
    max-height: none;
  }
}
.t-role-func-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 13px;
}

/* Compare modal - three columns side by side on desktop, single column
   on phones. Each column lists the functions in that bucket as compact
   tiles (name + monospaced code + optional module hint). */
.t-role-func-page__compare-picker {
  display: flex;
  gap: 24px;
  align-items: center;
  flex-wrap: wrap;
  padding-bottom: 12px;
  margin-bottom: 12px;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-role-func-page__compare-pair {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-role-func-page__compare-label {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-role-func-page__compare-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}
@media (max-width: 900px) {
  .t-role-func-page__compare-grid { grid-template-columns: 1fr; }
}
.t-role-func-page__compare-bucket {
  background: var(--tnzi-layout-bg);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  padding: 12px;
  max-height: 60vh;
  overflow-y: auto;
}
.t-role-func-page__compare-bucket-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}
.t-role-func-page__compare-bucket-title {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-role-func-page__compare-empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 24px 8px;
  font-size: 12px;
}
.t-role-func-page__compare-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-role-func-page__compare-list > li {
  padding: 8px 10px;
  background: var(--tnzi-container-bg);
  border-radius: 4px;
  border: 1px solid var(--tnzi-border);
}
.t-role-func-page__compare-fn-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-role-func-page__compare-fn-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  display: block;
  margin-top: 2px;
}
.t-role-func-page__compare-fn-module {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  margin-top: 2px;
}
.t-role-func-page__hint {
  margin: 0 0 12px;
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
  line-height: 1.5;
}
</style>
