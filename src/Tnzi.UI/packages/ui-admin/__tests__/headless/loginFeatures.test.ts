import { describe, it, expect, vi } from 'vitest'
import type { AuthConfigDto } from '@tnzi/core/services/identity'
import {
  mapAuthConfig,
  mergeFeatures,
  isModuleAvailable,
  firstReachableModule,
} from '../../src/headless/loginFeatures'
import { DEFAULT_LOGIN_FEATURES } from '../../src/pages/login/useLoginContext'

const fullConfig: AuthConfigDto = {
  allowUserNameLogin: true,
  allowEmailLogin: true,
  allowSmsLogin: false,
  useEmailAsUserName: true,
  enableCodeLogin: true,
  codeLoginViaSms: false,
  codeLoginViaEmail: true,
  enableRegistration: false,
  registerViaSms: false,
  registerViaEmail: false,
  enablePasswordRecovery: true,
  recoveryViaEmail: true,
  recoveryViaSms: false,
  enableCaptchaOnLogin: false,
  enableCaptchaOnRegister: false,
  oAuthProviders: [],
}

describe('mapAuthConfig', () => {
  it('maps backend config to features', () => {
    const f = mapAuthConfig(fullConfig)
    expect(f.passwordLogin).toBe(true) // username || email || sms
    expect(f.codeLogin).toBe(true)
    expect(f.register).toBe(false)
    expect(f.passwordRecovery).toBe(true)
    expect(f.identifiers).toEqual({ userName: true, email: true, phone: false })
    expect(f.codeChannels).toEqual({ sms: false, email: true })
  })

  it('passwordLogin is false only when all identifiers are off', () => {
    const off = mapAuthConfig({ ...fullConfig, allowUserNameLogin: false, allowEmailLogin: false, allowSmsLogin: false })
    expect(off.passwordLogin).toBe(false)
  })

  it('maps the captcha flags', () => {
    expect(mapAuthConfig(fullConfig).captchaOnLogin).toBe(false)
    expect(mapAuthConfig(fullConfig).captchaOnRegister).toBe(false)
    const on = mapAuthConfig({ ...fullConfig, enableCaptchaOnLogin: true, enableCaptchaOnRegister: true })
    expect(on.captchaOnLogin).toBe(true)
    expect(on.captchaOnRegister).toBe(true)
  })
})

describe('mergeFeatures', () => {
  it('consumer override wins; nested groups shallow-merge', () => {
    const merged = mergeFeatures(DEFAULT_LOGIN_FEATURES, { register: false, codeChannels: { sms: false } })
    expect(merged.register).toBe(false)
    expect(merged.codeChannels).toEqual({ sms: false, email: true }) // email kept from base
    expect(merged.codeLogin).toBe(true) // untouched
  })

  it('keeps an explicit false (uses ?? not ||)', () => {
    expect(mergeFeatures(DEFAULT_LOGIN_FEATURES, { codeLogin: false }).codeLogin).toBe(false)
  })

  it('does not mutate the (frozen) base', () => {
    mergeFeatures(DEFAULT_LOGIN_FEATURES, { register: false })
    expect(DEFAULT_LOGIN_FEATURES.register).toBe(true)
  })

  it('merges the captcha overrides (default off)', () => {
    expect(DEFAULT_LOGIN_FEATURES.captchaOnLogin).toBe(false)
    expect(DEFAULT_LOGIN_FEATURES.captchaOnRegister).toBe(false)
    expect(mergeFeatures(DEFAULT_LOGIN_FEATURES, { captchaOnLogin: true }).captchaOnLogin).toBe(true)
    expect(mergeFeatures(DEFAULT_LOGIN_FEATURES, { captchaOnRegister: true }).captchaOnRegister).toBe(true)
  })
})

describe('isModuleAvailable', () => {
  it('secondary modules need the feature AND the callback', () => {
    expect(isModuleAvailable('code-login', DEFAULT_LOGIN_FEATURES, {})).toBe(false) // no callback
    expect(isModuleAvailable('code-login', DEFAULT_LOGIN_FEATURES, { codeLogin: vi.fn() })).toBe(true)
    expect(isModuleAvailable('register', { ...DEFAULT_LOGIN_FEATURES, register: false }, { register: vi.fn() })).toBe(false) // feature off
    expect(isModuleAvailable('reset-pwd', DEFAULT_LOGIN_FEATURES, { resetPwd: vi.fn() })).toBe(true)
  })

  it('pwd-login is gated only by passwordLogin', () => {
    expect(isModuleAvailable('pwd-login', DEFAULT_LOGIN_FEATURES, {})).toBe(true)
    expect(isModuleAvailable('pwd-login', { ...DEFAULT_LOGIN_FEATURES, passwordLogin: false }, {})).toBe(false)
  })

  it('flow-driven modules are always reachable', () => {
    expect(isModuleAvailable('two-factor', { ...DEFAULT_LOGIN_FEATURES, passwordLogin: false }, {})).toBe(true)
    expect(isModuleAvailable('bind-wechat', DEFAULT_LOGIN_FEATURES, {})).toBe(true)
  })
})

describe('firstReachableModule', () => {
  it('falls back to code-login when pwd-login is disabled', () => {
    expect(
      firstReachableModule({ ...DEFAULT_LOGIN_FEATURES, passwordLogin: false }, { codeLogin: vi.fn() }),
    ).toBe('code-login')
  })

  it('defaults to pwd-login', () => {
    expect(firstReachableModule(DEFAULT_LOGIN_FEATURES, {})).toBe('pwd-login')
  })
})
