/**
 * Tool Permission API wrappers — session-rule inspection, evaluation debugging,
 * and persisted-rule CRUD.
 *
 * Mirrors `Tnzi.AI/Controllers/Admin/DefaultPermissionAdminController` (route
 * `admin/permissions`). The permission rule engine resolves tool-call decisions
 * through a 4-scope model (System / Project / User / Session) with
 * allow / ask / deny behaviors.
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// Enums (mirror backend Tnzi.AI.Security)
// ---------------------------------------------------------------------------

/** Permission decision behavior (no explicit values on the C# enum: Allow=0, Ask=1, Deny=2). */
export enum PermissionBehavior {
  Allow = 0,
  Ask = 1,
  Deny = 2,
}

/** Permission rule source scope (conflict resolution weight: Session > User > Project > System). */
export enum ToolPermissionScope {
  System = 0,
  Project = 1,
  User = 2,
  Session = 3,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** A single Session-level permission rule entry. */
export interface PermissionRuleItemDto {
  toolPattern: string;
  toolGroup?: string | null;
  commandPrefix?: string | null;
  serverName?: string | null;
  pathPrefix?: string | null;
  isSubAgentOnly: boolean;
  subAgentName?: string | null;
  isWorkflowOnly: boolean;
  workflowNodeName?: string | null;
  behavior: PermissionBehavior;
  scope: ToolPermissionScope;
  priority: number;
  isDestructiveOnly: boolean;
  reason?: string | null;
}

/** Response listing the current Session-level rules. */
export interface PermissionRulesDto {
  /** Whether the evaluator currently holds any rules. */
  hasRules: boolean;
  /** Current Session-level rules. */
  sessionRules: PermissionRuleItemDto[];
}

/** Evaluation test request — supplies a context and returns the matched decision (debug). */
export interface PermissionEvaluateRequestDto {
  toolName?: string | null;
  toolGroup?: string | null;
  workingDirectory?: string | null;
  candidatePaths?: string[] | null;
  serverName?: string | null;
  isSubAgent?: boolean;
  subAgentName?: string | null;
  isWorkflowRun?: boolean;
  workflowId?: string | null;
  workflowExecutionId?: string | null;
  workflowNodeName?: string | null;
  shellCommand?: string | null;
  isDestructive?: boolean;
  arguments?: Record<string, unknown> | null;
}

/** Evaluation result — the decision that would be taken for the supplied context. */
export interface PermissionEvaluateResultDto {
  toolName: string;
  behavior: PermissionBehavior;
  reason?: string | null;
  scope?: ToolPermissionScope | null;
  matchedRulePattern?: string | null;
  matchedToolGroup?: string | null;
  matchedServerName?: string | null;
  matchedPathPrefix?: string | null;
  matchedSubAgentName?: string | null;
  matchedWorkflowNodeName?: string | null;
}

/** A persisted (database-backed) permission rule. */
export interface PersistedPermissionRuleDto {
  id: string;
  toolPattern?: string | null;
  toolGroup?: string | null;
  commandPrefix?: string | null;
  serverName?: string | null;
  pathPrefix?: string | null;
  behavior: PermissionBehavior;
  scope: ToolPermissionScope;
  priority: number;
  isDestructiveOnly: boolean;
  isSubAgentOnly: boolean;
  reason?: string | null;
  userId?: string | null;
  isEnabled: boolean;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Create payload for a persisted permission rule. */
export interface CreatePersistedPermissionRuleDto {
  /** Tool name pattern (supports the `*` wildcard). */
  toolPattern?: string | null;
  toolGroup?: string | null;
  commandPrefix?: string | null;
  serverName?: string | null;
  pathPrefix?: string | null;
  behavior: PermissionBehavior;
  scope: ToolPermissionScope;
  priority?: number;
  isDestructiveOnly?: boolean;
  isSubAgentOnly?: boolean;
  reason?: string | null;
  userId?: string | null;
  isEnabled?: boolean;
}

// ---------------------------------------------------------------------------
// Admin: Permission API
// Route: /admin/permissions
// ---------------------------------------------------------------------------

/** Admin tool-permission management API */
export function usePermissionAdminApi(client: HttpClient) {
  const base = '/admin/permissions';
  return {
    /** Get the current Session-level rules + whether any rules are loaded */
    getRules: () =>
      client.get<PermissionRulesDto>(`${base}/rules`),

    /** Evaluate a context against the rules and return the resulting decision (debug) */
    evaluate: (data: PermissionEvaluateRequestDto) =>
      client.post<PermissionEvaluateResultDto>(`${base}/rules/evaluate`, data),

    /** Get all persisted (database-backed) permission rules */
    getPersistedRules: () =>
      client.get<PersistedPermissionRuleDto[]>(`${base}/persisted-rules`),

    /** Create a persisted permission rule */
    createPersistedRule: (data: CreatePersistedPermissionRuleDto) =>
      client.post<PersistedPermissionRuleDto>(`${base}/persisted-rules`, data),

    /** Update a persisted permission rule */
    updatePersistedRule: (id: string, data: CreatePersistedPermissionRuleDto) =>
      client.put<PersistedPermissionRuleDto>(`${base}/persisted-rules/${id}`, data),

    /** Delete a persisted permission rule */
    deletePersistedRule: (id: string) =>
      client.delete<void>(`${base}/persisted-rules/${id}`),
  };
}
