<template>
  <div class="space-y-4">
    <!-- Button variants -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Button</CardTitle>
        <CardDescription>按钮 - 所有变体和尺寸</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div>
          <p class="mb-2 text-sm font-medium">Variants</p>
          <div class="flex flex-wrap gap-2">
            <Button>Default</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="destructive">Destructive</Button>
            <Button variant="outline">Outline</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="link">Link</Button>
          </div>
        </div>
        <div>
          <p class="mb-2 text-sm font-medium">Sizes</p>
          <div class="flex flex-wrap items-center gap-2">
            <Button size="sm">Small</Button>
            <Button>Default</Button>
            <Button size="lg">Large</Button>
          </div>
        </div>
        <div>
          <p class="mb-2 text-sm font-medium">Disabled</p>
          <div class="flex flex-wrap gap-2">
            <Button disabled>Disabled Default</Button>
            <Button variant="outline" disabled>Disabled Outline</Button>
            <Button variant="destructive" disabled>Disabled Destructive</Button>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- Card variants -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Card</CardTitle>
        <CardDescription>卡片 - 标题、描述、内容、尾部</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-4 md:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle class="text-base">Card Title</CardTitle>
              <CardDescription>This is a card description.</CardDescription>
            </CardHeader>
            <CardContent>
              <p class="text-sm text-muted-foreground">
                shadcn-vue provides beautifully designed components.
              </p>
            </CardContent>
            <CardFooter class="flex justify-end gap-2">
              <Button variant="outline" size="sm">Cancel</Button>
              <Button size="sm">Confirm</Button>
            </CardFooter>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="text-base">Another Card</CardTitle>
              <CardDescription>With different content.</CardDescription>
            </CardHeader>
            <CardContent>
              <p class="text-sm text-muted-foreground">
                Cards are composable with Header, Content, and Footer sections.
              </p>
            </CardContent>
          </Card>
        </div>
      </CardContent>
    </Card>

    <!-- Input variants -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Input</CardTitle>
        <CardDescription>输入框 - 多种类型和状态</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          <div class="space-y-2">
            <Label>Default</Label>
            <Input placeholder="Type something..." />
          </div>
          <div class="space-y-2">
            <Label>Disabled</Label>
            <Input disabled placeholder="Disabled" />
          </div>
          <div class="space-y-2">
            <Label>Email</Label>
            <Input type="email" placeholder="email@example.com" />
          </div>
          <div class="space-y-2">
            <Label>Password</Label>
            <Input type="password" placeholder="Enter password" />
          </div>
          <div class="space-y-2">
            <Label>Number</Label>
            <Input type="number" placeholder="123" />
          </div>
          <div class="space-y-2">
            <Label>Search</Label>
            <Input type="search" placeholder="Search..." />
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- Label -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Label</CardTitle>
        <CardDescription>标签 - 表单字段标签</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-6">
          <div class="space-y-2">
            <Label for="label-demo-1">Username</Label>
            <Input id="label-demo-1" placeholder="Enter username" />
          </div>
          <div class="space-y-2">
            <Label for="label-demo-2">Email Address</Label>
            <Input id="label-demo-2" type="email" placeholder="Enter email" />
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- Checkbox -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Checkbox</CardTitle>
        <CardDescription>复选框 - 勾选状态</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="flex flex-wrap gap-6">
          <div class="flex items-center space-x-2">
            <Checkbox id="check-terms" />
            <Label for="check-terms" class="cursor-pointer">Accept terms and conditions</Label>
          </div>
          <div class="flex items-center space-x-2">
            <Checkbox id="check-newsletter" />
            <Label for="check-newsletter" class="cursor-pointer">Subscribe to newsletter</Label>
          </div>
          <div class="flex items-center space-x-2">
            <Checkbox id="check-disabled" disabled />
            <Label for="check-disabled" class="text-muted-foreground">Disabled checkbox</Label>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- Combined form example -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Combined Example</CardTitle>
        <CardDescription>原生组件组合示例 - 登录表单</CardDescription>
      </CardHeader>
      <CardContent>
        <Card class="max-w-md">
          <CardHeader>
            <CardTitle class="text-base">Sign In</CardTitle>
            <CardDescription>Enter your credentials</CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <div class="space-y-2">
              <Label for="native-username">Username</Label>
              <Input id="native-username" v-model="formData.username" placeholder="admin" />
            </div>
            <div class="space-y-2">
              <Label for="native-password">Password</Label>
              <Input id="native-password" v-model="formData.password" type="password" placeholder="password" />
            </div>
            <div class="flex items-center space-x-2">
              <Checkbox id="native-remember" v-model:checked="formData.remember" />
              <Label for="native-remember" class="cursor-pointer text-sm">Remember me</Label>
            </div>
          </CardContent>
          <CardFooter>
            <Button class="w-full" @click="handleFormSubmit">Sign in</Button>
          </CardFooter>
        </Card>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import {
  Button, Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter,
  Input, Label, Checkbox,
  useMessage,
} from '@tnzi/ui';

const message = useMessage();

const formData = ref({
  username: '',
  password: '',
  remember: false,
});

const handleFormSubmit = () => {
  console.log('[Native] form submit:', formData.value);
  message.show(`Sign in: ${formData.value.username || 'empty'}`, 'success');
};
</script>
