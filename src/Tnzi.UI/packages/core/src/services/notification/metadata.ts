/**
 * Notification Module Metadata
 */

/**
 * Notification type.
 *
 * String enum (member name = value) — the backend registers a global
 * JsonStringEnumConverter, so every enum field serializes as its PascalCase
 * member name; the enum members mirror Tnzi.Notification.Metadata.NotificationType
 * (Email / Sms / Push — there is no InApp on the backend). Inbound query params
 * accept both the string and the legacy numeric value.
 */
export enum NotificationType {
  Email = 'Email',
  Sms = 'Sms',
  Push = 'Push',
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
    default:
      return 'Unknown';
  }
}

/**
 * Notification status
 */
export enum NotificationStatus {
  Pending = 'Pending',
  Sending = 'Sending',
  Sent = 'Sent',
  Failed = 'Failed',
  PartiallySent = 'PartiallySent',
  Cancelled = 'Cancelled',
  Scheduled = 'Scheduled',
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
  Low = 'Low',
  Normal = 'Normal',
  High = 'High',
  Urgent = 'Urgent',
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
  Daily = 'Daily',
  Weekly = 'Weekly',
  Monthly = 'Monthly',
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
