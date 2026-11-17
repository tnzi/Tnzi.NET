/**
 * The two cards an external agent's run brings besides a permission request (the request's
 * `kind`): questions the agent asks, with their options and room for the user's own words, and a
 * plan it hands over to be approved or sent back with feedback. `TQuestionCard` and `TPlanCard`
 * render them; every word comes from the caller.
 */
import { CliInteractionOptionId, type CliPermissionOption, type CliQuestion } from '@tnzi/core/services/ai';
import { allowsKind, type ApprovalChoice } from './approval-card';

/** The longest answer the framework takes (`AI_CLI_QUESTION_ANSWERS_INVALID` beyond it). */
export const ANSWER_MAX_LENGTH = 2000;

export interface QuestionCardModel {
  key: string;
  icon?: string;
  title: string;
  questions: readonly CliQuestion[];
  expiresAt: string;
}

export interface PlanCardModel {
  key: string;
  icon?: string;
  title: string;
  /** The plan, in Markdown. */
  plan: string;
  /** A line above the plan about what it is (a draft taken from the agent's thinking); none when it is the plan itself. */
  note?: string | null;
  /** Approve first (the main one), then the other approvals, then sending it back. */
  choices: ApprovalChoice[];
  expiresAt: string;
}

/**
 * What the user picked on one question: option labels, and whether they write their own answer
 * (the "Other" choice) with its words. On a single-choice question the own answer stands instead
 * of an option; on a multiple-choice one it goes with them.
 */
export interface QuestionPick {
  selected: readonly string[];
  own: boolean;
  text: string;
}

export const EMPTY_PICK: QuestionPick = Object.freeze({ selected: Object.freeze([]) as readonly string[], own: false, text: '' });

/** The pick with `label` chosen: the only one on a single-choice question, toggled on a multiple-choice one. */
export function pickOption(question: CliQuestion, pick: QuestionPick, label: string): QuestionPick {
  if (!question.multiSelect) return { ...pick, selected: [label], own: false };
  const selected = pick.selected.includes(label) ? pick.selected.filter((l) => l !== label) : [...pick.selected, label];
  return { ...pick, selected };
}

/** The pick with the own answer turned on or off (on a single-choice question, instead of the options). */
export function pickOwn(question: CliQuestion, pick: QuestionPick, own: boolean): QuestionPick {
  return question.multiSelect ? { ...pick, own } : { ...pick, own, selected: own ? [] : pick.selected };
}

/** One question's answers as they go to the agent; empty when it has none yet. */
export function answersOf(question: CliQuestion, pick: QuestionPick): string[] {
  const own = pick.own ? pick.text.trim() : '';
  const known = pick.selected.filter((label) => question.options.some((option) => option.label === label));
  return question.multiSelect ? [...known, ...(own ? [own] : [])] : own ? [own] : known.slice(0, 1);
}

/** Whether a question's pick can go: at least one answer (exactly one when single-choice), none too long. */
export function isAnswered(question: CliQuestion, pick: QuestionPick): boolean {
  const answers = answersOf(question, pick);
  if (answers.length === 0 || (!question.multiSelect && answers.length !== 1)) return false;
  return answers.every((answer) => answer.length <= ANSWER_MAX_LENGTH);
}

/**
 * The answer body's `answers` (question text to its answers) when every question has one; null
 * while any is missing.
 */
export function questionAnswers(questions: readonly CliQuestion[], picks: readonly QuestionPick[]): Record<string, string[]> | null {
  const answers: Record<string, string[]> = {};
  for (const [index, question] of questions.entries()) {
    const pick = picks[index] ?? EMPTY_PICK;
    if (!isAnswered(question, pick)) return null;
    answers[question.question] = answersOf(question, pick);
  }
  return answers;
}

export interface PlanChoiceLabels {
  /** Approve, the run going on as it was (an agent that planned on its own). */
  approve: string;
  /** Approve in a run that started planning: each change asks after it. */
  approveAskEdits: string;
  approveAcceptEdits: string;
  reject: string;
}

/**
 * A plan request's options as the card's answers, in the page's words: approve first (the main
 * one), then approve with edits accepted when offered, then send back. Where accepting edits is
 * offered, the run started planning and the plain approval asks before each change: its words say
 * so. An option the page does not know keeps the agent's name, and its kind says whether it allows.
 */
export function planChoices(options: readonly CliPermissionOption[], labels: PlanChoiceLabels): ApprovalChoice[] {
  const asksAfter = options.some((o) => o.id === CliInteractionOptionId.ApproveAcceptEdits);
  const rank = (option: CliPermissionOption) =>
    option.id === CliInteractionOptionId.Approve
      ? 0
      : option.id === CliInteractionOptionId.ApproveAcceptEdits
        ? 1
        : option.id === CliInteractionOptionId.Reject
          ? 3
          : allowsKind(option.kind)
            ? 2
            : 4;
  return [...options]
    .sort((a, b) => rank(a) - rank(b))
    .map((option): ApprovalChoice => {
      if (option.id === CliInteractionOptionId.Approve)
        return { id: option.id, label: asksAfter ? labels.approveAskEdits : labels.approve, tone: 'allow', primary: true };
      if (option.id === CliInteractionOptionId.ApproveAcceptEdits) return { id: option.id, label: labels.approveAcceptEdits, tone: 'allow' };
      if (option.id === CliInteractionOptionId.Reject) return { id: option.id, label: labels.reject, tone: 'deny' };
      return { id: option.id, label: option.name, tone: allowsKind(option.kind) ? 'allow' : 'deny' };
    });
}

/** The question request's option that hands the answers over, and the one that declines. */
export function questionOptionIds(options: readonly CliPermissionOption[]): { submit: string; decline: string } {
  const submit = options.find((o) => o.id === CliInteractionOptionId.Submit) ?? options.find((o) => allowsKind(o.kind));
  const decline = options.find((o) => o.id === CliInteractionOptionId.Decline) ?? options.find((o) => !allowsKind(o.kind));
  return { submit: submit?.id ?? CliInteractionOptionId.Submit, decline: decline?.id ?? CliInteractionOptionId.Decline };
}
