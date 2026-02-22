/**
 * @tnzi/core/components/auth
 *
 * Authentication-related component interfaces.
 */

export type OAuthSocialProvider =
  | 'Google'
  | 'Microsoft'
  | 'Facebook'
  | 'Twitter'
  | 'GitHub';

/**
 * Login form Props
 */
export interface ILoginFormProps {
  /** Whether to show remember me option */
  showRememberMe?: boolean;
  /** Whether to show forgot password link */
  showForgotPassword?: boolean;
  /** Whether to show third-party login */
  showSocialLogin?: boolean;
  /** Third-party login provider list */
  socialProviders?: OAuthSocialProvider[];
  /** Whether loading */
  loading?: boolean;
  /** Whether disabled */
  disabled?: boolean;
  /** Username field label */
  usernameLabel?: string;
  /** Password field label */
  passwordLabel?: string;
  /** Submit button label */
  submitLabel?: string;
  /** Username field placeholder */
  usernamePlaceholder?: string;
  /** Password field placeholder */
  passwordPlaceholder?: string;
  /** Whether to show captcha */
  showCaptcha?: boolean;
  /** Captcha ID (required by backend when captcha is enabled) */
  captchaId?: string;
  /** Captcha image URL */
  captchaUrl?: string;
  /** Refresh captcha callback */
  onRefreshCaptcha?: () => void;
  /** Captcha field label */
  captchaLabel?: string;
  /** Captcha field placeholder */
  captchaPlaceholder?: string;
}

/**
 * Login form Emits
 */
export interface ILoginFormEmits {
  /** Submit login */
  submit: [
    credentials: {
      userName: string;
      password: string;
      rememberMe?: boolean;
      captchaId?: string;
      captchaCode?: string;
    }
  ];
  /** Click forgot password */
  forgotPassword: [];
  /** Click third-party login */
  socialLogin: [provider: OAuthSocialProvider];
}

/**
 * Register form Props
 */
export interface IRegisterFormProps {
  /** Whether to show username field */
  showUsername?: boolean;
  /** Whether to show phone field */
  showPhone?: boolean;
  /** Whether to show third-party login */
  showSocialLogin?: boolean;
  /** Third-party login provider list */
  socialProviders?: OAuthSocialProvider[];
  /** Whether loading */
  loading?: boolean;
  /** Whether disabled */
  disabled?: boolean;
  /** Email field label */
  emailLabel?: string;
  /** Username field label */
  usernameLabel?: string;
  /** Phone field label */
  phoneLabel?: string;
  /** Password field label */
  passwordLabel?: string;
  /** Confirm password field label */
  confirmPasswordLabel?: string;
  /** Submit button label */
  submitLabel?: string;
  /** Whether to show login link */
  showLoginLink?: boolean;
  /** Login link text */
  loginLinkText?: string;
  /** Whether to show captcha */
  showCaptcha?: boolean;
  /** Captcha ID (required by backend when captcha is enabled) */
  captchaId?: string;
  /** Captcha image URL */
  captchaUrl?: string;
  /** Refresh captcha callback */
  onRefreshCaptcha?: () => void;
  /** Captcha field label */
  captchaLabel?: string;
  /** Captcha field placeholder */
  captchaPlaceholder?: string;
}

/**
 * Register form Emits
 */
export interface IRegisterFormEmits {
  /** Submit registration */
  submit: [
    data: {
      email: string;
      password: string;
      userName?: string;
      firstName?: string;
      lastName?: string;
      phoneNumber?: string;
      captchaId?: string;
      captchaCode?: string;
    }
  ];
  /** Click login link */
  login: [];
  /** Click third-party login */
  socialLogin: [provider: OAuthSocialProvider];
}

/**
 * Password reset form Props
 */
export interface IPasswordResetProps {
  /** Whether loading */
  loading?: boolean;
  /** Whether disabled */
  disabled?: boolean;
  /** Email field label */
  emailLabel?: string;
  /** Verification code field label */
  codeLabel?: string;
  /** New password field label */
  passwordLabel?: string;
  /** Confirm password field label */
  confirmPasswordLabel?: string;
  /** Submit button label */
  submitLabel?: string;
  /** Cancel button label */
  cancelLabel?: string;
  /** Send code button label */
  sendCodeLabel?: string;
  /** Countdown seconds for send code button */
  countdownSeconds?: number;
}

/**
 * Password reset form Emits
 */
export interface IPasswordResetEmits {
  /** Submit password reset */
  submit: [data: { email: string; code: string; password: string }];
  /** Click cancel */
  cancel: [];
  /** Send verification code */
  sendCode: [email: string];
}
