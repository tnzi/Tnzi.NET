<script setup lang="ts">
import { ref } from 'vue';
import {
  ModelSelector as TModelSelector, Checkpoint as TCheckpoint, OpenIn as TOpenIn,
  AgentSelector as TAgentSelector, AgentHandoff as TAgentHandoff,
  PersonaSelector as TPersonaSelector, AgentStatus as TAgentStatus,
  Reasoning as TReasoning, ToolCallDisplay as TToolCallDisplay,
  SubtaskCard as TSubtaskCard, AgentPlan as TPlan, TaskItem as TTaskItem, AgentQueue as TQueue,
  SkillGallery as TSkillGallery, SkillCard as TSkillCard, SkillActivator as TSkillActivator,
  SourceCitations as TSourceCitations, KnowledgePanel as TKnowledgePanel,
  TokenUsage as TTokenUsage, QuotaStatus as TQuotaStatus, McpToolPanel as TMcpToolPanel,
} from '@tnzi/ui-ai/components';
import { mockAgents, mockPersonas, mockSkills, mockKnowledgeBases, mockCitations, mockMcpTools, mockModels, mockQueueSections } from '../mock/data';

const selectedModel = ref('claude-sonnet-4-6');
const modelOpen = ref(false);
const selectedAgent = ref('a1');
</script>

<template>
  <div class="h-full overflow-y-auto p-6 space-y-8">
    <!-- Agent -->
    <section>
      <h2 class="text-lg font-semibold mb-4 flex items-center gap-2">Agent Components</h2>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TModelSelector</h3>
          <TModelSelector v-model:model-value="selectedModel" v-model:open="modelOpen" :models="mockModels" />
          <p class="text-xs text-muted-foreground">Selected: {{ selectedModel }}</p>
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TAgentStatus</h3>
          <div class="space-y-2">
            <TAgentStatus name="Research Agent" status="running" />
            <TAgentStatus name="Code Assistant" status="completed" />
            <TAgentStatus name="Data Analyst" status="error" />
            <TAgentStatus name="Writing Coach" status="idle" />
          </div>
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TAgentHandoff</h3>
          <TAgentHandoff from-agent="Research Agent" to-agent="Code Assistant" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TOpenIn + TCheckpoint + TPersonaSelector</h3>
          <div class="flex flex-wrap gap-2">
            <TOpenIn query="How does async/await work?" />
            <TPersonaSelector :personas="mockPersonas" selected-id="p1" />
          </div>
          <TCheckpoint />
        </div>
        <div class="col-span-full space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TAgentSelector</h3>
          <TAgentSelector :agents="mockAgents" :selected-id="selectedAgent" @select="selectedAgent = $event" />
        </div>
      </div>
    </section>

    <!-- Reasoning -->
    <section>
      <h2 class="text-lg font-semibold mb-4">Reasoning Components</h2>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TReasoning</h3>
          <TReasoning content="The user is asking about error handling. I should show a Result wrapper pattern instead of basic try/catch..." :is-streaming="false" :default-open="true" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TToolCallDisplay</h3>
          <TToolCallDisplay :tool-call="{ name: 'web_search', durationMs: 820, output: 'Found 3 relevant results' }" />
          <TToolCallDisplay :tool-call="{ name: 'code_execution', durationMs: 150 }" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TPlan + TTaskItem</h3>
          <TPlan title="Research async/await patterns" description="3-step plan for comprehensive answer">
            <TTaskItem title="Search MDN documentation" :files="['mdn-async.md']" />
            <TTaskItem title="Find real-world examples" :files="['examples.ts', 'patterns.ts']" />
            <TTaskItem title="Compile best practices" />
          </TPlan>
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TQueue + TSubtaskCard</h3>
          <TQueue :sections="mockQueueSections" />
          <div class="mt-3">
            <TSubtaskCard title="Web Search" status="completed" />
            <TSubtaskCard title="Summarization" status="in_progress" />
            <TSubtaskCard title="Response Generation" status="pending" />
          </div>
        </div>
      </div>
    </section>

    <!-- Skill + Knowledge -->
    <section>
      <h2 class="text-lg font-semibold mb-4">Skill & Knowledge Components</h2>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div class="col-span-full space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TSkillGallery</h3>
          <TSkillGallery :skills="mockSkills" :categories="['Research', 'Coding', 'Creative', 'Data', 'Communication']" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TKnowledgePanel</h3>
          <TKnowledgePanel :bases="mockKnowledgeBases" :selected-ids="['kb1']" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TSourceCitations</h3>
          <TSourceCitations :citations="mockCitations" />
        </div>
      </div>
    </section>

    <!-- Context -->
    <section>
      <h2 class="text-lg font-semibold mb-4">Context Components</h2>
      <div class="flex flex-wrap gap-4 items-start">
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TTokenUsage</h3>
          <TTokenUsage :usage="{ inputTokens: 1250, outputTokens: 3400, cachedInputTokens: 200, cacheCreationTokens: 50, totalTokens: 4900 }" model-id="claude-sonnet-4-6" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TQuotaStatus</h3>
          <TQuotaStatus :used="7500" :limit="10000" reset-date="2026-04-01" />
        </div>
        <div class="space-y-3 rounded-lg border p-4">
          <h3 class="text-sm font-medium text-muted-foreground">TMcpToolPanel</h3>
          <TMcpToolPanel status="connected" :tools="mockMcpTools" />
        </div>
      </div>
    </section>
  </div>
</template>
