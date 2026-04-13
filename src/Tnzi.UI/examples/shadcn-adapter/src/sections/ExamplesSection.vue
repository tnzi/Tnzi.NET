<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Component Examples</h1>
      <p class="section-desc">
        Real-world shadcn-vue component combinations. Each card demonstrates how multiple components work together in common scenarios.
      </p>
    </div>

    <div class="examples-grid">
      <!-- 1. Login Form -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Login Form</CardTitle>
            <Badge variant="secondary" class="text-[10px]">Form + Input</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <form @submit.prevent="handleLogin">
            <div class="space-y-3">
              <div class="space-y-1">
                <Label for="login-email" class="text-xs">Email</Label>
                <Input id="login-email" v-model="loginForm.email" placeholder="name@example.com" />
              </div>
              <div class="space-y-1">
                <Label for="login-password" class="text-xs">Password</Label>
                <Input id="login-password" v-model="loginForm.password" type="password" placeholder="Password" />
              </div>
              <div style="display: flex; justify-content: space-between; align-items: center">
                <div class="flex items-center space-x-2">
                  <Checkbox id="login-remember" v-model:checked="loginForm.remember" />
                  <Label for="login-remember" class="text-xs cursor-pointer">Remember me</Label>
                </div>
                <Button variant="link" size="sm" class="text-xs h-auto p-0">Forgot password?</Button>
              </div>
              <Button type="submit" class="w-full" :disabled="isLoading">
                {{ isLoading ? 'Signing in...' : 'Sign In' }}
              </Button>
            </div>
          </form>
          <Separator class="my-3" />
          <div class="text-center text-xs text-muted-foreground mb-2">Or continue with</div>
          <div style="display: flex; justify-content: center; gap: 8px">
            <Button variant="outline" size="sm" class="text-xs">Google</Button>
            <Button variant="outline" size="sm" class="text-xs">GitHub</Button>
            <Button variant="outline" size="sm" class="text-xs">Twitter</Button>
          </div>
        </CardContent>
      </Card>

      <!-- 2. Register Form (Steps) -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Register Form</CardTitle>
            <Badge variant="secondary" class="text-[10px]">Stepper</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <Stepper v-model="registerStep" class="mb-4">
            <StepperItem v-for="step in 3" :key="step" :step="step" class="flex-1">
              <StepperTrigger>
                <StepperIndicator>{{ step }}</StepperIndicator>
              </StepperTrigger>
              <StepperTitle class="text-xs">{{ ['Account', 'Profile', 'Confirm'][step - 1] }}</StepperTitle>
              <StepperSeparator v-if="step < 3" />
            </StepperItem>
          </Stepper>
          <div v-if="registerStep === 1" class="space-y-3">
            <div class="space-y-1">
              <Label class="text-xs">Username</Label>
              <Input v-model="registerForm.username" placeholder="Choose a username" />
            </div>
            <div class="space-y-1">
              <Label class="text-xs">Email</Label>
              <Input v-model="registerForm.email" placeholder="your@email.com" />
            </div>
            <div class="space-y-1">
              <Label class="text-xs">Password</Label>
              <Input v-model="registerForm.password" type="password" placeholder="Create password" />
            </div>
          </div>
          <div v-else style="padding: 40px 0; text-align: center; color: var(--pg-text-muted)">
            Content for step {{ registerStep }}
          </div>
          <div style="display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px">
            <Button variant="outline" size="sm" :disabled="registerStep === 1" @click="registerStep--">Back</Button>
            <Button size="sm" :disabled="registerStep === 3" @click="registerStep++">Next</Button>
          </div>
        </CardContent>
      </Card>

      <!-- 3. Profile Settings -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Profile Settings</CardTitle>
            <Badge variant="secondary" class="text-[10px]">Avatar</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div style="display: flex; align-items: center; gap: 16px; margin-bottom: 16px">
            <Avatar class="h-16 w-16" style="background: var(--pg-primary, #6366f1)">
              <AvatarFallback class="text-lg text-white">JD</AvatarFallback>
            </Avatar>
            <div>
              <div style="font-weight: 700; font-size: 16px">{{ profileForm.name }}</div>
              <div style="font-size: 12px; color: var(--pg-text-muted)">{{ profileForm.email }}</div>
              <Button variant="ghost" size="sm" class="text-xs h-auto p-0 mt-1">Change Avatar</Button>
            </div>
          </div>
          <Separator class="my-3" />
          <div class="space-y-1 mb-3">
            <Label class="text-xs">Bio</Label>
            <Textarea v-model="profileForm.bio" placeholder="Tell us about yourself" :rows="2" />
          </div>
          <Button class="w-full" size="sm">Save Changes</Button>
        </CardContent>
      </Card>

      <!-- 4. Advanced Search -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Advanced Search</CardTitle>
            <Badge variant="secondary" class="text-[10px]">Select</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="space-y-3">
            <Input v-model="searchForm.keyword" placeholder="Search..." />
            <Select v-model="searchForm.category">
              <SelectTrigger>
                <SelectValue placeholder="Category" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="cat in searchCategories" :key="cat.value" :value="cat.value">{{ cat.label }}</SelectItem>
              </SelectContent>
            </Select>
            <Input v-model="searchForm.dateFrom" type="date" />
            <Select v-model="searchForm.status">
              <SelectTrigger>
                <SelectValue placeholder="Status" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="s in searchStatuses" :key="s.value" :value="s.value">{{ s.label }}</SelectItem>
              </SelectContent>
            </Select>
            <Separator />
            <div style="display: flex; justify-content: space-between">
              <Button variant="ghost" size="sm" @click="resetSearch">Reset</Button>
              <Button size="sm">Search</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 5. Filter Panel -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Filter Panel</CardTitle>
            <Badge variant="secondary" class="text-[10px]">Slider</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div style="margin-bottom: 16px">
            <div style="font-size: 13px; font-weight: 500; margin-bottom: 12px">Price Range</div>
            <Slider v-model="filterForm.priceRange" :min="0" :max="1000" :step="10" />
            <div style="display: flex; justify-content: space-between; font-size: 12px; color: var(--pg-text-muted); margin-top: 8px">
              <span>${{ filterForm.priceRange[0] }}</span>
              <span>${{ filterForm.priceRange[1] }}</span>
            </div>
          </div>
          <Separator class="my-3" />
          <div style="margin-bottom: 16px">
            <div style="font-size: 13px; font-weight: 500; margin-bottom: 8px">Rating</div>
            <div style="display: flex; gap: 4px">
              <Button
                v-for="star in 5"
                :key="star"
                variant="ghost"
                size="sm"
                class="h-8 w-8 p-0"
                @click="filterForm.rating = star"
              >
                <span :style="{ opacity: star <= filterForm.rating ? 1 : 0.3, fontSize: '16px' }">&#9733;</span>
              </Button>
            </div>
          </div>
          <Separator class="my-3" />
          <div class="flex items-center space-x-2 mb-3">
            <Checkbox id="filter-stock" v-model:checked="filterForm.inStock" />
            <Label for="filter-stock" class="text-xs cursor-pointer">In Stock Only</Label>
          </div>
          <Select v-model="filterForm.sortBy">
            <SelectTrigger>
              <SelectValue placeholder="Sort by" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="opt in sortOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</SelectItem>
            </SelectContent>
          </Select>
        </CardContent>
      </Card>

      <!-- 6. Dashboard Stats -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Dashboard</CardTitle>
            <Badge variant="secondary" class="text-[10px]">Stats</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px">
            <div v-for="stat in stats" :key="stat.label" class="stat-box">
              <div style="font-size: 11px; color: var(--pg-text-muted); margin-bottom: 4px">{{ stat.label }}</div>
              <div style="font-size: 18px; font-weight: 700; margin-bottom: 4px">{{ stat.value }}</div>
              <Badge :variant="stat.positive ? 'default' : 'destructive'" class="text-[10px]">{{ stat.change }}</Badge>
            </div>
          </div>
          <Separator class="my-3" />
          <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 8px">
            <span>Monthly Goal</span>
            <span>75%</span>
          </div>
          <Progress :model-value="75" />
        </CardContent>
      </Card>

      <!-- 7. Messages List -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Messages</CardTitle>
            <Badge>3</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="space-y-2">
            <div
              v-for="msg in messages"
              :key="msg.name"
              class="message-item"
              @click="selectedMessage = msg"
            >
              <div style="position: relative">
                <Avatar class="h-9 w-9">
                  <AvatarFallback>{{ msg.avatar }}</AvatarFallback>
                </Avatar>
                <span
                  v-if="msg.online"
                  style="position: absolute; bottom: 0; right: 0; width: 8px; height: 8px; border-radius: 50%; background: #22c55e; border: 2px solid white"
                />
              </div>
              <div style="flex: 1; min-width: 0">
                <div style="display: flex; justify-content: space-between; align-items: center">
                  <span style="font-size: 13px; font-weight: 600">{{ msg.name }}</span>
                  <span style="font-size: 10px; color: var(--pg-text-muted)">{{ msg.time }}</span>
                </div>
                <div style="font-size: 12px; color: var(--pg-text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                  {{ msg.message }}
                </div>
              </div>
              <span
                v-if="msg.unread"
                style="width: 8px; height: 8px; border-radius: 50%; background: #ef4444; flex-shrink: 0"
              />
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 8. Data Table (spans 2 columns) -->
      <Card class="example-card span-2">
        <CardHeader class="pb-3">
          <div class="card-header-row">
            <CardTitle class="text-sm font-bold">Data Table</CardTitle>
            <div style="display: flex; gap: 8px">
              <Button size="sm" class="text-xs h-7">Add New</Button>
              <Button variant="outline" size="sm" class="text-xs h-7">Export</Button>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Date</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in paginatedData" :key="row.id">
                <TableCell class="font-medium">{{ row.name }}</TableCell>
                <TableCell>{{ row.email }}</TableCell>
                <TableCell>
                  <Badge :variant="row.status === 'Active' ? 'default' : 'secondary'" class="text-[10px]">
                    {{ row.status }}
                  </Badge>
                </TableCell>
                <TableCell>{{ row.role }}</TableCell>
                <TableCell class="text-muted-foreground">{{ row.date }}</TableCell>
              </TableRow>
            </TableBody>
          </Table>
          <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 12px">
            <span style="font-size: 12px; color: var(--pg-text-muted)">
              Page {{ tablePage }} of {{ totalTablePages }}
            </span>
            <div style="display: flex; gap: 4px">
              <Button variant="outline" size="sm" class="h-7 text-xs" :disabled="tablePage <= 1" @click="tablePage--">Prev</Button>
              <Button variant="outline" size="sm" class="h-7 text-xs" :disabled="tablePage >= totalTablePages" @click="tablePage++">Next</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 9. Settings -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Settings</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="space-y-5">
            <div style="display: flex; justify-content: space-between; align-items: center">
              <div>
                <div style="font-size: 13px; font-weight: 500">Notifications</div>
                <div style="font-size: 10px; color: var(--pg-text-muted)">Receive alerts</div>
              </div>
              <Switch v-model:checked="settings.notifications" />
            </div>
            <div style="display: flex; justify-content: space-between; align-items: center">
              <div style="font-size: 13px; font-weight: 500">Theme</div>
              <Select v-model="settings.theme">
                <SelectTrigger class="w-[100px] h-8 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="opt in themeOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div style="display: flex; justify-content: space-between; align-items: center">
              <div style="font-size: 13px; font-weight: 500">Language</div>
              <Select v-model="settings.language">
                <SelectTrigger class="w-[100px] h-8 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="opt in languageOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div style="display: flex; justify-content: space-between; align-items: center">
              <div>
                <div style="font-size: 13px; font-weight: 500">Auto Save</div>
                <div style="font-size: 10px; color: var(--pg-text-muted)">Save changes automatically</div>
              </div>
              <Switch v-model:checked="settings.autoSave" />
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 10. Comments -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Comments</CardTitle>
        </CardHeader>
        <CardContent>
          <div v-for="comment in comments" :key="comment.user" style="display: flex; gap: 12px; margin-bottom: 16px">
            <Avatar class="h-8 w-8">
              <AvatarFallback class="text-xs">{{ comment.avatar }}</AvatarFallback>
            </Avatar>
            <div style="flex: 1">
              <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 4px">
                <span style="font-weight: 700">{{ comment.user }}</span>
                <span style="color: var(--pg-text-muted)">{{ comment.time }}</span>
              </div>
              <div style="font-size: 12px; margin-bottom: 8px">{{ comment.content }}</div>
              <div style="display: flex; gap: 16px">
                <Button variant="link" size="sm" class="h-auto p-0 text-xs">{{ comment.likes }} likes</Button>
                <Button variant="link" size="sm" class="h-auto p-0 text-xs">Reply</Button>
              </div>
            </div>
          </div>
          <Separator class="my-3" />
          <div style="display: flex; gap: 8px">
            <Input v-model="newComment" placeholder="Add comment..." class="flex-1" />
            <Button size="sm">Post</Button>
          </div>
        </CardContent>
      </Card>

      <!-- 11. Activity Timeline -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Activity Timeline</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="timeline">
            <div
              v-for="(item, index) in timelineItems"
              :key="index"
              class="timeline-item"
            >
              <div class="timeline-dot" :class="{ 'timeline-dot-active': index === 0 }" />
              <div v-if="index < timelineItems.length - 1" class="timeline-line" />
              <div class="timeline-content">
                <div style="font-size: 13px; font-weight: 600">{{ item.title }}</div>
                <div style="font-size: 12px; color: var(--pg-text-muted)">{{ item.description }}</div>
                <div style="font-size: 10px; color: var(--pg-text-muted); margin-top: 2px">{{ item.time }}</div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 12. User Card -->
      <Card class="example-card">
        <CardContent class="pt-6">
          <div style="display: flex; flex-direction: column; align-items: center; padding: 16px 0">
            <Avatar class="h-20 w-20" style="background: var(--pg-primary, #6366f1)">
              <AvatarFallback class="text-2xl text-white">JD</AvatarFallback>
            </Avatar>
            <h3 style="margin: 16px 0 4px; font-size: 18px; font-weight: 700">{{ userCard.name }}</h3>
            <p style="font-size: 12px; color: var(--pg-text-muted); margin: 0 0 16px">{{ userCard.role }}</p>
            <Button size="sm" class="rounded-full px-6">Follow</Button>
          </div>
          <Separator class="my-3" />
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
        </CardContent>
      </Card>

      <!-- 13. Checkout Flow (Stepper) -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Checkout Flow</CardTitle>
        </CardHeader>
        <CardContent>
          <Stepper v-model="currentStep" class="mb-4">
            <StepperItem v-for="(step, index) in stepOptions" :key="index" :step="index + 1" class="flex-1">
              <StepperTrigger>
                <StepperIndicator>{{ index + 1 }}</StepperIndicator>
              </StepperTrigger>
              <div class="hidden sm:block">
                <StepperTitle class="text-xs">{{ step.title }}</StepperTitle>
                <StepperDescription class="text-[10px]">{{ step.description }}</StepperDescription>
              </div>
              <StepperSeparator v-if="index < stepOptions.length - 1" />
            </StepperItem>
          </Stepper>
          <div class="step-info">
            Step {{ currentStep }} of {{ stepOptions.length }}: {{ stepOptions[currentStep - 1].description }}
          </div>
          <div style="display: flex; justify-content: space-between; margin-top: 16px">
            <Button variant="outline" size="sm" :disabled="currentStep === 1" @click="currentStep--">Back</Button>
            <Button size="sm" :disabled="currentStep === 4" @click="currentStep++">Next</Button>
          </div>
        </CardContent>
      </Card>

      <!-- 14. Notifications -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Notifications</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="space-y-3">
            <Alert>
              <AlertTitle class="text-xs font-semibold">Order Success</AlertTitle>
              <AlertDescription class="text-xs">Your payment has been processed.</AlertDescription>
            </Alert>
            <Alert>
              <AlertTitle class="text-xs font-semibold">System Info</AlertTitle>
              <AlertDescription class="text-xs">New version 2.0 is available.</AlertDescription>
            </Alert>
            <Alert variant="destructive">
              <AlertTitle class="text-xs font-semibold">Privacy Alert</AlertTitle>
              <AlertDescription class="text-xs">Review your data settings.</AlertDescription>
            </Alert>
          </div>
        </CardContent>
      </Card>

      <!-- 15. Status & Reviews -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Status & Reviews</CardTitle>
        </CardHeader>
        <CardContent>
          <div style="margin-bottom: 16px">
            <div class="sub-label">Status Tags</div>
            <div style="display: flex; flex-wrap: wrap; gap: 6px">
              <Badge>Active</Badge>
              <Badge variant="secondary">Pending</Badge>
              <Badge variant="destructive">Failed</Badge>
              <Badge variant="outline">New</Badge>
            </div>
          </div>
          <Separator class="my-3" />
          <div style="margin-bottom: 16px">
            <div class="sub-label">Badges with Counts</div>
            <div style="display: flex; gap: 16px; align-items: center">
              <div style="position: relative; display: inline-flex">
                <Button variant="outline" size="sm" class="text-xs">Inbox</Button>
                <Badge class="absolute -top-2 -right-2 h-5 w-5 p-0 text-[10px] flex items-center justify-center">5</Badge>
              </div>
              <div style="position: relative; display: inline-flex">
                <Button variant="outline" size="sm" class="text-xs">Online</Button>
                <span style="position: absolute; top: -2px; right: -2px; width: 8px; height: 8px; border-radius: 50%; background: #22c55e" />
              </div>
              <div style="position: relative; display: inline-flex">
                <Button variant="outline" size="sm" class="text-xs">Alerts</Button>
                <Badge variant="destructive" class="absolute -top-2 -right-3 h-5 px-1 text-[10px] flex items-center justify-center">99+</Badge>
              </div>
            </div>
          </div>
          <Separator class="my-3" />
          <div>
            <div class="sub-label">Quick Rate</div>
            <div style="display: flex; gap: 2px">
              <Button
                v-for="star in 5"
                :key="star"
                variant="ghost"
                size="sm"
                class="h-8 w-8 p-0"
                @click="quickRating = star"
              >
                <span :style="{ color: star <= quickRating ? '#f59e0b' : '#d1d5db', fontSize: '18px' }">&#9733;</span>
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 16. Miscellaneous -->
      <Card class="example-card">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-bold">Miscellaneous</CardTitle>
        </CardHeader>
        <CardContent>
          <Empty>
            <EmptyMedia>
              <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" style="opacity: 0.4"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" /><polyline points="10 9 9 9 8 9" /></svg>
            </EmptyMedia>
            <EmptyTitle class="text-sm">No recent activities</EmptyTitle>
            <EmptyDescription class="text-xs">Check back later for updates</EmptyDescription>
            <EmptyContent>
              <Button variant="outline" size="sm" class="text-xs">Refresh</Button>
            </EmptyContent>
          </Empty>
          <Separator class="my-3" />
          <div style="font-size: 12px; margin-bottom: 8px">Sync Progress</div>
          <Progress :model-value="65" />
          <div style="font-size: 12px; margin-top: 16px; margin-bottom: 8px">Storage Usage</div>
          <Progress :model-value="85" class="[&>div]:bg-destructive" />
        </CardContent>
      </Card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import {
  Button,
  Card, CardHeader, CardTitle, CardContent,
  Input, Label, Checkbox, Textarea,
  Avatar, AvatarFallback,
  Badge,
  Alert, AlertTitle, AlertDescription,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  Switch,
  Slider,
  Progress,
  Separator,
  Stepper, StepperDescription, StepperIndicator, StepperItem, StepperSeparator, StepperTitle, StepperTrigger,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
  Empty, EmptyContent, EmptyDescription, EmptyMedia, EmptyTitle,
  useMessage,
} from '@tnzi/ui';

const message = useMessage();

// 1. Login Form
const loginForm = ref({ email: '', password: '', remember: false });
const isLoading = ref(false);
function handleLogin() {
  isLoading.value = true;
  setTimeout(() => {
    isLoading.value = false;
    message.show('Login successful!', 'success');
  }, 1500);
}

// 2. Register Form
const registerForm = ref({ username: '', email: '', password: '' });
const registerStep = ref(1);

// 3. Profile
const profileForm = ref({
  name: 'John Doe',
  email: 'john@example.com',
  bio: 'Software developer',
});

// 4. Search
const searchForm = ref({
  keyword: '',
  category: '' as string,
  dateFrom: '',
  status: '' as string,
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
function resetSearch() {
  searchForm.value = { keyword: '', category: '', dateFrom: '', status: '' };
}

// 5. Filter
const filterForm = ref({
  priceRange: [0, 1000] as number[],
  rating: 4,
  inStock: false,
  sortBy: 'newest' as string,
});
const sortOptions = [
  { label: 'Newest', value: 'newest' },
  { label: 'Popular', value: 'popular' },
  { label: 'Price: Low to High', value: 'price_asc' },
  { label: 'Price: High to Low', value: 'price_desc' },
];

// 6. Dashboard
const stats = [
  { label: 'Total Revenue', value: '$45,231', change: '+20%', positive: true },
  { label: 'Orders', value: '+2,350', change: '+15%', positive: true },
  { label: 'Customers', value: '1,234', change: '+8%', positive: true },
  { label: 'Conversion', value: '3.6%', change: '-2%', positive: false },
];

// 7. Messages
const messages = ref([
  { name: 'Alice Freeman', avatar: 'A', message: 'Hey, meeting at 3?', time: '2m', unread: true, online: true },
  { name: 'Bob Smith', avatar: 'B', message: 'Project update ready', time: '15m', unread: false, online: true },
  { name: 'Carol White', avatar: 'C', message: 'Thanks for your help!', time: '1h', unread: false, online: false },
]);
const selectedMessage = ref(messages.value[0]);

// 8. Data Table
const tableData = [
  { id: 1, name: 'John Doe', email: 'john@example.com', status: 'Active', role: 'Admin', date: '2024-01-15' },
  { id: 2, name: 'Jane Smith', email: 'jane@example.com', status: 'Active', role: 'User', date: '2024-01-14' },
  { id: 3, name: 'Bob Johnson', email: 'bob@example.com', status: 'Inactive', role: 'Editor', date: '2024-01-13' },
  { id: 4, name: 'Sarah Wilson', email: 'sarah@example.com', status: 'Active', role: 'User', date: '2024-01-12' },
  { id: 5, name: 'Mike Brown', email: 'mike@example.com', status: 'Active', role: 'Admin', date: '2024-01-11' },
  { id: 6, name: 'Emily Davis', email: 'emily@example.com', status: 'Inactive', role: 'User', date: '2024-01-10' },
  { id: 7, name: 'Tom Garcia', email: 'tom@example.com', status: 'Active', role: 'Editor', date: '2024-01-09' },
  { id: 8, name: 'Lisa Martinez', email: 'lisa@example.com', status: 'Active', role: 'User', date: '2024-01-08' },
];
const tablePage = ref(1);
const pageSize = 4;
const totalTablePages = computed(() => Math.ceil(tableData.length / pageSize));
const paginatedData = computed(() => {
  const start = (tablePage.value - 1) * pageSize;
  return tableData.slice(start, start + pageSize);
});

// 9. Settings
const settings = ref({
  notifications: true,
  theme: 'light' as string,
  language: 'en' as string,
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

// 10. Comments
const comments = ref([
  { user: 'Alice', avatar: 'A', content: 'Great work on this feature!', time: '2 hours ago', likes: 12 },
  { user: 'Bob', avatar: 'B', content: 'Would be nice to have more options.', time: '5 hours ago', likes: 8 },
]);
const newComment = ref('');

// 11. Timeline
const timelineItems = [
  { time: '10:30 AM', title: 'Project created', description: 'Initial setup completed' },
  { time: '11:00 AM', title: 'Design approved', description: 'UI/UX design reviewed' },
  { time: '2:00 PM', title: 'Development started', description: 'Frontend implementation begins' },
  { time: '4:30 PM', title: 'First milestone', description: 'Core features implemented' },
];

// 12. User Card
const userCard = {
  name: 'John Doe',
  role: 'Senior Developer',
  followers: 1234,
  following: 567,
  projects: 89,
};

// 13. Checkout Steps
const currentStep = ref(2);
const stepOptions = [
  { title: 'Cart', description: 'Review items' },
  { title: 'Shipping', description: 'Address details' },
  { title: 'Payment', description: 'Choose method' },
  { title: 'Confirm', description: 'Review order' },
];

// 15. Quick Rate
const quickRating = ref(4);
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

.example-card {
  box-shadow: var(--pg-card-shadow, 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -2px rgba(0, 0, 0, 0.1));
  transition: transform 0.2s ease;
}
.example-card:hover {
  transform: translateY(-2px);
}

.card-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
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

/* Message list item */
.message-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.15s ease;
}
.message-item:hover {
  background: var(--pg-button-bg, #f1f5f9);
}

/* Timeline */
.timeline {
  position: relative;
  padding-left: 0;
}
.timeline-item {
  position: relative;
  display: flex;
  gap: 12px;
  padding-bottom: 20px;
}
.timeline-item:last-child {
  padding-bottom: 0;
}
.timeline-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--pg-border, #e2e8f0);
  flex-shrink: 0;
  margin-top: 5px;
  position: relative;
  z-index: 1;
}
.timeline-dot-active {
  background: var(--pg-primary, #6366f1);
}
.timeline-line {
  position: absolute;
  left: 4px;
  top: 17px;
  bottom: 0;
  width: 2px;
  background: var(--pg-border, #e2e8f0);
}
.timeline-content {
  flex: 1;
  min-width: 0;
}
</style>
