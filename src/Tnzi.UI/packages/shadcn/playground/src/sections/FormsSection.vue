<template>
  <div class="space-y-4">
    <!-- TForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TForm</CardTitle>
        <CardDescription>Basic form with validation rules and custom slot content.</CardDescription>
      </CardHeader>
      <CardContent>
        <TForm
          :model="basicFormModel"
          :rules="demoBasicFormRules"
          class="mx-auto max-w-lg"
          @submit="onFormSubmit"
          @reset="onFormReset"
          @validate-error="onFormError"
        >
          <template #default="{ errors, disabled }">
            <div class="space-y-4">
              <div class="space-y-1">
                <Label>Name <span class="text-destructive">*</span></Label>
                <Input v-model="basicFormModel.name" placeholder="Enter name" :disabled="disabled" />
                <p v-if="errors.name" class="text-xs text-destructive">{{ errors.name[0] }}</p>
              </div>
              <div class="space-y-1">
                <Label>Email <span class="text-destructive">*</span></Label>
                <Input v-model="basicFormModel.email" type="email" placeholder="Enter email" :disabled="disabled" />
                <p v-if="errors.email" class="text-xs text-destructive">{{ errors.email[0] }}</p>
              </div>
              <div class="space-y-1">
                <Label>Phone</Label>
                <Input v-model="basicFormModel.phone" placeholder="Enter phone" :disabled="disabled" />
                <p v-if="errors.phone" class="text-xs text-destructive">{{ errors.phone[0] }}</p>
              </div>
              <div class="space-y-1">
                <Label>Description</Label>
                <Input v-model="basicFormModel.description" placeholder="Enter description" :disabled="disabled" />
              </div>
            </div>
          </template>
        </TForm>
      </CardContent>
    </Card>

    <!-- TDynamicForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TDynamicForm</CardTitle>
        <CardDescription>Dynamic form generated from field configuration.</CardDescription>
      </CardHeader>
      <CardContent>
        <TDynamicForm
          v-model="dynamicFormModel"
          :fields="demoDynamicFormFields"
          class="mx-auto max-w-lg"
          @submit="onDynamicFormSubmit"
          @reset="onDynamicFormReset"
          @field-change="onFieldChange"
        />
      </CardContent>
    </Card>

    <!-- TSearchForm -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TSearchForm</CardTitle>
        <CardDescription>Search form with keyword input and reset.</CardDescription>
      </CardHeader>
      <CardContent>
        <TSearchForm
          v-model="searchFormModel"
          placeholder="Search products..."
          class="mx-auto max-w-lg"
          @search="onSearch"
          @reset="onSearchReset"
        />
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import {
  Card, CardHeader, CardTitle, CardDescription, CardContent,
  Input, Label,
  TForm, TDynamicForm, TSearchForm,
  useShadcnMessage,
} from '@tnzi/shadcn';
import {
  demoBasicForm, demoBasicFormRules, demoDynamicFormFields, demoSearchForm,
} from '@tnzi/core/playground';

const message = useShadcnMessage();

// TForm
const basicFormModel = reactive({ ...demoBasicForm });

const onFormSubmit = (data: any) => {
  console.log('[TForm] submit:', data);
  message.show('Form submitted successfully', 'success');
};

const onFormReset = () => {
  Object.assign(basicFormModel, demoBasicForm);
  console.log('[TForm] reset');
  message.show('Form reset', 'info');
};

const onFormError = (errors: Record<string, string[]>) => {
  console.log('[TForm] validation errors:', errors);
  message.show('Validation errors found', 'warning');
};

// TDynamicForm
const dynamicFormModel = ref<Record<string, any>>({});

const onDynamicFormSubmit = (data: any) => {
  console.log('[TDynamicForm] submit:', data);
  message.show('Dynamic form submitted', 'success');
};

const onDynamicFormReset = () => {
  dynamicFormModel.value = {};
  console.log('[TDynamicForm] reset');
  message.show('Dynamic form reset', 'info');
};

const onFieldChange = (key: string, value: any) => {
  console.log(`[TDynamicForm] field change: ${key} =`, value);
};

// TSearchForm
const searchFormModel = ref({ ...demoSearchForm });

const onSearch = (data: any) => {
  console.log('[TSearchForm] search:', data);
  message.show(`Searching: "${data.keyword}"`, 'info');
};

const onSearchReset = () => {
  console.log('[TSearchForm] reset');
  message.show('Search reset', 'info');
};
</script>
