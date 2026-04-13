<script setup lang="ts">
import { ref } from 'vue';
import { showToast } from 'vant';
import {
  demoBasicForm,
  demoBasicFormRules,
  demoDynamicFormFields,
  demoSearchForm,
} from '../data';

// TForm
const basicForm = ref({ ...demoBasicForm });
const onBasicSubmit = (data: unknown) => {
  console.log('Basic form:', data);
  showToast('Form submitted');
};

// TDynamicForm
const dynamicModel = ref<Record<string, any>>({});
const onDynamicSubmit = (data: unknown) => {
  console.log('Dynamic form:', data);
  showToast('Dynamic form submitted');
};
const onDynamicReset = () => {
  dynamicModel.value = {};
  showToast('Dynamic form reset');
};
const onFieldChange = (key: string, value: any) => {
  console.log('Field changed:', key, value);
};

// TSearchForm
const searchForm = ref({ ...demoSearchForm });
const onSearch = (data: unknown) => {
  console.log('Search:', data);
  showToast('Search triggered');
};
const onSearchReset = () => {
  searchForm.value = { keyword: '' };
  showToast('Search reset');
};
</script>

<template>
  <div class="forms-section">
    <!-- TForm -->
    <van-cell-group inset title="TForm">
      <div class="component-wrapper">
        <TForm :model="basicForm" :rules="demoBasicFormRules" @submit="onBasicSubmit">
          <template #default="{ model }">
            <van-field v-model="model.name" label="Name" placeholder="Enter name" />
            <van-field v-model="model.email" label="Email" placeholder="Enter email" />
            <van-field v-model="model.phone" label="Phone" placeholder="Enter phone" />
            <van-field
              v-model="model.description"
              type="textarea"
              label="Description"
              placeholder="Enter description"
              rows="2"
            />
          </template>
        </TForm>
      </div>
    </van-cell-group>

    <!-- TDynamicForm -->
    <van-cell-group inset title="TDynamicForm">
      <div class="component-wrapper">
        <TDynamicForm
          v-model:model-value="dynamicModel"
          :fields="demoDynamicFormFields"
          @submit="onDynamicSubmit"
          @reset="onDynamicReset"
          @field-change="onFieldChange"
        />
      </div>
    </van-cell-group>

    <!-- TSearchForm -->
    <van-cell-group inset title="TSearchForm">
      <div class="component-wrapper">
        <TSearchForm
          v-model="searchForm"
          placeholder="Search products..."
          @search="onSearch"
          @reset="onSearchReset"
        />
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.forms-section {
  padding-bottom: 8px;
}

.component-wrapper {
  padding: 4px 0;
}
</style>
