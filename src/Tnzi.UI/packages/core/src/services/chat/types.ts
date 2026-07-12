/**
 * Chat Module Types — system-level instant messaging (IM)
 * Aligned with Tnzi.NET backend Chat module (Tnzi.Chat/Dtos/*.cs)
 */

import type { SortedPagedQueryDto } from '../../types/pagination';

// ============================================
// IM (Instant Messaging) Types — /conversations/*
// ============================================

// String enums (member name = value): the backend registers a global
// JsonStringEnumConverter (including SignalR's AddJsonProtocol), so every enum
// field on a response DTO / realtime payload serializes as its PascalCase
// member name; inbound params accept both the string and the legacy number.
export enum ConversationType { Direct = 'Direct', Group = 'Group', System = 'System' }
export enum MemberRole { Owner = 'Owner', Member = 'Member' }
export enum MessageContentType { Text = 'Text', Image = 'Image', File = 'File', System = 'System' }

export interface ConversationListItemDto {
  id: string; type: ConversationType; title: string; avatarFileId?: string | null;
  lastMessagePreview?: string | null; lastMessageAt?: string | null;
  unreadCount: number; isMuted: boolean; memberCount: number;
  peerUserId?: string | null; peerStatus?: UserPresenceStatus | null;
  isSticky: boolean; remark?: string | null;
  /** Group composite avatar: earliest N joined members (Group only; null otherwise). */
  memberAvatars?: ChatContactDto[] | null;
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
  fileId?: string | null; fileName?: string | null; fileSize?: number | null;
  /** Rich system-notification title (null for plain messages). */
  title?: string | null;
  /** Rich system-notification click-through link (null for plain messages). */
  linkUrl?: string | null;
  /** Rich system-notification category tag (null for plain messages). */
  category?: string | null;
  sentAt: string;
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

/**
 * Message sound preset (synthesised client-side via WebAudio — no binary assets).
 * Two families: attention (longer, for closed window / non-active conversation) and
 * subtle (short/gentle, for in-conversation send+receive). `None` = silent.
 * Values mirror the backend `ChatSoundEffect` enum member names (wire PascalCase).
 */
export enum ChatSoundEffect {
  None = 'None',
  // Attention (notification) family — longer, multi-note
  Chime = 'Chime',
  DingDong = 'DingDong',
  TriTone = 'TriTone',
  Marimba = 'Marimba',
  Pulse = 'Pulse',
  Bell = 'Bell',
  // Subtle (in-conversation) family — short, gentle
  Pop = 'Pop',
  Tick = 'Tick',
  Blip = 'Blip',
  Soft = 'Soft',
  Drop = 'Drop',
}

/**
 * Visual attention effect on the launcher icon when a message arrives while the
 * chat window is closed. `None` keeps only the unread badge. Values mirror the
 * backend `ChatNewMessageEffect` enum member names (wire PascalCase).
 */
export enum ChatNewMessageEffect {
  None = 'None',
  Shake = 'Shake',
  Pulse = 'Pulse',
  Blink = 'Blink',
  Bounce = 'Bounce',
}

/** Deployment-level chat feature configuration. Backend: ChatClientConfigDto (GET /chat/config) */
export interface ChatClientConfigDto {
  enableGroups: boolean;
  maxGroupMembers: number;
  groupAvatarMemberCount: number;
  enablePresence: boolean;
  /** Whether users may set the "Invisible" status (false hides the option). */
  allowInvisible: boolean;
  /** Master toggle — false silences every chat sound. */
  enableMessageSound: boolean;
  /** Sound for messages arriving while the window is closed or in a non-active conversation. */
  notificationSound: ChatSoundEffect;
  /** Sound for send+receive within the conversation currently on screen. */
  messageSound: ChatSoundEffect;
  /** Launcher icon animation when a message arrives while the window is closed. */
  newMessageEffect: ChatNewMessageEffect;
  /** Flash the browser tab title when a message arrives and the tab is unfocused. */
  flashTitleOnMessage: boolean;
  enableFileMessages: boolean;
}

export enum UserPresenceStatus { Online = 'Online', Away = 'Away', Busy = 'Busy', Invisible = 'Invisible', Offline = 'Offline' }
export interface UserPresenceDto { userId: string; status: UserPresenceStatus; lastSeenAt?: string | null }
export interface SetPresenceDto { status: UserPresenceStatus }
export interface ChatContactProfileDto { userId: string; name: string; avatarFileId?: string | null; status: UserPresenceStatus; lastSeenAt?: string | null; email?: string | null; phone?: string | null; bio?: string | null }
export interface ConversationMemberSettingsDto { isMuted?: boolean; isSticky?: boolean; isHidden?: boolean; remark?: string | null; alias?: string | null }
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
export enum BroadcastTargetType { All = 'All', Roles = 'Roles', Users = 'Users' }

/** Broadcast history entry. Backend: BroadcastLogDto */
export interface BroadcastLogDto {
  id: string;
  content: string;
  targetType: BroadcastTargetType;
  targetSummary?: string | null;
  recipientCount: number;
  senderId?: string | null;
  senderName?: string | null;
  /** Provenance tag: null for admin-UI broadcasts; set for programmatic module sends. */
  source?: string | null;
  creationTime: string;
}
