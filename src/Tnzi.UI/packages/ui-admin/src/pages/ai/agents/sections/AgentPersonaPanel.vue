<template>
  <!--
    AgentPersonaPanel - the Persona (Soul) section. Persona is inline content on the
    agent (Agent.persona), injected as a <soul> block at runtime. A single agent has a
    single persona: this is a plain content editor, not a shared-catalog picker. Consuming
    apps that want a reusable persona library implement it themselves.
  -->
  <TDetailSection :title="t('detail.panels.persona')" :icon="icon" :hint="t('detail.persona.hint')" max-width="none">
    <div class="t-persona">
      <p class="t-persona__lead">{{ t('detail.persona.lead') }}</p>
      <NInput
        v-model:value="content"
        type="textarea"
        class="t-persona__editor font-mono"
        :rows="18"
        :placeholder="t('detail.persona.contentPlaceholder')"
      />
      <div class="t-persona__actions">
        <NButton size="small" :disabled="!dirty" @click="reset">{{ t('detail.persona.discard') }}</NButton>
        <NButton size="small" type="primary" :loading="saving" :disabled="!dirty" @click="save">
          {{ t('detail.persona.save') }}
        </NButton>
      </div>
    </div>
  </TDetailSection>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NInput } from 'naive-ui'
import TDetailSection from '../../../../components/detail/TDetailSection.vue'
import { makePageTranslator } from '../../../_shared/translate'

interface Props {
  /** Current inline persona content on the agent (null when unset). */
  persona: string | null
  /** Whether a save is in flight. */
  saving?: boolean
  /** Section-header icon (mirrors the active nav item's icon). */
  icon?: string
}
const props = withDefaults(defineProps<Props>(), { saving: false, icon: undefined })

const emit = defineEmits<{
  /** Persist the agent's inline persona (soul) content. Parent calls agents.update. */
  save: [content: string]
}>()

const t = makePageTranslator('ai.agents')

const content = ref(props.persona ?? '')
const baseline = ref(props.persona ?? '')

// Re-sync when the agent (or its persona) changes underneath us.
watch(() => props.persona, (v) => {
  content.value = v ?? ''
  baseline.value = v ?? ''
})

const dirty = computed(() => content.value !== baseline.value)

function reset(): void {
  content.value = baseline.value
}
function save(): void {
  emit('save', content.value)
}
</script>

<style scoped>
.t-persona { display: flex; flex-direction: column; gap: 12px; }
.t-persona__lead { margin: 0; font-size: 12.5px; color: var(--tnzi-base-text-muted, #888); }
.t-persona__editor :deep(textarea) { font-size: 13px; line-height: 1.6; }
.t-persona__actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
