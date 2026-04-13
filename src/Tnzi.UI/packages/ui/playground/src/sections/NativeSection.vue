<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Native Components</h1>
      <p class="section-desc">Showcase of Naive UI native components including buttons, inputs, selects, and more.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Buttons</h2>
      <PreviewBox full-width>
        <n-space vertical :size="12">
          <n-space>
            <n-button>Default</n-button>
            <n-button type="primary">Primary</n-button>
            <n-button type="success">Success</n-button>
            <n-button type="warning">Warning</n-button>
            <n-button type="error">Error</n-button>
            <n-button type="info">Info</n-button>
          </n-space>
          <n-space>
            <n-button size="tiny">Tiny</n-button>
            <n-button size="small">Small</n-button>
            <n-button size="medium">Medium</n-button>
            <n-button size="large">Large</n-button>
          </n-space>
          <n-space>
            <n-button dashed>Dashed</n-button>
            <n-button secondary>Secondary</n-button>
            <n-button tertiary>Tertiary</n-button>
            <n-button quaternary>Quaternary</n-button>
            <n-button round type="primary">Round</n-button>
            <n-button circle type="primary">A</n-button>
          </n-space>
          <n-space>
            <n-button type="primary" ghost>Ghost Primary</n-button>
            <n-button type="success" ghost>Ghost Success</n-button>
            <n-button type="primary" loading>Loading</n-button>
            <n-button type="primary" disabled>Disabled</n-button>
          </n-space>
          <n-button-group>
            <n-button>Left</n-button>
            <n-button>Center</n-button>
            <n-button>Right</n-button>
          </n-button-group>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Inputs</h2>
      <PreviewBox full-width>
        <n-space vertical :size="12" style="max-width: 400px">
          <n-input v-model:value="inputValue" placeholder="Default input" />
          <n-input v-model:value="inputValue" placeholder="Clearable" clearable />
          <n-input placeholder="Password" type="password" show-password-on="click" />
          <n-input placeholder="Textarea" type="textarea" :rows="2" />
          <n-input-number v-model:value="numberValue" placeholder="Number" style="width: 100%" />
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Select</h2>
      <PreviewBox>
        <n-space>
          <n-select
            v-model:value="selectValue"
            :options="selectOptions"
            placeholder="Select..."
            style="width: 200px"
          />
          <n-select
            v-model:value="multiSelectValue"
            :options="selectOptions"
            placeholder="Multi select"
            multiple
            style="width: 200px"
          />
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Checkbox / Radio / Switch</h2>
      <PreviewBox>
        <n-space vertical :size="12">
          <n-space>
            <n-checkbox v-model:checked="checkA">Option A</n-checkbox>
            <n-checkbox v-model:checked="checkB">Option B</n-checkbox>
            <n-checkbox :checked="false" disabled>Disabled</n-checkbox>
          </n-space>
          <n-radio-group v-model:value="radioValue">
            <n-space>
              <n-radio value="apple">Apple</n-radio>
              <n-radio value="banana">Banana</n-radio>
              <n-radio value="cherry">Cherry</n-radio>
            </n-space>
          </n-radio-group>
          <n-space align="center">
            <n-switch v-model:value="switchValue" />
            <n-text>Switch is {{ switchValue ? 'ON' : 'OFF' }}</n-text>
          </n-space>
        </n-space>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Slider</h2>
      <PreviewBox>
        <div style="max-width: 400px; width: 100%">
          <n-slider v-model:value="sliderValue" :step="1" />
          <n-text depth="3">Value: {{ sliderValue }}</n-text>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Tags & Badges</h2>
      <PreviewBox full-width>
        <div>
          <n-space>
            <n-tag>Default</n-tag>
            <n-tag type="primary">Primary</n-tag>
            <n-tag type="success">Success</n-tag>
            <n-tag type="warning">Warning</n-tag>
            <n-tag type="error">Error</n-tag>
            <n-tag type="info">Info</n-tag>
            <n-tag closable @close="() => {}">Closable</n-tag>
            <n-tag round type="primary">Round</n-tag>
          </n-space>
          <n-space style="margin-top: 12px">
            <n-badge :value="5">
              <n-button>Messages</n-button>
            </n-badge>
            <n-badge :value="99" :max="99">
              <n-button>Notifications</n-button>
            </n-badge>
            <n-badge dot>
              <n-button>Dot</n-button>
            </n-badge>
          </n-space>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Typography</h2>
      <PreviewBox full-width>
        <div>
          <n-h2>Heading 2</n-h2>
          <n-h3>Heading 3</n-h3>
          <n-p>Normal paragraph text. Naive UI provides a complete set of typography components.</n-p>
          <n-text type="primary">Primary text</n-text>
          <span> | </span>
          <n-text type="success">Success text</n-text>
          <span> | </span>
          <n-text type="warning">Warning text</n-text>
          <span> | </span>
          <n-text type="error">Error text</n-text>
          <span> | </span>
          <n-text depth="3">Secondary text</n-text>
        </div>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import PreviewBox from '../components/PreviewBox.vue';

const inputValue = ref('');
const numberValue = ref<number | null>(null);
const selectValue = ref<string | null>(null);
const multiSelectValue = ref<string[]>([]);
const checkA = ref(true);
const checkB = ref(false);
const radioValue = ref('apple');
const switchValue = ref(false);
const sliderValue = ref(40);

const selectOptions = [
  { label: 'JavaScript', value: 'js' },
  { label: 'TypeScript', value: 'ts' },
  { label: 'Python', value: 'py' },
  { label: 'Rust', value: 'rs' },
  { label: 'Go', value: 'go' },
];
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}
</style>
