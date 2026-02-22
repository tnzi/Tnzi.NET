/**
 * @tnzi/core/components/card
 *
 * Card component interfaces.
 */

/**
 * User card Props
 */
export interface IUserCardProps {
  /** User info */
  user: {
    id: string | number;
    name: string;
    avatar?: string;
    email?: string;
    role?: string;
    status?: 'active' | 'inactive' | 'pending';
    description?: string;
  };
  /** Whether to show action buttons */
  showActions?: boolean;
  /** Action button list */
  actions?: ('edit' | 'delete' | 'view')[];
  /** Whether clickable */
  clickable?: boolean;
  /** Card size */
  size?: 'small' | 'medium' | 'large';
  /** Whether to show status badge */
  showStatus?: boolean;
  /** Whether to show email */
  showEmail?: boolean;
  /** Whether to show role */
  showRole?: boolean;
  /** Custom avatar fallback */
  avatarFallback?: string;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * User card Emits
 */
export interface IUserCardEmits {
  /** Click card */
  click: [user: IUserCardProps['user']];
  /** Click edit */
  edit: [user: IUserCardProps['user']];
  /** Click delete */
  delete: [user: IUserCardProps['user']];
  /** Click view */
  view: [user: IUserCardProps['user']];
}

/**
 * Stat card Props
 */
export interface IStatCardProps {
  /** Title */
  title: string;
  /** Value */
  value: number | string;
  /** Suffix (e.g., "users", "%") */
  suffix?: string;
  /** Prefix */
  prefix?: string;
  /** Trend value (positive = up, negative = down) */
  trend?: number;
  /** Chart data */
  chartData?: Array<{ x: string | number; y: number }>;
  /** Card size */
  size?: 'small' | 'medium' | 'large';
  /** Color theme */
  color?: 'blue' | 'green' | 'orange' | 'red' | 'purple';
  /** Loading state */
  loading?: boolean;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}
