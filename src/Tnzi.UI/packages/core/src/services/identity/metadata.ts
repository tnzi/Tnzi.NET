/**
 * Identity Module Metadata - Enums and constants
 */

/**
 * User gender
 */
export enum Gender {
  Unknown = 0,
  Male = 1,
  Female = 2,
}

/**
 * Get gender display name
 */
export function getGenderLabel(gender: Gender): string {
  switch (gender) {
    case Gender.Male:
      return 'Male';
    case Gender.Female:
      return 'Female';
    default:
      return 'Unknown';
  }
}

/**
 * OAuth provider types
 */
export enum OAuthProvider {
  Google = 'Google',
  Microsoft = 'Microsoft',
  Facebook = 'Facebook',
  Twitter = 'Twitter',
  GitHub = 'GitHub',
}

/**
 * Get OAuth provider display name
 */
export function getOAuthProviderLabel(provider: OAuthProvider): string {
  return provider;
}

/**
 * Two-factor authentication type
 */
export enum TwoFactorType {
  Sms = 1,
  Email = 2,
}

/**
 * Login status
 */
export enum LoginStatus {
  Success = 1,
  Failed = 2,
}

/**
 * Password strength level
 */
export enum PasswordStrengthLevel {
  VeryWeak = 0,
  Weak = 1,
  Fair = 2,
  Strong = 3,
  VeryStrong = 4,
}

/**
 * Get password strength label
 */
export function getPasswordStrengthLabel(level: PasswordStrengthLevel): string {
  switch (level) {
    case PasswordStrengthLevel.VeryWeak:
      return 'Very Weak';
    case PasswordStrengthLevel.Weak:
      return 'Weak';
    case PasswordStrengthLevel.Fair:
      return 'Fair';
    case PasswordStrengthLevel.Strong:
      return 'Strong';
    case PasswordStrengthLevel.VeryStrong:
      return 'Very Strong';
    default:
      return 'Unknown';
  }
}

/**
 * Abnormal login type
 */
export enum AbnormalLoginType {
  NewDevice = 0,
  NewIpAddress = 1,
  LocationChange = 2,
  ImpossibleTravel = 3,
  FrequentAttempts = 4,
  UnusualTime = 5,
}

/**
 * Abnormal login recommended action
 */
export enum AbnormalLoginAction {
  None = 0,
  Notify = 1,
  RequireVerification = 2,
}
