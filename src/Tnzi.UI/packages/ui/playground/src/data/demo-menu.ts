/**
 * @tnzi/ui/playground/demo-menu
 *
 * Playground 导航菜单定义 - 统一所有 playground 的 section 结构。
 */

export interface PlaygroundSection {
  /** Section key */
  key: string;
  /** Section label (display name) */
  label: string;
  /** Icon name (optional, for sidebar/tab) */
  icon?: string;
  /** Description */
  description?: string;
  /** Whether this section is mobile-only or desktop-only */
  platform?: 'desktop' | 'mobile' | 'all';
}

/**
 * 标准 playground section 列表。
 * 各 playground 据此渲染侧边栏/TabBar 导航。
 */
export const playgroundSections: PlaygroundSection[] = [
  {
    key: 'theme',
    label: 'Theme',
    icon: 'palette',
    description: 'Theme configuration and live preview',
    platform: 'all',
  },
  {
    key: 'adapters',
    label: 'Adapters',
    icon: 'plug',
    description: 'Message, Dialog, Theme adapters',
    platform: 'all',
  },
  {
    key: 'stores',
    label: 'Stores',
    icon: 'database',
    description: 'Pinia state management',
    platform: 'all',
  },
  {
    key: 'auth',
    label: 'Auth',
    icon: 'lock',
    description: 'Login, Register, Password Reset forms',
    platform: 'all',
  },
  {
    key: 'forms',
    label: 'Forms',
    icon: 'edit',
    description: 'Form, DynamicForm, SearchForm',
    platform: 'all',
  },
  {
    key: 'data',
    label: 'Data',
    icon: 'table',
    description: 'DataTable, DataList',
    platform: 'all',
  },
  {
    key: 'cards',
    label: 'Cards',
    icon: 'credit-card',
    description: 'StatCard, UserCard',
    platform: 'all',
  },
  {
    key: 'navigation',
    label: 'Navigation',
    icon: 'compass',
    description: 'Menu, NavBar, TabBar, Breadcrumb',
    platform: 'all',
  },
  {
    key: 'layout',
    label: 'Layout',
    icon: 'layout',
    description: 'AdminLayout, Sidebar, Header',
    platform: 'desktop',
  },
  {
    key: 'native',
    label: 'Native',
    icon: 'box',
    description: 'Framework-specific native components',
    platform: 'all',
  },
];

/**
 * 获取指定平台的 section 列表。
 */
export function getSectionsForPlatform(platform: 'desktop' | 'mobile'): PlaygroundSection[] {
  return playgroundSections.filter(
    s => s.platform === 'all' || s.platform === platform
  );
}
