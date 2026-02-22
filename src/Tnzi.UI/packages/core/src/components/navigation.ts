/**
 * @tnzi/core/components/navigation
 *
 * Navigation component interfaces.
 */

import type { IMenuItem } from './layout';

/**
 * Tab bar Props (for mobile)
 */
export interface ITabBarProps {
  /** Tabs */
  tabs: ITabItem[];
  /** Active tab key */
  activeKey?: string;
  /** Badge config */
  badge?: Record<string, number>;
  /** Whether fixed at bottom */
  fixed?: boolean;
  /** Safe area bottom padding */
  safeAreaInsetBottom?: boolean;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Tab bar Emits
 */
export interface ITabBarEmits {
  /** Tab change */
  change: [key: string, tab: ITabItem];
}

/**
 * Tab item
 */
export interface ITabItem {
  /** Tab key */
  key: string;
  /** Tab label */
  label: string;
  /** Icon name */
  icon?: string;
  /** Badge count */
  badge?: number;
  /** Whether disabled */
  disabled?: boolean;
}

/**
 * Nav bar Props (for mobile)
 */
export interface INavBarProps {
  /** Title */
  title?: string;
  /** Whether show back button */
  showBack?: boolean;
  /** Whether show close button */
  showClose?: boolean;
  /** Left content (slot name or render data) */
  leftContent?: string | object;
  /** Right content (slot name or render data) */
  rightContent?: string | object;
  /** Whether fixed at top */
  fixed?: boolean;
  /** Background color */
  backgroundColor?: string;
  /** Text color */
  textColor?: string;
  /** Safe area top padding */
  safeAreaInsetTop?: boolean;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Nav bar Emits
 */
export interface INavBarEmits {
  /** Back click */
  back: [];
  /** Close click */
  close: [];
  /** Left content click */
  leftClick: [];
  /** Right content click */
  rightClick: [];
}

/**
 * Breadcrumb Props
 */
export interface IBreadcrumbProps {
  /** Items */
  items: IBreadcrumbItem[];
  /** Separator */
  separator?: string;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Breadcrumb Emits
 */
export interface IBreadcrumbEmits {
  /** Item click */
  itemClick: [item: IBreadcrumbItem, index: number];
}

/**
 * Breadcrumb item
 */
export interface IBreadcrumbItem {
  /** Item key */
  key: string;
  /** Item label */
  label: string;
  /** Route path */
  path?: string;
  /** Whether clickable */
  clickable?: boolean;
}

/**
 * Menu Props (for desktop)
 */
export interface IMenuProps {
  /** Menu items */
  items: IMenuItem[];
  /** Active menu key */
  activeKey?: string;
  /** Opened menu keys */
  openedKeys?: string[];
  /** Whether horizontal mode */
  horizontal?: boolean;
  /** Theme mode */
  mode?: 'light' | 'dark';
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Menu Emits
 */
export interface IMenuEmits {
  /** Menu item click */
  select: [key: string, item: IMenuItem];
  /** Menu open/close */
  openChange: [keys: string[]];
}
