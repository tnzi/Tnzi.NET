<script setup lang="ts">
/**
 * `BindWechat` — WeChat account binding placeholder (Phase I.7.1 skeleton).
 *
 * Soybean reference: `src/views/_builtin/login/modules/bind-wechat.vue` (11 lines —
 * literally `<div></div>` upstream). WeChat OAuth binding is an ecosystem-optional
 * flow; this module stays a placeholder unless a consumer overrides it.
 */
import { useLoginContext } from '../useLoginContext'

defineOptions({ name: 'BindWechat' })

const { translate } = useLoginContext()
</script>

<template>
  <div class="t-login-module t-login-module--bind-wechat">
    <p class="t-login-module__placeholder">
      {{ translate('admin.login.placeholder.bindWechat', 'WeChat binding — override this module to integrate') }}
    </p>
  </div>
</template>

<style scoped>
.t-login-module__placeholder {
  margin: 0;
  padding: 24px 0;
  color: var(--tnzi-base-text-muted, #888);
  font-size: 14px;
  text-align: center;
}
</style>
