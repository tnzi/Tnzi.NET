/**
 * Chat Module Metadata
 */

/**
 * Message type (visibility)
 */
export enum MessageType {
  Public = 1,
  Private = 2,
  Announcement = 3,
}

/**
 * Get message type label
 */
export function getMessageTypeLabel(type: MessageType): string {
  switch (type) {
    case MessageType.Public:
      return 'Public';
    case MessageType.Private:
      return 'Private';
    case MessageType.Announcement:
      return 'Announcement';
    default:
      return 'Unknown';
  }
}

/**
 * Message status
 */
export enum MessageStatus {
  Draft = 0,
  Scheduled = 1,
  Sent = 2,
  Cancelled = 3,
}

/**
 * Get message status label
 */
export function getMessageStatusLabel(status: MessageStatus): string {
  switch (status) {
    case MessageStatus.Draft:
      return 'Draft';
    case MessageStatus.Scheduled:
      return 'Scheduled';
    case MessageStatus.Sent:
      return 'Sent';
    case MessageStatus.Cancelled:
      return 'Cancelled';
    default:
      return 'Unknown';
  }
}
