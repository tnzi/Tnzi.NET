<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Cards</h1>
      <p class="section-desc">Statistical cards and user profile cards for dashboards.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Stat Cards</h2>
      <PreviewBox full-width>
        <n-grid :cols="4" :x-gap="16" :y-gap="16" responsive="screen" :item-responsive="true">
          <n-gi v-for="stat in demoStatCards" :key="stat.title" span="4 m:2 l:1">
            <TStatCard v-bind="stat" />
          </n-gi>
        </n-grid>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">User Cards</h2>
      <PreviewBox full-width>
        <n-grid :cols="3" :x-gap="16" :y-gap="16" responsive="screen" :item-responsive="true">
          <n-gi v-for="user in demoUsers" :key="user.id" span="3 m:1">
            <TUserCard
              :user="user"
              clickable
              show-status
              @click="handleUserClick"
              @view="handleUserView"
              @edit="handleUserEdit"
            />
          </n-gi>
        </n-grid>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useMessage } from 'naive-ui';
import { demoStatCards, demoUsers } from '@tnzi/core/playground';
import { TStatCard, TUserCard, createNaiveMessageAdapter } from '@tnzi/naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const msgAdapter = createNaiveMessageAdapter(message);

function handleUserClick(user: any) {
  msgAdapter.info(`Clicked: ${user.name}`);
}

function handleUserView(user: any) {
  msgAdapter.info(`View: ${user.name}`);
}

function handleUserEdit(user: any) {
  msgAdapter.info(`Edit: ${user.name}`);
}
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}
</style>
