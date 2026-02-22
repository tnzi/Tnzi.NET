<template>
  <div class="device-frame">
    <div class="device-notch" />
    <div class="device-screen">
      <slot />
    </div>
    <div class="device-home-indicator" />
  </div>
</template>

<style scoped>
.device-frame {
  position: relative;
  width: 390px;
  height: 844px;
  border-radius: 40px;
  border: 10px solid #1a1a1a;
  background: #1a1a1a;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3), inset 0 0 0 2px #333;
  overflow: hidden;
}

.device-notch {
  position: absolute;
  top: 10px;
  left: 50%;
  transform: translateX(-50%);
  width: 126px;
  height: 24px;
  border-radius: 0 0 16px 16px;
  background: #1a1a1a;
  z-index: 10;
  pointer-events: none;
}

.device-screen {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 30px;
  overflow-y: auto;
  overflow-x: hidden;
  background: var(--van-background, #f7f8fa);
  /* Create new containing block so position:fixed inside works relative to device screen */
  transform: translateZ(0);
}

.device-home-indicator {
  position: absolute;
  bottom: 8px;
  left: 50%;
  transform: translateX(-50%);
  width: 134px;
  height: 5px;
  border-radius: 3px;
  background: #666;
  pointer-events: none;
}
</style>
