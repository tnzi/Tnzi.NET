<script setup lang="ts">
/**
 * ChatArtifactPanel — Right panel for artifact display with code/preview toggle
 */

import { NTabPane, NTabs } from 'naive-ui';
import { ref } from 'vue';
import { useAiI18n } from '@/locale/index';
import Artifact from '@/components/artifact/Artifact.vue';
import ArtifactPreview from '@/components/artifact/ArtifactPreview.vue';
import ArtifactCodeView from '@/components/artifact/ArtifactCodeView.vue';

const t = useAiI18n();

defineProps<{
  /** Artifact title. */
  title: string;
  /** Code content. */
  code?: string;
  /** Code language. */
  language?: string;
  /** Preview URL. */
  previewUrl?: string;
  /** Preview HTML source (srcdoc). */
  previewHtml?: string;
}>();

defineEmits<{
  close: [];
  download: [];
}>();

const activeTab = ref<'preview' | 'code'>('preview');
</script>

<template>
  <Artifact :title="title" @close="$emit('close')" @download="$emit('download')">
    <template #actions>
      <NTabs
        v-model:value="activeTab"
        type="segment"
        size="small"
      >
        <NTabPane :tab="t.artifact.preview" name="preview" />
        <NTabPane :tab="t.artifact.code" name="code" />
      </NTabs>
    </template>
    <div class="h-full">
      <ArtifactPreview
        v-if="activeTab === 'preview'"
        :src="previewUrl"
        :srcdoc="previewHtml"
      />
      <ArtifactCodeView
        v-else
        :code="code ?? ''"
        :language="language"
        @download="$emit('download')"
      />
    </div>
  </Artifact>
</template>
