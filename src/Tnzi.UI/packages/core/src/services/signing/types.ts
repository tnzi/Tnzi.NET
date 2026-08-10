/**
 * Signing Module Types - mirrors `Tnzi.Signing/Dtos/`.
 *
 * Two audiences live in this file and they are deliberately NOT merged:
 * the admin side (`Envelope*`, `EnvelopeTemplate*`) speaks to authenticated
 * operators, while the recipient side (`SigningPacketDto`, `SubmitSigningDto`)
 * is served anonymously against a single-use token and therefore carries no
 * ids, no other recipients' emails and no host record reference.
 */

import type { PagedQueryDto } from '../../types/pagination';
import type {
  EnvelopeStatus,
  FieldPlacementMode,
  SigningFieldType,
  SigningRecipientStatus,
  TemplateSource,
} from './metadata';

// ── Templates ───────────────────────────────────────────────────────────────

/** Template list row (no fields - the list answers "which templates exist"). */
export interface EnvelopeTemplateListDto {
  id: string;
  name: string;
  category: string;
  source: TemplateSource;
  pageCount: number;
  fieldCount: number;
  requiresWetSignature: boolean;
  isActive: boolean;
  version: number;
  creationTime: string;
}

/** Template detail (with fields). */
export interface EnvelopeTemplateDto extends EnvelopeTemplateListDto {
  /** Comma-separated host types this template may be used for; empty = any. */
  hostEntityTypes?: string | null;
  bodyTemplate: string;
  sourceFileId?: string | null;
  sourceFileName?: string | null;
  renderedPdfFileId?: string | null;
  fields: TemplateFieldDto[];
}

/**
 * A field on the template design surface.
 *
 * Distinct from {@link RecipientFieldDto}: that one says "what the signer sees",
 * this one says "where it lands on the page, what it binds to, who fills it".
 *
 * Coordinates are normalized 0-1 with a top-left origin (same convention as
 * `Tnzi.Documents`).
 */
export interface TemplateFieldDto {
  id: string;
  key: string;
  label: string;
  type: SigningFieldType;
  /** Which role fills it; null = pre-filled by the sender. */
  recipientRole?: string | null;
  /** Merge-variable key this field binds to. */
  binding?: string | null;
  required: boolean;
  placementMode: FieldPlacementMode;
  anchorText?: string | null;
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
  fontSize?: number | null;
  sortOrder: number;
}

/** Field input (create/update). */
export interface TemplateFieldInputDto {
  key: string;
  label?: string | null;
  type: SigningFieldType;
  recipientRole?: string | null;
  binding?: string | null;
  required: boolean;
  placementMode: FieldPlacementMode;
  anchorText?: string | null;
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
  fontSize?: number | null;
  sortOrder: number;
}

export interface CreateEnvelopeTemplateDto {
  name: string;
  category?: string | null;
  source: TemplateSource;
  hostEntityTypes?: string | null;
  /** Body with `{{variable}}` placeholders (used by `Composed` templates). */
  bodyTemplate?: string | null;
  sourceFileId?: string | null;
  sourceFileName?: string | null;
  renderedPdfFileId?: string | null;
  pageCount: number;
  requiresWetSignature: boolean;
  isActive: boolean;
  fields: TemplateFieldInputDto[];
}

/** Update rebuilds the field set wholesale and bumps the version. */
export type UpdateEnvelopeTemplateDto = CreateEnvelopeTemplateDto;

export interface EnvelopeTemplateQueryDto extends PagedQueryDto {
  keyword?: string;
  category?: string;
  source?: TemplateSource;
  /** Matches templates allowing this host type, plus the unrestricted ones. */
  hostEntityType?: string;
  isActive?: boolean;
}

// ── Requests ────────────────────────────────────────────────────────────────

/** Request list row (progress only, no per-recipient detail). */
export interface EnvelopeListDto {
  id: string;
  title: string;
  hostEntityType?: string | null;
  hostEntityId?: string | null;
  templateId?: string | null;
  /** Computed from `expiresAt`; may report `Expired` while the row says `Sent`. */
  status: EnvelopeStatus;
  isSequential: boolean;
  expiresAt: string;
  completedAt?: string | null;
  creationTime: string;
  recipientCount: number;
  signedCount: number;
  finalPdfFileId?: string | null;
}

export interface EnvelopeQueryDto extends PagedQueryDto {
  keyword?: string;
  /** `Expired` uses the same derivation as the list, so the two always agree. */
  status?: EnvelopeStatus;
  hostEntityType?: string;
  hostEntityId?: string;
  templateId?: string;
}

/** Request detail (admin view). */
export interface EnvelopeDto {
  id: string;
  title: string;
  hostEntityType?: string | null;
  hostEntityId?: string | null;
  /** @see EnvelopeListDto.status */
  status: EnvelopeStatus;
  isSequential: boolean;
  expiresAt: string;
  completedAt?: string | null;
  /** Sealed final PDF; null until the request completes. */
  finalPdfFileId?: string | null;
  /** SHA-256 of the sealed PDF (the tamper-evidence anchor). */
  sha256?: string | null;
  /**
   * Completion certificate - a SEPARATE PDF recording who signed when and from
   * where, carrying the hash above. Completed but null = certificate generation
   * failed; the sealed document itself is still valid.
   */
  completionCertificateFileId?: string | null;
  recipients: SignerDto[];
}

export interface SignerDto {
  id: string;
  role: string;
  name: string;
  email?: string | null;
  order: number;
  status: SigningRecipientStatus;
  sentAt?: string | null;
  viewedAt?: string | null;
  signedAt?: string | null;
  declinedAt?: string | null;
  declineReason?: string | null;
}

export interface CreateSignerDto {
  /** Decides which fields this person is asked to fill. */
  role: string;
  name: string;
  email?: string | null;
}

export interface CreateEnvelopeDto {
  templateId: string;
  /** Falls back to the template name when omitted. */
  title?: string | null;
  /** Host type name; omit for a standalone document bound to no record. */
  hostEntityType?: string | null;
  hostEntityId?: string | null;
  isSequential: boolean;
  expiresInDays: number;
  /** Sequential signing follows this order. */
  recipients: CreateSignerDto[];
  /** Sender-prefilled values keyed by field key; bound fields need no entry. */
  prefilledValues?: Record<string, string | null>;
}

/**
 * One freshly issued signing link.
 *
 * ★ `token` is PLAINTEXT and is returned by `send` ONCE - the store only keeps a
 * hash. Whatever receives this must dispatch it immediately; losing it means
 * re-sending, which invalidates the previous link.
 */
export interface IssuedSigningLink {
  recipientId: string;
  name: string;
  email?: string | null;
  token: string;
}

// ── Recipient side (anonymous, token-bearing) ───────────────────────────────

/** What a recipient sees after opening their link. */
export interface SigningPacketDto {
  title: string;
  recipientName: string;
  recipientStatus: SigningRecipientStatus;
  requestStatus: EnvelopeStatus;
  /** False while earlier signers in a sequential request are still pending. */
  isMyTurn: boolean;
  fields: RecipientFieldDto[];
  /** Preview PDF - the working render, or the sealed document once complete. */
  documentFileId?: string | null;
  expiresAt: string;
}

/** A field awaiting this recipient. */
export interface RecipientFieldDto {
  key: string;
  label: string;
  type: SigningFieldType;
  required: boolean;
  /** Prefilled by the sender or carried in by a merge variable. */
  value?: string | null;
}

export interface SubmitSigningDto {
  values?: Record<string, string | null>;
  /** Signature image as a data URL. */
  signatureImage?: string | null;
  /**
   * The consent wording as shown at signing time.
   *
   * A snapshot of the text, not a link to a page that may change - afterwards
   * the question is "what exactly did they agree to".
   */
  consentText?: string | null;
}
