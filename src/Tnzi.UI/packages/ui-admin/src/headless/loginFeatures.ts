/**
 * Login feature derivation + module reachability.
 *
 * Extracted from `pages/login/index.vue` so the mapping/merge logic is unit
 * testable and the "is this module reachable" rule is shared between the route
 * guard (`activeModule`) and the in-form entry buttons (`PwdLogin`).
 */
import type { AuthConfigDto } from '@tnzi/core/services/identity'
import {
  type LoginCallbacks,
  type LoginFeatures,
  type LoginModule,
  type PartialLoginFeatures,
} from '../pages/login/useLoginContext'

/** Map the backend `GET /auth/config` payload to login feature flags. */
export function mapAuthConfig(c: AuthConfigDto): LoginFeatures {
  return {
    passwordLogin: c.allowUserNameLogin || c.allowEmailLogin || c.allowSmsLogin,
    codeLogin: c.enableCodeLogin,
    register: c.enableRegistration,
    passwordRecovery: c.enablePasswordRecovery,
    identifiers: {
      userName: c.allowUserNameLogin,
      email: c.allowEmailLogin,
      phone: c.allowSmsLogin,
    },
    codeChannels: { sms: c.codeLoginViaSms, email: c.codeLoginViaEmail },
    captchaOnLogin: c.enableCaptchaOnLogin,
    captchaOnRegister: c.enableCaptchaOnRegister,
  }
}

/**
 * Merge consumer overrides on top of base features (consumer wins; nested
 * groups shallow-merged). Always allocates fresh objects, so a frozen/shared
 * `base` (e.g. DEFAULT_LOGIN_FEATURES) is never mutated.
 */
export function mergeFeatures(base: LoginFeatures, override?: PartialLoginFeatures): LoginFeatures {
  return {
    passwordLogin: override?.passwordLogin ?? base.passwordLogin,
    codeLogin: override?.codeLogin ?? base.codeLogin,
    register: override?.register ?? base.register,
    passwordRecovery: override?.passwordRecovery ?? base.passwordRecovery,
    identifiers: { ...base.identifiers, ...override?.identifiers },
    codeChannels: { ...base.codeChannels, ...override?.codeChannels },
    captchaOnLogin: override?.captchaOnLogin ?? base.captchaOnLogin,
    captchaOnRegister: override?.captchaOnRegister ?? base.captchaOnRegister,
  }
}

/**
 * Whether a login module is reachable. For the feature-gated secondary modules
 * (code-login / register / reset-pwd) this is **backend-enabled AND the
 * consumer wired the matching callback** - so a deployment that enables a
 * method the consumer never implemented won't show a dead entry that errors on
 * submit. `pwd-login` is the default entry and is gated only by
 * `passwordLogin` (it shows a "not configured" hint if its callback is
 * missing, rather than vanishing). Flow-driven modules (two-factor /
 * bind-wechat) are always reachable.
 */
export function isModuleAvailable(
  module: LoginModule,
  features: LoginFeatures,
  callbacks: LoginCallbacks,
): boolean {
  switch (module) {
    case 'pwd-login':
      return features.passwordLogin
    case 'code-login':
      return features.codeLogin && !!callbacks.codeLogin
    case 'register':
      return features.register && !!callbacks.register
    case 'reset-pwd':
      return features.passwordRecovery && !!callbacks.resetPwd
    default:
      return true
  }
}

/**
 * First reachable module - used to redirect away from a disabled module that
 * was reached by direct URL. Falls back to `pwd-login`.
 */
export function firstReachableModule(features: LoginFeatures, callbacks: LoginCallbacks): LoginModule {
  const order: LoginModule[] = ['pwd-login', 'code-login', 'register', 'reset-pwd']
  for (const m of order) {
    if (isModuleAvailable(m, features, callbacks)) return m
  }
  return 'pwd-login'
}
