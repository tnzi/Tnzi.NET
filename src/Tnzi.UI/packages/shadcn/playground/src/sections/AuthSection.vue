<template>
  <div class="space-y-4">
    <!-- TLoginForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TLoginForm</CardTitle>
        <CardDescription>Login form with remember-me and forgot password.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="max-w-md">
          <TLoginForm
            :show-remember-me="true"
            :show-forgot-password="true"
            :show-social-login="true"
            :social-providers="['google', 'github', 'wechat']"
            @submit="onLoginSubmit"
            @forgot-password="onForgotPassword"
            @social-login="onSocialLogin"
          />
        </div>
      </CardContent>
    </Card>

    <!-- TRegisterForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TRegisterForm</CardTitle>
        <CardDescription>Registration form with email, password, and optional username.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="max-w-md">
          <TRegisterForm
            :show-username="true"
            :show-phone="true"
            :show-login-link="true"
            @submit="onRegisterSubmit"
            @login="onGoToLogin"
          />
        </div>
      </CardContent>
    </Card>

    <!-- TPasswordReset -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TPasswordReset</CardTitle>
        <CardDescription>Password reset via email verification.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="max-w-md">
          <TPasswordReset
            :countdown-seconds="60"
            @submit="onPasswordResetSubmit"
            @cancel="onPasswordResetCancel"
            @send-code="onSendCode"
          />
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@tnzi/shadcn';
import { TLoginForm, TRegisterForm, TPasswordReset, useShadcnMessage } from '@tnzi/shadcn';

const message = useShadcnMessage();

const onLoginSubmit = (data: any) => {
  console.log('[TLoginForm] submit:', data);
  message.show(`Login: ${data.userName}`, 'success');
};

const onForgotPassword = () => {
  console.log('[TLoginForm] forgotPassword');
  message.show('Forgot password clicked', 'info');
};

const onSocialLogin = (provider: string) => {
  console.log('[TLoginForm] socialLogin:', provider);
  message.show(`Social login: ${provider}`, 'info');
};

const onRegisterSubmit = (data: any) => {
  console.log('[TRegisterForm] submit:', data);
  message.show(`Register: ${data.email}`, 'success');
};

const onGoToLogin = () => {
  console.log('[TRegisterForm] login link clicked');
  message.show('Go to login', 'info');
};

const onPasswordResetSubmit = (data: any) => {
  console.log('[TPasswordReset] submit:', data);
  message.show('Password reset submitted', 'success');
};

const onPasswordResetCancel = () => {
  console.log('[TPasswordReset] cancel');
  message.show('Password reset cancelled', 'info');
};

const onSendCode = (email: string) => {
  console.log('[TPasswordReset] sendCode:', email);
  message.show(`Verification code sent to ${email}`, 'success');
};
</script>
