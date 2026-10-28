/**
 * Notification Module Metadata
 */

/**
 * Notification type.
 *
 * String enum (member name = value) - the backend registers a global
 * JsonStringEnumConverter, so every enum field serializes as its PascalCase
 * member name; the enum members mirror Tnzi.Notification.Metadata.NotificationType
 * (Email / Sms / Push / Fax - there is no InApp on the backend). Inbound query params
 * accept both the string and the legacy numeric value.
 */
export enum NotificationType {
  Email = 'Email',
  Sms = 'Sms',
  Push = 'Push',
  Fax = 'Fax',
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
    case NotificationType.Fax:
      return 'Fax';
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
    default:
      return 'Unknown';
  }
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
 * Error code of the 400 a preference upsert answers when its
 * `quietHoursTimeZone` is not a canonical IANA id this server can resolve.
 * Backend: ErrorCodes.NOTIFICATION_INVALID_TIME_ZONE.
 *
 * A page can tell this refusal apart from any other and put it on the zone
 * field: the zone usually came from the browser, so the person never typed it
 * and a generic "save failed" would not tell them what to change.
 */
export const NOTIFICATION_INVALID_TIME_ZONE = 'NOTIFICATION_INVALID_TIME_ZONE';

/**
 * Whether a failure (a thrown `HttpError` / `ApiError`, or a failed envelope)
 * is the time-zone refusal above.
 */
export function isInvalidTimeZoneError(value: unknown): boolean {
  if (!value || typeof value !== 'object') return false;
  const source = value as { errorCode?: unknown; cause?: { errorCode?: unknown } };
  return (source.errorCode ?? source.cause?.errorCode) === NOTIFICATION_INVALID_TIME_ZONE;
}
