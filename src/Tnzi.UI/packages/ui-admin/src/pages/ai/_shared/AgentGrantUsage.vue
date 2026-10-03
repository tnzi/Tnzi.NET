<template>
  <!--
    AgentGrantUsage - renders one GrantUsageState: which agents hold an active
    grant for a resource. Presentational: the caller owns loading (see
    useAgentGrantUsage) and passes the state in.

    `variant="panel"` is the record view (skill drawer, knowledge base tab,
    agent resource card popover). `variant="confirm"` is the body of a delete
    confirmation: it names the agents that lose the resource, and when the check
    failed it says so instead of implying nobody depends on it.
  -->
  <div class="t-grant-usage" :data-variant="variant" :data-state="state.status">
    <div v-if="state.status === 'loading' || state.status === 'idle'" class="t-grant-usage__muted">
      {{ t('loading') }}
    </div>

    <div v-else-if="state.status === 'error'" class="t-grant-usage__error" role="alert">
      <span>{{ variant === 'confirm' ? t('deleteCheckFailed', { message: state.message }) : t('failed', { message: state.message }) }}</span>
      <NButton size="tiny" text type="primary" class="t-grant-usage__retry" @click="emit('retry')">{{ t('retry') }}</NButton>
    </div>

    <template v-else>
      <div v-if="state.agents.length === 0" class="t-grant-usage__muted t-grant-usage__none">
        {{ t('none') }}
      </div>
      <template v-else>
        <div class="t-grant-usage__count">
          {{ variant === 'confirm' ? t('deleteWarning', { count: state.agents.length }) : t('count', { count: state.agents.length }) }}
        </div>
        <ul class="t-grant-usage__list">
          <li v-for="agent in state.agents" :key="agent.agentId" class="t-grant-usage__item">
            <NButton v-if="router" text size="small" type="primary" @click="openAgent(agent.agentId)">{{ agent.agentName }}</NButton>
            <span v-else>{{ agent.agentName }}</span>
            <NTag v-if="!agent.agentIsEnabled" size="tiny" :bordered="false">{{ t('agentDisabled') }}</NTag>
          </li>
        </ul>
      </template>
      <div v-if="variant === 'panel'" class="t-grant-usage__note">{{ t('scopeNote') }}</div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { NButton, NTag } from 'naive-ui'
import { useRouter, type Router } from 'vue-router'
import { makePageTranslator } from '../../_shared/translate'
import type { GrantUsageState } from './useAgentGrantUsage'

interface Props {
  state: GrantUsageState
  variant?: 'panel' | 'confirm'
}

withDefaults(defineProps<Props>(), { variant: 'panel' })
const emit = defineEmits<{ retry: [] }>()

const t = makePageTranslator('ai.grantUsage')

// 与 useDetail 同一做法：没装路由（裸挂载 / 某些宿主）时名称退回纯文本，不抛。
function tryGetRouter(): Router | undefined {
  try {
    return useRouter() as Router | undefined
  } catch {
    return undefined
  }
}
const router = tryGetRouter()

function openAgent(id: string): void {
  router?.push({ name: 'ai.agents.detail', params: { id } }).catch(() => undefined)
}
</script>

<style scoped>
.t-grant-usage {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-base-text);
}
.t-grant-usage__muted {
  color: var(--tnzi-base-text-muted, #888);
}
.t-grant-usage__error {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  color: var(--tnzi-error, #d03050);
}
.t-grant-usage__count {
  font-weight: 500;
  margin-bottom: 4px;
}
.t-grant-usage__list {
  margin: 0;
  padding: 0;
  list-style: none;
  max-height: 220px;
  overflow-y: auto;
}
.t-grant-usage__item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 2px 0;
  min-width: 0;
}
.t-grant-usage__note {
  margin-top: 6px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-grant-usage[data-variant='confirm'] {
  max-width: 320px;
  margin-top: 6px;
}
</style>
