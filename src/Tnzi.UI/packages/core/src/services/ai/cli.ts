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
} as const;
export type CliAgentEventType = (typeof CliAgentEventType)[keyof typeof CliAgentEventType];

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
  creationTime: string;
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
  agentRunId?: string | null;
  priority?: number;
  /**
   * Volatile per-turn context appended to the prompt tail.
   *
   * Deliberately NOT part of the brief: the brief sits in the provider's cache
   * prefix, so changing it per turn invalidates the whole conversation's cache.
   */
  perTurnContext?: string | null;
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
  usageJson?: string | null;
  estimatedCostUsd?: number | null;
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
  inputJson?: string | null;
  output?: string | null;
  status?: string | null;
  level?: string | null;
  creationTime: string;
}

/** A live event pushed over SSE. Shape matches `CliRunMessageDto` minus persistence fields. */
export interface CliAgentEvent {
  type: CliAgentEventType;
  content?: string | null;
  tool?: string | null;
  callId?: string | null;
  input?: Record<string, unknown> | null;
  output?: string | null;
  status?: string | null;
  level?: string | null;
  sessionId?: string | null;
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

    /** SSE stream URL. Pass the last received sequence to resume precisely. */
    streamUrl: (id: string, fromSequence = 0) =>
      `/admin/ai/cli-runs/${id}/stream?fromSequence=${fromSequence}`,
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

    /** SSE stream URL */
    streamUrl: (id: string, fromSequence = 0) =>
      `/ai/cli-runs/${id}/stream?fromSequence=${fromSequence}`,
  };
}
