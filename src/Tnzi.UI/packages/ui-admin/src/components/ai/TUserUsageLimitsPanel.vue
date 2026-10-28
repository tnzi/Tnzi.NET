<template>
  <div class="t-uul">
    <NSpin :show="loading">
      <TEmpty v-if="!loading && !data" :text="t('loadFailed')" size="small" />
      <div v-else-if="data && draft" class="t-uul__blocks">
        <!-- What the user has used, per window. -->
        <TDetailBlock icon="mdi:gauge" :title="t('user.usageTitle')" :hint="t('user.usageHint')">
          <template v-if="canManage" #actions>
            <NPopconfirm @positive-click="resetWindows">
              <template #trigger>
                <NButton size="small" tertiary type="warning" :loading="busy === 'reset'">
                  <template #icon><TSvgIcon icon="mdi:restore" :size="15" /></template>
                  {{ t('user.resetWindows') }}
                </NButton>
              </template>
              {{ t('user.confirmResetWindows') }}
            </NPopconfirm>
          </template>

          <div class="t-uul__windows">
            <div v-for="w in data.usage.windows" :key="w.window" class="t-uul__window">
              <div class="t-uul__window-head">
                <span class="t-uul__window-name">
                  {{ w.window === UsageLimitWindow.Weekly ? t('window.weekly') : t('window.session') }}
                  <span class="t-uul__window-length">{{ formatWindowLength(w.durationMinutes, t) }}</span>
                </span>
                <NTag v-if="w.exceeded" size="small" round :bordered="false" type="error">{{ t('user.exceeded') }}</NTag>
                <NTag v-else-if="w.warningReached" size="small" round :bordered="false" type="warning">{{ t('user.warning') }}</NTag>
              </div>
              <NProgress
                v-if="w.limitUsd != null"
                type="line"
                :percentage="barPercent(w.usedPercent)"
                :status="w.exceeded ? 'error' : w.warningReached ? 'warning' : 'success'"
                :show-indicator="false"
                :height="8"
              />
              <div class="t-uul__window-line">
                <span>
                  {{
                    w.limitUsd != null
                      ? t('user.usedOf', { used: formatUsd(w.usedUsd), limit: formatUsd(w.limitUsd), percent: Math.round(w.usedPercent ?? 0) })
                      : t('user.usedOnly', { used: formatUsd(w.usedUsd) })
                  }}
                </span>
                <span>{{ formatReset(w.resetsAt, t) ?? t('user.notStarted') }}</span>
              </div>
            </div>
          </div>
          <p class="t-uul__last-used">
            {{ data.usage.lastUsedAt ? t('user.lastUsed', { time: formatDateTime(data.usage.lastUsedAt) }) : t('user.neverUsed') }}
          </p>
        </TDetailBlock>

        <!-- The user's own settings over everyone's defaults. -->
        <TDetailBlock icon="mdi:account-cog-outline" :title="t('user.limitsTitle')" :hint="t('user.limitsHint')">
          <template #titleExtra>
            <NTag size="small" round :bordered="false" :type="data.hasCustomPolicy ? 'primary' : 'default'">
              {{ data.hasCustomPolicy ? t('user.custom') : t('user.followsDefaults') }}
            </NTag>
          </template>
          <template v-if="canManage && data.hasCustomPolicy" #actions>
            <NPopconfirm @positive-click="useDefaults">
              <template #trigger>
                <NButton size="small" tertiary :loading="busy === 'clear'">
                  <template #icon><TSvgIcon icon="mdi:backup-restore" :size="15" /></template>
                  {{ t('user.useDefaults') }}
                </NButton>
              </template>
              {{ t('user.confirmUseDefaults') }}
            </NPopconfirm>
          </template>

          <div class="t-uul__form">
            <TUsageLimitPolicyForm
              v-model="draft"
              :inherited="inherited"
              :catalog="data.catalog"
              :inherit-label="t('mode.inheritDefaults')"
              :session-window-minutes="windowMinutes(UsageLimitWindow.Session)"
              :weekly-window-minutes="windowMinutes(UsageLimitWindow.Weekly)"
              :readonly="!canManage"
            />

            <p class="t-uul__effective">
              <span class="t-uul__effective-label">{{ t('policy.effective') }}</span>
              {{ effectiveText }}
            </p>

            <div v-if="canManage" class="t-uul__actions">
              <span v-if="problem" class="t-uul__problem">{{ t(problem) }}</span>
              <NButton :disabled="!dirty || busy === 'save'" @click="discard">{{ t('policy.discard') }}</NButton>
              <NButton type="primary" :loading="busy === 'save'" :disabled="!dirty || !!problem" @click="save">
                {{ t('policy.save') }}
              </NButton>
            </div>
          </div>
        </TDetailBlock>
      </div>
    </NSpin>
  </div>
</template>

<script setup lang="ts">
/**
 * `TUserUsageLimitsPanel` - one user's usage limits: how much of each window
 * (5-hour session, weekly) they have used and when it resets, and their own
 * limits and models over everyone's defaults.
 *
 * Each item can follow the defaults ("Follow defaults", the default for a user
 * with no settings), be unlimited, or carry an amount; the form shows what
 * "follow" resolves to. "Use defaults for everything" removes the user's row;
 * "Reset usage" clears their current windows. Reads ride `ai.quota.view`,
 * writes `ai.quota.update`, which the panel checks itself.
 *
 * Hostable with nothing but a `userId` (a user detail page, say): strings come
 * from the `ai.quota` namespace of the admin locale bundle.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NPopconfirm, NProgress, NSpin, NTag } from 'naive-ui'
import { TEmpty, TSvgIcon, useSafeMessage } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core/utils'
import { UsageLimitWindow, type SaveUsagePolicyDto, type UserUsageLimitsDto } from '@tnzi/core/services/ai'
import TDetailBlock from '../detail/TDetailBlock.vue'
import TUsageLimitPolicyForm, { type InheritedUsagePolicy } from './TUsageLimitPolicyForm.vue'
import { createUsageLimitBridge, type UsageLimitBridge } from '../../services/bridges/usage-limit-bridge'
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { interpolate, translatePageKey } from '../../i18n/translate'
import {
  barPercent,
  formatReset,
  formatUsd,
  formatWindowLength,
  policyProblem,
  samePolicy,
  toDraft,
  toPayload,
} from './usage-limit-format'

const props = defineProps<{
  /** The user whose limits are shown. */
  userId: string
  /** A bridge to reuse (the host's own); one is created from the admin client otherwise. */
  bridge?: UsageLimitBridge
}>()

const emit = defineEmits<{
  /** After the user's settings or windows changed, with the fresh state. */
  updated: [limits: UserUsageLimitsDto]
}>()

defineOptions({ name: 'TUserUsageLimitsPanel' })

const t = (key: string, params?: Record<string, unknown>): string => interpolate(translatePageKey('ai.quota', key), params)
const message = useSafeMessage()
const { can } = usePermissionGuard()
const canManage = computed(() => can('ai.quota.update'))

const bridge = props.bridge ?? createUsageLimitBridge({ client: useAdminClient() })

const loading = ref(true)
const busy = ref<'save' | 'clear' | 'reset' | null>(null)
const data = ref<UserUsageLimitsDto | null>(null)
const draft = ref<SaveUsagePolicyDto | null>(null)

/** Only the latest load lands: with `userId` switched mid-flight, A's settings must not fill B's form. */
let loadSeq = 0

async function load(): Promise<void> {
  const seq = ++loadSeq
  loading.value = true
  try {
    const next = await bridge.getUser(props.userId)
    if (seq !== loadSeq) return
    apply(next)
  } catch (err) {
    if (seq !== loadSeq) return
    data.value = null
    message.error(t('loadFailedWith', { message: err instanceof Error ? err.message : String(err) }))
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}
void load()
watch(
  () => props.userId,
  () => {
    data.value = null
    draft.value = null
    void load()
  },
)

function apply(next: UserUsageLimitsDto): void {
  data.value = next
  draft.value = toDraft(next.policy)
}

const inherited = computed<InheritedUsagePolicy>(() => {
  const d = data.value?.defaults
  return {
    sessionLimitUsd: d?.sessionLimitUsd ?? null,
    weeklyLimitUsd: d?.weeklyLimitUsd ?? null,
    allowedModels: d?.allowedModels.map((m) => m.id) ?? [],
    defaultModel: d?.defaultModel ?? null,
  }
})

function windowMinutes(window: UsageLimitWindow): number | undefined {
  return data.value?.usage.windows.find((w) => w.window === window)?.durationMinutes
}

const dirty = computed(() => !!data.value && !!draft.value && !samePolicy(draft.value, toDraft(data.value.policy)))

const problem = computed(() => {
  if (!draft.value) return null
  return policyProblem(draft.value, draft.value.allowedModels ?? inherited.value.allowedModels)
})

const effectiveText = computed(() => {
  const e = data.value?.effective
  if (!e) return ''
  return t('policy.effectiveSummary', {
    session: e.sessionLimitUsd == null ? t('mode.unlimited') : formatUsd(e.sessionLimitUsd),
    weekly: e.weeklyLimitUsd == null ? t('mode.unlimited') : formatUsd(e.weeklyLimitUsd),
    models: e.allowedModels.length === 0
      ? t('policy.modelsNone')
      : e.allowedModels.map((m) => m.displayName || m.id).join(', '),
  })
})

function discard(): void {
  if (data.value) draft.value = toDraft(data.value.policy)
}

async function save(): Promise<void> {
  if (!draft.value || problem.value) return
  busy.value = 'save'
  try {
    const next = await bridge.saveUser(props.userId, toPayload(draft.value))
    apply(next)
    message.success(t('policy.saved'))
    emit('updated', next)
  } catch (err) {
    message.error(t('policy.saveFailed', { message: err instanceof Error ? err.message : String(err) }))
  } finally {
    busy.value = null
  }
}

async function useDefaults(): Promise<void> {
  busy.value = 'clear'
  try {
    await bridge.clearUser(props.userId)
    message.success(t('user.cleared'))
    await reloadAndAnnounce()
  } catch (err) {
    message.error(t('policy.saveFailed', { message: err instanceof Error ? err.message : String(err) }))
  } finally {
    busy.value = null
  }
}

async function resetWindows(): Promise<void> {
  busy.value = 'reset'
  try {
    await bridge.resetUser(props.userId)
    message.success(t('user.resetDone'))
    await reloadAndAnnounce()
  } catch (err) {
    message.error(t('policy.saveFailed', { message: err instanceof Error ? err.message : String(err) }))
  } finally {
    busy.value = null
  }
}

async function reloadAndAnnounce(): Promise<void> {
  await load()
  if (data.value) emit('updated', data.value)
}

defineExpose({ reload: load })
</script>

<style scoped>
.t-uul__blocks {
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.t-uul__windows {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
}
.t-uul__window {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-uul__window-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-uul__window-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-uul__window-length {
  margin-left: 6px;
  font-weight: 400;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-uul__window-line {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  font-variant-numeric: tabular-nums;
}
.t-uul__last-used {
  margin: 12px 0 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-uul__form {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-uul__effective {
  margin: 0;
  padding: 8px 12px;
  border-radius: 8px;
  background: var(--tnzi-layout-bg);
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-uul__effective-label {
  margin-right: 6px;
  font-weight: 500;
}
.t-uul__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 8px 12px;
}
.t-uul__problem {
  margin-right: auto;
  font-size: 12px;
  color: var(--tnzi-error);
}
</style>
