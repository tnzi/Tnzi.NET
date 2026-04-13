<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
interface IPasswordResetProps {
  loading?: boolean;
  disabled?: boolean;
  emailLabel?: string;
  codeLabel?: string;
  passwordLabel?: string;
  confirmPasswordLabel?: string;
  submitLabel?: string;
  cancelLabel?: string;
  sendCodeLabel?: string;
  countdownSeconds?: number;
}

interface IPasswordResetEmits {
  submit: [data: { email: string; code: string; password: string }];
  cancel: [];
  sendCode: [email: string];
}

const { t } = useI18n();

const props = withDefaults(defineProps<IPasswordResetProps>(), {
  loading: false,
  disabled: false,
  emailLabel: '',
  codeLabel: '',
  passwordLabel: '',
  confirmPasswordLabel: '',
  submitLabel: '',
  cancelLabel: '',
  sendCodeLabel: '',
  countdownSeconds: 60,
});

const emit = defineEmits<IPasswordResetEmits>();

const email = ref('');
const code = ref('');
const password = ref('');
const confirmPassword = ref('');
const countdown = ref(0);

const isDisabled = computed(() => props.disabled);
const canSendCode = computed(() => !isDisabled.value && email.value && countdown.value === 0);
const passwordMismatch = computed(() => confirmPassword.value !== '' && confirmPassword.value !== password.value);

let timer: ReturnType<typeof setInterval> | null = null;

const startCountdown = () => {
  countdown.value = props.countdownSeconds;
  timer = setInterval(() => {
    countdown.value -= 1;
    if (countdown.value <= 0 && timer) {
      clearInterval(timer);
      timer = null;
    }
  }, 1000);
};

const handleSendCode = () => {
  if (!canSendCode.value) return;
  emit('sendCode', email.value);
  startCountdown();
};

const handleSubmit = () => {
  if (passwordMismatch.value) return;
  emit('submit', {
    email: email.value,
    code: code.value,
    password: password.value,
  });
};

onUnmounted(() => {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
});
</script>

<template>
  <div class="rounded-xl bg-white">
    <van-form @submit="handleSubmit">
      <van-field
        v-model="email"
        type="email"
        :label="props.emailLabel || t('auth.email')"
        :placeholder="t('auth.email')"
        :disabled="isDisabled"
        required
      />

      <van-field
        v-model="code"
        :label="props.codeLabel || t('auth.verificationCode')"
        :placeholder="t('auth.enterVerificationCode')"
        :disabled="isDisabled"
        required
      >
        <template #button>
          <van-button
            size="small"
            type="primary"
            :disabled="!canSendCode"
            @click="handleSendCode"
          >
            <span v-if="countdown > 0">{{ countdown }}s</span>
            <span v-else>{{ props.sendCodeLabel || t('auth.sendCode') }}</span>
          </van-button>
        </template>
      </van-field>

      <van-field
        v-model="password"
        type="password"
        :label="props.passwordLabel || t('auth.newPassword')"
        :placeholder="t('auth.newPassword')"
        :disabled="isDisabled"
        required
      />

      <van-field
        v-model="confirmPassword"
        type="password"
        :label="props.confirmPasswordLabel || t('auth.confirmPassword')"
        :placeholder="t('auth.confirmPassword')"
        :error-message="passwordMismatch ? t('auth.passwordMismatch') : ''"
        :disabled="isDisabled"
        required
      />

      <div class="flex gap-3 px-4 pb-4 pt-2">
        <van-button
          round
          block
          plain
          :disabled="isDisabled || props.loading"
          @click="emit('cancel')"
        >
          {{ props.cancelLabel || t('common.cancel') }}
        </van-button>
        <van-button
          round
          block
          type="primary"
          native-type="submit"
          :loading="props.loading"
          :disabled="isDisabled || passwordMismatch"
        >
          {{ props.submitLabel || t('auth.resetPassword') }}
        </van-button>
      </div>
    </van-form>
  </div>
</template>
