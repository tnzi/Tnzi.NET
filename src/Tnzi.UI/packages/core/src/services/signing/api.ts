/**
 * Signing Module API - `Tnzi.Signing`.
 *
 * Two factories because the backend deliberately keeps two separate entry points:
 *
 * - {@link useAdminSigningApi} - `/admin/signing/*`, permission-gated operators.
 * - {@link useSigningRecipientApi} - `/signing/{token}`, ANONYMOUS. Signers are
 *   usually not users of the system at all; the one-time token is the whole of
 *   their identity.
 *
 * Merging them would make "who may read this document" depend on a parameter
 * rather than on the route - and one of these routes is open to anyone.
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  CreateEnvelopeDto,
  CreateEnvelopeTemplateDto,
  EnvelopeDto,
  EnvelopeListDto,
  EnvelopeQueryDto,
  EnvelopeTemplateDto,
  EnvelopeTemplateListDto,
  EnvelopeTemplateQueryDto,
  IssuedSigningLink,
  SigningPacketDto,
  SubmitSigningDto,
  UpdateEnvelopeTemplateDto,
} from './types';

// Aligned with the backend controller routes.
const ADMIN_REQUEST_BASE = '/admin/signing/requests';
const ADMIN_TEMPLATE_BASE = '/admin/signing/templates';
const RECIPIENT_BASE = '/signing';

/** Admin signing API - requests and templates. */
export function useAdminSigningApi(client: HttpClient) {
  return {
    // ── Requests ────────────────────────────────────────────────────────────

    /** List signing requests (paged). */
    getRequests: (params?: EnvelopeQueryDto) =>
      client.get<PagedList<EnvelopeListDto>>(ADMIN_REQUEST_BASE, { params }),

    /** Request detail, including per-recipient progress and the sealed output. */
    getRequest: (id: string) =>
      client.get<EnvelopeDto>(`${ADMIN_REQUEST_BASE}/${id}`),

    /** Start a request from a template (lands as a draft, not yet sent). */
    createRequest: (data: CreateEnvelopeDto) =>
      client.post<EnvelopeDto>(ADMIN_REQUEST_BASE, data),

    /**
     * Send: issue the one-time links.
     *
     * ★ The plaintext tokens come back HERE AND ONLY HERE - the store keeps
     * hashes. Dispatch them right away; losing them means re-sending, which
     * invalidates the links already issued.
     */
    sendRequest: (id: string) =>
      client.post<IssuedSigningLink[]>(`${ADMIN_REQUEST_BASE}/${id}/send`),

    /** Void a request that has not completed. */
    voidRequest: (id: string) =>
      client.post<void>(`${ADMIN_REQUEST_BASE}/${id}/void`),

    // ── Templates ───────────────────────────────────────────────────────────

    /** List templates (paged). */
    getTemplates: (params?: EnvelopeTemplateQueryDto) =>
      client.get<PagedList<EnvelopeTemplateListDto>>(ADMIN_TEMPLATE_BASE, { params }),

    /** Template detail, including its placed fields. */
    getTemplate: (id: string) =>
      client.get<EnvelopeTemplateDto>(`${ADMIN_TEMPLATE_BASE}/${id}`),

    createTemplate: (data: CreateEnvelopeTemplateDto) =>
      client.post<EnvelopeTemplateDto>(ADMIN_TEMPLATE_BASE, data),

    /** Update - rebuilds the field set wholesale and bumps the version. */
    updateTemplate: (id: string, data: UpdateEnvelopeTemplateDto) =>
      client.put<EnvelopeTemplateDto>(`${ADMIN_TEMPLATE_BASE}/${id}`, data),

    /** Delete. Returns 409 once any request has referenced it - deactivate instead. */
    deleteTemplate: (id: string) =>
      client.delete<void>(`${ADMIN_TEMPLATE_BASE}/${id}`),
  };
}

/**
 * Recipient-facing signing API - ANONYMOUS, addressed purely by token.
 *
 * The token goes in the path, so it is encoded at every call site.
 */
export function useSigningRecipientApi(client: HttpClient) {
  return {
    /** Open the packet. Sequential signers not yet up are told they are queued. */
    getPacket: (token: string) =>
      client.get<SigningPacketDto>(`${RECIPIENT_BASE}/${encodeURIComponent(token)}`),

    /** Submit this recipient's fields and signature. */
    submit: (token: string, data: SubmitSigningDto) =>
      client.post<SigningPacketDto>(`${RECIPIENT_BASE}/${encodeURIComponent(token)}`, data),

    /** Decline. One refusal voids the whole request. */
    decline: (token: string, reason?: string) =>
      client.post<SigningPacketDto>(
        `${RECIPIENT_BASE}/${encodeURIComponent(token)}/decline`,
        { reason: reason ?? null },
      ),
  };
}
