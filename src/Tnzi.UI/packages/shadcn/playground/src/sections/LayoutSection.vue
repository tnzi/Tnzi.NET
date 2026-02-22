<template>
  <div class="space-y-4">
    <!-- TAdminLayout -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TAdminLayout</CardTitle>
        <CardDescription>Admin layout with sidebar, header, and content area.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="h-96 overflow-hidden rounded-md border">
          <TAdminLayout
            :sidebar-items="demoMenuItems"
            logo-text="Tnzi Admin"
            :show-sidebar="true"
            :show-header="true"
          >
            <template #header>
              <THeader
                title="Dashboard"
                :show-theme-toggle="true"
                :show-breadcrumbs="true"
                :breadcrumbs="demoBreadcrumbs"
                :user="headerUser"
                :show-user-menu="true"
                @theme-toggle="onThemeToggle"
                @user-menu-click="onUserMenuClick"
              />
            </template>
            <div class="space-y-4">
              <p class="text-sm text-muted-foreground">
                This is the main content area of TAdminLayout.
              </p>
              <div class="grid gap-4 sm:grid-cols-2">
                <div class="rounded-md border p-4">
                  <p class="text-sm font-medium">Content Panel A</p>
                  <p class="text-xs text-muted-foreground">Sample content inside layout.</p>
                </div>
                <div class="rounded-md border p-4">
                  <p class="text-sm font-medium">Content Panel B</p>
                  <p class="text-xs text-muted-foreground">Another panel in the grid.</p>
                </div>
              </div>
            </div>
          </TAdminLayout>
        </div>
      </CardContent>
    </Card>

    <!-- TSidebar standalone -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TSidebar</CardTitle>
        <CardDescription>Standalone sidebar with collapse and nested menu.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex gap-4">
          <div class="h-72 w-64 overflow-hidden rounded-md border">
            <TSidebar
              :items="demoMenuItems"
              :collapsed="false"
              logo-text="Full Sidebar"
              @menu-click="onSidebarMenuClick"
            />
          </div>
          <div class="h-72 w-16 overflow-hidden rounded-md border">
            <TSidebar
              :items="demoMenuItems"
              :collapsed="true"
              logo-text="Mini"
              @menu-click="onSidebarMenuClick"
            />
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- THeader standalone -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">THeader</CardTitle>
        <CardDescription>Standalone header with title, breadcrumbs, and user menu.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="rounded-md border px-4 py-2">
          <THeader
            title="App Header"
            logo-text="Logo"
            :show-theme-toggle="true"
            :show-breadcrumbs="true"
            :breadcrumbs="demoBreadcrumbs"
            :user="headerUser"
            :show-user-menu="true"
            @theme-toggle="onThemeToggle"
            @user-menu-click="onUserMenuClick"
          />
        </div>
        <div class="rounded-md border px-4 py-2">
          <THeader
            title="Minimal Header"
            :show-theme-toggle="false"
            :show-breadcrumbs="false"
            :show-user-menu="false"
          />
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import {
  Card, CardHeader, CardTitle, CardDescription, CardContent,
  TAdminLayout, TSidebar, THeader,
  useShadcnMessage,
} from '@tnzi/shadcn';
import { demoMenuItems, demoBreadcrumbs } from '@tnzi/core/playground';

const message = useShadcnMessage();

const headerUser = {
  name: 'Admin User',
  avatar: '',
};

const onThemeToggle = (isDark: boolean) => {
  console.log('[THeader] theme toggle:', isDark);
  message.show(`Theme: ${isDark ? 'Dark' : 'Light'}`, 'info');
};

const onUserMenuClick = (action: string) => {
  console.log('[THeader] user menu:', action);
  message.show(`User menu: ${action}`, 'info');
};

const onSidebarMenuClick = (item: any) => {
  console.log('[TSidebar] menu click:', item);
  message.show(`Sidebar: ${item.label}`, 'info');
};
</script>
