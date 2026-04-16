import type { AdminMenuItem } from './types'

export const defaultAdminMenus: AdminMenuItem[] = [
  {
    key: 'dashboard',
    label: 'Dashboard',
    icon: 'LayoutDashboard',
    path: '/',
  },
  {
    key: 'identity',
    label: 'Identity',
    icon: 'Users',
    children: [
      { key: 'users', label: 'Users', path: '/identity/users', permission: 'identity.user.read' },
      { key: 'roles', label: 'Roles', path: '/identity/roles', permission: 'identity.role.read' },
      { key: 'tenants', label: 'Tenants', path: '/identity/tenants', permission: 'identity.tenant.read' },
    ],
  },
  {
    key: 'authorization',
    label: 'Authorization',
    icon: 'ShieldCheck',
    children: [
      { key: 'functions', label: 'Functions', path: '/authorization/functions', permission: 'authorization.function.read' },
      { key: 'permissions', label: 'Permissions', path: '/authorization/permissions', permission: 'authorization.permission.read' },
    ],
  },
  {
    key: 'storage',
    label: 'Storage',
    icon: 'HardDrive',
    path: '/storage',
    permission: 'storage.record.read',
  },
  {
    key: 'template',
    label: 'Template',
    icon: 'FileText',
    path: '/template',
    permission: 'template.template.read',
  },
  {
    key: 'notification',
    label: 'Notification',
    icon: 'Bell',
    path: '/notification',
    permission: 'notification.message.read',
  },
  {
    key: 'audit',
    label: 'Audit',
    icon: 'ClipboardList',
    children: [
      { key: 'audit-logs', label: 'Audit Logs', path: '/audit/logs', permission: 'audit.log.read' },
    ],
  },
  {
    key: 'system',
    label: 'System',
    icon: 'Settings',
    children: [
      { key: 'menus', label: 'Menus', path: '/system/menus', permission: 'system.menu.read' },
      { key: 'dictionaries', label: 'Dictionaries', path: '/system/dictionaries', permission: 'system.dictionary.read' },
      { key: 'access-logs', label: 'Access Logs', path: '/system/access-logs', permission: 'system.accesslog.read' },
    ],
  },
  {
    key: 'chat',
    label: 'Chat',
    icon: 'MessageSquare',
    path: '/chat',
    permission: 'chat.message.read',
  },
  {
    key: 'payment',
    label: 'Payment',
    icon: 'CreditCard',
    children: [
      { key: 'orders', label: 'Orders', path: '/payment/orders', permission: 'payment.order.read' },
      { key: 'subscriptions', label: 'Subscriptions', path: '/payment/subscriptions', permission: 'payment.subscription.read' },
    ],
  },
]
