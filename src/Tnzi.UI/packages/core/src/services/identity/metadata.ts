/**
 * Identity Module Metadata
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
