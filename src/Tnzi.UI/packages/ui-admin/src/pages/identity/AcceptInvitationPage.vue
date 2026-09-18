<template>
  <div class="t-invite">
    <div class="t-invite__card t-surface-card">
      <NSpin :show="loading">
        <!-- The link cannot be used: expired, revoked, already accepted, malformed, or
             absent. One sentence for all of them - telling them apart tells a prober
             which tokens are real. -->
        <div v-if="unavailable" class="t-invite__state">
          <TSvgIcon icon="mdi:link-variant-off" :size="40" class="t-invite__state-icon" />
          <p class="t-invite__state-title">{{ t('unavailable.title') }}</p>
          <p class="t-invite__state-hint">{{ t('unavailable.hint') }}</p>
          <NButton quaternary size="small" @click="goToLogin">{{ t('goToLogin') }}</NButton>
        </div>

        <!-- Activated, but this deployment does not sign the invitee in (or the host owns
             the session): send them to the login form with the account name in view. -->
        <div v-else-if="completed" class="t-invite__state">
          <TSvgIcon icon="mdi:account-check-outline" :size="40" class="t-invite__state-icon t-invite__state-icon--ok" />
          <p class="t-invite__state-title">{{ t('done.title') }}</p>
          <p class="t-invite__state-hint">{{ t('done.hint', { userName: preview!.userName }) }}</p>
          <NButton type="primary" @click="goToLogin">{{ t('goToLogin') }}</NButton>
        </div>

        <!-- The app's acceptance handler wants more before it activates the account. The
             token was NOT consumed: the same link works again once the steps are done. -->
        <div v-else-if="remainingSteps.length" class="t-invite__state">
          <TSvgIcon icon="mdi:progress-check" :size="40" class="t-invite__state-icon" />
          <p class="t-invite__state-title">{{ t('remaining.title') }}</p>
          <ul class="t-invite__steps">
            <li v-for="step in remainingSteps" :key="step">{{ stepLabel(step) }}</li>
          </ul>
          <p class="t-invite__state-hint">{{ t('remaining.hint') }}</p>
        </div>

        <div v-else-if="preview" class="t-invite__body">
          <TSvgIcon icon="mdi:email-open-outline" :size="44" class="t-invite__glyph" />
          <p class="t-invite__title">{{ t('title') }}</p>
          <p class="t-invite__account">{{ preview.userName }}</p>
          <p v-if="contact" class="t-invite__contact">{{ contact }}</p>
          <p class="t-invite__expires">{{ t('expires', { when: formatDateTime(preview.expiresAt) }) }}</p>

          <NForm
            ref="formRef"
            class="t-invite__form"
            :model="model"
            :rules="rules"
            size="large"
            label-placement="top"
            :show-require-mark="false"
            @keyup.enter="submit"
          >
            <NFormItem path="password" :label="t('password')">
              <NInput
                v-model:value="model.password"
                type="password"
                show-password-on="click"
                :placeholder="t('passwordPlaceholder')"
              />
            </NFormItem>
            <NFormItem path="confirmPassword" :label="t('confirmPassword')">
              <NInput
                v-model:value="model.confirmPassword"
                type="password"
                show-password-on="click"
                :placeholder="t('confirmPasswordPlaceholder')"
              />
            </NFormItem>
          </NForm>

          <NButton type="primary" block :loading="acting" @click="submit">
            <template #icon><TSvgIcon icon="mdi:account-check-outline" :size="18" /></template>
            {{ t('submit') }}
          </NButton>
          <p v-if="error" class="t-invite__error" role="alert">{{ error }}</p>
        </div>
      </NSpin>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * The invitee's half of invitation onboarding: the page the invitation email links to.
 *
 * Same shape as `pages/share/SharePage.vue` and `pages/notification/UnsubscribePage.vue`:
 * `meta.requiresAuth = false`, a centred card, no admin shell. The person opening this
 * link has, by definition, no account they can sign in with yet - every issuance path
 * refuses an `InvitationPending` account until the invitation is accepted - so an
 * acceptance page behind the login form would be unreachable. Until this landed the
 * default invite link (`{System:FrontendUrl}/accept-invitation?token=`) pointed at the
 * 404 route: the admin half (invite / resend / revoke) was complete, the recipient half
 * did not exist.
 *
 * Three things on purpose:
 *  - **Preview first, then act.** `GET invitations/{token}` does not consume the token
 *    and shows a masked contact so the invitee can confirm "this is me" - the link may
 *    have been forwarded.
 *  - **`completed: false` is not an error.** The app's acceptance handler may want more
 *    (the default one wants a password); the token is kept and the same link works
 *    again, so the page lists `remainingSteps` and says so.
 *  - **The issued session is applied here.** With `SignInAfterAccept` (the default) the
 *    backend answers `completed: true` with tokens; handing them to the runtime's auth
 *    manager takes the invitee straight in. Without a runtime (host passed `client`)
 *    or without tokens, the page sends them to the login form instead.
 *
 * ★ The token travels in the query string (`?token=`), as the backend's
 *   `DefaultInvitationUrlGenerator` builds it: mail clients handle query strings most
 *   consistently when forwarding, and the route path stays a plain literal.
 *
 * Override: register a route at the same path (`/accept-invitation`) to replace this
 * page, or set `Identity:Invitation:AcceptUrlTemplate` to point the link elsewhere.
 */
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NForm, NFormItem, NInput, NSpin, type FormRules } from 'naive-ui'
import { TSvgIcon, useFormRules } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core/utils'
import type { InvitationPreviewDto } from '@tnzi/core/services/identity'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminRuntime } from '../../plugin/runtime'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { resetAllFileUrlResolvers } from '../../services/file-url-resolver'
import { useNaiveForm } from '../../headless/useNaiveForm'
import { humanise, makePageTranslator, translatePageKey } from '../_shared/translate'

defineOptions({ name: 'TnziAdminAcceptInvitationPage' })

const route = useRoute()
const router = useRouter()
const client = useAdminClient(false)
const runtime = useAdminRuntime()
const t = makePageTranslator('identity.acceptInvitation')
// The shared password rules speak `admin.login.*`; resolve those absolutely, and
// keep the rule's own fallback when the dictionary has no entry.
const { rules: r } = useFormRules((key: string, fallback?: string) => {
  const hit = translatePageKey('', key)
  return hit && hit !== humanise(key) ? hit : (fallback ?? key)
})
const { formRef, validate } = useNaiveForm()

// Per-call factory: the bridge holds no state, and the client is only present once
// the plugin is installed. The delivery mode travels with it: `accept` issues the
// session, and in cookie mode the refresh token is only the Set-Cookie on that
// response, kept by a cross-origin SPA only when the request carried credentials.
const bridge = () =>
  createIdentityBridge({ client: client!, withCredentials: runtime?.auth.cookieDelivery ?? false })

const token = computed(() => String(route.query.token ?? ''))

const loading = ref(true)
const acting = ref(false)
const completed = ref(false)
const error = ref('')
const remainingSteps = ref<string[]>([])
const preview = ref<InvitationPreviewDto | null>(null)
const model = reactive({ password: '', confirmPassword: '' })

const unavailable = computed(() => !loading.value && !preview.value)

/** Masked email / phone as the backend returns it; nothing when the invite carries neither. */
const contact = computed(() => preview.value?.maskedEmail || preview.value?.maskedPhoneNumber || '')

const rules = computed<FormRules>(() => ({
  password: r.password(),
  confirmPassword: [r.matches(() => model.password, t('passwordMismatch'))],
}))

/** Known step names get a sentence; an app-defined step falls back to its humanised name. */
function stepLabel(step: string): string {
  return t(`steps.${step}`)
}

function goToLogin(): void {
  void router.replace({ name: 'login' })
}

onMounted(async () => {
  if (!client || !token.value) {
    loading.value = false
    return
  }
  try {
    preview.value = await bridge().invitationAcceptance.preview(token.value)
  } finally {
    loading.value = false
  }
})

async function submit(): Promise<void> {
  if (!token.value || acting.value) return
  try {
    await validate()
  } catch {
    return
  }
  error.value = ''
  acting.value = true
  try {
    const result = await bridge().invitationAcceptance.accept({
      token: token.value,
      password: model.password,
    })
    if (!result.completed) {
      remainingSteps.value = result.remainingSteps ?? []
      return
    }
    const issued = result.token
    if (runtime && issued?.accessToken) {
      // The admin store persists the previous identity, and the auth guard
      // skips loading permissions while it holds one: an invitee opening the
      // link in a browser that still carries a prior admin's store (the admin
      // who sent it, testing the link) would land on the dashboard as that
      // admin with every request 403ing. Reset it the way a sign-out does.
      useAdminAuthStore().logout()
      resetAllFileUrlResolvers()
      await runtime.auth.applyTokenSession({
        accessToken: issued.accessToken,
        refreshToken: issued.refreshToken ?? undefined,
        expiresIn: issued.expiresIn ?? undefined,
      })
      // The auth guard loads permissions on the first navigation with a live token.
      void router.replace({ name: 'dashboard' })
      return
    }
    completed.value = true
  } catch (err) {
    // The bridge rejects with the backend's message (HttpError): a weak password,
    // an expired link, a refused activation. Show it rather than a generic line.
    error.value = err instanceof Error && err.message ? err.message : t('failed')
  } finally {
    acting.value = false
  }
}
</script>

<style scoped>
.t-invite {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 24px;
  background: var(--tnzi-layout-bg);
}

.t-invite__card {
  width: 100%;
  max-width: 420px;
  padding: 32px 28px;
  border: var(--tnzi-surface-card-border);
  box-shadow: var(--tnzi-surface-card-shadow);
  border-radius: var(--tnzi-admin-radius-lg, 10px);
  background: var(--tnzi-surface-card-bg);
}

.t-invite__body,
.t-invite__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  text-align: center;
}

.t-invite__form {
  width: 100%;
  text-align: left;
}

.t-invite__glyph,
.t-invite__state-icon {
  color: var(--tnzi-base-text-muted);
}

.t-invite__state-icon--ok {
  color: var(--tnzi-success);
}

.t-invite__title,
.t-invite__state-title {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}

.t-invite__account {
  margin: 0;
  font-size: 15px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}

.t-invite__contact,
.t-invite__expires,
.t-invite__state-hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}

.t-invite__steps {
  margin: 0;
  padding-left: 18px;
  text-align: left;
  font-size: 14px;
  color: var(--tnzi-base-text);
}

.t-invite__error {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-error);
}
</style>
