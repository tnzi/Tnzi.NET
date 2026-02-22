<template>
  <div class="space-y-4">
    <!-- Mode Selection -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Mode</CardTitle>
        <CardDescription>Select light, dark, or system theme mode.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex gap-2">
          <Button
            v-for="mode in modes"
            :key="mode.value"
            :variant="themeConfig.mode === mode.value ? 'default' : 'outline'"
            size="sm"
            @click="updateMode(mode.value)"
          >
            {{ mode.label }}
          </Button>
        </div>
      </CardContent>
    </Card>

    <!-- Presets -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Presets</CardTitle>
        <CardDescription>Apply a preset theme configuration.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="preset in themePresets"
            :key="preset.key"
            variant="outline"
            size="sm"
            @click="applyPreset(preset)"
          >
            {{ preset.name }}
          </Button>
          <Button variant="destructive" size="sm" @click="resetTheme">
            Reset
          </Button>
        </div>
      </CardContent>
    </Card>

    <!-- Color Pickers -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Colors</CardTitle>
        <CardDescription>Customize the 12 theme colors.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
          <div v-for="key in colorKeys" :key="key" class="space-y-1">
            <Label class="text-xs capitalize">{{ colorLabels[key] }}</Label>
            <div class="flex items-center gap-2">
              <input
                type="color"
                :value="themeConfig.colors[key]"
                class="h-8 w-10 cursor-pointer rounded border bg-background"
                @input="onColorChange(key, ($event.target as HTMLInputElement).value)"
              />
              <span class="text-xs text-muted-foreground">{{ themeConfig.colors[key] }}</span>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- Sliders -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Layout</CardTitle>
        <CardDescription>Adjust border radius, font size, and spacing.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="space-y-2">
          <Label>Border Radius: {{ themeConfig.borderRadius }}px</Label>
          <input
            type="range"
            :value="themeConfig.borderRadius"
            min="0"
            max="24"
            step="1"
            class="w-full"
            @input="updateProperty('borderRadius', Number(($event.target as HTMLInputElement).value))"
          />
        </div>
        <div class="space-y-2">
          <Label>Font Size: {{ themeConfig.fontSize }}px</Label>
          <input
            type="range"
            :value="themeConfig.fontSize"
            min="10"
            max="24"
            step="1"
            class="w-full"
            @input="updateProperty('fontSize', Number(($event.target as HTMLInputElement).value))"
          />
        </div>
        <div class="space-y-2">
          <Label>Spacing: {{ themeConfig.spacing }}px</Label>
          <input
            type="range"
            :value="themeConfig.spacing"
            min="2"
            max="16"
            step="1"
            class="w-full"
            @input="updateProperty('spacing', Number(($event.target as HTMLInputElement).value))"
          />
        </div>
      </CardContent>
    </Card>

    <!-- Font Family -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Font Family</CardTitle>
        <CardDescription>Choose a font family.</CardDescription>
      </CardHeader>
      <CardContent>
        <select
          :value="themeConfig.fontFamily"
          class="w-full rounded-md border bg-background px-3 py-2 text-sm"
          @change="updateProperty('fontFamily', ($event.target as HTMLSelectElement).value)"
        >
          <option v-for="opt in fontFamilyOptions" :key="opt.value" :value="opt.value">
            {{ opt.label }}
          </option>
        </select>
      </CardContent>
    </Card>

    <!-- Preview -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Preview</CardTitle>
        <CardDescription>Preview the current theme settings.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-3">
        <div class="flex flex-wrap gap-2">
          <Button>Primary</Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="destructive">Destructive</Button>
          <Button variant="outline">Outline</Button>
          <Button variant="ghost">Ghost</Button>
        </div>
        <div class="flex gap-4">
          <Input placeholder="Sample input..." class="max-w-xs" />
          <div class="flex items-center gap-2">
            <Checkbox id="preview-check" />
            <Label for="preview-check">Checkbox</Label>
          </div>
        </div>
        <Card class="max-w-sm">
          <CardHeader>
            <CardTitle class="text-base">Preview Card</CardTitle>
            <CardDescription>This card demonstrates the current theme settings.</CardDescription>
          </CardHeader>
          <CardContent>
            <p class="text-sm text-muted-foreground">
              Content area with muted foreground text.
            </p>
          </CardContent>
        </Card>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import type { ThemeColors, ThemeMode } from '@tnzi/core/types/theme';
import { themePresets, fontFamilyOptions } from '@tnzi/core/playground';
import {
  Button, Card, CardHeader, CardTitle, CardDescription, CardContent,
  Input, Label, Checkbox,
} from '@tnzi/shadcn';
import { usePlaygroundTheme } from '../composables/usePlaygroundTheme';

const { themeConfig, updateMode, updateColors, updateProperty, applyPreset, resetTheme } = usePlaygroundTheme();

const modes: { label: string; value: ThemeMode }[] = [
  { label: 'Light', value: 'light' },
  { label: 'Dark', value: 'dark' },
  { label: 'System', value: 'system' },
];

const colorKeys: (keyof ThemeColors)[] = [
  'primary', 'secondary', 'success', 'warning',
  'danger', 'info', 'background', 'surface',
  'text', 'textSecondary', 'border', 'divider',
];

const colorLabels: Record<keyof ThemeColors, string> = {
  primary: 'Primary',
  secondary: 'Secondary',
  success: 'Success',
  warning: 'Warning',
  danger: 'Danger',
  info: 'Info',
  background: 'Background',
  surface: 'Surface',
  text: 'Text',
  textSecondary: 'Text 2nd',
  border: 'Border',
  divider: 'Divider',
};

const onColorChange = (key: keyof ThemeColors, value: string) => {
  updateColors({ [key]: value });
};
</script>
