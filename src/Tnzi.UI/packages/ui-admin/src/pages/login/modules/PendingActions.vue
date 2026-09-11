<script setup lang="ts">
/**
 * `PendingActions` - 登录被要求先办完几件事时的那一步。
 *
 * 后端在凭据与 2FA 都通过之后，若账号还欠着义务（改密 / 绑验证器 / 确认邮箱），
 * 会答 403 `IDENTITY_PENDING_ACTIONS_REQUIRED` 并带上一枚临时令牌；
 * `default-auth` 把它交给 shell，shell 切到本模块。
 *
 * ★ 这不是「找回密码」（`ResetPwd`）：那条路从邮箱验证码进来、用户可能压根登不进去；
 * 这条路上用户**身份已经证明过了**，只是必须先把欠的事办完才能继续。
 *
 * ★ **一次办一件，办完继续留在本页**。后端答 `completed: false` 时临时令牌仍然有效，
 * 前端据此换到下一件 —— 而不是把人退回登录页重来一遍。
 */
import { computed, onMounted, reactive, ref } from 'vue'
import { NForm, NFormItem, NInput, NButton, NSpace, NAlert, NQrCode, NSpin, type FormRules } from 'naive-ui'
import { useFormRules, useLoginContext } from '@tnzi/ui'
import { useNaiveForm } from '../../../headless/useNaiveForm'

defineOptions({ name: 'PendingActions' })

const { translate, toggleLoginModule, callbacks, ui, pendingAction, helpers } = useLoginContext()
const { rules: r } = useFormRules(translate)
const { formRef, validate } = useNaiveForm()

const model = reactive({ password: '', confirmPassword: '', code: '' })
const submitting = ref(false)
const sending = ref(false)
const loading = ref(true)
const submitError = ref('')

/** 还欠哪些，以及办它们需要的材料。挑战本身只带名字，材料要另外问。 */
const remaining = ref<string[]>([])
/**
 * 读取待办清单本身失败了（令牌过期 / 网络断了 / 后端出错）。
 *
 * ★ 必须与「没有待办」分开。两者的 `remaining` 都是空的，而把它们混为一谈的后果是
 * 渲染出一张**猜出来的**表单（三元链的兜底分支），用户填完只会拿到一句
 * 「该流程尚未配置」—— 真正的原因一个字都没说。
 */
const loadFailed = ref(false)
const totpSetup = ref<{ sharedKey: string; authenticatorUri: string } | null>(null)
const maskedEmail = ref<string | null>(null)

/** 当前正在办的那一件。一次只办一件，办完刷新列表。 */
const current = computed(() => remaining.value[0] ?? null)
const account = computed(() => pendingAction.value?.userName ?? '')

// 按当前这一件动态给规则。写成显式的 FormRules 而不是三元的联合类型：
// 两个分支的键不同，联合类型会让 naive-ui 的索引签名对不上。
const rules = computed<FormRules>(() => {
  if (current.value === 'ChangePassword') {
    return {
      password: r.password(),
      confirmPassword: [
        r.matches(
          () => model.password,
          translate('admin.login.errorPasswordMismatch', 'Passwords do not match'),
        ),
      ],
    } as FormRules
  }

  return {
    code: [
      {
        required: true,
        trigger: ['blur', 'input'],
        message: translate('admin.login.errorEmptyCode', 'Please enter the verification code'),
      },
    ],
  } as FormRules
})

async function refresh(): Promise<void> {
  const token = pendingAction.value?.tempToken
  if (!token || !callbacks.describePendingActions) {
    loading.value = false
    return
  }

  try {
    const described = await callbacks.describePendingActions(token)
    if (!described) {
      loadFailed.value = true
      return
    }

    loadFailed.value = false
    remaining.value = described.requiredActions
    totpSetup.value = described.totpSetup ?? null
    maskedEmail.value = described.maskedEmail ?? null
  } catch {
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

onMounted(refresh)

async function handleSubmit(): Promise<void> {
  submitError.value = ''
  await validate()

  const token = pendingAction.value?.tempToken
  if (!token) {
    // 令牌没了（刷新过页面）。挑战一次性且 10 分钟有效，拿不到只能重新登录 ——
    // 说清楚，而不是让人对着一个永远提交失败的表单。
    submitError.value = translate(
      'admin.login.pendingActions.expired',
      'This request expired. Please sign in again.',
    )
    return
  }

  submitting.value = true
  try {
    const outcome = await runCurrentAction(token)
    if (!outcome) return

    if (outcome.challenged) {
      // 办完了，但签发会话还要再过一道二次验证；shell 已切到 two-factor 模块。
      // 这枚待办令牌已被消费，留着它只会让「返回」再撞一次必败的表单。
      helpers.clearPendingAction()
      return
    }

    if (outcome.completed) {
      // 全办完了，会话已由回调建立。
      helpers.clearPendingAction()
      return
    }

    // 还欠着别的：同一枚令牌继续用，换到下一件。
    remaining.value = outcome.remainingActions
    model.password = ''
    model.confirmPassword = ''
    model.code = ''
    await refresh()
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}

async function runCurrentAction(tempToken: string) {
  switch (current.value) {
    case 'ChangePassword':
      if (!callbacks.completePasswordChange) return notConfigured()
      return await callbacks.completePasswordChange({ tempToken, newPassword: model.password }, helpers)
    case 'EnrollTotp':
      if (!callbacks.completeTotpEnrollment) return notConfigured()
      return await callbacks.completeTotpEnrollment({ tempToken, code: model.code }, helpers)
    case 'ConfirmEmail':
      if (!callbacks.completeEmailConfirmation) return notConfigured()
      return await callbacks.completeEmailConfirmation({ tempToken, code: model.code }, helpers)
    default:
      return notConfigured()
  }
}

function notConfigured(): null {
  submitError.value = translate('admin.login.notConfigured', 'This flow is not configured')
  return null
}

async function handleSendCode(): Promise<void> {
  const token = pendingAction.value?.tempToken
  if (!token || !callbacks.sendPendingActionEmailCode) return

  sending.value = true
  try {
    await callbacks.sendPendingActionEmailCode(token)
  } catch (err) {
    submitError.value = err instanceof Error ? err.message : String(err)
  } finally {
    sending.value = false
  }
}

/** 放弃并回到登录页。挑战作废，用户可以重新开始。 */
function handleCancel(): void {
  helpers.clearPendingAction()
  toggleLoginModule('pwd-login')
}
</script>

<template>
  <NSpin :show="loading">
    <!--
      读不到待办清单时给一条能行动的说明 + 回登录页，而不是猜一个表单出来。
      令牌一次性且十分钟有效，所以「重新登录」是这里唯一真的能解决问题的动作。
    -->
    <div v-if="loadFailed" class="flex flex-col gap-16px">
      <NAlert type="error" :bordered="false">
        {{
          translate(
            'admin.login.pendingActions.loadFailed',
            'We could not load what is required. Please sign in again.',
          )
        }}
      </NAlert>
      <NButton size="large" block :round="ui.pill" @click="handleCancel">
        {{ translate('admin.login.back', 'Back') }}
      </NButton>
    </div>

    <div v-else class="flex flex-col gap-16px">
      <NAlert type="info" :bordered="false">
        {{
          current === 'ChangePassword'
            ? translate('admin.login.pendingActions.changePassword', 'You must set a new password before continuing.')
            : current === 'EnrollTotp'
              ? translate('admin.login.pendingActions.enrollTotp', 'You must set up an authenticator app before continuing.')
              : translate('admin.login.pendingActions.confirmEmail', 'You must confirm your email address before continuing.')
        }}
      </NAlert>

      <div v-if="account" class="text-14px op-70">
        {{ translate('admin.login.pendingActions.forAccount', 'Signing in as') }}
        <strong>{{ account }}</strong>
      </div>

      <div v-if="remaining.length > 1" class="text-13px op-60">
        {{ translate('admin.login.pendingActions.stepsLeft', 'Steps remaining') }}: {{ remaining.length }}
      </div>

      <NForm v-if="current" ref="formRef" :model="model" :rules="rules" :show-label="ui.labeled" size="large">
        <template v-if="current === 'ChangePassword'">
          <NFormItem
            path="password"
            :label="ui.labeled ? translate('admin.login.newPassword', 'New password') : undefined"
          >
            <NInput
              v-model:value="model.password"
              type="password"
              show-password-on="click"
              :round="ui.pill"
              :placeholder="translate('admin.login.newPassword', 'New password')"
            />
          </NFormItem>
          <NFormItem
            path="confirmPassword"
            :label="ui.labeled ? translate('admin.login.confirmPassword', 'Confirm password') : undefined"
          >
            <NInput
              v-model:value="model.confirmPassword"
              type="password"
              show-password-on="click"
              :round="ui.pill"
              :placeholder="translate('admin.login.confirmPassword', 'Confirm password')"
              @keyup.enter="handleSubmit"
            />
          </NFormItem>
        </template>

        <template v-else>
          <div v-if="current === 'EnrollTotp' && totpSetup" class="mb-16px flex flex-col items-center gap-8px">
            <NQrCode :value="totpSetup.authenticatorUri" :size="160" />
            <div class="text-12px op-60">
              {{ translate('admin.login.pendingActions.totpManualKey', 'Or enter this key manually') }}:
              <code>{{ totpSetup.sharedKey }}</code>
            </div>
          </div>

          <div v-if="current === 'ConfirmEmail'" class="mb-12px flex items-center justify-between gap-8px">
            <span class="text-13px op-70">{{ maskedEmail }}</span>
            <NButton size="small" tertiary :loading="sending" @click="handleSendCode">
              {{ translate('admin.login.pendingActions.sendCode', 'Send code') }}
            </NButton>
          </div>

          <NFormItem
            path="code"
            :label="ui.labeled ? translate('admin.login.code', 'Verification code') : undefined"
          >
            <NInput
              v-model:value="model.code"
              :round="ui.pill"
              :placeholder="translate('admin.login.code', 'Verification code')"
              @keyup.enter="handleSubmit"
            />
          </NFormItem>
        </template>

        <div v-if="submitError" class="mb-12px text-14px c-error">{{ submitError }}</div>

        <NSpace vertical :size="16">
          <NButton type="primary" size="large" block :round="ui.pill" :loading="submitting" @click="handleSubmit">
            {{ translate('admin.login.pendingActions.submit', 'Continue') }}
          </NButton>
          <NButton quaternary size="large" block :round="ui.pill" @click="handleCancel">
            {{ translate('admin.login.back', 'Back') }}
          </NButton>
        </NSpace>
      </NForm>
    </div>
  </NSpin>
</template>
