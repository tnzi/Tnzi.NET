/**
 * @tnzi/ui 简体中文 locale
 */

import type { Locale } from './en'

export const zhCN: Locale = {
  tnzi: {
    auth: {
      login: {
        title: '登录',
        username: '用户名',
        password: '密码',
        remember: '记住我',
        forgot: '忘记密码？',
        submit: '登录',
        noAccount: '还没有账号？',
        signUp: '注册',
      },
      register: {
        title: '创建账号',
        username: '用户名',
        email: '邮箱',
        password: '密码',
        confirmPassword: '确认密码',
        submit: '创建账号',
        hasAccount: '已有账号？',
        signIn: '登录',
      },
      passwordReset: {
        title: '重置密码',
        email: '邮箱',
        newPassword: '新密码',
        confirmPassword: '确认密码',
        sendCode: '发送验证码',
        submit: '重置密码',
        backToLogin: '返回登录',
      },
    },
    layout: {
      header: {
        search: '搜索',
        notifications: '通知',
        profile: '个人资料',
        settings: '设置',
        logout: '退出登录',
      },
      sidebar: {
        collapse: '收起',
        expand: '展开',
      },
      breadcrumb: {
        home: '首页',
      },
    },
    feedback: {
      confirm: {
        title: '确认',
        ok: '确定',
        cancel: '取消',
      },
      loading: '加载中...',
      empty: '暂无数据',
      error: '出错了',
      retry: '重试',
    },
    control: {
      button: {
        submit: '提交',
        cancel: '取消',
        save: '保存',
        delete: '删除',
        edit: '编辑',
        add: '新增',
        refresh: '刷新',
        reset: '重置',
        search: '搜索',
      },
      pagination: {
        prev: '上一页',
        next: '下一页',
        page: '第',
        of: '页，共',
        total: '条',
      },
    },
  },
}
