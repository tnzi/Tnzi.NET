import { createApp } from 'vue';
import { createPinia } from 'pinia';
import naive from 'naive-ui';
import TnziNaiveUi from '../../src';
import App from './App.vue';

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.use(naive);
app.use(TnziNaiveUi);
app.mount('#app');
