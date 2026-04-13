import { createApp } from 'vue';
import { createTnziUi } from '@/plugin';
import { setStoreHttpClient } from '@/stores/factory';
import { createHttpClient } from '@tnzi/core/http/http';
import App from './App.vue';

// Playground mock HTTP client — no real backend
const mockHttpClient = createHttpClient({
  baseURL: 'http://localhost:5000/api',
});

const app = createApp(App);
app.use(createTnziUi());

// Inject mock HTTP client so store composables don't throw during setup
setStoreHttpClient(mockHttpClient);

app.mount('#app');
