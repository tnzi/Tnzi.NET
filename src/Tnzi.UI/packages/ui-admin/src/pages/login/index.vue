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
import { translatePageKey } from '../_shared/translate'
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

/**
 * Default translate function — drives the login modules through the bundled
 * `admin.login.*` locale entries. The consumer can override by passing
 * `defineAdminApp({ login: { translate } })`; we only fall back to the
 * built-in resolver when no override was supplied so existing custom
 * translate functions keep working.
 */
function defaultTranslate(key: string, fallback?: string): string {
  // `translatePageKey('', absoluteKey)` resolves keys under `admin.*` against
  // the active locale and humanises misses; we prefer the caller-supplied
  // fallback over the humanised form so English copy stays clean when a
  // bundled locale doesn't yet ship the key.
  const hit = translatePageKey('', key)
  // translatePageKey returns the humanised last segment on miss (e.g.
  // `admin.login.userNamePlaceholder` → `User Name Placeholder`); that is
  // worse than the caller-supplied English fallback. Treat any humanised
  // tail as a miss when a fallback is available.
  if (!hit) return fallback ?? key
  // Cheap miss detection: if the lookup just returned the humanised last
  // segment we'd rather use the caller fallback.
  const lastSeg = key.split('.').pop() ?? key
  const humanised = lastSeg.replace(/([a-z])([A-Z])/g, '$1 $2')
  const looksLikeHumanised =
    hit === humanised.charAt(0).toUpperCase() + humanised.slice(1)
  if (looksLikeHumanised && fallback) return fallback
  return hit
}

const activeTranslate = computed(() => config.translate ?? defaultTranslate)
</script>

<template>
  <TLoginPage
    :module="activeModule"
    :module-components="moduleComponents"
    :brand="config.brand ?? 'Tnzi Admin'"
    :brand-icon="config.brandIcon"
    :brand-icon-size="config.brandIconSize"
    :translate="activeTranslate"
    :callbacks="config.callbacks ?? {}"
    :demo-accounts="config.demoAccounts ?? []"
    :on-toggle-module="toggleLoginModule"
  />
</template>
