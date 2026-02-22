<template>
  <div class="space-y-4">
    <!-- TStatCard -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TStatCard</CardTitle>
        <CardDescription>Statistical cards for dashboards with trend indicators.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <TStatCard
            v-for="(stat, index) in demoStatCards"
            :key="index"
            :title="stat.title"
            :value="stat.value"
            :trend="stat.trend"
            :color="stat.color"
          />
        </div>
        <div class="mt-4">
          <p class="mb-2 text-sm font-medium">Loading State:</p>
          <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <TStatCard title="Loading..." :value="0" :loading="true" color="blue" />
            <TStatCard title="Small" :value="42" trend="5.0" color="green" size="small" />
            <TStatCard title="Large" :value="9999" trend="-2.5" color="red" size="large" />
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- TUserCard -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TUserCard</CardTitle>
        <CardDescription>User profile cards with avatar, role, and action buttons.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <TUserCard
            v-for="user in demoUsers"
            :key="user.id"
            :user="user"
            :show-actions="true"
            :actions="['view', 'edit', 'delete']"
            @click="onUserClick"
            @view="onUserView"
            @edit="onUserEdit"
            @delete="onUserDelete"
          />
        </div>
        <div class="mt-4">
          <p class="mb-2 text-sm font-medium">Different Sizes:</p>
          <div class="grid gap-4 sm:grid-cols-3">
            <TUserCard :user="demoUsers[0]" size="small" :clickable="false" />
            <TUserCard :user="demoUsers[1]" size="medium" :clickable="false" />
            <TUserCard :user="demoUsers[2]" size="large" :clickable="false" />
          </div>
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import {
  Card, CardHeader, CardTitle, CardDescription, CardContent,
  TStatCard, TUserCard,
  useShadcnMessage,
} from '@tnzi/shadcn';
import { demoStatCards, demoUsers } from '@tnzi/core/playground';

const message = useShadcnMessage();

const onUserClick = (user: any) => {
  console.log('[TUserCard] click:', user);
  message.show(`Clicked: ${user.name}`, 'info');
};

const onUserView = (user: any) => {
  console.log('[TUserCard] view:', user);
  message.show(`View: ${user.name}`, 'info');
};

const onUserEdit = (user: any) => {
  console.log('[TUserCard] edit:', user);
  message.show(`Edit: ${user.name}`, 'info');
};

const onUserDelete = (user: any) => {
  console.log('[TUserCard] delete:', user);
  message.show(`Delete: ${user.name}`, 'warning');
};
</script>
