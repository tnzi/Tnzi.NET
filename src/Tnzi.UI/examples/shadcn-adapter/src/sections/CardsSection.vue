<template>
  <div class="space-y-4">
    <!-- StatCard -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">StatCard</CardTitle>
        <CardDescription>Statistical cards for dashboards with trend indicators.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
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
            <StatCard title="Loading..." :value="0" :loading="true" color="blue" />
            <StatCard title="Small" :value="42" trend="5.0" color="green" size="small" />
            <StatCard title="Large" :value="9999" trend="-2.5" color="red" size="large" />
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- UserCard -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">UserCard</CardTitle>
        <CardDescription>User profile cards with avatar, role, and action buttons.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <UserCard
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
            <UserCard :user="demoUsers[0]" size="small" :clickable="false" />
            <UserCard :user="demoUsers[1]" size="medium" :clickable="false" />
            <UserCard :user="demoUsers[2]" size="large" :clickable="false" />
          </div>
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import {
  Card, CardHeader, CardTitle, CardDescription, CardContent,
  StatCard, UserCard,
  useMessage,
} from '@tnzi/ui';
import { demoStatCards, demoUsers } from '@/playground';

const message = useMessage();

const onUserClick = (user: any) => {
  console.log('[UserCard] click:', user);
  message.show(`Clicked: ${user.name}`, 'info');
};

const onUserView = (user: any) => {
  console.log('[UserCard] view:', user);
  message.show(`View: ${user.name}`, 'info');
};

const onUserEdit = (user: any) => {
  console.log('[UserCard] edit:', user);
  message.show(`Edit: ${user.name}`, 'info');
};

const onUserDelete = (user: any) => {
  console.log('[UserCard] delete:', user);
  message.show(`Delete: ${user.name}`, 'warning');
};
</script>
