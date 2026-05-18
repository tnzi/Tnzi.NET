<script setup lang="ts">
/**
 * `Workbench` — default landing page for the admin shell.
 *
 * Soybean inspiration: `src/views/home/index.vue` — header banner with a
 * time-of-day greeting + weather strip, four KPI cards (gradient tiles),
 * a line chart for activity over time, and a pie chart for distribution.
 *
 * Data here is intentionally static / canned so the page is presentable
 * the moment a fresh consumer (Acme et al.) lands. Consumers wanting
 * real metrics can either:
 *   - register a route at `/admin` to replace this page entirely, or
 *   - import the underlying `TDashboardPage` + `THeaderBanner` components
 *     and supply their own props.
 */
import { computed } from 'vue'
import { NCard, NGrid, NGi } from 'naive-ui'
import TDashboardPage, {
  type KpiCard,
  type ChartSeriesPoint,
} from '../../components/pages/TDashboardPage.vue'
import THeaderBanner from '../../components/dashboard/THeaderBanner.vue'
import TProjectTimeline from '../../components/dashboard/TProjectTimeline.vue'
import type { TimelineItem } from '../../components/dashboard/TProjectTimeline.vue'
import { useAdminLoginConfig } from '../../plugin/loginConfig'

const loginConfig = useAdminLoginConfig()

const userName = computed(() => loginConfig.user?.userName ?? 'there')

const kpis: KpiCard[] = [
  {
    key: 'visits',
    title: 'Visits',
    value: 5083,
    icon: 'mdi:chart-areaspline',
    gradient: { start: '#ec4786', end: '#b955a4' },
  },
  {
    key: 'sales',
    title: 'Sales',
    value: 537,
    unit: '$',
    icon: 'mdi:cash-multiple',
    gradient: { start: '#865ec0', end: '#5144b4' },
  },
  {
    key: 'downloads',
    title: 'Downloads',
    value: 507471,
    icon: 'mdi:download',
    gradient: { start: '#56cdf3', end: '#719de3' },
  },
  {
    key: 'transactions',
    title: 'Transactions',
    value: 4980,
    icon: 'mdi:cart-arrow-down',
    gradient: { start: '#fcbc25', end: '#f68057' },
  },
]

const lineCategories = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const lineSeries = [
  { name: 'Downloads', data: [1024, 1300, 980, 1450, 1820, 2100, 1700, 2400, 2800, 2600, 3000, 3500] },
  { name: 'Registrations', data: [180, 230, 210, 260, 320, 360, 310, 420, 480, 460, 520, 600] },
]

const pieData: ChartSeriesPoint[] = [
  { name: 'Working', value: 32 },
  { name: 'Meeting', value: 18 },
  { name: 'Breaks', value: 14 },
  { name: 'Other', value: 8 },
]

const timeline: TimelineItem[] = [
  {
    key: '1',
    title: 'Production deploy v0.2.30',
    description: 'Footer copyright + tab bar wiring rolled out to production.',
    time: '2 hours ago',
    tone: 'success',
  },
  {
    key: '2',
    title: 'Phase I.7.7 merged',
    description: 'Breadcrumb + user avatar dropdown shipped to ui-admin.',
    time: 'Yesterday',
    tone: 'info',
  },
  {
    key: '3',
    title: 'Sidebar visual parity (I.7.6)',
    description: 'Logo + mdi icons + active row 3px border accent.',
    time: '2 days ago',
  },
  {
    key: '4',
    title: 'WaveBg blob rewrite (I.7.2)',
    description: 'Soybean-parity organic blobs on the login page.',
    time: '3 days ago',
  },
]
</script>

<template>
  <!-- t-page-scroll: TAdminContent itself no longer scrolls (so CRUD
       pages can pin their pagination); long-form pages opt in by
       wrapping their body with this utility class. -->
  <div class="t-workbench t-page-scroll">
    <THeaderBanner :user-name="userName" />
    <TDashboardPage
      :kpis="kpis"
      :line-series="lineSeries"
      :line-categories="lineCategories"
      :pie-data="pieData"
      line-title="Activity over the year"
      pie-title="Time distribution"
    />
    <NGrid :x-gap="16" :y-gap="16" responsive="screen" item-responsive>
      <NGi span="24 s:24 m:14">
        <NCard :bordered="false" class="t-workbench__card" title="Recent activity">
          <TProjectTimeline :items="timeline" />
        </NCard>
      </NGi>
      <NGi span="24 s:24 m:10">
        <NCard :bordered="false" class="t-workbench__card">
          <h3 class="m-0 mb-12px text-16px font-500">Tips</h3>
          <p class="m-0 text-14px text-muted leading-relaxed">
            Welcome to the admin workbench. The sidebar lists every module the
            backend has loaded; click any group to see its sub-features. You can
            close tabs, drag them to reorder, and the active route is always
            highlighted in the sidebar.
          </p>
        </NCard>
      </NGi>
    </NGrid>
  </div>
</template>

<style scoped>
.t-workbench {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-workbench__card {
  background: var(--tnzi-container-bg, #fff);
  border-radius: 8px;
  /* soybean parity — soft drop shadow on unbordered cards. */
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
</style>
