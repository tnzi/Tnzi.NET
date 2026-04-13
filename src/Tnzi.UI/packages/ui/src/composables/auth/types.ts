export interface LoginCredentials {
  username: string
  password: string
  rememberMe?: boolean
  captcha?: string
}

export interface LoginState extends LoginCredentials {
  errors: Partial<Record<keyof LoginCredentials, string>>
}

export interface RegisterCredentials {
  username: string
  email: string
  phone?: string
  password: string
  confirmPassword: string
  agreeTerms: boolean
  captcha?: string
}

export interface RegisterState extends RegisterCredentials {
  errors: Partial<Record<keyof RegisterCredentials, string>>
}

export interface PasswordResetCredentials {
  email: string
  captcha: string
  newPassword: string
  confirmPassword: string
}

export interface PasswordResetState extends PasswordResetCredentials {
  step: 'request' | 'verify' | 'done'
  errors: Partial<Record<keyof PasswordResetCredentials, string>>
  countdown: number
}

export interface LoginProvider {
  login(credentials: LoginCredentials): Promise<{ success: true; user: unknown } | { success: false; error: string }>
  loginWithSocial?(provider: string): Promise<void>
  sendVerificationCode?(email: string): Promise<void>
}

export interface RegisterProvider {
  register(credentials: RegisterCredentials): Promise<{ success: true; user: unknown } | { success: false; error: string }>
  checkUsernameAvailable?(username: string): Promise<boolean>
  sendVerificationCode?(contact: string): Promise<void>
}

export interface PasswordResetProvider {
  requestReset(email: string): Promise<void>
  verifyCode(email: string, code: string): Promise<void>
  resetPassword(email: string, code: string, newPassword: string): Promise<void>
}

export type SocialProvider = 'google' | 'github' | 'wechat' | 'feishu' | 'dingtalk' | string
