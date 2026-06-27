/**
 * Chat Module Types — system-level instant messaging (IM)
 * Aligned with Tnzi.NET backend Chat module (Tnzi.Chat/Dtos/*.cs)
 */

import type { SortedPagedQueryDto } from '../../types/pagination';

// ============================================
// IM (Instant Messaging) Types — /conversations/*
// ============================================

export enum ConversationType { Direct = 1, Group = 2, System = 3 }
export enum MemberRole { Owner = 1, Member = 2 }
export enum MessageContentType { Text = 1, Image = 2, File = 3, System = 4 }

export interface ConversationListItemDto {
  id: string; type: ConversationType; title: string; avatarFileId?: string | null;
  lastMessagePreview?: string | null; lastMessageAt?: string | null;
  unreadCount: number; isMuted: boolean; memberCount: number;
  peerUserId?: string | null; peerStatus?: UserPresenceStatus | null;
  isSticky: boolean; remark?: string | null;
}
export interface ConversationMemberDto {
  userId: string; name: string; avatarFileId?: string | null; role: MemberRole;
  status?: UserPresenceStatus | null; lastSeenAt?: string | null; alias?: string | null;
}
export interface ConversationDto {
  id: string; type: ConversationType; title: string; avatarFileId?: string | null;
  ownerId?: string | null; memberCount: number; lastMessageAt?: string | null;
  notice?: string | null; isSticky: boolean; isMuted: boolean;
  myRemark?: string | null; myAlias?: string | null;
  members: ConversationMemberDto[];
}
export interface ChatMessageDto {
  id: string; conversationId: string; senderId?: string | null; senderName?: string | null;
  senderAvatarFileId?: string | null;
  contentType: MessageContentType; content: string;
  fileId?: string | null; fileName?: string | null; fileSize?: number | null; sentAt: string;
}
export interface MessageThreadDto { messages: ChatMessageDto[]; hasMore: boolean; }
export interface SendMessageDto { contentType: MessageContentType; content?: string; fileId?: string; fileName?: string; fileSize?: number; }
export interface StartDirectDto { userId: string; }
export interface CreateGroupDto { title: string; memberIds: string[]; }
export interface AddMembersDto { userIds: string[]; }
export interface MuteRequestDto { muted: boolean; }
export interface RenameGroupDto { title: string; }
export interface BroadcastDto { content: string; roleIds?: string[]; userIds?: string[]; all?: boolean; }
export interface ChatContactDto { userId: string; name: string; avatarFileId?: string | null; }

export enum UserPresenceStatus { Online = 1, Away = 2, Busy = 3, Invisible = 4, Offline = 5 }
export interface UserPresenceDto { userId: string; status: UserPresenceStatus; lastSeenAt?: string | null }
export interface SetPresenceDto { status: UserPresenceStatus }
export interface ChatContactProfileDto { userId: string; name: string; avatarFileId?: string | null; status: UserPresenceStatus; lastSeenAt?: string | null; email?: string | null; phone?: string | null; bio?: string | null }
export interface ConversationMemberSettingsDto { isMuted?: boolean; isSticky?: boolean; remark?: string | null; alias?: string | null }
export interface UpdateNoticeDto { notice?: string | null }
export interface SearchMessagesParams { keyword: string; before?: string; limit?: number }

// ============================================
// IM Admin (system maintenance) Types — /admin/chat/*
// ============================================

/** Global chat statistics overview. Backend: ChatStatisticsDto */
export interface ChatStatisticsDto {
  totalConversations: number;
  directConversations: number;
  groupConversations: number;
  systemConversations: number;
  totalMessages: number;
  messagesToday: number;
  activeMembers: number;
  onlineUsers: number;
}

/** Admin conversation paged query. Backend: AdminConversationQueryDto */
export interface AdminConversationQueryDto extends SortedPagedQueryDto {
  type?: ConversationType;
  keyword?: string;
  userId?: string;
}

/** Admin conversation list item. Backend: AdminConversationListItemDto */
export interface AdminConversationListItemDto {
  id: string;
  type: ConversationType;
  title?: string | null;
  ownerId?: string | null;
  ownerName?: string | null;
  memberCount: number;
  lastMessagePreview?: string | null;
  lastMessageAt?: string | null;
  creationTime: string;
}

/** Admin conversation member view. Backend: AdminConversationMemberDto */
export interface AdminConversationMemberDto {
  userId: string;
  name: string;
  avatarFileId?: string | null;
  role: MemberRole;
  alias?: string | null;
  unreadCount: number;
  lastReadAt?: string | null;
  joinedAt: string;
}

/** Admin conversation detail. Backend: AdminConversationDetailDto */
export interface AdminConversationDetailDto {
  id: string;
  type: ConversationType;
  title?: string | null;
  notice?: string | null;
  ownerId?: string | null;
  ownerName?: string | null;
  directKey?: string | null;
  memberCount: number;
  messageCount: number;
  lastMessageAt?: string | null;
  creationTime: string;
  members: AdminConversationMemberDto[];
}

/** Admin presence overview query. Backend: PresenceOverviewQueryDto */
export interface PresenceOverviewQueryDto {
  status?: UserPresenceStatus;
  onlineOnly?: boolean;
}

/** Admin presence user detail. Backend: PresenceUserDto */
export interface PresenceUserDto {
  userId: string;
  name: string;
  avatarFileId?: string | null;
  intentStatus: UserPresenceStatus;
  effectiveStatus: UserPresenceStatus;
  hasConnection: boolean;
  lastSeenAt?: string | null;
  lastChangedAt?: string | null;
}

/** Admin presence overview. Backend: PresenceOverviewDto */
export interface PresenceOverviewDto {
  total: number;
  online: number;
  away: number;
  busy: number;
  offline: number;
  users: PresenceUserDto[];
}

/** Broadcast target type. Backend: BroadcastTargetType */
export enum BroadcastTargetType { All = 1, Roles = 2, Users = 3 }

/** Broadcast history entry. Backend: BroadcastLogDto */
export interface BroadcastLogDto {
  id: string;
  content: string;
  targetType: BroadcastTargetType;
  targetSummary?: string | null;
  recipientCount: number;
  senderId?: string | null;
  senderName?: string | null;
  creationTime: string;
}
