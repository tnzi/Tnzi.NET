<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Forms</h1>
      <p class="section-desc">Basic forms with validation, dynamic schema-driven forms, and search input.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Basic Form</h2>
      <PreviewBox :full-width="true">
        <div style="width: 100%; max-width: 560px; margin: 0 auto;">
          <TForm
            :model="basicForm"
            :rules="basicRules"
            label-placement="left"
            :label-width="100"
            @submit="handleBasicSubmit"
            @reset="handleBasicReset"
          >
            <n-form-item label="Name" path="name">
              <n-input v-model:value="basicForm.name" placeholder="Enter name" />
            </n-form-item>
            <n-form-item label="Email" path="email">
              <n-input v-model:value="basicForm.email" placeholder="Enter email" />
            </n-form-item>
            <n-form-item label="Phone" path="phone">
              <n-input v-model:value="basicForm.phone" placeholder="Enter phone" />
            </n-form-item>
            <n-form-item label="Description">
              <n-input v-model:value="basicForm.description" type="textarea" placeholder="Enter description" :rows="3" />
            </n-form-item>
          </TForm>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Dynamic Form</h2>
      <PreviewBox :full-width="true">
        <div style="width: 100%; max-width: 560px; margin: 0 auto;">
          <TDynamicForm
            :model="dynamicModel"
            :fields="demoDynamicFormFields"
            @submit="handleDynamicSubmit"
          />
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Search Form</h2>
      <PreviewBox :full-width="true">
        <div style="width: 100%; max-width: 560px; margin: 0 auto;">
          <TSearchForm
            v-model="searchKeyword"
            placeholder="Search products..."
            @search="handleSearch"
            @reset="searchKeyword = ''"
          />
          <n-text v-if="lastSearch" depth="3" style="display: block; margin-top: 8px">
            Last search: "{{ lastSearch }}"
          </n-text>
        </div>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { useMessage } from 'naive-ui';
import {
  demoBasicForm,
  demoBasicFormRules,
  demoDynamicFormFields,
} from '@tnzi/core/playground';
import { TForm, TDynamicForm, TSearchForm, createNaiveMessageAdapter } from '@tnzi/naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const msgAdapter = createNaiveMessageAdapter(message);

// Basic form
const basicForm = reactive({ ...demoBasicForm });
const basicRules: Record<string, any[]> = {};
for (const [key, rules] of Object.entries(demoBasicFormRules)) {
  basicRules[key] = rules.map(r => ({
    required: r.required,
    message: r.message,
    trigger: 'blur',
    pattern: r.pattern,
  }));
}

function handleBasicSubmit(data: Record<string, any>) {
  msgAdapter.success(`Submitted: ${JSON.stringify(data)}`);
}

function handleBasicReset() {
  Object.assign(basicForm, demoBasicForm);
}

// Dynamic form
const dynamicModel = reactive<Record<string, any>>({});
for (const field of demoDynamicFormFields) {
  dynamicModel[field.key] = field.type === 'switch' ? false : null;
}

function handleDynamicSubmit(data: Record<string, any>) {
  msgAdapter.success(`Dynamic form: ${JSON.stringify(data)}`);
}

// Search
const searchKeyword = ref('');
const lastSearch = ref('');

function handleSearch(keyword: string) {
  lastSearch.value = keyword;
  msgAdapter.info(`Searching: "${keyword}"`);
}
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
