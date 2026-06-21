<template>
  <TContentPage :title="t('title')" :translate="t">
    <NCard size="small" :bordered="false" class="t-broadcast-card">
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="120px">
        <NFormItem :label="t('content')" path="content">
          <NInput
            v-model:value="form.content"
            type="textarea"
            :rows="5"
            :placeholder="t('content')"
            size="small"
          />
        </NFormItem>

        <NFormItem :label="t('target')" path="targetMode">
          <NRadioGroup v-model:value="form.targetMode" size="small">
            <NRadio value="all">{{ t('targetAll') }}</NRadio>
            <NRadio value="roles">{{ t('targetRoles') }}</NRadio>
            <NRadio value="users">{{ t('targetUsers') }}</NRadio>
          </NRadioGroup>
        </NFormItem>

        <NFormItem v-if="form.targetMode === 'roles'" :label="t('roleIds')" path="targetIds">
          <NInput
            v-model:value="form.targetIds"
            :placeholder="t('roleIds')"
            size="small"
          />
        </NFormItem>

        <NFormItem v-if="form.targetMode === 'users'" :label="t('userIds')" path="targetIds">
          <NInput
            v-model:value="form.targetIds"
            :placeholder="t('userIds')"
            size="small"
          />
        </NFormItem>

        <NFormItem>
          <NButton type="primary" size="small" :loading="sending" @click="handleSend">
            {{ t('send') }}
          </NButton>
        </NFormItem>
      </NForm>
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import {
  NButton, NCard, NForm, NFormItem, NInput, NRadio, NRadioGroup, useMessage,
} from 'naive-ui'
import type { FormInst, FormRules } from 'naive-ui'
import type { BroadcastDto } from '@tnzi/core/services/chat'
import { createChatBridge } from '../../services/bridges/chat-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import TContentPage from '../../components/layout/TContentPage.vue'

const bridge = createChatBridge({ client: useAdminClient() })
const message = useMessage()
const t = (key: string) => translatePageKey('chat.broadcast', key)

const formRef = ref<FormInst | null>(null)
const sending = ref(false)

const form = ref({
  content: '',
  targetMode: 'all' as 'all' | 'roles' | 'users',
  targetIds: '',
})

const rules: FormRules = {
  content: [{ required: true, message: 'Content is required', trigger: 'blur' }],
}

function parseIds(raw: string): string[] {
  return raw.split(',').map((s) => s.trim()).filter(Boolean)
}

async function handleSend(): Promise<void> {
  await formRef.value?.validate()
  const dto: BroadcastDto = { content: form.value.content }
  if (form.value.targetMode === 'roles') {
    dto.roleIds = parseIds(form.value.targetIds)
  } else if (form.value.targetMode === 'users') {
    dto.userIds = parseIds(form.value.targetIds)
  } else {
    // 'all' mode → system-wide notification to every user
    dto.all = true
  }
  sending.value = true
  try {
    await bridge.broadcast(dto)
    message.success(t('success'))
    form.value = { content: '', targetMode: 'all', targetIds: '' }
  } catch {
    message.error('Failed to send broadcast')
  } finally {
    sending.value = false
  }
}
</script>

<style scoped>
.t-broadcast-card {
  max-width: 720px;
}
</style>
