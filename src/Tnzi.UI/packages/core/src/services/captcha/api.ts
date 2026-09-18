/**
 * Captcha API - client config + Altcha challenges.
 * Backend: DefaultCaptchaController [Route("captcha")] (Tnzi.AspNetCore).
 * The `image` provider's challenge lives on the auth API (`useAuthApi().getCaptchaJson`).
 */

import type { HttpClient } from '../../http/http';
import type { AltchaChallengeDto, CaptchaClientConfigDto } from './types';

export function useCaptchaApi(client: HttpClient) {
  return {
    /** Which provider to render and with what - GET /captcha/config */
    getConfig: () => client.get<CaptchaClientConfigDto>('/captcha/config'),

    /** Issue an Altcha challenge bound to a purpose - GET /captcha/altcha/challenge?purpose= (404 unless Altcha is active) */
    getAltchaChallenge: (purpose: string) =>
      client.get<AltchaChallengeDto>('/captcha/altcha/challenge', { params: { purpose } }),
  };
}
