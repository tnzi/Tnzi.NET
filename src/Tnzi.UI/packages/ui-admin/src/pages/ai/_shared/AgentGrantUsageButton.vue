<template>
  <!--
    "Used by" affordance for one resource card: a small button that opens a
    popover and loads the reverse lookup on first open (and on every reopen, so
    it never shows a stale answer). Loading on demand keeps a section with 20
    cards from firing 20 reverse lookups on paint.
  -->
  <NPopover trigger="click" placement="bottom-start" :show-arrow="false" @update:show="onShow">
    <template #trigger>
      <NButton size="tiny" text class="t-grant-usage-button" :aria-label="t('button')">
        <template #icon><TSvgIcon icon="mdi:account-multiple-outline" :size="14" /></template>
        {{ t('button') }}
      </NButton>
    </template>
    <div class="t-grant-usage-button__body">
      <div class="t-grant-usage-button__title">{{ t('title') }}</div>
      <AgentGrantUsage :state="usage.state.value" @retry="reload" />
    </div>
  </NPopover>
</template>

<script setup lang="ts">
import { NButton, NPopover } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { makePageTranslator } from '../../_shared/translate'
import { createAgentGrantBridge, type AgentGrantResourceType } from '../../../services/bridges/agent-grant-bridge'
import { useAdminClient } from '../../../plugin/client'
import AgentGrantUsage from './AgentGrantUsage.vue'
import { useAgentGrantUsage } from './useAgentGrantUsage'

interface Props {
  type: AgentGrantResourceType
  resourceKey: string
}

const props = defineProps<Props>()
const t = makePageTranslator('ai.grantUsage')
const usage = useAgentGrantUsage(createAgentGrantBridge({ client: useAdminClient() }))

function reload(): void {
  void usage.load(props.type, props.resourceKey)
}

function onShow(show: boolean): void {
  if (show) reload()
}
</script>

<style scoped>
.t-grant-usage-button {
  font-size: 12px;
}
.t-grant-usage-button__body {
  min-width: 220px;
  max-width: 320px;
}
.t-grant-usage-button__title {
  font-weight: 600;
  font-size: 13px;
  margin-bottom: 6px;
}
</style>
