/**
 * @tnzi/ui/playground/demo-data
 *
 * 共享 mock 数据，供各 playground 统一使用。
 */

import type {
  IMenuItem,
  ITableColumn,
  IFormRule,
  ITabItem,
  IBreadcrumbItem,
} from '@tnzi/core/types/shared-ui';
import type { FormSchemaItem } from '@tnzi/ui';

// IUserCardProps and IStatCardProps are UI contracts — define locally for demo usage
interface IUserCardUser {
  id: string | number;
  name: string;
  avatar?: string;
  email?: string;
  role?: string;
  status?: 'active' | 'inactive' | 'pending';
  description?: string;
}

interface IStatCardData {
  title: string;
  value: number | string;
  suffix?: string;
  prefix?: string;
  trend?: number;
  chartData?: Array<{ x: string | number; y: number }>;
  size?: 'small' | 'medium' | 'large';
  color?: 'blue' | 'green' | 'orange' | 'red' | 'purple';
  loading?: boolean;
}

// ============================================
// Menu / Navigation
// ============================================

export const demoMenuItems: IMenuItem[] = [
  { key: 'dashboard', label: 'Dashboard', icon: 'home', path: '/' },
  { key: 'orders', label: 'Orders', icon: 'list', path: '/orders', badge: 3 },
  {
    key: 'settings',
    label: 'Settings',
    icon: 'settings',
    children: [
      { key: 'profile', label: 'Profile', path: '/settings/profile' },
      { key: 'security', label: 'Security', path: '/settings/security' },
      { key: 'notifications', label: 'Notifications', path: '/settings/notifications' },
    ],
  },
  { key: 'analytics', label: 'Analytics', icon: 'chart', path: '/analytics' },
  { key: 'users', label: 'Users', icon: 'users', path: '/users' },
];

export const demoTabs: ITabItem[] = [
  { key: 'home', label: 'Home', icon: 'home-o' },
  { key: 'orders', label: 'Orders', icon: 'records-o', badge: 3 },
  { key: 'messages', label: 'Messages', icon: 'chat-o', badge: 12 },
  { key: 'profile', label: 'Profile', icon: 'user-o' },
];

export const demoBreadcrumbs: IBreadcrumbItem[] = [
  { key: 'home', label: 'Home', path: '/', clickable: true },
  { key: 'settings', label: 'Settings', path: '/settings', clickable: true },
  { key: 'profile', label: 'Profile' },
];

// ============================================
// Table Data
// ============================================

export interface DemoProduct {
  id: number;
  name: string;
  category: string;
  price: number;
  stock: number;
  status: 'active' | 'inactive' | 'draft';
  createdAt: string;
}

export const demoTableData: DemoProduct[] = [
  { id: 1, name: 'Wireless Mouse', category: 'Electronics', price: 29.99, stock: 150, status: 'active', createdAt: '2025-01-15' },
  { id: 2, name: 'Mechanical Keyboard', category: 'Electronics', price: 89.99, stock: 75, status: 'active', createdAt: '2025-01-20' },
  { id: 3, name: 'USB-C Hub', category: 'Accessories', price: 45.00, stock: 200, status: 'active', createdAt: '2025-02-01' },
  { id: 4, name: 'Monitor Stand', category: 'Furniture', price: 35.50, stock: 0, status: 'inactive', createdAt: '2025-02-05' },
  { id: 5, name: 'Webcam HD', category: 'Electronics', price: 59.99, stock: 42, status: 'active', createdAt: '2025-02-10' },
  { id: 6, name: 'Desk Lamp', category: 'Furniture', price: 24.99, stock: 88, status: 'draft', createdAt: '2025-02-12' },
  { id: 7, name: 'Headphone Stand', category: 'Accessories', price: 19.99, stock: 120, status: 'active', createdAt: '2025-02-15' },
  { id: 8, name: 'Cable Organizer', category: 'Accessories', price: 12.99, stock: 300, status: 'active', createdAt: '2025-02-18' },
];

export const demoTableColumns: ITableColumn<DemoProduct>[] = [
  { key: 'id', title: 'ID', width: 60, align: 'center' },
  { key: 'name', title: 'Product Name', sortable: true },
  { key: 'category', title: 'Category', width: 120 },
  { key: 'price', title: 'Price', width: 100, align: 'right', sortable: true },
  { key: 'stock', title: 'Stock', width: 80, align: 'center', sortable: true },
  { key: 'status', title: 'Status', width: 100 },
  { key: 'createdAt', title: 'Created', width: 120 },
];

export const demoTableActions = {
  buttons: [
    { key: 'view', label: 'View', type: 'primary' as const },
    { key: 'edit', label: 'Edit', type: 'default' as const },
    { key: 'delete', label: 'Delete', type: 'danger' as const },
  ],
};

// ============================================
// Form Data
// ============================================

export const demoBasicForm = {
  name: '',
  email: '',
  phone: '',
  description: '',
};

export const demoBasicFormRules: Record<string, IFormRule[]> = {
  name: [{ required: true, message: 'Name is required' }],
  email: [
    { required: true, message: 'Email is required' },
    { pattern: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: 'Invalid email format' },
  ],
  phone: [{ pattern: /^\d{10,11}$/, message: 'Invalid phone number' }],
};

// 基于 TSchemaForm 的声明式 schema（TDynamicForm/IDynamicFormField 已退役）。
// 内置字段类型仅 text/textarea/number/switch/select/date，故 email 归 text、
// radio 归 select。
export const demoSchemaFormItems: FormSchemaItem[] = [
  { key: 'username', label: 'Username', type: 'text', required: true, placeholder: 'Enter username' },
  { key: 'email', label: 'Email', type: 'text', required: true, placeholder: 'Enter email' },
  { key: 'age', label: 'Age', type: 'number', min: 1, max: 150, placeholder: 'Enter age' },
  {
    key: 'role',
    label: 'Role',
    type: 'select',
    required: true,
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'Editor', value: 'editor' },
      { label: 'Viewer', value: 'viewer' },
    ],
  },
  {
    key: 'department',
    label: 'Department',
    type: 'select',
    options: [
      { label: 'Engineering', value: 'eng' },
      { label: 'Marketing', value: 'mkt' },
      { label: 'Sales', value: 'sales' },
    ],
  },
  { key: 'bio', label: 'Bio', type: 'textarea', placeholder: 'Tell us about yourself' },
  { key: 'active', label: 'Active', type: 'switch' },
];

export const demoSearchForm = {
  keyword: '',
};

// ============================================
// User / Card Data
// ============================================

export const demoUsers: IUserCardUser[] = [
  { id: '1', name: 'Alice Johnson', email: 'alice@example.com', role: 'Admin', status: 'active', description: 'Platform administrator' },
  { id: '2', name: 'Bob Smith', email: 'bob@example.com', role: 'Editor', status: 'active', description: 'Content editor' },
  { id: '3', name: 'Carol Williams', email: 'carol@example.com', role: 'Viewer', status: 'inactive', description: 'External reviewer' },
];

export const demoStatCards: IStatCardData[] = [
  { title: 'Total Users', value: 12846, suffix: '', trend: 12.5, color: 'blue' },
  { title: 'Revenue', value: '$48,290', trend: 8.2, color: 'green' },
  { title: 'Orders', value: 1234, trend: -3.1, color: 'orange' },
  { title: 'Conversion', value: '3.24%', trend: 0.8, color: 'purple' },
];

// ============================================
// Auth Mock
// ============================================

export const demoMockUser = {
  id: '1',
  userName: 'admin',
  nickname: 'Administrator',
  email: 'admin@example.com',
  roles: ['Admin', 'User'],
};
