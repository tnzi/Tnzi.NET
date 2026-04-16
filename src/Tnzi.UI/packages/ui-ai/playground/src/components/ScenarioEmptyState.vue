<script setup lang="ts">
import { Icon } from '@iconify/vue'
</script>

<template>
  <div class="scenario-empty">
    <Icon icon="lucide:message-circle-plus" class="scenario-empty__icon" />
    <h3 class="scenario-empty__title">Pick a scenario to begin</h3>
    <p class="scenario-empty__hint">
      The left sidebar lists ten showcase conversations that exercise different
      <strong>@tnzi/ui-ai</strong> components.
    </p>
  </div>
</template>

<style scoped>
.scenario-empty {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  height: 100%; padding: 48px; text-align: center; gap: 12px;
}
.scenario-empty__icon { font-size: 48px; opacity: .3; }
.scenario-empty__title { font-size: 18px; font-weight: 600; margin: 0; }
.scenario-empty__hint { font-size: 13px; opacity: .6; max-width: 380px; margin: 0; }
</style>
