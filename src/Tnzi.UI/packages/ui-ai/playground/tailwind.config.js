import aiConfig from '../tailwind.config.js';

/** @type {import('tailwindcss').Config} */
export default {
  ...aiConfig,
  content: [
    './index.html',
    './src/**/*.{vue,ts,tsx}',
    '../src/**/*.{vue,ts,tsx}',
  ],
};
