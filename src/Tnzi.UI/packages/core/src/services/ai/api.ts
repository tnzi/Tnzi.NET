/**
 * AI Module API - Agent and chat operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  AgentDto,
  CreateAgentDto,
  UpdateAgentDto,
  AgentQueryDto,
  RunAgentDto,
  AgentResponseDto,
} from './types';

const ADMIN_BASE = '/admin/agents';
const USER_BASE = '/chat';

/**
 * Admin Agent Management API
 */
export function useAdminAgentApi(client: HttpClient) {
  return {
    /** Get agent list */
    getList: (data?: AgentQueryDto) =>
      client.post<PagedList<AgentDto>>(`${ADMIN_BASE}/query`, data ?? {}),

    /** Get agent by ID */
    getById: (id: string) =>
      client.get<AgentDto>(`${ADMIN_BASE}/${id}`),

    /** Create agent */
    create: (data: CreateAgentDto) =>
      client.post<AgentDto>(ADMIN_BASE, data),

    /** Update agent */
    update: (id: string, data: UpdateAgentDto) =>
      client.put<AgentDto>(`${ADMIN_BASE}/${id}`, data),

    /** Delete agent */
    delete: (id: string) =>
      client.delete<void>(`${ADMIN_BASE}/${id}`),

    /** Run agent */
    run: (id: string, data: RunAgentDto) =>
      client.post<AgentResponseDto>(`${ADMIN_BASE}/${id}/run`, data),

    /** Build stream URL for running agent */
    getRunStreamUrl: (id: string, message: string, threadId?: string, userId?: string) =>
      client.resolveUrl(`${ADMIN_BASE}/${id}/run/stream`, {
        message,
        threadId,
        userId,
      }),
  };
}

/**
 * User AI Chat API
 */
export function useAiApi(client: HttpClient) {
  return {
    /** Chat with AI */
    chat: (data: { message: string; agentId?: string; threadId?: string; userId?: string }) =>
      client.post<AgentResponseDto>(USER_BASE, data),

    /** Build stream URL */
    getChatStreamUrl: (message: string, agentId?: string, threadId?: string, userId?: string) =>
      client.resolveUrl(`${USER_BASE}/stream`, {
        message,
        agentId,
        threadId,
        userId,
      }),
  };
}
