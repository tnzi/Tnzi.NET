<template>
  <TUserCenterSection :title="t('notifications.title')">
    <template #actions>
      <NPopconfirm @positive-click="restoreDefaults">
        <template #trigger>
          <NButton size="small" tertiary :disabled="loading || !rows.length" :loading="resetting" data-test="notify-restore">
            {{ t('notifications.restoreDefaults') }}
          </NButton>
        </template>
        {{ t('notifications.confirmRestore') }}
      </NPopconfirm>
    </template>

    <NSpin :show="loading">
      <p class="t-uc-hint">{{ t('notifications.hint') }}</p>

      <NForm label-placement="top" :show-feedback="false">
        <!-- One zone new quiet hours are entered in: people think of "my
             nights", not of a separate clock per channel. Changing it converts
             every set window (same instants today) instead of relabelling it.
             A refusal of the zone by the server lands here, not in a toast that
             says "save failed" about a value the person never typed. -->
        <NFormItem
          :label="t('notifications.timeZone')"
          :show-feedback="!!zoneError"
          :validation-status="zoneError ? 'error' : undefined"
          :feedback="zoneError ?? undefined"
        >
          <NSelect
            :value="draft.timeZone"
            filterable
            :options="zoneOptions"
            data-test="notify-zone"
            @update:value="setSharedZone"
          />
        </NFormItem>
        <p class="t-uc-hint">
          {{ t('notifications.timeZoneHint') }}
          <NButton
            v-if="draft.timeZone !== browserZone"
            text
            size="tiny"
            type="primary"
            data-test="notify-use-browser-zone"
            @click="setSharedZone(browserZone)"
          >
            {{ t('notifications.useBrowserZone', { zone: browserZone }) }}
          </NButton>
        </p>

        <div class="t-uc-notify-channels">
          <TDetailBlock
            v-for="c in draft.channels"
            :key="c.channel"
            :icon="CHANNEL_ICONS[c.channel]"
            :title="channelLabel(c.channel)"
            :data-test="`notify-channel-${c.channel}`"
          >
            <template #actions>
              <NSwitch v-model:value="c.isEnabled" :data-test="`notify-enabled-${c.channel}`" />
            </template>
            <div class="t-uc-form-grid">
              <NFormItem :label="t('notifications.quietHours')">
                <div class="t-uc-notify-quiet-block">
                  <div class="t-uc-notify-quiet">
                    <NSwitch
                      :value="c.quietHours"
                      :disabled="!c.isEnabled"
                      :data-test="`notify-quiet-${c.channel}`"
                      @update:value="(on) => toggleQuietHours(c, on)"
                    />
                    <NSelect
                      v-model:value="c.quietHoursStart"
                      :options="quietHourOptions([c.quietHoursStart])"
                      size="small"
                      :disabled="!c.isEnabled || !c.quietHours"
                      :placeholder="t('notifications.startPlaceholder')"
                      :data-test="`notify-quiet-start-${c.channel}`"
                    />
                    <span class="t-uc-hint">{{ t('notifications.until') }}</span>
                    <NSelect
                      v-model:value="c.quietHoursEnd"
                      :options="quietHourOptions([c.quietHoursEnd])"
                      size="small"
                      :disabled="!c.isEnabled || !c.quietHours"
                      :placeholder="t('notifications.endPlaceholder')"
                      :data-test="`notify-quiet-end-${c.channel}`"
                    />
                  </div>
                  <!-- A window stored in another zone (set by an operator, or a
                       UTC row from before zones existed) is shown in that zone and
                       says so; saving another field of this channel keeps it. -->
                  <p
                    v-if="c.quietHours && c.timeZone !== draft.timeZone"
                    class="t-uc-hint t-uc-notify-own-zone"
                    :data-test="`notify-own-zone-${c.channel}`"
                  >
                    {{ t('notifications.ownZone', { zone: c.timeZone }) }}
                    <NButton
                      text
                      size="tiny"
                      type="primary"
                      :data-test="`notify-convert-${c.channel}`"
                      @click="moveToSharedZone(c)"
                    >
                      {{ t('notifications.convertToZone', { zone: draft.timeZone }) }}
                    </NButton>
                  </p>
                </div>
              </NFormItem>
              <NFormItem :label="t('notifications.maxPerHour')">
                <NInputNumber
                  v-model:value="c.maxFrequencyPerHour"
                  :min="1"
                  :precision="0"
                  clearable
                  size="small"
                  :disabled="!c.isEnabled"
                  :placeholder="t('notifications.noLimit')"
                />
              </NFormItem>
            </div>
          </TDetailBlock>
        </div>
      </NForm>
      <p class="t-uc-hint">{{ t('notifications.quietHoursHint') }}</p>

      <!-- Per-category rows (set by an operator or by the application's own
           screens) override the channel switch for one kind of message. Shown
           so a person can tell why, say, newsletters stopped while Email is on,
           and removable because they are the person's own rows. -->
      <TDetailBlock
        v-if="rules.length"
        icon="mdi:filter-variant"
        :title="t('notifications.rules.title')"
        :hint="t('notifications.rules.hint')"
      >
        <div v-for="r in rules" :key="r.id" class="t-uc-row" :data-test="`notify-rule-${r.id}`">
          <span class="t-uc-row-label">{{ channelLabel(r.channel) }} · {{ r.category }}</span>
          <div class="t-uc-actions">
            <NTag size="small" :bordered="false" :type="r.isEnabled ? 'success' : 'warning'">
              {{ r.isEnabled ? t('notifications.rules.on') : t('notifications.rules.off') }}
            </NTag>
            <NPopconfirm @positive-click="removeRule(r.id)">
              <template #trigger>
                <NButton size="tiny" type="warning" ghost :loading="removingId === r.id">
                  {{ t('notifications.rules.remove') }}
                </NButton>
              </template>
              {{ t('notifications.rules.confirmRemove') }}
            </NPopconfirm>
          </div>
        </div>
      </TDetailBlock>

      <div class="t-uc-save-bar">
        <NButton size="small" :disabled="!dirty || saving" data-test="notify-revert" @click="revert">
          {{ t('reset') }}
        </NButton>
        <NButton
          size="small"
          type="primary"
          :disabled="!dirty"
          :loading="saving"
          data-test="notify-save"
          @click="save"
        >
          {{ t('save') }}
        </NButton>
      </div>
    </NSpin>
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { NButton, NForm, NFormItem, NInputNumber, NPopconfirm, NSelect, NSpin, NSwitch, NTag } from 'naive-ui'
import { isInvalidTimeZoneError, type NotificationPreferenceDto } from '@tnzi/core/services/notification'
import TUserCenterSection from './TUserCenterSection.vue'
import TDetailBlock from '../../../components/detail/TDetailBlock.vue'
import { createNotificationBridge } from '../../../services/bridges/notification-bridge'
import { useAdminClient } from '../../../plugin/client'
import { browserTimeZone, timeZoneIds } from '../../../utils/time-zones'
import { createGuardedLoader } from '../guarded-loader'
import { useUserCenterContext } from '../user-center-context'
import {
  DEFAULT_QUIET_END,
  DEFAULT_QUIET_START,
  buildDraft,
  categoryRules,
  changeSharedZone,
  changedChannels,
  cloneDraft,
  findProblems,
  moveChannelToZone,
  pendingUpserts,
  quietHourOptions,
  type ChannelDraft,
  type PreferencesDraft,
  type SelfServiceChannel,
} from '../notification-preferences'

const ctx = useUserCenterContext()
const t = ctx.t
const bridge = createNotificationBridge({ client: useAdminClient() })

const CHANNEL_ICONS: Record<SelfServiceChannel, string> = {
  Email: 'mdi:email-outline',
  Sms: 'mdi:message-text-outline',
  Push: 'mdi:cellphone-message',
  Fax: 'mdi:fax',
}
const CHANNEL_KEYS: Record<string, string> = { email: 'email', sms: 'sms', push: 'push', fax: 'fax' }

/** Channel label for a section row or a rule row (rules may name any channel spelling). */
function channelLabel(channel: string): string {
  const key = CHANNEL_KEYS[channel.toLowerCase()]
  return key ? t(`notifications.channels.${key}`) : channel
}

const browserZone = browserTimeZone()
const rows = ref<NotificationPreferenceDto[]>([])
const initial = ref<PreferencesDraft>(buildDraft([], browserZone))
const draft = ref<PreferencesDraft>(cloneDraft(initial.value))
const loading = ref(false)
const saving = ref(false)
const resetting = ref(false)
const removingId = ref<string | null>(null)
/** The server's refusal of a zone, shown on the zone field. */
const zoneError = ref<string | null>(null)

const rules = computed(() => categoryRules(rows.value))
const zoneOptions = computed(() =>
  timeZoneIds([draft.value.timeZone, initial.value.timeZone, browserZone]).map((id) => ({ label: id, value: id })),
)
const upserts = computed(() => pendingUpserts(initial.value, draft.value))
const dirty = computed(() => upserts.value.length > 0)

function apply(list: NotificationPreferenceDto[]): void {
  rows.value = list
  initial.value = buildDraft(list, browserZone)
  draft.value = cloneDraft(initial.value)
  zoneError.value = null
}

/** A new shared zone converts every set window to it (same instants today). */
function setSharedZone(zone: string): void {
  draft.value = changeSharedZone(draft.value, zone)
  zoneError.value = null
}

/** Move one channel kept in its own zone over to the shared one, converting its window. */
function moveToSharedZone(c: ChannelDraft): void {
  const moved = moveChannelToZone(c, draft.value.timeZone)
  draft.value = {
    ...draft.value,
    channels: draft.value.channels.map((x) => (x.channel === c.channel ? moved : x)),
  }
}

const load = createGuardedLoader<NotificationPreferenceDto[]>({
  flag: loading,
  fetch: () => bridge.myPreferences.list(),
  apply,
  onError: (e) => ctx.message.error(e instanceof Error ? e.message : String(e)),
  timeoutMessage: t('loadTimeout'),
})

/**
 * Switching quiet hours on starts from a typical night in the shared zone
 * rather than two empty selects. Times kept from before (switched off without
 * saving) come back in the zone they were in.
 */
function toggleQuietHours(c: ChannelDraft, on: boolean): void {
  c.quietHours = on
  if (on && (!c.quietHoursStart || !c.quietHoursEnd)) {
    c.quietHoursStart ??= DEFAULT_QUIET_START
    c.quietHoursEnd ??= DEFAULT_QUIET_END
    c.timeZone = draft.value.timeZone
  }
}

function revert(): void {
  draft.value = cloneDraft(initial.value)
  zoneError.value = null
}

async function save(): Promise<void> {
  const problem = findProblems(changedChannels(initial.value, draft.value))[0]
  if (problem) {
    ctx.message.warning(
      t(problem.kind === 'incomplete' ? 'notifications.quietHoursIncomplete' : 'notifications.quietHoursEmpty', {
        channel: channelLabel(problem.channel),
      }),
    )
    return
  }
  saving.value = true
  try {
    // One row per channel; sequential so a failure says which channel stopped
    // the save, and the draft is kept (not reloaded) so nothing typed is lost.
    for (const input of upserts.value) {
      try {
        await bridge.myPreferences.set(input)
      } catch (e) {
        if (isInvalidTimeZoneError(e)) {
          zoneError.value = t('notifications.zoneRejected', { zone: input.quietHoursTimeZone ?? '' })
        }
        throw e
      }
    }
    ctx.message.success(t('notifications.saved'))
    await load()
  } catch (e) {
    ctx.message.error(zoneError.value ?? (e instanceof Error ? e.message : String(e)))
  } finally {
    saving.value = false
  }
}

async function restoreDefaults(): Promise<void> {
  resetting.value = true
  try {
    await bridge.myPreferences.reset()
    ctx.message.success(t('notifications.restored'))
    await load()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    resetting.value = false
  }
}

async function removeRule(id: string): Promise<void> {
  removingId.value = id
  try {
    await bridge.myPreferences.remove(id)
    ctx.message.success(t('notifications.rules.removed'))
    await load()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    removingId.value = null
  }
}

onMounted(() => void load())
watch(() => ctx.reloadKey.value, () => void load())
</script>

<style src="./user-center-sections.css"></style>
