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
 * Two-factor authentication type.
 *
 * String enum (member name = value): the backend registers a global
 * `JsonStringEnumConverter`, so `TwoFactorChallengeDto.supportedTypes` and
 * friends arrive as PascalCase names. Inbound params accept the name too
 * (the converter allows integers as well, so sending the name is safe).
 * Backend: `Tnzi.Identity/Entities/TwoFactorCode.cs`.
 */
export enum TwoFactorType {
  Sms = 'Sms',
  Email = 'Email',
  /** TOTP (authenticator app). Configured/verified via the dedicated
   *  totp/setup + totp/enable endpoints, NOT the code-channel enable flow. */
  Totp = 'Totp',
}

/**
 * Login status. String enum - backend `Tnzi.Identity/Entities/LoginLog.cs`
 * serializes it by member name.
 */
export enum LoginStatus {
  Success = 'Success',
  Failed = 'Failed',
}

/**
 * Password strength level. String enum - backend
 * `Tnzi.Identity/Services/Interfaces/IPasswordPolicyService.cs` returns it on
 * `PasswordStrengthResultDto.level`, serialized by member name.
 */
export enum PasswordStrengthLevel {
  VeryWeak = 'VeryWeak',
  Weak = 'Weak',
  Fair = 'Fair',
  Strong = 'Strong',
  VeryStrong = 'VeryStrong',
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
 * Abnormal login type. String enum - backend
 * `Tnzi.Identity/Services/Interfaces/ILoginSecurityService.cs` returns these on
 * `AbnormalLoginResultDto.abnormalTypes`, serialized by member name.
 */
export enum AbnormalLoginType {
  NewDevice = 'NewDevice',
  NewIpAddress = 'NewIpAddress',
  LocationChange = 'LocationChange',
  ImpossibleTravel = 'ImpossibleTravel',
  FrequentAttempts = 'FrequentAttempts',
  UnusualTime = 'UnusualTime',
}

/**
 * Abnormal login recommended action. String enum - backend
 * `ILoginSecurityService.cs` returns it on `AbnormalLoginResultDto.recommendedAction`.
 */
export enum AbnormalLoginAction {
  None = 'None',
  Notify = 'Notify',
  RequireVerification = 'RequireVerification',
  /** Block the login outright. Was missing from the mirror. */
  Block = 'Block',
}
