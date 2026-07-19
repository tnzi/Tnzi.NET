/**
 * Default auth orchestration for `defineAdminApp({ runtime })`.
 *
 * Every admin consumer used to hand-write the SAME login callbacks in its
 * `main.ts`: map the login page's `{ account, type }` to the backend's split
 * `email` / `phoneNumber` / `TwoFactorType` fields, then call the framework's
 * own `authApi` + `AuthStateManager`. That is pure framework glue — both the
 * login-page contract and the backend DTOs are framework-owned, so the
 * consumer had no real choice to make. This module generates the whole set
 * from a wired {@link TnziClient} runtime; the consumer only overrides a
 * callback when it genuinely diverges from the standard flow.
 */

import { TwoFactorType } from '@tnzi/core/services/identity'
import type { TnziClient } from '@tnzi/core/state'
import type { LoginCallbacks } from '../pages/login/useLoginContext'

/**
 * The wired core runtime the framework drives the default auth flow from. This
 * is exactly the object `createTnziClient()` returns — pass it straight to
 * `defineAdminApp({ runtime })`.
 */
export type AdminAuthRuntime = TnziClient

/**
 * Map the login module's auto-detected account type (email / phone) to the
 * backend's split `email` / `phoneNumber` fields + the `TwoFactorType` channel.
 * The login page emits `type: 'email' | 'phone'`; the backend code/recovery
 * DTOs split it into separate email / phoneNumber fields plus a channel
 * discriminator. Both ends are framework contracts — hence framework-owned.
 */
export function codeChannelFields(account: string, type?: 'phone' | 'email') {
  const isEmail = type === 'email'
  return {
    email: isEmail ? account : null,
    phoneNumber: isEmail ? null : account,
    type: isEmail ? TwoFactorType.Email : TwoFactorType.Sms,
  }
}

/**
 * Build the standard login callbacks from a wired runtime. Each callback just
 * calls the framework's own `auth` state manager / `authApi` with the
 * framework's own DTOs — the exact wiring consumers copied by hand. Which of
 * these the login page actually surfaces is decided independently by the
 * backend `GET /auth/config` feature gating, so shipping all five here is
 * safe: unused ones are simply never invoked.
 */
export function buildDefaultLoginCallbacks(runtime: AdminAuthRuntime): LoginCallbacks {
  const { auth, authApi } = runtime
  return {
    // userName accepts username / email / phone — the backend resolves the
    // identifier (AuthService.FindUserByLoginInputAsync).
    pwdLogin: async ({ userName, password }) => {
      await auth.login({ userName, password })
    },
    // Send a verification code for code-login / password-recovery / register.
    sendCode: async ({ account, type, purpose }) => {
      const f = codeChannelFields(account, type)
      const res =
        purpose === 'code-login'
          ? await authApi.sendCodeLoginCode(f)
          : purpose === 'reset-pwd'
            ? await authApi.sendPasswordRecoveryCode(f)
            : await authApi.sendQuickRegisterCode({ email: f.email, phoneNumber: f.phoneNumber })
      if (!res.succeeded) throw new Error(res.message ?? 'Failed to send the verification code')
    },
    // Verification-code login → establish a persisted session from the returned
    // tokens, then run the normal post-login flow (framework-wrapped `after()`).
    codeLogin: async ({ account, code, type }) => {
      const f = codeChannelFields(account, type)
      const res = await authApi.codeLogin({ email: f.email, phoneNumber: f.phoneNumber, code, type: f.type })
      const accessToken = res.data?.accessToken
      if (!res.succeeded || !accessToken) {
        throw new Error(res.message ?? 'Verification code login failed')
      }
      await auth.applyTokenSession({
        accessToken,
        refreshToken: res.data?.refreshToken,
        expiresIn: res.data?.expiresIn,
      })
    },
    // Reset the password via a verification code; the login shell bounces back
    // to the password form on success so the user signs in with the new one.
    resetPwd: async ({ account, code, password, type }) => {
      const f = codeChannelFields(account, type)
      const res = await authApi.resetPasswordByCode({
        email: f.email,
        phoneNumber: f.phoneNumber,
        code,
        newPassword: password,
        type: f.type,
      })
      if (!res.succeeded) throw new Error(res.message ?? 'Password reset failed')
    },
    // Quick register: account + code → passwordless account → set the chosen
    // password. Requires the backend's quick-register flow to be enabled (the
    // register entry only shows when `GET /auth/config` reports it on).
    register: async ({ account, code, password, type }) => {
      const f = codeChannelFields(account, type)
      const qr = await authApi.quickRegister({ email: f.email, phoneNumber: f.phoneNumber, code })
      if (!qr.succeeded || !qr.data) throw new Error(qr.message ?? 'Registration failed')
      if (qr.data.requirePasswordSetup && qr.data.setPasswordToken) {
        const sp = await authApi.setPassword({
          userId: qr.data.userId,
          token: qr.data.setPasswordToken,
          password,
        })
        if (!sp.succeeded) throw new Error(sp.message ?? 'Failed to set the password')
      }
      // The login shell returns to pwd-login on success.
    },
  }
}
