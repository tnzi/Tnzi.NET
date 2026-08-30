<template>
  <div class="t-unsub">
    <div class="t-unsub__card t-surface-card">
      <NSpin :show="loading">
        <!-- 令牌用不了：密钥轮换过、被篡改、格式不对，或者根本没带。
             一律同一句话 —— 区分开就等于告诉试探者哪些令牌是真的。 -->
        <div v-if="unavailable" class="t-unsub__state">
          <TSvgIcon icon="mdi:link-variant-off" :size="40" class="t-unsub__state-icon" />
          <p class="t-unsub__state-title">{{ t('unavailable.title') }}</p>
          <p class="t-unsub__state-hint">{{ t('unavailable.hint') }}</p>
        </div>

        <!-- 已退订：给一条回头路。点错的人不该只剩「重新去订阅」这一条路。 -->
        <div v-else-if="done" class="t-unsub__state">
          <TSvgIcon icon="mdi:email-off-outline" :size="40" class="t-unsub__state-icon t-unsub__state-icon--ok" />
          <p class="t-unsub__state-title">{{ t('done.title') }}</p>
          <p class="t-unsub__state-hint">{{ t('done.hint', { address: target!.maskedAddress }) }}</p>
          <NButton quaternary size="small" :loading="acting" @click="resubscribe">
            <template #icon><TSvgIcon icon="mdi:undo-variant" :size="16" /></template>
            {{ t('done.undo') }}
          </NButton>
        </div>

        <div v-else-if="restored" class="t-unsub__state">
          <TSvgIcon icon="mdi:email-check-outline" :size="40" class="t-unsub__state-icon t-unsub__state-icon--ok" />
          <p class="t-unsub__state-title">{{ t('restored.title') }}</p>
          <p class="t-unsub__state-hint">{{ t('restored.hint', { address: target!.maskedAddress }) }}</p>
        </div>

        <!-- 确认这一步不能省：链接可能被转发，也可能被人替别人点开。 -->
        <div v-else-if="target" class="t-unsub__body">
          <TSvgIcon icon="mdi:email-remove-outline" :size="44" class="t-unsub__glyph" />
          <p class="t-unsub__address">{{ target.maskedAddress }}</p>
          <p class="t-unsub__scope">{{ scopeText }}</p>

          <NInput
            v-model:value="reason"
            type="textarea"
            :rows="2"
            :maxlength="500"
            :placeholder="t('reasonPlaceholder')"
          />

          <NButton type="primary" block :loading="acting" @click="unsubscribe">
            <template #icon><TSvgIcon icon="mdi:email-off-outline" :size="18" /></template>
            {{ t('confirm') }}
          </NButton>
          <p v-if="failed" class="t-unsub__error">{{ t('failed') }}</p>
        </div>
      </NSpin>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * 退订链接的**收件人**页面 —— 一键退订这条链路存在的全部理由就是这一屏。
 *
 * 与 `pages/share/SharePage.vue` 同一形态：路由 `meta.requiresAuth = false`，
 * 自绘一张居中卡片，不挂侧栏 / 顶栏 / 权限守卫。群发的收件人未必是本系统的注册用户
 * （客户名单、导入的联系人、已注销的账号），而且要求先登录才能退订，本身就违背
 * 「一键退订」的合规要求。身份由令牌自身的签名担保。
 *
 * 三件事刻意如此：
 *  - 失败一律同一句「链接不可用」，不区分密钥轮换 / 被篡改 / 格式不对；
 *  - **先回显再退订**：链接可能被转发，收件人得先看清自己要退掉什么；
 *  - 退订之后给一条**回头路**（重新订阅）—— 点错的人不该只剩「去别处重新订阅」。
 *
 * ★ 地址是**掩码后**回显的（`a***@example.com`）：链接可能被转发或出现在日志里，
 *   完整地址不该对拿到链接的任何人可见，掩码保留的信息量刚够收件人确认「这是我」。
 */
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { NButton, NInput, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { NotificationType, type UnsubscribePreviewDto } from '@tnzi/core/services/notification'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'

const route = useRoute()
const client = useAdminClient(false)
const t = makePageTranslator('notification.unsubscribe')

// 每次调用现建：bridge 是 per-call 工厂（从不持有单例状态），而 client 只有在插件
// 装好之后才非空。
const bridge = () => createNotificationBridge({ client: client! })

// ★ 令牌走查询串不走路径段：邮件服务商与邮件客户端在转发链接时对查询串的处理最一致，
//   而签名令牌里含 base64url 的 `.` 与 `-`，放进路径段还要跟路由匹配规则打架。
const token = computed(() => String(route.query.token ?? ''))

const loading = ref(true)
const acting = ref(false)
const failed = ref(false)
const done = ref(false)
const restored = ref(false)
const reason = ref('')
const target = ref<UnsubscribePreviewDto | null>(null)

const unavailable = computed(() => !loading.value && !target.value)

/** 「不再接收本站的营销邮件」/「…这一类邮件」——整渠道与单分类是两句不同的话。 */
const scopeText = computed(() => {
  const channel = channelLabel(target.value?.channel)
  return target.value?.category
    ? t('scope.category', { channel, category: target.value.category })
    : t('scope.channel', { channel })
})

function channelLabel(channel?: NotificationType): string {
  switch (channel) {
    case NotificationType.Email: return t('channel.email')
    case NotificationType.Sms: return t('channel.sms')
    case NotificationType.Push: return t('channel.push')
    case NotificationType.Fax: return t('channel.fax')
    default: return t('channel.unknown')
  }
}

onMounted(async () => {
  if (!client || !token.value) {
    loading.value = false
    return
  }
  try {
    target.value = await bridge().publicUnsubscribe.preview(token.value)
  } finally {
    loading.value = false
  }
})

async function unsubscribe(): Promise<void> {
  if (!token.value) return
  failed.value = false
  acting.value = true
  try {
    const ok = await bridge().publicUnsubscribe.unsubscribe(token.value, reason.value || undefined)
    if (ok) done.value = true
    else failed.value = true
  } finally {
    acting.value = false
  }
}

async function resubscribe(): Promise<void> {
  if (!token.value) return
  acting.value = true
  try {
    if (await bridge().publicUnsubscribe.resubscribe(token.value)) {
      done.value = false
      restored.value = true
    }
  } finally {
    acting.value = false
  }
}
</script>

<style scoped>
.t-unsub {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 24px;
  background: var(--tnzi-layout-bg);
}

.t-unsub__card {
  width: 100%;
  max-width: 400px;
  padding: 32px 28px;
  border: var(--tnzi-surface-card-border);
  box-shadow: var(--tnzi-surface-card-shadow);
  border-radius: var(--tnzi-admin-radius-lg, 10px);
  background: var(--tnzi-surface-card-bg);
}

.t-unsub__body,
.t-unsub__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  text-align: center;
}

.t-unsub__glyph,
.t-unsub__state-icon {
  color: var(--tnzi-base-text-muted);
}

.t-unsub__state-icon--ok {
  color: var(--tnzi-success);
}

.t-unsub__address {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.t-unsub__scope,
.t-unsub__state-hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}

.t-unsub__state-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
}

.t-unsub__error {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-error);
}
</style>
