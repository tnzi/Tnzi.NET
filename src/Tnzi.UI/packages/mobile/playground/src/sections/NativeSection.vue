<script setup lang="ts">
import { ref } from 'vue';
import { showToast } from 'vant';

// Switch
const switchValue = ref(true);

// Stepper
const stepperValue = ref(1);

// Slider
const sliderValue = ref(50);

// Collapse
const activeCollapseNames = ref(['1']);
</script>

<template>
  <div class="native-section">
    <!-- Buttons -->
    <van-cell-group inset title="Buttons (van-button)">
      <div class="btn-grid">
        <van-button type="primary" size="small">Primary</van-button>
        <van-button type="success" size="small">Success</van-button>
        <van-button type="warning" size="small">Warning</van-button>
        <van-button type="danger" size="small">Danger</van-button>
        <van-button size="small">Default</van-button>
        <van-button type="primary" size="small" plain>Plain</van-button>
        <van-button type="primary" size="small" round>Round</van-button>
        <van-button type="primary" size="small" :disabled="true">Disabled</van-button>
      </div>
    </van-cell-group>

    <!-- Cell & Card -->
    <van-cell-group inset title="Cell & Card (van-cell, van-card)">
      <van-cell title="Cell Title" value="Value" label="Description text" />
      <van-cell title="Link Cell" is-link value="Detail" />
      <van-cell title="With Icon" icon="location-o" value="Location" />
    </van-cell-group>

    <div class="card-wrapper">
      <van-card
        num="2"
        price="29.90"
        desc="Vant native card component"
        title="Sample Product"
        thumb="https://fastly.jsdelivr.net/npm/@vant/assets/ipad.jpeg"
      />
    </div>

    <!-- Form Controls -->
    <van-cell-group inset title="Form Controls">
      <van-cell title="Switch" center>
        <template #right-icon>
          <van-switch v-model="switchValue" />
        </template>
      </van-cell>
      <van-cell title="Stepper" center>
        <template #right-icon>
          <van-stepper v-model="stepperValue" />
        </template>
      </van-cell>
      <van-cell title="Slider">
        <template #right-icon>
          <div class="slider-wrapper">
            <van-slider v-model="sliderValue" />
          </div>
        </template>
      </van-cell>
    </van-cell-group>

    <!-- Field -->
    <van-cell-group inset title="Form Fields (van-field)">
      <van-field label="Text" placeholder="Enter text" />
      <van-field label="Password" type="password" placeholder="Enter password" />
      <van-field label="Textarea" type="textarea" rows="2" placeholder="Enter content" />
    </van-cell-group>

    <!-- Tags & Badges -->
    <van-cell-group inset title="Tags & Badges">
      <div class="btn-grid">
        <van-tag type="primary">Primary</van-tag>
        <van-tag type="success">Success</van-tag>
        <van-tag type="warning">Warning</van-tag>
        <van-tag type="danger">Danger</van-tag>
        <van-tag plain type="primary">Plain</van-tag>
        <van-tag round type="primary">Round</van-tag>
        <van-tag size="large" type="primary">Large</van-tag>
      </div>
      <div class="btn-grid">
        <van-badge :content="5">
          <van-button size="mini">Badge</van-button>
        </van-badge>
        <van-badge dot>
          <van-button size="mini">Dot</van-button>
        </van-badge>
      </div>
    </van-cell-group>

    <!-- Collapse -->
    <van-cell-group inset title="Collapse (van-collapse)">
      <van-collapse v-model="activeCollapseNames">
        <van-collapse-item title="About Vant" name="1">
          Vant is a lightweight, customizable mobile component library based on Vue 3.
        </van-collapse-item>
        <van-collapse-item title="About Tnzi" name="2">
          Tnzi UI provides cross-platform business components built on top of Vant and shadcn-vue.
        </van-collapse-item>
        <van-collapse-item title="Getting Started" name="3">
          Install @tnzi/mobile and register with app.use(TnziMobile).
        </van-collapse-item>
      </van-collapse>
    </van-cell-group>

    <!-- Grid -->
    <van-cell-group inset title="Grid (van-grid)">
      <van-grid :column-num="4">
        <van-grid-item icon="photo-o" text="Photo" @click="showToast('Photo')" />
        <van-grid-item icon="search" text="Search" @click="showToast('Search')" />
        <van-grid-item icon="star-o" text="Favorite" @click="showToast('Favorite')" />
        <van-grid-item icon="setting-o" text="Settings" @click="showToast('Settings')" />
      </van-grid>
    </van-cell-group>
  </div>
</template>

<style scoped>
.native-section {
  padding-bottom: 8px;
}

.btn-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 12px;
  align-items: center;
}

.card-wrapper {
  padding: 0 12px;
}

.slider-wrapper {
  width: 120px;
}
</style>
