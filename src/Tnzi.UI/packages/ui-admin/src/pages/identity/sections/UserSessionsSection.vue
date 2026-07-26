<template>
  <TDetailSection :title="t('detail.sections.sessions')" :hint="t('detail.sessions.hint')" max-width="none">
    <template #actions>
      <NButton size="small" tertiary :loading="loading" @click="load">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="15" /></template>
        {{ t('admin.common.reload') }}
      </NButton>
      <NPopconfirm v-if="canRevoke && activeCount > 0" @positive-click="revokeAll">
        <template #trigger>
          <NButton size="small" type="error" tertiary :loading="revoking">
            <template #icon><TSvgIcon icon="mdi:logout-variant" :size="15" /></template>
            {{ t('detail.sessions.revokeAll') }}
          </NButton>
        </template>
        {{ t('detail.sessions.confirmRevokeAll') }}
      </NPopconfirm>
    </template>

    <NSpin :show="loading">
      <TEmpty v-if="!loading && !sessions.length" :text="t('detail.sessions.empty')" size="small" />
      <div v-else class="us-list">
        <TItemCard
          v-for="s in sessions"
          :key="s.id"
          :title="s.deviceInfo || t('detail.sessions.unknownDevice')"
          :icon="deviceIcon(s)"
          :icon-tone="s.isRevoked ? 'default' : 'success'"
          :muted="s.isRevoked"
          :tags="sessionTags(s)"
        >
          <template #meta>
            <div class="us-meta">
              <span class="us-meta__item">
                <TSvgIcon icon="mdi:ip-network-outline" :size="13" />{{ s.ipAddress || EMPTY_DASH }}
              </span>
              <span class="us-meta__item">
                <TSvgIcon icon="mdi:login" :size="13" />{{ t('detail.sessions.signedIn') }}
                <TRelativeTime :value="s.creationTime" />
              </span>
              <span class="us-meta__item">
                <TSvgIcon icon="mdi:pulse" :size="13" />{{ t('detail.sessions.lastActive') }}
                <TRelativeTime :value="s.lastActivityTime" />
              </span>
            </div>
          </template>

          <template v-if="canRevoke && !s.isRevoked" #actions>
            <NPopconfirm @positive-click="revokeOne(s.id)">
              <template #trigger>
                <NButton size="tiny" tertiary type="error">{{ t('detail.sessions.revoke') }}</NButton>
              </template>
              {{ t('detail.sessions.confirmRevoke') }}
            </NPopconfirm>
          </template>
        </TItemCard>
      </div>
    </NSpin>
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * The user's signed-in devices.
 *
 * Every row here is a live door into the account, so the panel leads with the
 * device and shows where and when it was last used. Revoking is the action an
 * admin comes here to take, so it sits on the row (and once for all sessions in
 * the header) instead of behind a menu.
 */
import { computed, ref } from 'vue'
import { NButton, NPopconfirm, NSpin } from 'naive-ui'
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TDetailSection from '../../../components/detail/TDetailSection.vue'
import TEmpty from '../../../components/data/TEmpty.vue'
import TItemCard, { type ItemCardTag } from '../../../components/data/TItemCard.vue'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { createIdentityBridge } from '../../../services/bridges/identity-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safeMessage'
import type { UserSessionDto } from '@tnzi/core/services/identity'

const props = defineProps<{
  userId: string
  canRevoke: boolean
  t: (key: string, named?: Record<string, unknown>) => string
}>()

const bridge = createIdentityBridge({ client: useAdminClient() })
const message = useSafeMessage()

const sessions = ref<UserSessionDto[]>([])
const loading = ref(true)
const revoking = ref(false)

const activeCount = computed(() => sessions.value.filter((s) => !s.isRevoked).length)

async function load(): Promise<void> {
  loading.value = true
  try {
    // Revoked sessions are included: "this device was signed out on Tuesday" is
    // part of the account's story, and hiding them makes a revoke look like the
    // session vanished.
    sessions.value = await bridge.sessions.listForUser(props.userId, true)
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    loading.value = false
  }
}
void load()

function sessionTags(s: UserSessionDto): ItemCardTag[] {
  return s.isRevoked
    ? [{ label: props.t('detail.sessions.revoked'), type: 'default' }]
    : [{ label: props.t('detail.sessions.active'), type: 'success' }]
}

/** Rough device glyph from the user agent, so the list is scannable. */
function deviceIcon(s: UserSessionDto): string {
  const ua = `${s.deviceInfo ?? ''} ${s.userAgent ?? ''}`.toLowerCase()
  if (/iphone|android|mobile/.test(ua)) return 'mdi:cellphone'
  if (/ipad|tablet/.test(ua)) return 'mdi:tablet'
  return 'mdi:monitor'
}

async function revokeOne(id: string): Promise<void> {
  try {
    await bridge.sessions.revoke(id)
    message.success(props.t('detail.sessions.revoked'))
    await load()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

async function revokeAll(): Promise<void> {
  revoking.value = true
  try {
    await bridge.sessions.revokeAllForUser(props.userId)
    message.success(props.t('detail.sessions.revokedAll'))
    await load()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    revoking.value = false
  }
}
</script>

<style scoped>
.us-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.us-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.us-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}
</style>
