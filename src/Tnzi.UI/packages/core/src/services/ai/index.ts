/**
 * @tnzi/core/services/ai
 *
 * AI Service - Agents, Chat, and LLM integration.
 */

export * from './metadata';
export * from './types';
export * from './api';
export * from './streaming';
export { useRagApi, useAdminKnowledgeBaseApi } from './rag';
export type {
  RagQueryParams,
  RagChatParams,
  KnowledgeBaseCreateParams,
  KnowledgeBaseUpdateParams,
  SearchTestParams,
  ReindexResultDto,
} from './rag';

// Permission rule engine (admin/permissions)
export { usePermissionAdminApi, PermissionBehavior, ToolPermissionScope } from './permission';
export type {
  PermissionRuleItemDto,
  PermissionRulesDto,
  PermissionEvaluateRequestDto,
  PermissionEvaluateResultDto,
  PersistedPermissionRuleDto,
  CreatePersistedPermissionRuleDto,
} from './permission';

// Agent tasks (admin/ai/tasks)
export { useAdminTaskApi, AgentTaskStatus } from './task';
export type { AgentTaskDto } from './task';

// Sub-agent types (admin/ai/sub-agent-types)
export { useAdminSubAgentTypeApi, ToolApprovalMode } from './sub-agent-type';
export type { SubAgentTypeDto, SubAgentTypeInputDto } from './sub-agent-type';

// Channels + Gateway (admin/channels, admin/gateway) — Tnzi.AI.Channels
export { useChannelsAdminApi } from './channels';
export type {
  ChannelAdapterDto,
  ChannelModuleStatusDto,
  GatewayStatusDto,
  GatewayConnectionInfo,
  SessionBindingRuleDto,
} from './channels';

// Devices (admin/devices) — Tnzi.AI.Device
export { useDeviceAdminApi } from './device';
export type {
  DevicePlatform,
  DeviceConnectionState,
  DeviceNodeDto,
  PairingRequestDto,
  DevicePairingInfoDto,
} from './device';

// Sandbox (admin/sandbox) — Tnzi.AI.Sandbox
export { useSandboxAdminApi } from './sandbox';
export type { SandboxStatusDto } from './sandbox';

