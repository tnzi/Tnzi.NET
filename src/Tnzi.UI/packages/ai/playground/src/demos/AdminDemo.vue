<script setup lang="ts">
import { ref } from 'vue';
import { AdminLayout, AgentManagement, UsageAnalytics, SkillManagement } from '@tnzi/ai/admin';
import { mockAdminAgents, mockSkills, mockUsageSummary, mockUsageLogs } from '../mock/data';

const activePage = ref('agents');
</script>

<template>
  <div class="h-full">
    <AdminLayout :active-page="activePage" @navigate="activePage = $event">
      <AgentManagement v-if="activePage === 'agents'" :agents="mockAdminAgents" />
      <UsageAnalytics v-else-if="activePage === 'usage'" :summary="mockUsageSummary" :logs="mockUsageLogs" />
      <SkillManagement v-else-if="activePage === 'skills'" :skills="mockSkills" />
      <div v-else class="flex h-full items-center justify-center text-muted-foreground">
        <p>{{ activePage }} page — demo data not wired</p>
      </div>
    </AdminLayout>
  </div>
</template>
