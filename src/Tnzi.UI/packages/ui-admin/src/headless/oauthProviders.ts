/**
 * Map backend-enabled OAuth providers (from `GET /auth/config`) to the
 * `LoginThirdPartyProvider` shape the login page renders.
 *
 * Each provider gets a brand icon/color and an `onClick` that navigates the
 * browser to the backend OAuth start endpoint
 * (`GET /auth/oauth/{provider}/login`), which redirects to the provider's
 * consent screen. The login page calls this automatically when the consumer
 * did not supply its own `thirdParty` array.
 */
import { oauthLoginUrl, type OAuthProviderInfoDto } from '@tnzi/core/services/identity'
import type { HttpClient } from '@tnzi/core/http'
import type { LoginThirdPartyProvider } from '../pages/login/useLoginContext'

interface ProviderStyle {
  icon: string
  color: string
}

/** Brand icon + color per known provider key (lowercase). */
const PROVIDER_STYLES: Record<string, ProviderStyle> = {
  github: { icon: 'mdi:github', color: '#181717' },
  google: { icon: 'mdi:google', color: '#DB4437' },
  microsoft: { icon: 'mdi:microsoft', color: '#00A4EF' },
  facebook: { icon: 'mdi:facebook', color: '#1877F2' },
  twitter: { icon: 'mdi:twitter', color: '#1DA1F2' },
}

const FALLBACK_STYLE: ProviderStyle = { icon: 'mdi:login-variant', color: '' }

/**
 * Build login-page third-party buttons from the backend's enabled providers.
 *
 * @param providers Enabled providers from `AuthConfigDto.oauthProviders`.
 * @param client HttpClient (supplies the baseUrl for the redirect target).
 * @param returnUrl Optional post-login return URL.
 */
export function buildOAuthProviders(
  providers: OAuthProviderInfoDto[],
  client: HttpClient,
  returnUrl?: string,
): LoginThirdPartyProvider[] {
  return providers.map((p) => {
    const style = PROVIDER_STYLES[p.provider.toLowerCase()] ?? FALLBACK_STYLE
    return {
      key: p.provider,
      icon: style.icon,
      label: p.displayName,
      color: style.color || undefined,
      onClick: () => {
        window.location.assign(oauthLoginUrl(client, p.provider, returnUrl))
      },
    }
  })
}
