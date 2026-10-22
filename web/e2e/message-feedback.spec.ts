import { expect, test } from '@playwright/test'
import { expectAppMessage } from './helpers'

test('连续同文案提示重叠时等待本次动作产生的新提示', async ({ page }) => {
  await page.setContent('<main></main>')

  const appendSuccessMessage = () => page.evaluate(() => {
    const message = document.createElement('div')
    message.className = 'n-message n-message--success-type'
    message.textContent = '用户授权已保存'
    document.body.append(message)
  })

  await appendSuccessMessage()
  await expectAppMessage(page, /用户授权已保存/)

  let secondFeedbackSeen = false
  const secondFeedback = expectAppMessage(page, /用户授权已保存/, 2_000).then(() => {
    secondFeedbackSeen = true
  })
  await page.waitForTimeout(50)
  expect(secondFeedbackSeen, '旧提示不能冒充第二次保存的反馈').toBe(false)

  await appendSuccessMessage()
  await secondFeedback

  const messages = page.locator('.n-message').filter({ hasText: /用户授权已保存/ })
  await expect(messages).toHaveCount(2)
})
