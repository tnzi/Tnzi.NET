import { createApp } from 'vue';
import { createPinia } from 'pinia';
import Vant from 'vant';
import 'vant/lib/index.css';
import { createTnziMobile } from '../../src';
import App from './App.vue';

const app = createApp(App);
app.use(createPinia());
app.use(Vant);
app.use(createTnziMobile());
app.mount('#app');
