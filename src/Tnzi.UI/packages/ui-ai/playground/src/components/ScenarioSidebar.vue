<script setup lang="ts">
import { Icon } from '@iconify/vue'
import { usePlaygroundStore } from '../state/playground-store'

const store = usePlaygroundStore()

function onSelect(id: string): void {
  store.selectScenario(id)
}
</script>

<template>
  <div class="scenario-sidebar">
    <div class="scenario-sidebar__top">
      <button type="button" class="scenario-sidebar__new">
        <Icon icon="lucide:plus" /> New Chat
      </button>
      <button type="button" class="scenario-sidebar__cmdk" @click="store.commandPaletteOpen.value = true">
        <Icon icon="lucide:search" />
        <span>Search…</span>
        <kbd>⌘K</kbd>
      </button>
    </div>

    <div class="scenario-sidebar__group">
      <div class="scenario-sidebar__group-label">Showcase Scenarios</div>
      <button
        v-for="scenario in store.scenarioIndex.scenarios"
        :key="scenario.meta.id"
        type="button"
        class="scenario-sidebar__item"
        :class="{ 'scenario-sidebar__item--active': scenario.meta.id === store.currentScenarioId.value }"
        @click="onSelect(scenario.meta.id)"
      >
        <Icon :icon="scenario.meta.icon" class="scenario-sidebar__item-icon" />
        <span class="scenario-sidebar__item-label">{{ scenario.meta.title }}</span>
        <span class="scenario-sidebar__item-category">{{ scenario.meta.category }}</span>
      </button>
    </div>

    <div class="scenario-sidebar__group">
      <div class="scenario-sidebar__group-label">Your Chats</div>
      <div class="scenario-sidebar__empty">
        <Icon icon="lucide:message-square-plus" />
        <span>Start a chat from any scenario</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.scenario-sidebar { display: flex; flex-direction: column; height: 100%; padding: 12px; gap: 16px; }
.scenario-sidebar__top { display: flex; flex-direction: column; gap: 6px; }
.scenario-sidebar__new,
.scenario-sidebar__cmdk {
  display: flex; align-items: center; gap: 8px;
  padding: 8px 12px; border-radius: 8px;
  background: transparent; border: 1px solid var(--tnzi-ai-border, #e5e7eb);
  font-size: 13px; cursor: pointer; color: inherit;
}
.scenario-sidebar__new { background: var(--tnzi-ai-accent-soft, rgba(99,102,241,.12)); }
.scenario-sidebar__cmdk kbd { margin-left: auto; font-size: 10px; opacity: .6; }
.scenario-sidebar__group { display: flex; flex-direction: column; gap: 2px; }
.scenario-sidebar__group-label {
  font-size: 10px; text-transform: uppercase; letter-spacing: .05em;
  opacity: .5; padding: 0 8px; margin-bottom: 4px;
}
.scenario-sidebar__item {
  display: flex; align-items: center; gap: 10px;
  padding: 8px 10px; border-radius: 6px;
  background: transparent; border: none; font-size: 13px;
  text-align: left; cursor: pointer; color: inherit; width: 100%;
}
.scenario-sidebar__item:hover { background: var(--tnzi-ai-border, #f3f4f6); }
.scenario-sidebar__item--active { background: var(--tnzi-ai-accent-soft, rgba(99,102,241,.12)); font-weight: 500; }
.scenario-sidebar__item-label { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.scenario-sidebar__item-category { font-size: 9px; text-transform: uppercase; opacity: .5; }
.scenario-sidebar__empty {
  display: flex; align-items: center; gap: 8px;
  padding: 12px 10px; font-size: 12px; opacity: .5;
}
</style>
