import { createApp } from 'vue';
import { createPinia } from 'pinia';
import Vant from 'vant';
import 'vant/lib/index.css';
import TnziVant from '../../src';
import App from './App.vue';

const app = createApp(App);
app.use(createPinia());
app.use(Vant);
app.use(TnziVant);
app.mount('#app');
