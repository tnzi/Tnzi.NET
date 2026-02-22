/**
 * @tnzi/core/components/layout
 *
 * Layout component interfaces.
 */

/**
 * Layout Props
 */
export interface ILayoutProps {
  /** Whether show sidebar */
  showSidebar?: boolean;
  /** Whether show header */
  showHeader?: boolean;
  /** Whether show footer */
  showFooter?: boolean;
  /** Layout mode */
  mode?: 'fixed' | 'fluid';
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Sidebar Props
 */
export interface ISidebarProps {
  /** Menu items */
  items: IMenuItem[];
  /** Collapsed state */
  collapsed?: boolean;
  /** Whether dark theme */
  dark?: boolean;
  /** Logo URL */
  logo?: string;
  /** Logo text */
  logoText?: string;
  /** Sidebar width */
  width?: number | string;
  /** Collapsed width */
  collapsedWidth?: number | string;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Sidebar Emits
 */
export interface ISidebarEmits {
  /** Collapse change */
  collapseChange: [collapsed: boolean];
  /** Menu item click */
  menuClick: [item: IMenuItem];
}

/**
 * Menu item
 */
export interface IMenuItem {
  /** Item key */
  key: string;
  /** Item label */
  label: string;
  /** Icon name */
  icon?: string;
  /** Parent key */
  parentKey?: string;
  /** Route path */
  path?: string;
  /** Children items */
  children?: IMenuItem[];
  /** Badge count */
  badge?: number;
  /** Whether disabled */
  disabled?: boolean;
  /** Whether hidden */
  hidden?: boolean;
}

/**
 * Header Props
 */
export interface IHeaderProps {
  /** Title */
  title?: string;
  /** Logo URL */
  logo?: string;
  /** Logo text */
  logoText?: string;
  /** User info */
  user?: {
    name?: string;
    avatar?: string;
  };
  /** Whether show breadcrumbs */
  showBreadcrumbs?: boolean;
  /** Breadcrumbs (used by simple header layouts) */
  breadcrumbs?: Array<{ key?: string; label: string; path?: string }>;
  /** Whether show user menu */
  showUserMenu?: boolean;
  /** Whether show theme toggle */
  showThemeToggle?: boolean;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Header Emits
 */
export interface IHeaderEmits {
  /** User menu click */
  userMenuClick: [action: string];
  /** Theme toggle */
  themeToggle: [dark: boolean];
}

/**
 * Footer Props
 */
export interface IFooterProps {
  /** Copyright text */
  copyright?: string;
  /** Links */
  links?: Array<{ label: string; href: string }>;
  /** Social links */
  socialLinks?: Array<{ icon: string; href: string }>;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Hero section Props (for marketing pages)
 */
export interface IHeroProps {
  /** Title */
  title: string;
  /** Subtitle */
  subtitle?: string;
  /** Description */
  description?: string;
  /** CTA button */
  cta?: {
    label: string;
    to?: string;
    href?: string;
  };
  /** Background image URL */
  background?: string;
  /** Overlay opacity */
  overlayOpacity?: number;
  /** Height */
  height?: string;
  /** Alignment */
  align?: 'left' | 'center' | 'right';
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}
