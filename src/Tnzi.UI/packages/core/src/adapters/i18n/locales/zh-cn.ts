/**
 * @tnzi/core/adapters/i18n/locales/zh-cn
 *
 * Chinese translations.
 */

export default {
  // Common
  common: {
    home: '首页',
    loading: '加载中...',
    success: '成功',
    error: '错误',
    warning: '警告',
    info: '信息',
    confirm: '确认',
    cancel: '取消',
    delete: '删除',
    edit: '编辑',
    view: '查看',
    save: '保存',
    search: '搜索',
    reset: '重置',
    submit: '提交',
    close: '关闭',
    back: '返回',
    next: '下一步',
    prev: '上一步',
    select: '请选择',
    selectAll: '全选',
    deselectAll: '取消全选',
    more: '更多',
    noData: '暂无数据',
    required: '此字段为必填项',
    minLength: '至少 {min} 个字符',
    maxLength: '最多 {max} 个字符',
    invalid: '无效值',
    invalidFormat: '格式不正确',
  },

  // Auth
  auth: {
    login: '登录',
    logout: '退出',
    register: '注册',
    username: '用户名',
    password: '密码',
    confirmPassword: '确认密码',
    email: '邮箱',
    phone: '手机号',
    rememberMe: '记住我',
    forgotPassword: '忘记密码？',
    noAccount: '还没有账号？',
    hasAccount: '已有账号？',
    loginSuccess: '登录成功',
    registerSuccess: '注册成功',
    logoutSuccess: '退出成功',
    loginNow: '立即登录',
    passwordMismatch: '两次输入的密码不一致',
    verificationCode: '验证码',
    enterVerificationCode: '请输入验证码',
    sendCode: '发送验证码',
    newPassword: '新密码',
    resetPassword: '重置密码',
  },

  // Table
  table: {
    total: '共 {count} 条',
    page: '页',
    size: '条/页',
    jumpTo: '跳至',
    refresh: '刷新',
    export: '导出',
    print: '打印',
    columns: '列设置',
  },

  // Form
  form: {
    required: '此字段为必填项',
    invalidEmail: '邮箱格式不正确',
    invalidPhone: '手机号格式不正确',
    minLength: '至少 {min} 个字符',
    maxLength: '最多 {max} 个字符',
    invalidFormat: '格式不正确',
  },

  // Upload
  upload: {
    selectFile: '选择文件',
    dragDrop: '拖拽文件到此处',
    uploading: '上传中...',
    uploadSuccess: '上传成功',
    uploadFailed: '上传失败',
    fileSizeLimit: '文件大小超出限制',
    fileTypeLimit: '不支持的文件类型',
  },

  // Date
  date: {
    today: '今天',
    yesterday: '昨天',
    tomorrow: '明天',
    weekAgo: '一周前',
    monthAgo: '一个月前',
    yearAgo: '一年前',
  },
} as const;

