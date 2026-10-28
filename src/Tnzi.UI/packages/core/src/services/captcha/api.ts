/**
 * Captcha API - client config + Altcha challenges.
 * Backend: DefaultCaptchaController [Route("captcha")] (Tnzi.AspNetCore).
 * The `image` provider's challenge lives on the auth API (`useAuthApi().getCaptchaJson`).
 */

import type { HttpClient } from '../../http/http';
import { DEFAULT_REQUEST_TIMEOUT } from '../../http/http';
import { createFailedApiResult, createFailedApiResultFromError } from '../../errors/api-error';
import type { ApiResult } from '../../types/api';
import type { AltchaChallengeDto, CaptchaClientConfigDto } from './types';

const ALTCHA_CHALLENGE_PATH = '/captcha/altcha/challenge';

export function useCaptchaApi(client: HttpClient) {
  return {
    /** Which provider to render and with what - GET /captcha/config */
    getConfig: () => client.get<CaptchaClientConfigDto>('/captcha/config'),

    /**
     * Issue an Altcha challenge bound to a purpose - GET /captcha/altcha/challenge?purpose= (404 unless Altcha is active).
     *
     * On success the endpoint answers the bare challenge document the `<altcha-widget>` expects, not the
     * `ApiResult` envelope, so it is read raw and parsed here. Through `client.get` the document is
     * normalised as an envelope whose `data` is undefined: a successful result with no challenge in it.
     * Failures (404 when Altcha is not the active provider, 400 for an unknown purpose) are envelopes
     * with a real status and arrive as failed results.
     */
    getAltchaChallenge: async (purpose: string): Promise<ApiResult<AltchaChallengeDto>> => {
      const response = await client.download(ALTCHA_CHALLENGE_PATH, {
        params: { purpose },
        timeout: DEFAULT_REQUEST_TIMEOUT,
      });
      if (!response.succeeded || !response.data) {
        return { ...response, data: undefined };
      }

      let document: unknown;
      try {
        document = JSON.parse(await response.data.text());
      } catch (error) {
        return createFailedApiResultFromError<AltchaChallengeDto>(error);
      }

      if (!document || typeof document !== 'object' || typeof (document as { challenge?: unknown }).challenge !== 'string') {
        return createFailedApiResult<AltchaChallengeDto>({
          code: 500,
          message: 'The Altcha challenge endpoint did not return a challenge document.',
        });
      }

      return { ...response, data: document as AltchaChallengeDto };
    },
  };
}
