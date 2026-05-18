<script setup lang="ts">
/**
 * Login route component — registered at `/login/:module(…)?` (see
 * `router/routes.ts`). Soybean reference:
 * `src/views/_builtin/login/index.vue`.
 *
 * Responsibilities:
 *   - Read `route.params.module` (defaults to `'pwd-login'`).
 *   - Validate against the known module set; fall back to `'pwd-login'` on any
 *     unrecognised value so deep-links can't break the page.
 *   - Inject `useAdminLoginConfig()` (Phase I.7.2+) and forward
 *     callbacks / brand / demoAccounts to `TLoginPage`.
 *   - Wire `toggleLoginModule` to `router.replace({ path: '/login/' + name })`
 *     so URL stays canonical for refreshes and browser back/forward.
 *
 * Consumers configure the page via `defineAdminApp({ login: { … } })`. To
 * fully replace the route component, pass `loginComponent` to `defineAdminApp`.
 */
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import TLoginPage from '../../components/pages/TLoginPage.vue'
import { useAdminLoginConfig } from '../../plugin/loginConfig'
import { type LoginModule } from './useLoginContext'
import PwdLogin from './modules/PwdLogin.vue'
import CodeLogin from './modules/CodeLogin.vue'
import Register from './modules/Register.vue'
import ResetPwd from './modules/ResetPwd.vue'
import BindWechat from './modules/BindWechat.vue'
import TwoFactorChallenge from './modules/TwoFactorChallenge.vue'

defineOptions({ name: 'TnziAdminLoginPage' })

const KNOWN_MODULES: readonly LoginModule[] = [
  'pwd-login',
  'code-login',
  'register',
  'reset-pwd',
  'bind-wechat',
  'two-factor',
] as const

const moduleComponents = {
  'pwd-login': PwdLogin,
  'code-login': CodeLogin,
  register: Register,
  'reset-pwd': ResetPwd,
  'bind-wechat': BindWechat,
  'two-factor': TwoFactorChallenge,
}

const route = useRoute()
const router = useRouter()
const config = useAdminLoginConfig()

const activeModule = computed<LoginModule>(() => {
  const raw = route.params.module
  const value = Array.isArray(raw) ? raw[0] : raw
  if (typeof value === 'string' && (KNOWN_MODULES as readonly string[]).includes(value)) {
    return value as LoginModule
  }
  return 'pwd-login'
})

function toggleLoginModule(name: LoginModule): void {
  // `replace` (not `push`) so each module switch doesn't pollute history —
  // matches soybean's `useRouterPush().toggleLoginModule` behaviour.
  router.replace({ path: `/login/${name}` })
}
</script>

<template>
  <TLoginPage
    :module="activeModule"
    :module-components="moduleComponents"
    :brand="config.brand ?? 'Tnzi Admin'"
    :brand-icon="config.brandIcon"
    :brand-icon-size="config.brandIconSize"
    :translate="config.translate"
    :callbacks="config.callbacks ?? {}"
    :demo-accounts="config.demoAccounts ?? []"
    :on-toggle-module="toggleLoginModule"
  />
</template>
