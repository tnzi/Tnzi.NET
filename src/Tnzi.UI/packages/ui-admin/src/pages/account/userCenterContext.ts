/**
 * Shared state + provide/inject wiring for the User Center shell and its
 * section components.
 *
 * The shell (`UserCenter.vue`) owns the current user's `profile` / `detail`
 * (which the header avatar + name + roles read) and the backend capability
 * probe; each self-loading section (profile / security / sessions / …) injects
 * this context to reach the bridge, the shared profile refs, the reload bus and
 * the field-level config - so a section stays decoupled and a consumer override
 * component can opt into the exact same context if it wants.
 */
import { computed, inject, provide, ref, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type {
  AuthConfigDto,
  OAuthProviderInfoDto,
  UserDto,
  UserDetailDto,
} from '@tnzi/core/services/identity'
import { resolveAvatarUrl } from '../../utils/resolveAvatarUrl'
import type { IdentityBridge } from '../../services/bridges/identity-bridge'
import type { StorageBridge } from '../../services/bridges/storage-bridge'
import { useSafeMessage } from '../_shared/safeMessage'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { createGuardedLoader } from './guardedLoader'
import type { AdminUserCenterConfig, UserCenterProfileField } from '../../plugin/userCenterConfig'

/** Self-service affordances the backend deployment actually supports, derived
 *  from `GET /auth/config`. Drives "follow system config" visibility. */
export interface UserCenterCapabilities {
  /** Show the "change email" affordance (some email channel is enabled). */
  emailChannel: boolean
  /** Show the "change phone" affordance (some SMS channel is enabled). */
  smsChannel: boolean
  /** OAuth providers available for linking a new third-party account. */
  oauthProviders: OAuthProviderInfoDto[]
}

/**
 * Fail-open capability derivation. When the probe fails (`config == null`) we
 * assume the two core-identity change flows are available (they almost always
 * are), but offer NO OAuth link buttons we couldn't actually fulfil.
 */
export function deriveCapabilities(config: AuthConfigDto | null): UserCenterCapabilities {
  if (!config) return { emailChannel: true, smsChannel: true, oauthProviders: [] }
  const emailChannel = !!(
    config.allowEmailLogin ||
    config.codeLoginViaEmail ||
    config.recoveryViaEmail ||
    config.registerViaEmail
  )
  const smsChannel = !!(
    config.allowSmsLogin ||
    config.codeLoginViaSms ||
    config.recoveryViaSms ||
    config.registerViaSms
  )
  return { emailChannel, smsChannel, oauthProviders: config.oAuthProviders ?? [] }
}

export interface UserCenterContext {
  bridge: IdentityBridge
  storage: StorageBridge
  authStore: ReturnType<typeof useAdminAuthStore>
  message: ReturnType<typeof useSafeMessage>
  /** Page translator bound to `account.userCenter`. */
  t: (key: string, params?: Record<string, unknown>) => string
  /** Clears auth + hard-navigates to login (deactivate / delete / revoke-all). */
  logoutAndRedirect: () => void
  /** The resolved consumer config (never null - empty object by default). */
  config: AdminUserCenterConfig

  // ── shared profile state (header + ProfileSection) ──
  profile: Ref<UserDto | null>
  detail: Ref<UserDetailDto | null>
  resolvedAvatarUrl: ComputedRef<string | null>
  loadingProfile: Ref<boolean>
  /** Reload the basic profile (+ fire-and-forget detail). */
  loadProfile: () => Promise<void>
  setProfile: (p: UserDto | null) => void
  setDetail: (d: UserDetailDto | null) => void

  // ── backend-driven capabilities ──
  authConfig: Ref<AuthConfigDto | null>
  capabilities: ComputedRef<UserCenterCapabilities>
  loadAuthConfig: () => Promise<void>

  // ── reload bus (header Refresh → active section re-fetch) ──
  reloadKey: Ref<number>
  /** Bump the reload key AND reload the profile - wired to the header Refresh. */
  reload: () => Promise<void>

  // ── Profile field-level config ──
  isFieldHidden: (field: UserCenterProfileField) => boolean
  isFieldReadonly: (field: UserCenterProfileField) => boolean
}

export interface UserCenterStateDeps {
  bridge: IdentityBridge
  storage: StorageBridge
  authStore: ReturnType<typeof useAdminAuthStore>
  message: ReturnType<typeof useSafeMessage>
  t: (key: string, params?: Record<string, unknown>) => string
  config: AdminUserCenterConfig
  logoutAndRedirect: () => void
}

/**
 * Build the shared User Center state. Called once by the shell; the returned
 * object is provided to the section subtree via {@link provideUserCenterContext}.
 */
export function createUserCenterState(deps: UserCenterStateDeps): UserCenterContext {
  const profile = ref<UserDto | null>(null)
  const detail = ref<UserDetailDto | null>(null)
  const loadingProfile = ref(false)
  const authConfig = ref<AuthConfigDto | null>(null)
  const reloadKey = ref(0)

  // Avatar rendering only needs the (synchronous) preview-URL builder. Reads the
  // (possibly newer) `detail` first - the detail endpoint owns `avatarUrl` -
  // then falls back to the basic `profile`. TAvatar degrades a broken image to
  // the name initial internally.
  const avatarStorage = { getPreviewUrl: deps.storage.files.previewUrl }
  const resolvedAvatarUrl = computed<string | null>(
    () => resolveAvatarUrl(detail.value, avatarStorage) ?? resolveAvatarUrl(profile.value, avatarStorage),
  )

  const loadProfile = createGuardedLoader<UserDto>({
    flag: loadingProfile,
    fetch: () => deps.bridge.me.getProfile(),
    apply: (p) => {
      // A failed backend envelope unwraps to `undefined` - surface it as a real
      // error (caught by the guard) instead of a TypeError + half-rendered header.
      if (!p) throw new Error(deps.t('loadFailed'))
      profile.value = p
      // Detail load is optional - fire-and-forget OUTSIDE the loadingProfile
      // window so a slow/hung detail endpoint can never pin the profile spinner.
      void deps.bridge.me
        .getDetail()
        .then((d) => {
          detail.value = d
        })
        .catch(() => undefined)
    },
    onError: (e) => deps.message.error(e instanceof Error ? e.message : String(e)),
    timeoutMessage: deps.t('loadTimeout'),
  })

  async function loadAuthConfig(): Promise<void> {
    // Fail-open: getAuthConfig already swallows errors → null.
    authConfig.value = await deps.bridge.getAuthConfig()
  }

  const capabilities = computed<UserCenterCapabilities>(() => deriveCapabilities(authConfig.value))

  const hiddenFields = new Set<UserCenterProfileField>(deps.config.profile?.hideFields ?? [])
  const readonlyFields = new Set<UserCenterProfileField>(deps.config.profile?.readonlyFields ?? [])

  async function reload(): Promise<void> {
    reloadKey.value += 1
    await loadProfile()
  }

  return {
    bridge: deps.bridge,
    storage: deps.storage,
    authStore: deps.authStore,
    message: deps.message,
    t: deps.t,
    logoutAndRedirect: deps.logoutAndRedirect,
    config: deps.config,
    profile,
    detail,
    resolvedAvatarUrl,
    loadingProfile,
    loadProfile,
    setProfile: (p) => {
      profile.value = p
    },
    setDetail: (d) => {
      detail.value = d
    },
    authConfig,
    capabilities,
    loadAuthConfig,
    reloadKey,
    reload,
    isFieldHidden: (field) => hiddenFields.has(field),
    isFieldReadonly: (field) => readonlyFields.has(field),
  }
}

const USER_CENTER_CTX_KEY: InjectionKey<UserCenterContext> = Symbol('tnzi-user-center-ctx')

export function provideUserCenterContext(ctx: UserCenterContext): void {
  provide(USER_CENTER_CTX_KEY, ctx)
}

/**
 * Inject the User Center context. Throws when used outside a UserCenter section
 * subtree so a mis-registered custom section fails loudly instead of rendering
 * against `undefined`.
 */
export function useUserCenterContext(): UserCenterContext {
  const ctx = inject(USER_CENTER_CTX_KEY, null)
  if (!ctx) {
    throw new Error(
      'useUserCenterContext() must be called inside a UserCenter section (the built-in sections and consumer overrides run within this provider).',
    )
  }
  return ctx
}
