<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Layout</h1>
      <p class="section-desc">Page layout components including basic layouts, admin layouts, and collapsible sidebars.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Basic Layout</h2>
      <PreviewBox full-width min-height="200px">
        <div style="border: 1px solid var(--n-border-color); border-radius: 4px; overflow: hidden; width: 100%">
          <n-layout style="height: 200px">
            <n-layout-header bordered style="height: 48px; padding: 0 16px; display: flex; align-items: center">
              <n-text strong>Header</n-text>
            </n-layout-header>
            <n-layout-content content-style="padding: 16px;">
              <n-p>Main content area.</n-p>
            </n-layout-content>
          </n-layout>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Admin Layout (TAdminLayout)</h2>
      <PreviewBox full-width min-height="360px">
        <div style="border: 1px solid var(--n-border-color); border-radius: 4px; overflow: hidden; height: 360px; width: 100%">
          <TAdminLayout
            :sidebar-items="demoMenuItems"
            :sidebar-width="200"
            logo-text="Tnzi"
            title="Admin Panel"
            @menu-select="handleMenuSelect"
          >
            <n-breadcrumb style="margin-bottom: 12px">
              <n-breadcrumb-item>Home</n-breadcrumb-item>
              <n-breadcrumb-item>Dashboard</n-breadcrumb-item>
            </n-breadcrumb>
            <n-card size="small" title="Dashboard">
              <n-p>Welcome to the admin dashboard. This uses TAdminLayout component.</n-p>
              <n-space>
                <n-tag type="success">Online</n-tag>
                <n-tag>v1.0.0</n-tag>
              </n-space>
            </n-card>
          </TAdminLayout>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Collapsible Sider</h2>
      <PreviewBox full-width min-height="240px">
        <div style="border: 1px solid var(--n-border-color); border-radius: 4px; overflow: hidden; width: 100%">
          <n-layout has-sider style="height: 240px">
            <n-layout-sider
              bordered
              show-trigger
              collapse-mode="width"
              :collapsed-width="48"
              :width="180"
              :native-scrollbar="false"
              content-style="padding: 8px 0;"
            >
              <n-menu :options="siderMenuOptions" default-value="inbox" />
            </n-layout-sider>
            <n-layout-content content-style="padding: 16px;">
              <n-p>Click the trigger to collapse/expand the sider.</n-p>
            </n-layout-content>
          </n-layout>
        </div>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useMessage } from 'naive-ui';
import type { MenuOption } from 'naive-ui';
import { demoMenuItems } from '../data/index';
import { TAdminLayout, createMessageAdapter } from '@tnzi/ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const msgAdapter = createMessageAdapter(message);

function handleMenuSelect(key: string) {
  msgAdapter.info(`Menu: ${key}`);
}

const siderMenuOptions: MenuOption[] = [
  { label: 'Inbox', key: 'inbox' },
  { label: 'Starred', key: 'starred' },
  { label: 'Sent', key: 'sent' },
  { label: 'Drafts', key: 'drafts' },
  { label: 'Trash', key: 'trash' },
];
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
