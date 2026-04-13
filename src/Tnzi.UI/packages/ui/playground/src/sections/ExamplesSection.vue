<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Component Examples</h1>
      <p class="section-desc">
        Real-world Naive UI component combinations. Each card demonstrates how multiple components work together in common scenarios.
      </p>
    </div>

    <div class="examples-grid">
      <!-- Login Form -->
      <n-card title="Login Form" :bordered="false" size="small">
        <template #header-extra>
          <n-tag size="small" type="info">Form + Input</n-tag>
        </template>
        <n-form label-placement="top" @submit.prevent="handleLogin">
          <n-form-item label="Email">
            <n-input v-model:value="loginForm.email" placeholder="name@example.com" />
          </n-form-item>
          <n-form-item label="Password">
            <n-input v-model:value="loginForm.password" type="password" placeholder="Password" />
          </n-form-item>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <n-checkbox v-model:checked="loginForm.remember">Remember me</n-checkbox>
            <n-button text type="primary" size="small">Forgot password?</n-button>
          </div>
          <n-button type="primary" block :loading="isLoading" style="margin-top: 16px">
            Sign In
          </n-button>
        </n-form>
        <n-divider>Or continue with</n-divider>
        <div style="display: flex; justify-content: center; gap: 8px">
          <n-button quaternary size="small">Google</n-button>
          <n-button quaternary size="small">GitHub</n-button>
          <n-button quaternary size="small">Twitter</n-button>
        </div>
      </n-card>

      <!-- Register Form (Steps) -->
      <n-card title="Register Form" :bordered="false" size="small">
        <template #header-extra>
          <n-tag size="small" type="success">Steps</n-tag>
        </template>
        <n-steps :current="registerStep" size="small" style="margin-bottom: 16px">
          <n-step title="Account" />
          <n-step title="Profile" />
          <n-step title="Confirm" />
        </n-steps>
        <n-form v-if="registerStep === 1" label-placement="top">
          <n-form-item label="Username">
            <n-input v-model:value="registerForm.username" placeholder="Choose a username" />
          </n-form-item>
          <n-form-item label="Email">
            <n-input v-model:value="registerForm.email" placeholder="your@email.com" />
          </n-form-item>
          <n-form-item label="Password">
            <n-input v-model:value="registerForm.password" type="password" placeholder="Create password" />
          </n-form-item>
        </n-form>
        <div v-else style="padding: 40px 0; text-align: center; color: var(--pg-text-muted)">
          Content for step {{ registerStep }}
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px">
          <n-button size="small" :disabled="registerStep === 1" @click="registerStep--">Back</n-button>
          <n-button size="small" type="primary" :disabled="registerStep === 3" @click="registerStep++">Next</n-button>
        </div>
      </n-card>

      <!-- Profile Settings -->
      <n-card title="Profile Settings" :bordered="false" size="small">
        <template #header-extra>
          <n-tag size="small" type="warning">Avatar</n-tag>
        </template>
        <div style="display: flex; align-items: center; gap: 16px; margin-bottom: 16px">
          <n-avatar :size="64" round style="background: var(--pg-primary, #6366f1)">JD</n-avatar>
          <div>
            <div style="font-weight: 700; font-size: 16px">{{ profileForm.name }}</div>
            <div style="font-size: 12px; color: var(--pg-text-muted)">{{ profileForm.email }}</div>
            <n-button size="tiny" quaternary style="margin-top: 4px">Change Avatar</n-button>
          </div>
        </div>
        <n-divider style="margin: 12px 0" />
        <n-form label-placement="top">
          <n-form-item label="Bio">
            <n-input v-model:value="profileForm.bio" type="textarea" placeholder="Tell us about yourself" :rows="2" />
          </n-form-item>
        </n-form>
        <n-button type="primary" size="small" block>Save Changes</n-button>
      </n-card>

      <!-- Advanced Search -->
      <n-card title="Advanced Search" :bordered="false" size="small">
        <template #header-extra>
          <n-tag size="small" type="info">Selects</n-tag>
        </template>
        <n-space vertical size="medium">
          <n-input v-model:value="searchForm.keyword" placeholder="Search..." clearable />
          <n-select v-model:value="searchForm.category" :options="searchCategories" placeholder="Category" size="small" clearable />
          <n-date-picker v-model:value="searchForm.dateRange" type="daterange" size="small" style="width: 100%" />
          <n-select v-model:value="searchForm.status" :options="searchStatuses" placeholder="Status" size="small" clearable />
          <n-divider style="margin: 4px 0" />
          <div style="display: flex; justify-content: space-between">
            <n-button size="small" quaternary @click="searchForm = { keyword: '', category: null, dateRange: null, status: null }">Reset</n-button>
            <n-button type="primary" size="small">Search</n-button>
          </div>
        </n-space>
      </n-card>

      <!-- Filter Panel -->
      <n-card title="Filter Panel" :bordered="false" size="small">
        <template #header-extra>
          <n-tag size="small">Slider</n-tag>
        </template>
        <div style="margin-bottom: 16px">
          <div style="font-size: 13px; font-weight: 500; margin-bottom: 12px">Price Range</div>
          <n-slider v-model:value="filterForm.priceRange" range :step="10" />
          <div style="display: flex; justify-content: space-between; font-size: 12px; color: var(--pg-text-muted); margin-top: 4px">
            <span>${{ filterForm.priceRange[0] }}</span>
            <span>${{ filterForm.priceRange[1] }}</span>
          </div>
        </div>
        <n-divider style="margin: 12px 0" />
        <div style="margin-bottom: 16px">
          <div style="font-size: 13px; font-weight: 500; margin-bottom: 8px">Minimum Rating</div>
          <n-rate :value="filterForm.rating" />
        </div>
        <n-divider style="margin: 12px 0" />
        <n-checkbox v-model:checked="filterForm.inStock">In Stock Only</n-checkbox>
        <n-select v-model:value="filterForm.sortBy" :options="sortOptions" size="small" placeholder="Sort by" style="margin-top: 16px" />
      </n-card>

      <!-- Dashboard Stats -->
      <n-card title="Dashboard" :bordered="false" size="small">
        <template #header-extra>
          <n-tag size="small" type="success">Stats</n-tag>
        </template>
        <n-grid :cols="2" x-gap="12" y-gap="12">
          <n-gi v-for="stat in stats" :key="stat.label">
            <div class="stat-box">
              <div style="font-size: 11px; color: var(--pg-text-muted); margin-bottom: 4px">{{ stat.label }}</div>
              <div style="font-size: 18px; font-weight: 700; margin-bottom: 4px">{{ stat.value }}</div>
              <n-tag :type="stat.positive ? 'success' : 'error'" size="tiny">{{ stat.change }}</n-tag>
            </div>
          </n-gi>
        </n-grid>
        <n-divider style="margin: 12px 0" />
        <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 8px">
          <span>Monthly Goal</span>
          <span>75%</span>
        </div>
        <n-progress type="line" :percentage="75" :show-indicator="false" />
      </n-card>

      <!-- Messages List -->
      <n-card title="Messages" :bordered="false" size="small">
        <template #header-extra>
          <n-badge :value="3" :max="9">
            <n-text>Inbox</n-text>
          </n-badge>
        </template>
        <n-list hoverable clickable>
          <n-list-item v-for="msg in messages" :key="msg.name" @click="selectedMessage = msg">
            <n-thing>
              <template #avatar>
                <n-badge dot :type="msg.unread ? 'error' : 'default'" :processing="msg.online">
                  <n-avatar round>{{ msg.avatar }}</n-avatar>
                </n-badge>
              </template>
              <template #header>{{ msg.name }}</template>
              <template #header-extra>
                <span style="font-size: 10px; color: var(--pg-text-muted)">{{ msg.time }}</span>
              </template>
              <template #description>
                <div style="font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 140px">{{ msg.message }}</div>
              </template>
            </n-thing>
          </n-list-item>
        </n-list>
      </n-card>

      <!-- Data Table (spans 2 columns) -->
      <n-card title="Data Table" :bordered="false" size="small" class="span-2">
        <template #header-extra>
          <n-space>
            <n-button size="tiny" type="primary">Add New</n-button>
            <n-button size="tiny">Export</n-button>
          </n-space>
        </template>
        <n-data-table :columns="tableColumns" :data="tableData" size="small" :pagination="{ pageSize: 5 }" />
      </n-card>

      <!-- Settings -->
      <n-card title="Settings" :bordered="false" size="small">
        <n-space vertical size="large">
          <div style="display: flex; justify-content: space-between; align-items: center">
            <div>
              <div style="font-size: 13px; font-weight: 500">Notifications</div>
              <div style="font-size: 10px; color: var(--pg-text-muted)">Receive alerts</div>
            </div>
            <n-switch v-model:value="settings.notifications" size="small" />
          </div>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <div style="font-size: 13px; font-weight: 500">Theme</div>
            <n-select v-model:value="settings.theme" :options="themeOptions" size="small" style="width: 100px" />
          </div>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <div style="font-size: 13px; font-weight: 500">Language</div>
            <n-select v-model:value="settings.language" :options="languageOptions" size="small" style="width: 100px" />
          </div>
          <div style="display: flex; justify-content: space-between; align-items: center">
            <div>
              <div style="font-size: 13px; font-weight: 500">Auto Save</div>
              <div style="font-size: 10px; color: var(--pg-text-muted)">Save changes automatically</div>
            </div>
            <n-switch v-model:value="settings.autoSave" size="small" />
          </div>
        </n-space>
      </n-card>

      <!-- Comments -->
      <n-card title="Comments" :bordered="false" size="small">
        <div v-for="comment in comments" :key="comment.user" style="display: flex; gap: 12px; margin-bottom: 16px">
          <n-avatar round size="small">{{ comment.avatar }}</n-avatar>
          <div style="flex: 1">
            <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 4px">
              <span style="font-weight: 700">{{ comment.user }}</span>
              <span style="color: var(--pg-text-muted)">{{ comment.time }}</span>
            </div>
            <div style="font-size: 12px; margin-bottom: 8px">{{ comment.content }}</div>
            <n-space :size="16">
              <n-button text size="tiny">{{ comment.likes }} likes</n-button>
              <n-button text size="tiny">Reply</n-button>
            </n-space>
          </div>
        </div>
        <n-divider style="margin: 12px 0" />
        <n-input-group>
          <n-input v-model:value="newComment" size="small" placeholder="Add comment..." />
          <n-button size="small" type="primary">Post</n-button>
        </n-input-group>
      </n-card>

      <!-- Activity Timeline -->
      <n-card title="Activity Timeline" :bordered="false" size="small">
        <n-timeline size="small">
          <n-timeline-item
            v-for="(item, index) in timelineItems"
            :key="index"
            :time="item.time"
            :type="index === 0 ? 'success' : 'default'"
            :title="item.title"
            :content="item.description"
          />
        </n-timeline>
      </n-card>

      <!-- User Card -->
      <n-card :bordered="false" size="small">
        <div style="display: flex; flex-direction: column; align-items: center; padding: 16px 0">
          <n-avatar :size="80" round style="background: var(--pg-primary, #6366f1)">JD</n-avatar>
          <h3 style="margin: 16px 0 4px; font-size: 18px">{{ userCard.name }}</h3>
          <p style="font-size: 12px; color: var(--pg-text-muted); margin: 0 0 16px">{{ userCard.role }}</p>
          <n-button type="primary" size="small" round>Follow</n-button>
        </div>
        <n-divider style="margin: 12px 0" />
        <div style="display: flex; justify-content: space-around; text-align: center; padding: 8px 0">
          <div>
            <div style="font-weight: 700; font-size: 18px">{{ userCard.followers }}</div>
            <div style="font-size: 10px; color: var(--pg-text-muted); text-transform: uppercase; letter-spacing: 0.05em">Followers</div>
          </div>
          <div>
            <div style="font-weight: 700; font-size: 18px">{{ userCard.following }}</div>
            <div style="font-size: 10px; color: var(--pg-text-muted); text-transform: uppercase; letter-spacing: 0.05em">Following</div>
          </div>
          <div>
            <div style="font-weight: 700; font-size: 18px">{{ userCard.projects }}</div>
            <div style="font-size: 10px; color: var(--pg-text-muted); text-transform: uppercase; letter-spacing: 0.05em">Projects</div>
          </div>
        </div>
      </n-card>

      <!-- Checkout Flow (Steps) -->
      <n-card title="Checkout Flow" :bordered="false" size="small">
        <n-steps :current="currentStep" size="small" style="margin-bottom: 24px">
          <n-step v-for="(step, index) in stepOptions" :key="index" :title="step.title" :description="step.description" />
        </n-steps>
        <div class="step-info">
          Step {{ currentStep }} of {{ stepOptions.length }}: {{ stepOptions[currentStep - 1].description }}
        </div>
        <div style="display: flex; justify-content: space-between; margin-top: 16px">
          <n-button size="small" :disabled="currentStep === 1" @click="currentStep--">Back</n-button>
          <n-button size="small" type="primary" :disabled="currentStep === 4" @click="currentStep++">Next</n-button>
        </div>
      </n-card>

      <!-- Notifications -->
      <n-card title="Notifications" :bordered="false" size="small">
        <n-space vertical size="medium">
          <n-alert type="success" title="Order Success" closable>Your payment has been processed.</n-alert>
          <n-alert type="info" title="System Info">New version 2.0 is available.</n-alert>
          <n-alert type="warning" title="Privacy Alert" closable>Review your data settings.</n-alert>
        </n-space>
      </n-card>

      <!-- Tags & Badges -->
      <n-card title="Status & Reviews" :bordered="false" size="small">
        <div style="margin-bottom: 16px">
          <div class="sub-label">Status Tags</div>
          <n-space>
            <n-tag type="success" size="small">Active</n-tag>
            <n-tag type="warning" size="small">Pending</n-tag>
            <n-tag type="error" size="small">Failed</n-tag>
            <n-tag type="info" size="small">New</n-tag>
          </n-space>
        </div>
        <n-divider style="margin: 12px 0" />
        <div style="margin-bottom: 16px">
          <div class="sub-label">Badges</div>
          <n-space size="large">
            <n-badge :value="5"><n-button size="small">Inbox</n-button></n-badge>
            <n-badge dot type="success"><n-button size="small">Online</n-button></n-badge>
            <n-badge :value="99" :max="99"><n-button size="small">Alerts</n-button></n-badge>
          </n-space>
        </div>
        <n-divider style="margin: 12px 0" />
        <div>
          <div class="sub-label">Quick Rate</div>
          <n-rate :value="4" allow-half />
        </div>
      </n-card>

      <!-- Empty State & Progress -->
      <n-card title="Miscellaneous" :bordered="false" size="small">
        <n-empty description="No recent activities" style="padding: 16px 0">
          <template #extra>
            <n-button size="tiny" secondary>Refresh</n-button>
          </template>
        </n-empty>
        <n-divider style="margin: 12px 0" />
        <div style="font-size: 12px; margin-bottom: 8px">Sync Progress</div>
        <n-progress type="line" :percentage="65" processing />
        <div style="font-size: 12px; margin-top: 16px; margin-bottom: 8px">Storage Usage</div>
        <n-progress type="line" :percentage="85" status="error" />
      </n-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useMessage } from 'naive-ui';

const message = useMessage();

// Login Form
const loginForm = ref({ email: '', password: '', remember: false });
const isLoading = ref(false);
function handleLogin() {
  isLoading.value = true;
  setTimeout(() => {
    isLoading.value = false;
    message.success('Login successful!');
  }, 1500);
}

// Register Form
const registerForm = ref({ username: '', email: '', password: '' });
const registerStep = ref(1);

// Profile
const profileForm = ref({
  name: 'John Doe',
  email: 'john@example.com',
  bio: 'Software developer',
});

// Search
const searchForm = ref<{ keyword: string; category: string | null; dateRange: [number, number] | null; status: string | null }>({
  keyword: '',
  category: null,
  dateRange: null,
  status: null,
});
const searchCategories = [
  { label: 'All Categories', value: 'all' },
  { label: 'Documents', value: 'docs' },
  { label: 'Images', value: 'images' },
  { label: 'Videos', value: 'videos' },
];
const searchStatuses = [
  { label: 'Any Status', value: 'any' },
  { label: 'Active', value: 'active' },
  { label: 'Archived', value: 'archived' },
];

// Filter
const filterForm = ref({
  priceRange: [0, 1000] as [number, number],
  rating: 4,
  inStock: false,
  sortBy: 'newest',
});
const sortOptions = [
  { label: 'Newest', value: 'newest' },
  { label: 'Popular', value: 'popular' },
  { label: 'Price: Low to High', value: 'price_asc' },
  { label: 'Price: High to Low', value: 'price_desc' },
];

// Dashboard
const stats = [
  { label: 'Total Revenue', value: '$45,231', change: '+20%', positive: true },
  { label: 'Orders', value: '+2,350', change: '+15%', positive: true },
  { label: 'Customers', value: '1,234', change: '+8%', positive: true },
  { label: 'Conversion', value: '3.6%', change: '-2%', positive: false },
];

// Messages
const messages = ref([
  { name: 'Alice Freeman', avatar: 'A', message: 'Hey, meeting at 3?', time: '2m', unread: true, online: true },
  { name: 'Bob Smith', avatar: 'B', message: 'Project update ready', time: '15m', unread: false, online: true },
  { name: 'Carol White', avatar: 'C', message: 'Thanks for your help!', time: '1h', unread: false, online: false },
]);
const selectedMessage = ref(messages.value[0]);

// Data Table
const tableData = [
  { id: 1, name: 'John Doe', email: 'john@example.com', status: 'Active', role: 'Admin', date: '2024-01-15' },
  { id: 2, name: 'Jane Smith', email: 'jane@example.com', status: 'Active', role: 'User', date: '2024-01-14' },
  { id: 3, name: 'Bob Johnson', email: 'bob@example.com', status: 'Inactive', role: 'Editor', date: '2024-01-13' },
  { id: 4, name: 'Sarah Wilson', email: 'sarah@example.com', status: 'Active', role: 'User', date: '2024-01-12' },
];
const tableColumns = [
  { title: 'Name', key: 'name' },
  { title: 'Email', key: 'email' },
  { title: 'Status', key: 'status' },
  { title: 'Role', key: 'role' },
  { title: 'Date', key: 'date' },
];

// Settings
const settings = ref({
  notifications: true,
  theme: 'light',
  language: 'en',
  autoSave: true,
});
const themeOptions = [
  { label: 'Light', value: 'light' },
  { label: 'Dark', value: 'dark' },
];
const languageOptions = [
  { label: 'English', value: 'en' },
  { label: '中文', value: 'zh' },
  { label: '日本語', value: 'ja' },
];

// Comments
const comments = ref([
  { user: 'Alice', avatar: 'A', content: 'Great work on this feature!', time: '2 hours ago', likes: 12 },
  { user: 'Bob', avatar: 'B', content: 'Would be nice to have more options.', time: '5 hours ago', likes: 8 },
]);
const newComment = ref('');

// Timeline
const timelineItems = [
  { time: '10:30 AM', title: 'Project created', description: 'Initial setup completed' },
  { time: '11:00 AM', title: 'Design approved', description: 'UI/UX design reviewed' },
  { time: '2:00 PM', title: 'Development started', description: 'Frontend implementation begins' },
  { time: '4:30 PM', title: 'First milestone', description: 'Core features implemented' },
];

// User Card
const userCard = {
  name: 'John Doe',
  role: 'Senior Developer',
  followers: 1234,
  following: 567,
  projects: 89,
};

// Steps
const currentStep = ref(2);
const stepOptions = [
  { title: 'Cart', description: 'Review items' },
  { title: 'Shipping', description: 'Address details' },
  { title: 'Payment', description: 'Choose method' },
  { title: 'Confirm', description: 'Review order' },
];
</script>

<style scoped>
.section-page {
  max-width: 1200px;
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

.examples-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}

@media (max-width: 1200px) {
  .examples-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 768px) {
  .examples-grid {
    grid-template-columns: 1fr;
  }
}

.span-2 {
  grid-column: span 2;
}

@media (max-width: 768px) {
  .span-2 {
    grid-column: span 1;
  }
}

:deep(.n-card) {
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -2px rgba(0, 0, 0, 0.1);
  transition: transform 0.2s ease;
}
:deep(.n-card:hover) {
  transform: translateY(-2px);
}
:deep(.n-card-header__main) {
  font-size: 14px;
  font-weight: 700;
}

.stat-box {
  padding: 12px;
  background: var(--pg-button-bg, #f1f5f9);
  border: 1px solid var(--pg-border, #e2e8f0);
  border-radius: 8px;
}

.step-info {
  font-size: 12px;
  color: var(--pg-text-muted);
  background: var(--pg-button-bg, #f1f5f9);
  padding: 12px;
  border-radius: 8px;
}

.sub-label {
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--pg-text-muted, #64748b);
  margin-bottom: 12px;
}
</style>
