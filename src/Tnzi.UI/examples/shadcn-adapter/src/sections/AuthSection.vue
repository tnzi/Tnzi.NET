<template>
  <div class="space-y-4">
    <!-- LoginForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">LoginForm</CardTitle>
        <CardDescription>Login form with remember-me and forgot password.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="max-w-md">
          <LoginForm
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

    <!-- RegisterForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">RegisterForm</CardTitle>
        <CardDescription>Registration form with email, password, and optional username.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="max-w-md">
          <RegisterForm
            :show-username="true"
            :show-phone="true"
            :show-login-link="true"
            @submit="onRegisterSubmit"
            @login="onGoToLogin"
          />
        </div>
      </CardContent>
    </Card>

    <!-- PasswordReset -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">PasswordReset</CardTitle>
        <CardDescription>Password reset via email verification.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="max-w-md">
          <PasswordReset
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
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@tnzi/ui';
import { LoginForm, RegisterForm, PasswordReset, useMessage } from '@tnzi/ui';

const message = useMessage();

const onLoginSubmit = (data: any) => {
  console.log('[LoginForm] submit:', data);
  message.show(`Login: ${data.userName}`, 'success');
};

const onForgotPassword = () => {
  console.log('[LoginForm] forgotPassword');
  message.show('Forgot password clicked', 'info');
};

const onSocialLogin = (provider: string) => {
  console.log('[LoginForm] socialLogin:', provider);
  message.show(`Social login: ${provider}`, 'info');
};

const onRegisterSubmit = (data: any) => {
  console.log('[RegisterForm] submit:', data);
  message.show(`Register: ${data.email}`, 'success');
};

const onGoToLogin = () => {
  console.log('[RegisterForm] login link clicked');
  message.show('Go to login', 'info');
};

const onPasswordResetSubmit = (data: any) => {
  console.log('[PasswordReset] submit:', data);
  message.show('Password reset submitted', 'success');
};

const onPasswordResetCancel = () => {
  console.log('[PasswordReset] cancel');
  message.show('Password reset cancelled', 'info');
};

const onSendCode = (email: string) => {
  console.log('[PasswordReset] sendCode:', email);
  message.show(`Verification code sent to ${email}`, 'success');
};
</script>
