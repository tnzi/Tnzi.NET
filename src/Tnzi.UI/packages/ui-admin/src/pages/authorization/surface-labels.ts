/**
 * Localized display names for the permission matrix (`TPermissionMatrix`
 * `labelOverrides`). Keys are surface prefixes (`user`, `finance.account`)
 * plus `module:{code}` keys for module headers; values are the zh-cn names,
 * aligned with the sidebar menu wording so operators see the same term in
 * both places. Codes not in the map (e.g. a consumer app's own permission
 * provider) fall back to the backend display names, and consumers can extend
 * or override the map via `mergeSurfaceLabels`.
 *
 * English needs no map - the backend display names ("Users", "Blog Posts")
 * ARE the English labels.
 */

export const ZH_SURFACE_LABELS: Record<string, string> = {
  // ── Module headers ─────────────────────────────────────────────────────
  'module:identity': '身份管理',
  'module:authorization': '权限',
  'module:system': '系统管理',
  'module:storage': '存储',
  'module:audit': '审计',
  'module:notification': '通知',
  'module:chat': '会话',
  'module:payment': '支付',
  'module:template': '模板',
  'module:ai': 'AI 管理',
  'module:finance': '财务',
  'module:payroll': '薪酬',

  // ── Identity ───────────────────────────────────────────────────────────
  identity: '身份管理',
  user: '用户',
  role: '角色',
  tenant: '租户',
  organization: '组织',
  session: '会话',
  'identity.loginLog': '登录日志',
  'identity.loginSecurity': '登录安全',

  // ── Authorization ──────────────────────────────────────────────────────
  authorization: '权限中心',
  'authorization.functionModule': '功能模块',
  'authorization.permission': '权限点',
  'authorization.roleFunction': '角色权限',
  'authorization.entityRole': '数据权限',

  // ── System ─────────────────────────────────────────────────────────────
  system: '系统管理',
  'system.menu': '菜单',
  'system.parameter': '系统参数',
  'system.appearance': '外观主题',
  'system.dictionary': '数据字典',
  'system.accessLog': '访问日志',
  'system.scheduledJob': '计划任务',
  'system.diagnostics': '系统诊断',
  'system.health': '健康检查',
  'system.localization': '本地化',
  'system.log': '系统日志',
  'system.performance': '性能监控',
  'system.signalr': 'SignalR 监控',
  feature: '功能开关',

  // ── Storage ────────────────────────────────────────────────────────────
  storage: '存储',
  'storage.file': '文件',
  'storage.chunk': '分片',
  'storage.version': '版本',

  // ── Audit ──────────────────────────────────────────────────────────────
  audit: '审计',
  'audit.log': '审计日志',
  'audit.operation': '操作记录',

  // ── Notification ───────────────────────────────────────────────────────
  notification: '通知',
  'notification.message': '通知消息',
  'notification.subscription': '订阅',
  'notification.template': '通知模板',

  // ── Chat ───────────────────────────────────────────────────────────────
  chat: '会话',
  'chat.session': '聊天会话',

  // ── Payment ────────────────────────────────────────────────────────────
  payment: '支付',
  'payment.order': '订单',
  'payment.subscription': '订阅',
  'payment.refund': '退款',
  'payment.invoice': '发票管理',
  'payment.promotion': '优惠管理',
  'payment.statistics': '收入大盘',

  // ── Template ───────────────────────────────────────────────────────────
  template: '模板',
  'template.template': '模板',
  'template.layout': '布局',

  // ── AI ─────────────────────────────────────────────────────────────────
  ai: 'AI 管理',
  'ai.agent': '智能体',
  'ai.agentRun': '智能体执行',
  'ai.workflow': '工作流',
  'ai.workflowRun': '工作流执行',
  'ai.skill': '技能库',
  'ai.persona': '人格',
  'ai.knowledge': '知识库',
  'ai.mcp': 'MCP 服务器',
  'ai.provider': 'AI 供应商',
  'ai.quota': '配额',
  'ai.thread': '会话线程',
  'ai.usage': '用量统计',
  'ai.evaluation': '评测',
  'ai.channels': 'IM 桥接',
  'ai.permissions': '工具权限',
  'ai.sandbox': '沙箱',
  'ai.sql': 'AI SQL 查询',

  // ── Finance ────────────────────────────────────────────────────────────
  finance: '财务',
  'finance.account': '科目表',
  'finance.journal': '会计凭证',
  'finance.rate': '汇率',
  'finance.fiscalYear': '会计年度',
  'finance.report': '财务报表',
  'finance.customer': '客户',
  'finance.vendor': '供应商',
  'finance.item': '目录项',
  'finance.tax': '税务设置',
  'finance.document': '财务单据',
  'finance.reconciliation': '银行对账',
  'finance.revaluation': '汇兑重估',
  'finance.bankAccount': '银行账户',
  'finance.bankFeed': '银行流水',
  'finance.partyBank': '往来方银行账户',
  'finance.check': '支票打印',
  'finance.eft': 'EFT 批量付款',
  'finance.receipt': '收据采集',

  // ── Payroll ────────────────────────────────────────────────────────────
  payroll: '薪酬',
  'payroll.employee': '员工',
  'payroll.config': '薪酬配置',
  'payroll.run': '发薪批次',
  'payroll.pack': '国家薪酬包',
}

/**
 * Merge consumer-supplied labels over the built-in zh map (e.g. a Acme
 * `acme.blog.post: '博客文章'` entry). Later maps win.
 */
export function mergeSurfaceLabels(
  ...maps: Array<Record<string, string> | null | undefined>
): Record<string, string> {
  const merged: Record<string, string> = {}
  for (const map of maps) {
    if (map) Object.assign(merged, map)
  }
  return merged
}
