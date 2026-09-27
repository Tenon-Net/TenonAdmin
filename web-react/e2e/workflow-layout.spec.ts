import { readFileSync } from 'node:fs'

import { expect, test, type Page } from '@playwright/test'

/**
 * 设计器画布的几何约束,不经登录也不经后端:直接把真实 CSS 注进空白页量盒子。
 * 对齐 Vue 侧同名用例(那边从 `designer.vue` 的 `<style scoped>` 里抠,React 侧样式本来就是独立文件)。
 */
const designerCss = readFileSync(
  new URL('../src/views/workflow/definition/components/wf-designer.css', import.meta.url),
  'utf8',
)

if (!/\.wf-stage\b/.test(designerCss)) {
  throw new Error('wf-designer.css must define the .wf-stage canvas rules')
}

interface LayoutGeometry {
  canvasLeft: number
  canvasWidth: number
  scrollWidth: number
  treeLeft: number
  treeRight: number
  treeWidth: number
}

async function measureTree(page: Page, treeWidth: number): Promise<LayoutGeometry> {
  await page.setContent(`
    <style>
      ${designerCss}
      html, body { margin: 0; }
      .wf-canvas { width: 496px; height: 300px; min-height: 0; }
      .wf-test-tree { width: ${treeWidth}px; height: 40px; }
    </style>
    <div class="wf-canvas">
      <div class="wf-stage">
        <div class="wf-stage-inner">
          <div class="wf-test-tree"></div>
        </div>
      </div>
    </div>
  `)

  return await page.evaluate(() => {
    const canvas = document.querySelector<HTMLElement>('.wf-canvas')
    const tree = document.querySelector<HTMLElement>('.wf-test-tree')

    if (!canvas || !tree) {
      throw new Error('workflow layout fixture is incomplete')
    }

    const canvasRect = canvas.getBoundingClientRect()
    const treeRect = tree.getBoundingClientRect()

    return {
      canvasLeft: canvasRect.left,
      canvasWidth: canvasRect.width,
      scrollWidth: canvas.scrollWidth,
      treeLeft: treeRect.left,
      treeRight: treeRect.right,
      treeWidth: treeRect.width,
    }
  })
}

test.describe('workflow designer layout', () => {
  test('keeps a wide tree fully reachable from the canvas start edge', async ({ page }) => {
    const geometry = await measureTree(page, 1200)

    expect(geometry.treeLeft, JSON.stringify(geometry)).toBeGreaterThanOrEqual(geometry.canvasLeft)
    expect(geometry.scrollWidth, JSON.stringify(geometry)).toBeGreaterThanOrEqual(geometry.treeWidth)
  })

  test('centers a narrow tree in the canvas', async ({ page }) => {
    const geometry = await measureTree(page, 220)
    const canvasCenter = geometry.canvasLeft + geometry.canvasWidth / 2
    const treeCenter = geometry.treeLeft + geometry.treeWidth / 2

    expect(treeCenter).toBeCloseTo(canvasCenter, 5)
  })
})
