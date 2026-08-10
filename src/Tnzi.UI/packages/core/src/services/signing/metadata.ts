/**
 * Signing Module Metadata - mirrors `Tnzi.Signing/Metadata/SigningEnums.cs`.
 *
 * ★ Every enum here is a STRING enum (member name = value). The backend registers
 * a global `JsonStringEnumConverter`, so these arrive on the wire as names. A
 * numeric mirror would make `dto.status === EnvelopeStatus.Sent` silently never
 * match - the same defect that was fixed across eight enums on 2026-07-26.
 */

/** Where a template's page content comes from. */
export enum TemplateSource {
  /** Assembled by the platform; field boxes are captured while it lays out. */
  Composed = 'Composed',
  /** An uploaded PDF or Word file. */
  Uploaded = 'Uploaded',
}

/** What a field collects. */
export enum SigningFieldType {
  Text = 'Text',
  Date = 'Date',
  Number = 'Number',
  Checkbox = 'Checkbox',
  Signature = 'Signature',
  Initials = 'Initials',
}

/** How a field is located on the rendered PDF. */
export enum FieldPlacementMode {
  /** Fixed normalized box (page + x/y/w/h, 0-1, top-left origin). */
  Absolute = 'Absolute',
  /** Located by searching the page text for `anchorText`. */
  Anchor = 'Anchor',
}

/**
 * Lifecycle of one signing request.
 *
 * ★ `Expired` is DERIVED from `expiresAt`, never stored: nothing anyone does turns
 * a request into "expired", time simply passes. The backend computes it on read,
 * so a row stored as `Sent` can legitimately report `Expired` here.
 */
export enum EnvelopeStatus {
  Draft = 'Draft',
  Sent = 'Sent',
  InProgress = 'InProgress',
  Completed = 'Completed',
  Declined = 'Declined',
  Expired = 'Expired',
  Voided = 'Voided',
}

/** Status of one recipient inside a request. */
export enum SigningRecipientStatus {
  /** Not their turn yet (sequential signing). */
  Pending = 'Pending',
  Sent = 'Sent',
  Viewed = 'Viewed',
  Signed = 'Signed',
  Declined = 'Declined',
}

/**
 * Statuses a request can no longer move out of.
 *
 * Drives "can this still be voided / resent" in the UI without hard-coding the
 * list at each call site.
 */
export const TERMINAL_ENVELOPE_STATUSES: readonly EnvelopeStatus[] = [
  EnvelopeStatus.Completed,
  EnvelopeStatus.Declined,
  EnvelopeStatus.Voided,
  EnvelopeStatus.Expired,
];

/** Whether a request has reached an outcome it cannot leave. */
export function isTerminalEnvelopeStatus(status: EnvelopeStatus): boolean {
  return TERMINAL_ENVELOPE_STATUSES.includes(status);
}
