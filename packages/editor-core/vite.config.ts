import { defineConfig } from 'vitest/config'

// 内核不产出独立构建物：它以源码形式被各宿主的 bundler 消费。
// 此配置只服务于内核自身的契约测试。
export default defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
  },
})
