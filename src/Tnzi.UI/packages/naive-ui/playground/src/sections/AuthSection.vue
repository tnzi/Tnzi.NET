<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Authentication</h1>
      <p class="section-desc">Login, registration, and password reset form components.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Login Form</h2>
      <PreviewBox>
        <div style="width: 100%; max-width: 400px; margin: 0 auto;">
          <TLoginForm
            @submit="handleLogin"
            @forgotPassword="handleForgot"
          />
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Register Form</h2>
      <PreviewBox>
        <div style="width: 100%; max-width: 400px; margin: 0 auto;">
          <TRegisterForm
            @submit="handleRegister"
            @login="handleGoLogin"
          />
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Password Reset</h2>
      <PreviewBox>
        <div style="width: 100%; max-width: 400px; margin: 0 auto;">
          <TPasswordReset
            @submit="handleReset"
            @login="handleGoLogin"
          />
        </div>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useMessage } from 'naive-ui';
import { TLoginForm, TRegisterForm, TPasswordReset, createNaiveMessageAdapter } from '@tnzi/naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const msgAdapter = createNaiveMessageAdapter(message);

function handleLogin(credentials: { userName: string; password: string; rememberMe?: boolean }) {
  msgAdapter.success(`Login: ${credentials.userName}`);
}

function handleForgot() {
  msgAdapter.info('Forgot password clicked');
}

function handleRegister(data: { email: string; password: string; userName?: string }) {
  msgAdapter.success(`Registered: ${data.userName || data.email}`);
}

function handleGoLogin() {
  msgAdapter.info('Navigate to login');
}

function handleReset(data: { email: string }) {
  msgAdapter.success(`Reset link sent to ${data.email}`);
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
