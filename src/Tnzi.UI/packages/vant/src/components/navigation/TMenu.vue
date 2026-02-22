<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { IMenuEmits, IMenuItem, IMenuProps } from '@tnzi/core/components';

const props = withDefaults(defineProps<IMenuProps>(), {
  items: () => [],
  openedKeys: () => [],
});

const emit = defineEmits<IMenuEmits>();

const internalOpenKeys = ref<string[]>([...(props.openedKeys ?? [])]);

watch(
  () => props.openedKeys,
  (value) => {
    internalOpenKeys.value = [...(value ?? [])];
  },
);

const visibleItems = computed(() => props.items.filter((item) => !item.hidden));

const isOpen = (key: string) => internalOpenKeys.value.includes(key);

const toggleOpen = (item: IMenuItem) => {
  if (isOpen(item.key)) {
    internalOpenKeys.value = internalOpenKeys.value.filter((key) => key !== item.key);
  } else {
    internalOpenKeys.value = [...internalOpenKeys.value, item.key];
  }
  emit('openChange', internalOpenKeys.value);
};

const onSelect = (item: IMenuItem) => {
  if (item.disabled) return;
  if (item.children?.length) {
    toggleOpen(item);
    return;
  }
  emit('select', item.key, item);
};

const onSelectChild = (item: IMenuItem) => {
  if (item.disabled) return;
  emit('select', item.key, item);
};
</script>

<template>
  <section class="t-menu" :class="props.class" :style="props.style">
    <van-cell-group inset>
      <template v-for="item in visibleItems" :key="item.key">
        <van-cell
          :title="item.label"
          :value="item.badge != null ? String(item.badge) : ''"
          :label="item.path"
          :is-link="!item.disabled"
          :class="{ 'active-cell': props.activeKey === item.key }"
          @click="onSelect(item)"
        />

        <van-cell-group
          v-if="item.children?.length && isOpen(item.key)"
          class="submenu"
          inset
        >
          <van-cell
            v-for="child in item.children"
            :key="child.key"
            :title="child.label"
            :label="child.path"
            :is-link="!child.disabled"
            :class="{ 'active-cell': props.activeKey === child.key }"
            @click="onSelectChild(child)"
          />
        </van-cell-group>
      </template>
    </van-cell-group>
  </section>
</template>

<style scoped>
.submenu {
  margin: 6px 0 10px;
}

.active-cell {
  --van-cell-text-color: var(--van-primary-color);
  font-weight: 600;
}
</style>
