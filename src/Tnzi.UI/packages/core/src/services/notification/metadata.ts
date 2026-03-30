/**
 * Notification Module Metadata
 */

/**
 * Notification type
 */
export enum NotificationType {
  Email = 1,
  Sms = 2,
  Push = 3,
  InApp = 4,
}

/**
 * Get notification type label
 */
export function getNotificationTypeLabel(type: NotificationType): string {
  switch (type) {
    case NotificationType.Email:
      return 'Email';
    case NotificationType.Sms:
      return 'SMS';
    case NotificationType.Push:
      return 'Push';
    case NotificationType.InApp:
      return 'In-App';
    default:
      return 'Unknown';
  }
}

/**
 * Notification status
 */
export enum NotificationStatus {
  Pending = 0,
  Sending = 1,
  Sent = 2,
  Failed = 3,
  PartiallySent = 4,
  Cancelled = 5,
  Scheduled = 6,
}

/**
 * Get notification status label
 */
export function getNotificationStatusLabel(status: NotificationStatus): string {
  switch (status) {
    case NotificationStatus.Pending:
      return 'Pending';
    case NotificationStatus.Sending:
      return 'Sending';
    case NotificationStatus.Sent:
      return 'Sent';
    case NotificationStatus.Failed:
      return 'Failed';
    case NotificationStatus.PartiallySent:
      return 'Partially Sent';
    case NotificationStatus.Cancelled:
      return 'Cancelled';
    case NotificationStatus.Scheduled:
      return 'Scheduled';
    default:
      return 'Unknown';
  }
}

/**
 * Notification priority
 */
export enum NotificationPriority {
  Low = 0,
  Normal = 1,
  High = 2,
  Urgent = 3,
}

/**
 * Get notification priority label
 */
export function getNotificationPriorityLabel(priority: NotificationPriority): string {
  switch (priority) {
    case NotificationPriority.Low:
      return 'Low';
    case NotificationPriority.Normal:
      return 'Normal';
    case NotificationPriority.High:
      return 'High';
    case NotificationPriority.Urgent:
      return 'Urgent';
    default:
      return 'Unknown';
  }
}

/**
 * Recipient type
 */
export enum RecipientType {
  User = 1,
  Email = 2,
  Phone = 3,
  Role = 4,
  Organization = 5,
  All = 6,
}

/**
 * Trend interval for statistics
 * Backend: TrendInterval
 */
export enum TrendInterval {
  Daily = 0,
  Weekly = 1,
  Monthly = 2,
}

/**
 * Get trend interval label
 */
export function getTrendIntervalLabel(interval: TrendInterval): string {
  switch (interval) {
    case TrendInterval.Daily:
      return 'Daily';
    case TrendInterval.Weekly:
      return 'Weekly';
    case TrendInterval.Monthly:
      return 'Monthly';
    default:
      return 'Unknown';
  }
}

/**
 * Digest frequency
 */
export enum DigestFrequency {
  None = 'none',
  Daily = 'daily',
  Weekly = 'weekly',
}
