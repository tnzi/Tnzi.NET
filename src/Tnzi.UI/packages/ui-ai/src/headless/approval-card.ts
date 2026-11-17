/**
 * An approval card, independent of what it approves: a request the agent waits on (a permission
 * request of an external CLI agent, a high-risk call an application confirms with its user) shown
 * as a title, a few fields, the text or command in question, and the answers, with the time left
 * before no answer counts as a no.
 *
 * Every word on a card comes from its model or the component's props: the model is built by
 * whoever knows what is approved (`run-request-cards.ts` for an external agent's requests, an
 * application's own builder for its own kinds). `TApprovalCard` renders it.
 */
import { CliPermissionOptionKind, type CliPermissionOption } from '@tnzi/core/services/ai';

export type ApprovalTone = 'allow' | 'deny';

/** One answer: its id goes back to whoever asked. The `primary` one is the card's main button. */
export interface ApprovalChoice {
  id: string;
  label: string;
  tone: ApprovalTone;
  primary?: boolean;
  /** Not answerable yet (a question card's submit before every question has an answer). */
  disabled?: boolean;
}

export interface ApprovalField {
  label: string;
  value: string;
  mono?: boolean;
  /** The whole of what `value` sums up, behind a toggle under it. */
  more?: { label: string; items: string[] } | null;
}

/**
 * A code the user reads elsewhere and types on the card (a mailed one-time code): the main answer
 * waits until it has `length` digits, and goes with them.
 */
export interface ApprovalCode {
  length: number;
  label: string;
  /** What to check before typing it. */
  hint?: string | null;
}

export interface ApprovalCardModel {
  /** Unique among the cards on screen. */
  key: string;
  /** Iconify name shown before the title. */
  icon?: string;
  /** What is asked, in a few words ("The agent wants to run a command"). */
  title: string;
  /** One more line: what it is for. */
  summary?: string | null;
  /** What must be read before answering, shown above everything else and set apart. */
  warning?: string | null;
  fields: ApprovalField[];
  /** The text or command at stake, shown as is (scrolls when long). */
  detail?: { text: string; mono?: boolean } | null;
  choices: ApprovalChoice[];
  code?: ApprovalCode | null;
  /** ISO time after which no answer is a no. */
  expiresAt: string;
}

/** The digits of a typed code when there are exactly `length` of them (spaces and dashes aside); otherwise null. */
export function codeDigits(value: string, length: number): string | null {
  const digits = value.replace(/[\s-]/g, '');
  return digits.length === length && /^[0-9]+$/.test(digits) ? digits : null;
}

/** Whole seconds left until `expiresAt` (never below zero); 0 for a time that cannot be read. */
export function secondsLeft(expiresAt: string, nowMs: number): number {
  const end = Date.parse(expiresAt);
  if (Number.isNaN(end)) return 0;
  return Math.max(0, Math.ceil((end - nowMs) / 1000));
}

/** `m:ss`, the way a countdown reads. */
export function formatApprovalCountdown(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
}

/** Whether an option of a permission request allows; any kind it does not know is a refusal. */
export function allowsKind(kind: string): boolean {
  return kind === CliPermissionOptionKind.AllowOnce || kind === CliPermissionOptionKind.AllowAlways;
}

export interface PermissionChoiceLabels {
  allowOnce: string;
  allowAlways: string;
  reject: string;
}

/**
 * A permission request's options as the card's answers: allow once first (the main one), then
 * allow always when offered, then the refusals; each in the page's words, not the CLI's.
 */
export function permissionChoices(options: readonly CliPermissionOption[], labels: PermissionChoiceLabels): ApprovalChoice[] {
  const rank = (kind: string) => (kind === CliPermissionOptionKind.AllowOnce ? 0 : kind === CliPermissionOptionKind.AllowAlways ? 1 : 2);
  return [...options]
    .sort((a, b) => rank(a.kind) - rank(b.kind))
    .map((option): ApprovalChoice => {
      if (option.kind === CliPermissionOptionKind.AllowOnce) return { id: option.id, label: labels.allowOnce, tone: 'allow', primary: true };
      if (option.kind === CliPermissionOptionKind.AllowAlways) return { id: option.id, label: labels.allowAlways, tone: 'allow' };
      return { id: option.id, label: labels.reject, tone: 'deny' };
    })
    // Two refusals (once, always) read the same here: one button is enough.
    .filter((choice, index, all) => choice.tone === 'allow' || all.findIndex((c) => c.tone === 'deny') === index);
}
