/**
 * Agent Task API wrappers — query persisted tasks created during Agent runs.
 *
 * Mirrors `Tnzi.AI/Controllers/Admin/DefaultTaskAdminController` (route
 * `admin/ai/tasks`). Tasks are produced by the agent's todo / task tooling and
 * persisted per run for inspection.
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// Enums (mirror backend Tnzi.AI.Entities.AgentTaskStatus)
// ---------------------------------------------------------------------------

/** Agent task status. */
export enum AgentTaskStatus {
  Pending = 0,
  InProgress = 1,
  Completed = 2,
  Skipped = 3,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** A persisted Agent task. */
export interface AgentTaskDto {
  id: string;
  /** Associated AgentRun ID. */
  runId: string;
  /** Parent task ID (null for top-level tasks). */
  parentTaskId?: string | null;
  /** Task title / content. */
  title: string;
  status: AgentTaskStatus;
  /** Ordering index within the run. */
  orderIndex: number;
  /** Task result / output. */
  result?: string | null;
  /** Completion timestamp (null while not completed). */
  completedAt?: string | null;
  creationTime: string;
}

// ---------------------------------------------------------------------------
// Admin: Agent Task API
// Route: /admin/ai/tasks
// ---------------------------------------------------------------------------

/** Admin agent-task query API */
export function useAdminTaskApi(client: HttpClient) {
  const base = '/admin/ai/tasks';
  return {
    /** Get the tasks belonging to a specific run */
    getByRunId: (runId: string) =>
      client.get<AgentTaskDto[]>(base, { params: { runId } }),

    /** Get tasks filtered by status */
    getByStatus: (status: AgentTaskStatus) =>
      client.get<AgentTaskDto[]>(`${base}/by-status`, { params: { status } }),
  };
}
