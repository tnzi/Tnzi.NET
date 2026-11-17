/**
 * External CLI agent runtime API wrappers - `Tnzi.AI.Cli`.
 *
 * The module turns Claude Code and every ACP-speaking coding CLI into a
 * framework-scheduled agent runtime. It is an optional sub-module and is
 * **disabled by default** (`AI:Cli:Enabled=false`); the backend returns 501
 * with `AI_CLI_MODULE_NOT_LOADED` / `AI_CLI_DISABLED` until it is switched on.
 *
 * Backend docs: `docs/modules/ai-cli.md`.
 */

import type { HttpClient } from '../../http/http';
import type { PagedList, PagedQueryDto } from '../../types/pagination';
import type { CliBackgroundTask } from './types';

// ---------------------------------------------------------------------------
// Enums (mirrored from Tnzi.AI.Metadata - PascalCase string wire form)
// ---------------------------------------------------------------------------

/** Status of one external agent run. */
export const CliRunStatus = {
  Queued: 'Queued',
  Dispatched: 'Dispatched',
  Running: 'Running',
  Completed: 'Completed',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
  TimedOut: 'TimedOut',
} as const;
export type CliRunStatus = (typeof CliRunStatus)[keyof typeof CliRunStatus];

/** Terminal statuses - a run in one of these will never emit another event. */
export const CLI_RUN_TERMINAL_STATUSES: readonly CliRunStatus[] = [
  CliRunStatus.Completed,
  CliRunStatus.Failed,
  CliRunStatus.Cancelled,
  CliRunStatus.TimedOut,
];

/** Normalised event type produced by every protocol adapter. */
export const CliAgentEventType = {
  Text: 'Text',
  Thinking: 'Thinking',
  ToolUse: 'ToolUse',
  ToolResult: 'ToolResult',
  Status: 'Status',
  Error: 'Error',
  Log: 'Log',
  /**
   * The run waits for the person attending the turn: to allow an action, to answer the agent's questions, or to
   * approve its plan (`permission.kind`). Answer with `answerPermission`.
   */
  PermissionRequest: 'PermissionRequest',
  /** A request got its outcome (allowed, denied, timed out, ended with the run). */
  PermissionResolved: 'PermissionResolved',
  /**
   * One turn ended inside the run and the CLI goes on (it still has background tasks and starts another turn when
   * they finish). `turn` carries the stored answer's id and the running tasks; events after it are the next answer.
   */
  TurnCompleted: 'TurnCompleted',
  /** The CLI compacted the conversation's context; `compaction` carries the trigger and the token counts. */
  Compacted: 'Compacted',
  /**
   * A CLI task started, made progress or finished: a sub-agent (claude's `Agent` tool) or a command running in the
   * background. `callId` is the call that started it, `task` its kind, description, state and usage; `parentCallId`
   * the sub-agent that dispatched this one, when nested. Stop one with `useChatApi().stopTask`.
   */
  Task: 'Task',
  /**
   * A message the user queued while the turn ran reached the agent (`userMessage`): taken in between two of its steps
   * (`taken`; the answer before it and the message itself are stored in the thread, and events after it answer it), or
   * declined by the CLI (it is held in the queue with the reason).
   */
  UserMessage: 'UserMessage',
} as const;
export type CliAgentEventType = (typeof CliAgentEventType)[keyof typeof CliAgentEventType];

/** Where a permission request stands. */
export const CliPermissionStatus = {
  Pending: 'Pending',
  Allowed: 'Allowed',
  Denied: 'Denied',
  /** Nobody answered within `AI:Cli:Approval:Timeout`; the CLI was told no. */
  TimedOut: 'TimedOut',
  /** The run ended before the answer reached the CLI; the CLI was told no. */
  Cancelled: 'Cancelled',
} as const;
export type CliPermissionStatus = (typeof CliPermissionStatus)[keyof typeof CliPermissionStatus];

/**
 * Kinds of permission options (the ACP vocabulary). Only the two `allow_*` kinds allow; any other
 * kind, including one the client does not know, is a refusal.
 */
export const CliPermissionOptionKind = {
  AllowOnce: 'allow_once',
  AllowAlways: 'allow_always',
  RejectOnce: 'reject_once',
  RejectAlways: 'reject_always',
} as const;

/**
 * What a `PermissionRequest` waits for (`CliAgentPermission.kind`). All three share the event, the answer
 * endpoint and the timeout / cancel / restart rules.
 */
export const CliInteractionKind = {
  /** An action the permission mode does not allow by itself ("ask me" and "plan" modes). */
  Permission: 'permission',
  /** The agent asks the user questions with options (`questions`). Answer `submit` with `answers`, or `decline`. */
  Question: 'question',
  /** The agent hands over its plan (`plan`) and asks to start. Answer `approve` / `approve_accept_edits` / `reject`. */
  Plan: 'plan',
} as const;
export type CliInteractionKind = (typeof CliInteractionKind)[keyof typeof CliInteractionKind];

/** Where a plan request's `plan` came from (`CliAgentPermission.planSource`). */
export const CliPlanSource = {
  /** The plan the agent passed to ExitPlanMode (from the CLI's request, or from the agent's tool call when the CLI dropped it). */
  Input: 'input',
  /** No plan was passed: the agent's reply text since plan mode began (sub-agents and thinking left out). */
  AssistantText: 'assistant_text',
  /** No plan and no reply text: the thinking the CLI sent since plan mode began. A draft; say so when showing it. */
  AssistantThinking: 'assistant_thinking',
} as const;
export type CliPlanSource = (typeof CliPlanSource)[keyof typeof CliPlanSource];

/** Option ids the framework offers on question and plan requests (permission requests use the kinds as ids). */
export const CliInteractionOptionId = {
  /** Question: hand over the answers (`answers` required). */
  Submit: 'submit',
  /** Question: do not answer. The agent is told there is no answer, never given empty ones. */
  Decline: 'decline',
  /** Plan: approve and continue in the run's mode (a run started in plan mode asks before edits). */
  Approve: 'approve',
  /** Plan: approve, and stop asking about file edits for this session. Only on runs started in plan mode. */
  ApproveAcceptEdits: 'approve_accept_edits',
  /** Plan: keep planning; the answer's `message` is handed to the agent as feedback. */
  Reject: 'reject',
} as const;

/** One question the agent asks (an item of claude's `AskUserQuestion`). */
export interface CliQuestion {
  /** The question text. Answers are keyed by it. */
  question: string;
  /** A very short label (e.g. "Auth method"), when given. */
  header?: string | null;
  /** The choices. The user may also write an answer of their own. */
  options: { label: string; description?: string | null }[];
  /** True when more than one choice may be picked. */
  multiSelect: boolean;
}

/** One answer a permission request offers. Answer with its `id`. */
export interface CliPermissionOption {
  id: string;
  name: string;
  /** One of `CliPermissionOptionKind`, or a kind the CLI added (treat as a refusal). */
  kind: string;
}

/**
 * A permission request (on `PermissionRequest` events) or its outcome (on `PermissionResolved` events).
 * The tool name, tool call id, input and the CLI's description are on the event itself
 * (`tool` / `callId` / `input` / `content`).
 */
export interface CliAgentPermission {
  /** The id to answer with; the outcome event carries the same id. */
  requestId?: string | null;
  /** What the request waits for; one of `CliInteractionKind` (missing on requests stored before kinds existed = `permission`). */
  kind?: CliInteractionKind | string;
  /** Question requests: the questions, in the agent's order. */
  questions?: CliQuestion[] | null;
  /**
   * Plan requests: the plan (markdown). When the CLI hands over none (no Write tool, so no plan file), the framework
   * fills it from what the agent wrote since plan mode began; `planSource` says which. Null when there was nothing.
   */
  plan?: string | null;
  /** Where `plan` came from: one of `CliPlanSource`; null without a plan. */
  planSource?: CliPlanSource | string | null;
  /** Outcome of an answered question: question text to the answers given. */
  answers?: Record<string, string[]> | null;
  title?: string | null;
  /** Why the CLI asks, when it says. */
  reason?: string | null;
  /** The path that triggered the request (e.g. a file outside the working directory), when there is one. */
  blockedPath?: string | null;
  options: CliPermissionOption[];
  /** After this moment (UTC) an unanswered request is denied. */
  expiresAt?: string | null;
  /** `Pending` on requests; the outcome on outcome events. */
  status: CliPermissionStatus;
  /** Outcome events: the option given to the CLI (null when timed out or ended with the run). */
  optionId?: string | null;
  /** Outcome events: who answered (null when timed out or ended with the run). */
  answeredBy?: string | null;
}

/** Answer to a permission, question or plan request. */
export interface CliPermissionAnswerDto {
  /** One of the request's `options[].id`. */
  optionId: string;
  /**
   * When refusing: a note handed to the agent (e.g. what to do instead; for a plan, feedback on it).
   * Ignored when allowing.
   */
  message?: string | null;
  /**
   * Question requests answered with `submit`: question text to its answers. Every question needs one;
   * a single-choice question exactly one. Usually option labels, or the user's own words.
   */
  answers?: Record<string, string[]> | null;
}

/**
 * Stable failure classification.
 *
 * Decided at the branch that makes the judgement, never reverse-engineered from
 * an error string - so it is safe to localise per value.
 */
export const CliRunFailureReason = {
  Unknown: 'Unknown',
  ExecutableNotFound: 'ExecutableNotFound',
  LaunchFailed: 'LaunchFailed',
  HandshakeTimeout: 'HandshakeTimeout',
  ProviderError: 'ProviderError',
  RateLimited: 'RateLimited',
  QuotaExceeded: 'QuotaExceeded',
  AuthenticationFailed: 'AuthenticationFailed',
  NetworkError: 'NetworkError',
  IdleTimeout: 'IdleTimeout',
  HardTimeout: 'HardTimeout',
  ProcessCrashed: 'ProcessCrashed',
  ResumeRejected: 'ResumeRejected',
  WorkspacePrepareFailed: 'WorkspacePrepareFailed',
  Cancelled: 'Cancelled',
  ToolTimeout: 'ToolTimeout',
  /** The external CLI's own account reached its usage limit. */
  UsageLimit: 'UsageLimit',
  /** The usage limit this deployment set for the user was reached while the run waited in the queue. */
  UserUsageLimit: 'UserUsageLimit',
  /**
   * The run's process could not be reattached after a server restart (the execution host
   * reported it gone, or could not be reached in time). What it had written stays in the
   * thread; the turn is not run again.
   */
  Interrupted: 'Interrupted',
  /**
   * The execution target the run was placed on (for example a user's workspace
   * container) never came online: the deployment refused to start it, or it did
   * not connect within the activation timeout. No process was started.
   */
  TargetUnavailable: 'TargetUnavailable',
  /**
   * The turn carried input the CLI cannot take: an image, sent to a runtime that
   * reported at its handshake that it accepts none. Nothing was sent.
   */
  UnsupportedInput: 'UnsupportedInput',
} as const;
export type CliRunFailureReason =
  (typeof CliRunFailureReason)[keyof typeof CliRunFailureReason];

/** Where a runtime executes. */
export const CliRuntimeMode = {
  InProcess: 'InProcess',
  RemoteDaemon: 'RemoteDaemon',
} as const;
export type CliRuntimeMode = (typeof CliRuntimeMode)[keyof typeof CliRuntimeMode];

/**
 * Availability of a registered runtime.
 *
 * `Offline` is a probe outcome, not a manual state - the admin API rejects an
 * attempt to set it (use `Disabled` to take a runtime out of service).
 */
export const CliRuntimeStatus = {
  Offline: 'Offline',
  Online: 'Online',
  Disabled: 'Disabled',
} as const;
export type CliRuntimeStatus = (typeof CliRuntimeStatus)[keyof typeof CliRuntimeStatus];

/**
 * Working-directory strategy - which is really "how much continuity does this
 * agent get", because coding CLIs archive their sessions per project directory.
 */
export const CliWorkDirectoryMode = {
  /**
   * One directory per conversation thread, created and reclaimed by the framework.
   * The default, and the only framework-managed mode where multi-turn conversation
   * carries context: the CLI can only resume a session if the cwd comes back.
   */
  PerThread: 'PerThread',
  /** A user-supplied absolute path. The framework NEVER deletes it. */
  UserProvided: 'UserProvided',
  /**
   * A fresh directory every run. Deliberately has NO continuity - nothing the agent
   * writes, and no session, survives to the next turn. For work that must start clean
   * each time (batch jobs, evaluations, mutually untrusted tasks).
   */
  PerRun: 'PerRun',
} as const;
export type CliWorkDirectoryMode =
  (typeof CliWorkDirectoryMode)[keyof typeof CliWorkDirectoryMode];

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** One external agent CLI available on one host. */
export interface CliRuntimeDto {
  id: string;
  hostId: string;
  providerKey: string;
  providerDisplayName?: string | null;
  protocol?: string | null;
  name: string;
  executablePath: string;
  /** Detected CLI version. Observation only - never used to branch behaviour. */
  cliVersion?: string | null;
  mode: CliRuntimeMode;
  status: CliRuntimeStatus;
  lastSeenAt?: string | null;
  hostInfoJson?: string | null;
  maxConcurrentRuns: number;
  launchHeader?: string | null;
  /** What the CLI reported about itself at its last handshake; null until it has run (or for stream-json). */
  capabilities?: CliAgentCapabilities | null;
  /** When `capabilities` was reported. */
  capabilitiesReportedAt?: string | null;
  creationTime: string;
}

/**
 * What a CLI reported about itself at its handshake (ACP `initialize` and the
 * session options of `session/new`). Claimed support, not a measurement.
 */
export interface CliAgentCapabilities {
  agentName?: string | null;
  agentVersion?: string | null;
  /** Takes image blocks in a prompt. */
  images: boolean;
  audio: boolean;
  /** Takes embedded resources (file contents) in a prompt. */
  embeddedContext: boolean;
  /** Can resume an earlier session. */
  resumeSession: boolean;
  mcpHttp: boolean;
  mcpSse: boolean;
  /** Values of the `thought_level` session option; empty when the effort cannot be set. */
  reasoningEfforts: string[];
  /** Values of the `model` session option; empty when not reported. */
  models: string[];
}

/** Admin-editable runtime fields. Probed values (path, version) are not among them. */
export interface UpdateCliRuntimeDto {
  name?: string | null;
  status?: CliRuntimeStatus | null;
  maxConcurrentRuns?: number | null;
}

/** Agent → runtime binding. Its existence is what makes an agent run externally. */
export interface CliAgentBindingDto {
  id: string;
  agentId: string;
  cliRuntimeId: string;
  cliRuntimeName?: string | null;
  providerKey?: string | null;
  model?: string | null;
  /** Runtime-native reasoning effort, round-tripped verbatim (never normalised). */
  thinkingLevel?: string | null;
  customArgs?: string[] | null;
  mcpConfigJson?: string | null;
  workDirectoryMode: CliWorkDirectoryMode;
  userWorkDirectory?: string | null;
  injectAgentInstructions: boolean;
  materializeSkills: boolean;
  /** Per-agent idle watchdog override. Tightening only - never loosening. */
  idleWatchdog?: string | null;
}

/** Create or update an Agent → runtime binding. */
export interface UpsertCliAgentBindingDto {
  cliRuntimeId: string;
  model?: string | null;
  thinkingLevel?: string | null;
  customArgs?: string[] | null;
  mcpConfigJson?: string | null;
  workDirectoryMode: CliWorkDirectoryMode;
  userWorkDirectory?: string | null;
  injectAgentInstructions: boolean;
  materializeSkills: boolean;
  idleWatchdog?: string | null;
}

/** Enqueue one external run. */
export interface CliRunRequestDto {
  agentId: string;
  prompt: string;
  threadId?: string | null;
  /**
   * The framework AgentRun this external run belongs to. Same rule as `priority`: kept only for
   * callers holding `ai.cliRun.execute` (or in-process callers); anyone else's value is dropped.
   */
  agentRunId?: string | null;
  /**
   * Queue priority, higher first. Applied only for callers holding `ai.cliRun.execute`
   * (or in-process callers without a signed-in user); anyone else is queued at 0.
   */
  priority?: number;
  /**
   * Volatile per-turn context appended to the prompt tail.
   *
   * Deliberately NOT part of the brief: the brief sits in the provider's cache
   * prefix, so changing it per turn invalidates the whole conversation's cache.
   */
  perTurnContext?: string | null;
  /**
   * Whether someone waits on this turn. In the "ask me" permission mode, attended turns hand permission
   * requests to the user (`PermissionRequest` events); unattended ones (scheduled jobs) deny them on the spot.
   * Default false.
   */
  attended?: boolean | null;
  /** The turn's model; checked against the user's allowed models (`AI_MODEL_NOT_ALLOWED`) unless the caller holds `ai.cliRun.execute`. */
  model?: string | null;
  /**
   * The turn's reasoning effort (runtime-native); checked against the model's efforts (`AI_EFFORT_NOT_ALLOWED`) unless the
   * caller holds `ai.cliRun.execute`. Null = the binding's `thinkingLevel`.
   */
  effort?: string | null;
}

/** One external run record. */
export interface CliRunDto {
  id: string;
  agentId: string;
  cliRuntimeId: string;
  providerKey?: string | null;
  agentRunId?: string | null;
  threadId?: string | null;
  status: CliRunStatus;
  priority: number;
  prompt: string;
  /** Final deliverable - process narration excluded. */
  output?: string | null;
  error?: string | null;
  failureReason?: CliRunFailureReason | null;
  providerSessionId?: string | null;
  workDirectory?: string | null;
  dispatchedAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  durationMs: number;
  /** This turn's usage per model (not the session's running total). */
  usageJson?: string | null;
  /** This turn's cost (USD). */
  estimatedCostUsd?: number | null;
  /** The model the user picked for this turn; null = the binding's model. */
  model?: string | null;
  /** The reasoning effort picked for this turn; null = the binding's `thinkingLevel`. */
  effort?: string | null;
  /** Tokens in the context window when this turn ended (null when the CLI does not report it). */
  contextTokens?: number | null;
  /** The size of that model's context window, when the CLI reports it. */
  contextWindow?: number | null;
  creationTime: string;
}

/** One persisted event of an external run. */
export interface CliRunMessageDto {
  id: string;
  runId: string;
  /** Monotonic per-run sequence. Reconnects resume from it. */
  sequence: number;
  type: CliAgentEventType;
  content?: string | null;
  tool?: string | null;
  callId?: string | null;
  /** The sub-agent this event happened in: the `callId` of the call that dispatched it. Null for the main agent. */
  parentCallId?: string | null;
  inputJson?: string | null;
  output?: string | null;
  status?: string | null;
  level?: string | null;
  /** Permission request / outcome (`PermissionRequest` / `PermissionResolved`). */
  permission?: CliAgentPermission | null;
  /** A turn that ended inside the run (`TurnCompleted`). */
  turn?: CliTurnBoundary | null;
  /** A context compaction (`Compacted`). */
  compaction?: CliCompaction | null;
  /** A CLI task's start, progress or outcome (`Task`). */
  task?: CliTask | null;
  /** A queued message handed to the turn and what became of it (`UserMessage`). */
  userMessage?: CliUserMessage | null;
  creationTime: string;
}

/** Where a CLI task stands (`CliTask.status`, `ToolTaskDetailDto.status`). */
export const CliTaskStatus = {
  Running: 'running',
  Completed: 'completed',
  Failed: 'failed',
  /** Stopped by the agent (`TaskStop`), by the user, or because the run ended before it finished. */
  Stopped: 'stopped',
} as const;
export type CliTaskStatus = (typeof CliTaskStatus)[keyof typeof CliTaskStatus];

/** The kind of a CLI task (`CliTask.kind`, `ToolTaskDetailDto.kind`); another kind comes as the CLI's own word. */
export const CliTaskKind = {
  /** A sub-agent the agent dispatched. */
  Agent: 'agent',
  /** A command, usually one started in the background. */
  Command: 'command',
} as const;
export type CliTaskKind = (typeof CliTaskKind)[keyof typeof CliTaskKind];

/**
 * A CLI task as one `Task` event reports it. Fields an event leaves out are null (a missing `status` means the
 * state did not change); merge the events of one task by "a value wins".
 */
export interface CliTask {
  /** The CLI's task id (what stopping it takes). */
  taskId?: string | null;
  kind?: CliTaskKind | string | null;
  /** A sub-agent's type (claude's `subagent_type`, e.g. `general-purpose`). */
  agentType?: string | null;
  /** The model a sub-agent answers with. */
  model?: string | null;
  /** The short description it was started with. */
  description?: string | null;
  /** The task a sub-agent was given. */
  prompt?: string | null;
  /** Runs in the background (the call that started it returned at once). */
  background?: boolean | null;
  status?: CliTaskStatus | string | null;
  toolUseCount?: number | null;
  totalTokens?: number | null;
  durationMs?: number | null;
  /** What it is doing now ("Reading alpha.txt"). */
  activity?: string | null;
  lastTool?: string | null;
  /** Its report when it finished (a sub-agent's report, a command's exit note). */
  summary?: string | null;
}

/** A turn that ended inside the run while the CLI still had background tasks. */
export interface CliTurnBoundary {
  /** The answer stored in the thread for that turn; absent when nothing was stored. */
  assistantMessageId?: string | null;
  backgroundTasks: CliBackgroundTask[];
}

/** A context compaction. */
export interface CliCompaction {
  /** `manual` (`/compact`) or `auto`; absent when the CLI did not say. */
  trigger?: string | null;
  preTokens?: number | null;
  postTokens?: number | null;
}

/** A live event pushed over SSE. Shape matches `CliRunMessageDto` minus persistence fields. */
export interface CliAgentEvent {
  type: CliAgentEventType;
  content?: string | null;
  tool?: string | null;
  callId?: string | null;
  /** The sub-agent this event happened in: the `callId` of the call that dispatched it. Null for the main agent. */
  parentCallId?: string | null;
  input?: Record<string, unknown> | null;
  output?: string | null;
  status?: string | null;
  level?: string | null;
  sessionId?: string | null;
  /** Permission request / outcome (`PermissionRequest` / `PermissionResolved`). */
  permission?: CliAgentPermission | null;
  /** A turn that ended inside the run (`TurnCompleted`). */
  turn?: CliTurnBoundary | null;
  /** A context compaction (`Compacted`). */
  compaction?: CliCompaction | null;
  /** A CLI task's start, progress or outcome (`Task`). */
  task?: CliTask | null;
  /** A queued message handed to the turn and what became of it (`UserMessage`). */
  userMessage?: CliUserMessage | null;
}

/**
 * A message the thread's queue handed to a running turn, and what became of it (`UserMessage` event). When taken, the
 * answer up to that point is stored as its own message (`assistantMessageId`), the user's message after it
 * (`threadMessageId`), and the rest of the answer as another: the conversation reads the same after a reload.
 */
export interface CliUserMessage {
  /** The queued message (also the uuid it was handed to the CLI with). */
  messageId: string;
  /** Taken in by the agent; false when the CLI declined it (`reason`). */
  taken: boolean;
  text?: string | null;
  reason?: string | null;
  threadMessageId?: string | null;
  assistantMessageId?: string | null;
}

/** Run list query. */
export interface CliRunQueryDto extends PagedQueryDto {
  agentId?: string | null;
  cliRuntimeId?: string | null;
  status?: CliRunStatus | null;
  threadId?: string | null;
  startTime?: string | null;
  endTime?: string | null;
}

/** One provider entry in the catalogue. */
export interface CliProviderOptionDto {
  key: string;
  displayName: string;
  protocol: string;
  defaultExecutable: string;
  launchHeader?: string | null;
  enabled: boolean;
  /**
   * Whether this protocol has an adapter in the running backend version.
   *
   * Present in the catalogue does NOT mean usable - render a disabled option
   * rather than letting an admin pick something that will 501 on first run.
   */
  implemented: boolean;
}

/** Outcome of one PATH probe. */
export interface CliRuntimeProbeResultDto {
  runtimes: CliRuntimeDto[];
  notFound: string[];
}

// ---------------------------------------------------------------------------
// Admin: runtimes (/admin/ai/cli-runtimes)
// ---------------------------------------------------------------------------

/** Admin API for the external CLI runtime registry. */
export function useAdminCliRuntimeApi(client: HttpClient) {
  return {
    /** List registered runtimes */
    getList: () => client.get<CliRuntimeDto[]>('/admin/ai/cli-runtimes'),

    /** Get one runtime */
    getById: (id: string) => client.get<CliRuntimeDto>(`/admin/ai/cli-runtimes/${id}`),

    /** List provider descriptors available in this deployment */
    getProviders: () =>
      client.get<CliProviderOptionDto[]>('/admin/ai/cli-runtimes/providers'),

    /** Probe this host's PATH now and register/update runtimes */
    probe: () =>
      client.post<CliRuntimeProbeResultDto>('/admin/ai/cli-runtimes/probe'),

    /** Update the admin-editable fields */
    update: (id: string, input: UpdateCliRuntimeDto) =>
      client.put<CliRuntimeDto>(`/admin/ai/cli-runtimes/${id}`, input),

    /** Delete a runtime registration (fails while agents are still bound to it) */
    delete: (id: string) => client.delete<void>(`/admin/ai/cli-runtimes/${id}`),
  };
}

// ---------------------------------------------------------------------------
// Admin: bindings (/admin/ai/cli-bindings)
// ---------------------------------------------------------------------------

/** Admin API for Agent → runtime bindings. */
export function useAdminCliBindingApi(client: HttpClient) {
  return {
    /**
     * Get an agent's binding.
     *
     * Returns `data: null` (not 404) when the agent has none - "this agent runs
     * built-in" is a normal answer, so render an unbound state rather than an error.
     */
    getByAgentId: (agentId: string) =>
      client.get<CliAgentBindingDto | null>(`/admin/ai/cli-bindings/${agentId}`),

    /** Create or update a binding */
    upsert: (agentId: string, input: UpsertCliAgentBindingDto) =>
      client.put<CliAgentBindingDto>(`/admin/ai/cli-bindings/${agentId}`, input),

    /** Remove the binding - the agent returns to built-in execution */
    delete: (agentId: string) =>
      client.delete<void>(`/admin/ai/cli-bindings/${agentId}`),
  };
}

// ---------------------------------------------------------------------------
// Admin: runs (/admin/ai/cli-runs)
// ---------------------------------------------------------------------------

/** Admin API for external run records. */
export function useAdminCliRunApi(client: HttpClient) {
  return {
    /** Paged run list */
    getList: (query?: CliRunQueryDto) =>
      client.get<PagedList<CliRunDto>>('/admin/ai/cli-runs', { params: query }),

    /** Get one run */
    getById: (id: string) => client.get<CliRunDto>(`/admin/ai/cli-runs/${id}`),

    /** Replay persisted events (detail page) */
    getMessages: (id: string, fromSequence = 0) =>
      client.get<CliRunMessageDto[]>(`/admin/ai/cli-runs/${id}/messages`, {
        params: { fromSequence },
      }),

    /** Cancel a run - a running one has its whole process tree terminated */
    cancel: (id: string) => client.post<void>(`/admin/ai/cli-runs/${id}/cancel`),

    /** Answer a permission request on behalf of whoever started the run (`ai.cliRun.execute`). */
    answerPermission: (id: string, requestId: string, answer: CliPermissionAnswerDto) =>
      client.post<void>(
        `/admin/ai/cli-runs/${encodeURIComponent(id)}/permissions/${encodeURIComponent(requestId)}`,
        answer,
      ),

    /**
     * SSE stream URL, resolved against the client's baseUrl so it can be
     * handed to `streamCliRun` (which fetches it verbatim). Pass the last
     * received sequence to resume precisely.
     */
    streamUrl: (id: string, fromSequence = 0) =>
      client.resolveUrl(`/admin/ai/cli-runs/${id}/stream`, { fromSequence }),
  };
}

// ---------------------------------------------------------------------------
// User-facing: runs (/ai/cli-runs)
// ---------------------------------------------------------------------------

/** User-facing API to dispatch a task to an externally-bound agent. */
export function useCliRunApi(client: HttpClient) {
  return {
    /** Enqueue a run - returns the runId immediately, execution is in the background */
    enqueue: (input: CliRunRequestDto) => client.post<string>('/ai/cli-runs', input),

    /** Get one of your own runs */
    getById: (id: string) => client.get<CliRunDto>(`/ai/cli-runs/${id}`),

    /** Cancel one of your own runs */
    cancel: (id: string) => client.post<void>(`/ai/cli-runs/${id}/cancel`),

    /** Answer a permission request of one of your own runs: pick one of the request's options. */
    answerPermission: (id: string, requestId: string, answer: CliPermissionAnswerDto) =>
      client.post<void>(`/ai/cli-runs/${encodeURIComponent(id)}/permissions/${encodeURIComponent(requestId)}`, answer),

    /** SSE stream URL, resolved against the client's baseUrl (see the admin twin). */
    streamUrl: (id: string, fromSequence = 0) =>
      client.resolveUrl(`/ai/cli-runs/${id}/stream`, { fromSequence }),
  };
}
