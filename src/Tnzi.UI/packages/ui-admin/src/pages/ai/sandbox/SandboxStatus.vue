<template>
  <!--
    SandboxStatus — surfaces /admin/sandbox/status exposed by
    `Tnzi.AI.Sandbox`. Single status card showing the active provider,
    data root, and the three denylists (commands / file patterns /
    environment variables) that the local provider applies to every
    sandbox-bound tool call.
  -->
  <div class="t-sandbox-page t-page-scroll">
    <div class="t-sandbox-page__header">
      <div class="t-sandbox-page__title">
        <TSvgIcon icon="mdi:cube-outline" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-sandbox-page__toolbar">
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
      </div>
    </div>

    <NAlert
      v-if="status && !status.enabled"
      :title="t('disabled.title')"
      type="warning"
      :closable="false"
    >
      {{ t('disabled.body') }}
    </NAlert>

    <NCard size="small" :bordered="false">
      <div class="t-sandbox-page__overview">
        <div class="t-sandbox-page__row">
          <span class="t-sandbox-page__label">
            <TSvgIcon icon="mdi:server" :size="14" />
            {{ t('fields.provider') }}
          </span>
          <NTag :type="status?.enabled ? 'success' : 'default'" size="medium" :bordered="false">
            {{ status?.provider ?? '—' }}
          </NTag>
        </div>
        <div class="t-sandbox-page__row">
          <span class="t-sandbox-page__label">
            <TSvgIcon icon="mdi:folder-outline" :size="14" />
            {{ t('fields.dataRoot') }}
          </span>
          <code class="t-sandbox-page__path">{{ status?.dataRoot ?? '—' }}</code>
        </div>
        <div class="t-sandbox-page__row">
          <span class="t-sandbox-page__label">
            <TSvgIcon icon="mdi:power" :size="14" />
            {{ t('fields.enabled') }}
          </span>
          <NTag :type="status?.enabled ? 'success' : 'warning'" size="small" :bordered="false">
            {{ status?.enabled ? t('status.enabled') : t('status.disabled') }}
          </NTag>
        </div>
      </div>
    </NCard>

    <div class="t-sandbox-page__denylists">
      <NCard :title="t('sections.deniedCommands')" size="small" :bordered="false">
        <template #header-extra>
          <NTag :bordered="false" size="small">{{ status?.deniedCommands.length ?? 0 }}</NTag>
        </template>
        <div v-if="status?.deniedCommands.length" class="t-sandbox-page__chips">
          <NTag
            v-for="cmd in status.deniedCommands"
            :key="cmd"
            type="error"
            :bordered="false"
            size="small"
            class="t-sandbox-page__mono"
          >
            {{ cmd }}
          </NTag>
        </div>
        <NEmpty v-else size="small" :description="t('empty.deniedCommands')" />
      </NCard>

      <NCard :title="t('sections.deniedPatterns')" size="small" :bordered="false">
        <template #header-extra>
          <NTag :bordered="false" size="small">{{ status?.deniedPatterns.length ?? 0 }}</NTag>
        </template>
        <div v-if="status?.deniedPatterns.length" class="t-sandbox-page__chips">
          <NTag
            v-for="p in status.deniedPatterns"
            :key="p"
            type="warning"
            :bordered="false"
            size="small"
            class="t-sandbox-page__mono"
          >
            {{ p }}
          </NTag>
        </div>
        <NEmpty v-else size="small" :description="t('empty.deniedPatterns')" />
      </NCard>

      <NCard :title="t('sections.envBlacklist')" size="small" :bordered="false">
        <template #header-extra>
          <NTag :bordered="false" size="small">{{ status?.environmentBlacklist.length ?? 0 }}</NTag>
        </template>
        <div v-if="status?.environmentBlacklist.length" class="t-sandbox-page__chips">
          <NTag
            v-for="env in status.environmentBlacklist"
            :key="env"
            :bordered="false"
            size="small"
            class="t-sandbox-page__mono"
          >
            {{ env }}
          </NTag>
        </div>
        <NEmpty v-else size="small" :description="t('empty.envBlacklist')" />
      </NCard>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { NAlert, NButton, NCard, NEmpty, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../../plugin/client'
import { createSandboxBridge, type SandboxStatusDto } from '../../../services/bridges/sandbox-bridge'
import { interpolate, translatePageKey } from '../../_shared/translate'

const bridge = createSandboxBridge({ client: useAdminClient() })
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('ai.sandbox', key), params)

const loading = ref(false)
const status = ref<SandboxStatusDto | null>(null)

async function refresh(): Promise<void> {
  loading.value = true
  try {
    status.value = await bridge.getStatus()
  } catch {
    status.value = null
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
.t-sandbox-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 0;
}
.t-sandbox-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-sandbox-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-sandbox-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-sandbox-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-sandbox-page__overview {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-sandbox-page__row {
  display: flex;
  align-items: center;
  gap: 16px;
}
.t-sandbox-page__label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 13px;
  min-width: 120px;
}
.t-sandbox-page__path {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  word-break: break-all;
}
.t-sandbox-page__denylists {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}
@media (max-width: 900px) {
  .t-sandbox-page__denylists { grid-template-columns: 1fr; }
}
.t-sandbox-page__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.t-sandbox-page__mono {
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
}
</style>
