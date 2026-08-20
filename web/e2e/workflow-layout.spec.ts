import { readFileSync } from 'node:fs'

import { expect, test, type Page } from '@playwright/test'

const designerUrl = new URL('../src/views/workflow/definition/designer.vue', import.meta.url)
const designerSource = readFileSync(designerUrl, 'utf8')
const scopedStyle = designerSource.match(/<style scoped>([\s\S]*?)<\/style>/)?.[1]

if (!scopedStyle) {
  throw new Error('designer.vue must contain a scoped style block')
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
      ${scopedStyle}
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
