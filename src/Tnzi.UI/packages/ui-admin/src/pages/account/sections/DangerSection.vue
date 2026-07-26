<template>
  <TUserCenterSection :title="t('danger.title')">
    <p class="t-uc-hint">{{ t('danger.hint') }}</p>

    <NAlert type="info" :show-icon="false">
      <strong>{{ t('danger.export.title') }}</strong>
      <p class="t-uc-hint">{{ t('danger.export.hint') }}</p>
      <NButton size="small" :loading="exporting" @click="exportData">
        <template #icon><TSvgIcon icon="mdi:download" :size="14" /></template>
        {{ t('danger.export.button') }}
      </NButton>
    </NAlert>

    <NAlert type="warning" :show-icon="false">
      <strong>{{ t('danger.deactivate.title') }}</strong>
      <p class="t-uc-hint">{{ t('danger.deactivate.hint') }}</p>
      <NPopconfirm @positive-click="deactivateAccount">
        <template #trigger>
          <NButton size="small" type="warning" ghost :loading="deactivating">
            {{ t('danger.deactivate.button') }}
          </NButton>
        </template>
        {{ t('danger.deactivate.confirm') }}
      </NPopconfirm>
    </NAlert>

    <NAlert type="error" :show-icon="false">
      <strong>{{ t('danger.delete.title') }}</strong>
      <p class="t-uc-hint">{{ t('danger.delete.hint') }}</p>
      <NPopconfirm @positive-click="deleteAccount">
        <template #trigger>
          <NButton size="small" type="error" :loading="deleting">
            {{ t('danger.delete.button') }}
          </NButton>
        </template>
        {{ t('danger.delete.confirm') }}
      </NPopconfirm>
    </NAlert>
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NAlert, NButton, NPopconfirm } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { downloadBlob } from '@tnzi/core'
import TUserCenterSection from './TUserCenterSection.vue'
import { useUserCenterContext } from '../userCenterContext'

const ctx = useUserCenterContext()
const t = ctx.t

const exporting = ref(false)
const deactivating = ref(false)
const deleting = ref(false)

async function exportData(): Promise<void> {
  exporting.value = true
  try {
    const data = await ctx.bridge.me.exportPersonalData()
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
    downloadBlob(blob, `personal-data-${new Date().toISOString().slice(0, 10)}.json`)
    ctx.message.success(t('danger.export.success'))
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    exporting.value = false
  }
}

async function deactivateAccount(): Promise<void> {
  deactivating.value = true
  try {
    await ctx.bridge.me.deactivate()
    ctx.message.success(t('danger.deactivate.success'))
    // Account is now disabled - current session is dead from the server's POV.
    ctx.logoutAndRedirect()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    deactivating.value = false
  }
}

async function deleteAccount(): Promise<void> {
  deleting.value = true
  try {
    await ctx.bridge.me.deleteAccount()
    ctx.message.success(t('danger.delete.success'))
    ctx.logoutAndRedirect()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  } finally {
    deleting.value = false
  }
}
</script>
