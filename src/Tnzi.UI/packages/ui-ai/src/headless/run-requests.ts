/**
 * An external agent's requests as pending cards: the framework's source of them (the stream's
 * `permission_request` / `permission_resolved` events, and `GET chat/threads/{id}/permissions` for
 * the ones a page opened later must find), and the default card for each kind, worded from the
 * words given (`AiLocaleMessages.approvals` in `TChatApp`).
 */
import {
  CliInteractionKind,
  CliPlanSource,
  type ChatPermissionRequestDto,
  type ChatPermissionRequestEventData,
  type useChatApi,
} from '@tnzi/core/services/ai';
import { permissionChoices, type ApprovalCardModel, type ApprovalField } from './approval-card';
import type { ApprovalSource, PendingInteraction } from './approval-queue';
import { planChoices, type PlanCardModel, type QuestionCardModel } from './interaction-card';

/** The source id of an external agent's requests. */
export const RUN_REQUEST_SOURCE = 'run';

/** The refusals that mean a request no longer waits: answered elsewhere, timed out, its run over. */
export const RUN_REQUEST_CLOSED_CODES: ReadonlySet<string> = new Set([
  'AI_CLI_PERMISSION_REQUEST_CLOSED',
  'AI_CLI_PERMISSION_REQUEST_NOT_FOUND',
  'AI_CLI_RUN_NOT_FOUND',
  'AI_CHAT_RUN_NOT_FOUND',
]);

export type RunRequestKind = 'permission' | 'question' | 'plan';

/** A pending card of an external agent's request. */
export type RunRequestItem = PendingInteraction<ChatPermissionRequestDto>;

/** Which card a request is: a kind the page does not know (or a question without questions) is a permission. */
export function runRequestKind(request: Pick<ChatPermissionRequestDto, 'kind' | 'questions'>): RunRequestKind {
  if (request.kind === CliInteractionKind.Question && request.questions?.length) return 'question';
  if (request.kind === CliInteractionKind.Plan) return 'plan';
  return 'permission';
}

/** The card key of a request. */
export function runRequestKey(requestId: string): string {
  return `${RUN_REQUEST_SOURCE}:${requestId}`;
}

/** A request as a pending item. */
export function runRequestItem(request: ChatPermissionRequestDto): RunRequestItem {
  return {
    key: runRequestKey(request.requestId),
    kind: runRequestKind(request),
    expiresAt: request.expiresAt ?? new Date(0).toISOString(),
    createdAt: request.createdAt ?? null,
    data: request,
  };
}

/** A `permission_request` event of `runId` as a pending item. */
export function runRequestFromEvent(runId: string, data: ChatPermissionRequestEventData, at: string = new Date().toISOString()): RunRequestItem {
  return runRequestItem({ ...data, runId, createdAt: at });
}

/** What the source needs of `useChatApi(http)`. */
export type RunRequestApi = Pick<ReturnType<typeof useChatApi>, 'getPendingPermissions' | 'answerRunPermission'>;

/** An answer the server refused for another reason than the request being closed. */
export class ApprovalAnswerError extends Error {
  constructor(
    message: string,
    readonly code?: string | null,
  ) {
    super(message);
    this.name = 'ApprovalAnswerError';
  }
}

/** The framework's source of an external agent's requests. */
export function createRunRequestSource(api: RunRequestApi): ApprovalSource {
  return {
    id: RUN_REQUEST_SOURCE,
    async load(threadId) {
      const result = await api.getPendingPermissions(threadId);
      // A conversation the server does not know (yet, or any more) waits for nothing
      if (!result.succeeded) {
        if (result.code === 404) return [];
        throw new ApprovalAnswerError(result.message || 'Could not read the pending requests.', result.errorCode);
      }
      return (result.data ?? []).map(runRequestItem);
    },
    async answer(item, answer) {
      const request = item.data as ChatPermissionRequestDto;
      const result = await api.answerRunPermission(request.runId, request.requestId, {
        optionId: answer.optionId,
        message: answer.message ?? null,
        answers: answer.answers ?? null,
      });
      if (result.succeeded) return 'answered';
      if (result.errorCode && RUN_REQUEST_CLOSED_CODES.has(result.errorCode)) return 'closed';
      throw new ApprovalAnswerError(result.message || 'The answer was not accepted.', result.errorCode);
    },
  };
}

// ---------------------------------------------------------------- default cards

/** The words of the default cards. Placeholders: `{action}`, `{tool}`, `{count}`. */
export interface RunRequestCardLabels {
  permission: {
    /** `{action}` is one of `actions`. */
    title: string;
    allowOnce: string;
    allowAlways: string;
    reject: string;
    reason: string;
    path: string;
    file: string;
    url: string;
    /** By tool name (`Bash`, `Write`, `Edit`, `WebFetch`, ...); `other` takes `{tool}`. */
    actions: Readonly<Record<string, string>> & { other: string };
  };
  question: { title: string; titleMany: string };
  plan: { title: string; approve: string; approveAskEdits: string; approveAcceptEdits: string; reject: string; draft: string };
}

const fill = (template: string, values: Readonly<Record<string, string | number>>) =>
  template.replace(/\{(\w+)\}/g, (match, key: string) => (key in values ? String(values[key]) : match));

/** Tools whose cards have an icon of their own; any other gets a shield. */
const TOOL_ICONS: Readonly<Record<string, string>> = {
  Bash: 'lucide:terminal',
  Write: 'lucide:file-pen',
  Edit: 'lucide:file-pen',
  MultiEdit: 'lucide:file-pen',
  NotebookEdit: 'lucide:file-pen',
  WebFetch: 'lucide:globe',
  WebSearch: 'lucide:search',
};

function inputText(input: unknown, name: string): string | null {
  if (typeof input !== 'object' || input === null) return null;
  const value = (input as Record<string, unknown>)[name];
  return typeof value === 'string' && value ? value : null;
}

/** The heart of a permission request: the command, the file's new text, the query; else the input as JSON. */
export function permissionDetail(request: Pick<ChatPermissionRequestDto, 'input'>): { text: string; mono: boolean } | null {
  const command = inputText(request.input, 'command');
  if (command) return { text: command, mono: true };
  const content = inputText(request.input, 'content') ?? inputText(request.input, 'new_string');
  if (content) return { text: content, mono: true };
  const query = inputText(request.input, 'query') ?? inputText(request.input, 'prompt');
  if (query) return { text: query, mono: false };
  if (request.input == null || Object.keys(request.input).length === 0) return null;
  return { text: JSON.stringify(request.input, null, 2), mono: true };
}

/** The default card of a permission request: what the tool would do, on what, and the command or text at stake. */
export function permissionCardModel(item: RunRequestItem, labels: RunRequestCardLabels): ApprovalCardModel {
  const request = item.data;
  const words = labels.permission;
  const tool = request.tool ?? '';
  const action = tool && tool !== 'other' && words.actions[tool] ? words.actions[tool]! : fill(words.actions.other, { tool: tool || '?' });
  const file = inputText(request.input, 'file_path') ?? inputText(request.input, 'notebook_path');
  const url = inputText(request.input, 'url');
  const fields: ApprovalField[] = [
    ...(file ? [{ label: words.file, value: file, mono: true }] : []),
    ...(url ? [{ label: words.url, value: url, mono: true }] : []),
    ...(request.blockedPath && request.blockedPath !== file ? [{ label: words.path, value: request.blockedPath, mono: true }] : []),
    ...(request.reason ? [{ label: words.reason, value: request.reason }] : []),
  ];
  const detail = permissionDetail(request);
  const summary = request.description ?? request.title ?? null;
  return {
    key: item.key,
    icon: TOOL_ICONS[tool] ?? 'lucide:shield-question',
    title: fill(words.title, { action }),
    // A search's description is often its query word for word: said once, in the box.
    summary: summary && summary.trim() !== detail?.text.trim() ? summary : null,
    fields,
    detail,
    choices: permissionChoices(request.options, { allowOnce: words.allowOnce, allowAlways: words.allowAlways, reject: words.reject }),
    expiresAt: item.expiresAt,
  };
}

/** The default card of the agent's questions. */
export function questionCardModel(item: RunRequestItem, labels: RunRequestCardLabels): QuestionCardModel {
  const questions = item.data.questions ?? [];
  return {
    key: item.key,
    icon: 'lucide:message-circle-question',
    title: fill(questions.length > 1 ? labels.question.titleMany : labels.question.title, { count: questions.length }),
    questions,
    expiresAt: item.expiresAt,
  };
}

/** The default card of the agent's plan; a plan taken from its thinking says it is a draft. */
export function planCardModel(item: RunRequestItem, labels: RunRequestCardLabels): PlanCardModel {
  const request = item.data;
  const words = labels.plan;
  return {
    key: item.key,
    icon: 'lucide:list-checks',
    title: words.title,
    plan: request.plan ?? '',
    note: request.plan?.trim() && request.planSource === CliPlanSource.AssistantThinking ? words.draft : null,
    choices: planChoices(request.options, {
      approve: words.approve,
      approveAskEdits: words.approveAskEdits,
      approveAcceptEdits: words.approveAcceptEdits,
      reject: words.reject,
    }),
    expiresAt: item.expiresAt,
  };
}
