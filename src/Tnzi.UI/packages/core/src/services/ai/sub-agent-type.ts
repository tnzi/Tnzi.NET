/**
 * Sub-Agent Type API wrappers - CRUD over persisted custom sub-agent type
 * definitions.
 *
 * Mirrors `Tnzi.AI/Controllers/Admin/DefaultSubAgentTypeAdminController` (route
 * `admin/ai/sub-agent-types`). Persisting a type makes it available to the
 * sub-agent registry (e.g. for the `spawn_agent` tool).
 */

import type { HttpClient } from '../../http/http';

// ---------------------------------------------------------------------------
// Enums (mirror backend Tnzi.AI.Options.ToolApprovalMode)
// ---------------------------------------------------------------------------

/** Default tool-approval mode for a sub-agent type. */
export enum ToolApprovalMode {
  /** Never require approval (default). */
  NeverRequire = 0,
  /** Always require approval. */
  AlwaysRequire = 1,
  /** Decide per configured allow/deny lists. */
  Specific = 2,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

/** A persisted sub-agent type definition. */
export interface SubAgentTypeDto {
  id: string;
  /** Type name (unique identifier). */
  name: string;
  /** Type description. */
  description: string;
  toolGroups?: string[] | null;
  excludedToolGroups?: string[] | null;
  maxTurns: number;
  instructions?: string | null;
  defaultModel?: string | null;
  defaultApprovalMode?: ToolApprovalMode | null;
  capabilityTags?: string[] | null;
  isEnabled: boolean;
  creationTime: string;
  lastModificationTime?: string | null;
}

/** Create/update payload for a sub-agent type (shared shape). */
export interface SubAgentTypeInputDto {
  /** Type name (unique identifier). */
  name: string;
  /** Type description. */
  description: string;
  toolGroups?: string[] | null;
  excludedToolGroups?: string[] | null;
  /** Maximum turns (backend default 50). */
  maxTurns?: number;
  instructions?: string | null;
  defaultModel?: string | null;
  defaultApprovalMode?: ToolApprovalMode | null;
  capabilityTags?: string[] | null;
  /** Whether the type is enabled (backend default true). */
  isEnabled?: boolean;
}

// ---------------------------------------------------------------------------
// Admin: Sub-Agent Type API
// Route: /admin/ai/sub-agent-types
// ---------------------------------------------------------------------------

/** Admin sub-agent-type management API */
export function useAdminSubAgentTypeApi(client: HttpClient) {
  const base = '/admin/ai/sub-agent-types';
  return {
    /** Get all sub-agent type definitions */
    getAll: () =>
      client.get<SubAgentTypeDto[]>(base),

    /** Create a sub-agent type definition */
    create: (data: SubAgentTypeInputDto) =>
      client.post<SubAgentTypeDto>(base, data),

    /** Update a sub-agent type definition */
    update: (id: string, data: SubAgentTypeInputDto) =>
      client.put<SubAgentTypeDto>(`${base}/${id}`, data),

    /** Delete a sub-agent type definition */
    delete: (id: string) =>
      client.delete<void>(`${base}/${id}`),
  };
}
