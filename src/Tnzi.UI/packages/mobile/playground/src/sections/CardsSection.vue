<script setup lang="ts">
import { showToast } from 'vant';
import { demoStatCards, demoUsers } from '../data';

const onViewUser = (user: unknown) => {
  console.log('View user:', user);
  showToast('View user');
};

const onEditUser = (user: unknown) => {
  console.log('Edit user:', user);
  showToast('Edit user');
};
</script>

<template>
  <div class="cards-section">
    <!-- TStatCard -->
    <van-cell-group inset title="TStatCard">
      <div class="stat-grid">
        <TStatCard
          v-for="(card, index) in demoStatCards"
          :key="index"
          :title="card.title"
          :value="card.value"
          :suffix="card.suffix"
          :trend="card.trend"
          :color="card.color"
        />
      </div>
    </van-cell-group>

    <!-- TUserCard -->
    <van-cell-group inset title="TUserCard">
      <div class="user-list">
        <TUserCard
          v-for="user in demoUsers"
          :key="user.id"
          :user="user"
          :show-actions="true"
          @view="onViewUser"
          @edit="onEditUser"
        />
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.cards-section {
  padding-bottom: 8px;
}

.stat-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
  padding: 8px 12px;
}

.user-list {
  display: grid;
  gap: 6px;
  padding: 8px 12px;
}
</style>
