<script setup lang="ts">
/**
 * ChatArtifactPanel — Right panel for artifact display with code/preview toggle
 */

import { ref } from 'vue';
import { useAiI18n } from '@/locale/index';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/primitives/ui/tabs';
import TArtifact from '@/components/artifact/TArtifact.vue';
import TArtifactPreview from '@/components/artifact/TArtifactPreview.vue';
import TArtifactCodeView from '@/components/artifact/TArtifactCodeView.vue';

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
  <TArtifact :title="title" @close="$emit('close')" @download="$emit('download')">
    <template #actions>
      <Tabs v-model="activeTab">
        <TabsList class="h-7">
          <TabsTrigger value="preview" class="text-xs px-2 h-6">
            {{ t.artifact.preview }}
          </TabsTrigger>
          <TabsTrigger value="code" class="text-xs px-2 h-6">
            {{ t.artifact.code }}
          </TabsTrigger>
        </TabsList>
      </Tabs>
    </template>
    <div class="h-full">
      <TArtifactPreview
        v-if="activeTab === 'preview'"
        :src="previewUrl"
        :srcdoc="previewHtml"
      />
      <TArtifactCodeView
        v-else
        :code="code ?? ''"
        :language="language"
        @download="$emit('download')"
      />
    </div>
  </TArtifact>
</template>
