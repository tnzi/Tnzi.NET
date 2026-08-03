<template>
  <div class="t-share">
    <div class="t-share__card">
      <NSpin :show="loading">
        <!-- 链接用不了：撤销 / 过期 / 次数用尽 / 根本不存在，一律同一句话。
             区分开就等于告诉试探者哪些令牌是真的。 -->
        <div v-if="unavailable" class="t-share__state">
          <TSvgIcon icon="mdi:link-variant-off" :size="40" class="t-share__state-icon" />
          <p class="t-share__state-title">{{ t('unavailable.title') }}</p>
          <p class="t-share__state-hint">{{ t('unavailable.hint') }}</p>
        </div>

        <div v-else-if="file" class="t-share__body">
          <TSvgIcon :icon="glyph.icon" :size="44" :style="{ color: glyph.color }" />
          <p class="t-share__name" :title="file.fileName">{{ file.fileName }}</p>
          <p class="t-share__meta">
            {{ formatFileSize(file.size) }}
            <template v-if="file.expiresAt">
              &middot; {{ t('expiresOn', { date: formatDateOnly(file.expiresAt, { utc: true }) }) }}
            </template>
          </p>

          <NForm v-if="file.requirePassword" class="t-share__form" @submit.prevent="download">
            <NInput
              v-model:value="password"
              type="password"
              show-password-on="click"
              :placeholder="t('passwordPlaceholder')"
              :status="failed ? 'error' : undefined"
              @keyup.enter="download"
            />
            <p v-if="failed" class="t-share__error">{{ t('wrongPassword') }}</p>
          </NForm>

          <NButton
            type="primary"
            block
            :loading="downloading"
            :disabled="file.requirePassword && !password"
            @click="download"
          >
            <template #icon><TSvgIcon icon="mdi:download" :size="18" /></template>
            {{ t('download') }}
          </NButton>
        </div>
      </NSpin>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * 分享链接的**收件人**页面 —— 这个功能存在的全部理由就是这一屏。
 *
 * 它刻意长在 admin 应用里但**不在 admin 外壳内**：路由 `meta.requiresAuth = false`，
 * 页面自绘一张居中卡片，不挂侧栏 / 顶栏 / 权限守卫。收件人是客户、审计师、供应商，
 * 他们没有账号，看到一整套后台 chrome 只会困惑。
 *
 * 三件事刻意做得很轻：
 *  - 失败一律同一句「链接不可用」，不区分撤销 / 过期 / 次数用尽 / 不存在；
 *  - 有口令时**先预览再输**，收件人得先知道自己要打开的是什么；
 *  - 下载走浏览器原生导航（匿名 URL），不在前端读成 blob —— 大文件、断点续传、
 *    浏览器自己的下载管理器都因此照常工作。
 */
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { NButton, NForm, NInput, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly, formatFileSize } from '@tnzi/core'
import type { FileSharePreviewDto } from '@tnzi/core/services/storage'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { fileGlyph } from '../storage/file-icons'

const route = useRoute()
const client = useAdminClient(false)
const t = makePageTranslator('storage.share')

// Built per call rather than once at setup: `createStorageBridge` is a
// per-call factory (bridges never hold singleton state) and `client` is only
// non-null once the plugin is installed.
const shareBridge = () => createStorageBridge({ client: client! })

const token = computed(() => String(route.params.token ?? ''))
const loading = ref(true)
const downloading = ref(false)
const failed = ref(false)
const password = ref('')
const file = ref<FileSharePreviewDto | null>(null)

const unavailable = computed(() => !loading.value && !file.value)
const glyph = computed(() => fileGlyph(file.value?.contentType, extensionOf(file.value?.fileName)))

/** 只有 contentType 认不出来时才回退到扩展名。 */
function extensionOf(name?: string | null): string | null {
  const dot = name?.lastIndexOf('.') ?? -1
  return dot > 0 ? name!.slice(dot) : null
}

onMounted(async () => {
  if (!client || !token.value) {
    loading.value = false
    return
  }
  try {
    // 网络问题与"链接不可用"对收件人是同一件事：他什么都做不了。
    // bridge 的 preview() 已把两者都折成 null。
    file.value = await shareBridge().publicShare.preview(token.value)
  } finally {
    loading.value = false
  }
})

async function download(): Promise<void> {
  if (!client || !file.value) return
  failed.value = false
  downloading.value = true
  try {
    const share = shareBridge().publicShare
    // 口令错了要在**原地**给反馈，而不是让浏览器跳去一个 401 页面 —— 跳走了收件人
    // 就再也回不到这一屏。校验走独立端点：拿下载端点探测会消耗一次访问配额，
    // maxAccessCount = 1 的链接在真正下载之前就用完了。
    if (file.value.requirePassword && !(await share.verifyPassword(token.value, password.value))) {
      failed.value = true
      return
    }
    window.location.href = share.downloadUrl(token.value, password.value)
  } finally {
    downloading.value = false
  }
}
</script>

<style scoped>
.t-share {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 24px;
  background: var(--tnzi-layout-bg);
}

.t-share__card {
  width: 100%;
  max-width: 380px;
  padding: 32px 28px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-lg, 10px);
  background: var(--tnzi-container-bg);
}

.t-share__body,
.t-share__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  text-align: center;
}

.t-share__state-icon {
  color: var(--tnzi-text-3);
}

.t-share__name {
  /* 文件名可以很长，且中间往往才是有信息量的部分 —— 截断到两行而不是省略号一刀切。 */
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  overflow-wrap: anywhere;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.t-share__meta,
.t-share__state-hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3);
}

.t-share__state-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
}

.t-share__form {
  width: 100%;
}

.t-share__error {
  margin: 6px 0 0;
  font-size: 12px;
  color: var(--tnzi-error);
  text-align: left;
}
</style>
