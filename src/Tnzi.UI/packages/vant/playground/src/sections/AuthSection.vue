<script setup lang="ts">
import { showToast } from 'vant';

const onLogin = (data: unknown) => {
  console.log('Login:', data);
  showToast('Login submitted');
};

const onRegister = (data: unknown) => {
  console.log('Register:', data);
  showToast('Register submitted');
};
</script>

<template>
  <div class="auth-section">
    <!-- TLoginForm -->
    <van-cell-group inset title="TLoginForm">
      <div class="component-wrapper">
        <TLoginForm @submit="onLogin" />
      </div>
    </van-cell-group>

    <!-- TRegisterForm -->
    <van-cell-group inset title="TRegisterForm">
      <div class="component-wrapper">
        <TRegisterForm @submit="onRegister" />
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.auth-section {
  padding-bottom: 8px;
}

.component-wrapper {
  padding: 4px 0;
}
</style>
