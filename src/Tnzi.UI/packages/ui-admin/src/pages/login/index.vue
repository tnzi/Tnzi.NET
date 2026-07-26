<script setup lang="ts">
/**
 * Login route component - registered at `/login/:module(…)?` (see
 * `router/routes.ts`). Soybean reference:
 * `src/views/_builtin/login/index.vue`.
 *
 * Responsibilities:
 *   - Resolve the active module from `route.params.module`, falling back to the
 *     first reachable module when an unknown/disabled module is deep-linked.
 *   - Inject `useAdminLoginConfig()` and forward callbacks / brand / etc.
 *   - Fetch `GET /auth/config` (unless `loadConfigFromServer === false`) and
 *     map it to login features, merged with the consumer's `features` override
 *     (consumer wins). On fetch failure the merged defaults (everything on)
 *     keep the page behaving as before this landed.
 *   - Auto-render the backend's enabled OAuth providers as third-party buttons
 *     unless the consumer supplied its own `thirdParty` array (an explicit `[]`
 *     force-hides them).
 *   - Wire `toggleLoginModule` to `router.replace({ name: 'login', params: { module } })`.
 *
 * Consumers configure the page via `defineAdminApp({ login: { … } })`. To
 * fully replace the route component, pass `loginComponent` to `defineAdminApp`.
 */
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthApi } from '@tnzi/core/services/identity'
import TLoginPage from '../../components/pages/TLoginPage.vue'
import { useAdminClient } from '../../plugin/client'
import { useAdminLoginConfig } from '../../plugin/loginConfig'
import { buildOAuthProviders } from '../../headless/oauthProviders'
import { mapAuthConfig, mergeFeatures, isModuleAvailable, firstReachableModule } from '../../headless/loginFeatures'
import { humanise, translatePageKey } from '../_shared/translate'
import {
  DEFAULT_LOGIN_FEATURES,
  type LoginModule,
  type LoginFeatures,
  type LoginThirdPartyProvider,
} from './useLoginContext'
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
// Optional - isolated mounts / tests may have no client; the config fetch and
// OAuth auto-render are simply skipped in that case.
const client = useAdminClient(false)

// ---- Feature flags ----------------------------------------------------------
// Backend `GET /auth/config` mapped to login features, then merged with the
// consumer's `features` override (consumer wins). A reactive object so the
// async fetch updates the modules in place; the everything-on defaults keep
// the page usable until the fetch resolves (or if it fails / opted out).
const features = reactive<LoginFeatures>(mergeFeatures(DEFAULT_LOGIN_FEATURES, config.features))

const activeModule = computed<LoginModule>(() => {
  const raw = route.params.module
  const value = Array.isArray(raw) ? raw[0] : raw
  const requested =
    typeof value === 'string' && (KNOWN_MODULES as readonly string[]).includes(value)
      ? (value as LoginModule)
      : 'pwd-login'
  // Guard feature-gated modules reached by direct URL when the backend disabled
  // them (or the consumer never wired the callback): redirect to the first
  // reachable module so a disabled method can't be used via a deep link.
  if (!isModuleAvailable(requested, features, config.callbacks ?? {})) {
    return firstReachableModule(features, config.callbacks ?? {})
  }
  return requested
})

function toggleLoginModule(name: LoginModule): void {
  // `replace` (not `push`) so each module switch doesn't pollute history -
  // matches soybean's `useRouterPush().toggleLoginModule` behaviour.
  // Navigate by route NAME + module param: a literal `/login/${name}` path
  // breaks whenever the login route is mounted under a basePath prefix
  // (e.g. '/admin/login/...') - names are deployment/prefix-agnostic.
  // Carry the current query along so the `?next=` deep-link (written by the
  // auth guard / session-expired redirect) survives module switches and the
  // post-login redirect still lands on the original page.
  router.replace({ name: 'login', params: { module: name }, query: route.query })
}

/**
 * Default translate function - drives the login modules through the bundled
 * `admin.login.*` locale entries. The consumer can override by passing
 * `defineAdminApp({ login: { translate } })`; we only fall back to the
 * built-in resolver when no override was supplied so existing custom
 * translate functions keep working.
 */
function defaultTranslate(key: string, fallback?: string): string {
  const hit = translatePageKey('', key)
  if (!hit) return fallback ?? key
  if (fallback && hit === humanise(key)) return fallback
  return hit
}

const activeTranslate = computed(() => config.translate ?? defaultTranslate)

// Consumer-supplied third-party providers win; otherwise we auto-render the
// backend's enabled OAuth providers once the config resolves. An explicit `[]`
// counts as "provided" → force-hides third-party (distinguished from omitted).
const autoThirdParty = ref<LoginThirdPartyProvider[]>([])
const hasConsumerThirdParty = computed(() => config.thirdParty !== undefined)
const resolvedThirdParty = computed<LoginThirdPartyProvider[]>(() =>
  hasConsumerThirdParty.value ? config.thirdParty! : autoThirdParty.value,
)

onMounted(async () => {
  if (config.loadConfigFromServer === false || !client) return
  try {
    const res = await useAuthApi(client).getConfig()
    if (!res.succeeded || !res.data) return
    Object.assign(features, mergeFeatures(mapAuthConfig(res.data), config.features))
    if (!hasConsumerThirdParty.value) {
      autoThirdParty.value = buildOAuthProviders(res.data.oAuthProviders ?? [], client)
    }
  } catch {
    // Backend too old / unreachable - keep the merged defaults (everything on).
  }
})
</script>

<template>
  <TLoginPage
    :module="activeModule"
    :module-components="moduleComponents"
    :layout="config.layout ?? 'wave'"
    :brand="config.brand ?? 'Tnzi Admin'"
    :brand-icon="config.brandIcon"
    :brand-icon-size="config.brandIconSize"
    :tagline="config.tagline"
    :tagline-sub="config.taglineSub"
    :copyright="config.copyright"
    :translate="activeTranslate"
    :callbacks="config.callbacks ?? {}"
    :demo-accounts="config.demoAccounts ?? []"
    :third-party="resolvedThirdParty"
    :features="features"
    :aside-component="config.asideComponent"
    :content-component="config.contentComponent"
    :qr-component="config.qrComponent"
    :show-lang-switch="config.showLangSwitch"
    :show-theme-switch="config.showThemeSwitch"
    :on-toggle-module="toggleLoginModule"
  />
</template>
