import { test, expect } from 'vitest'

/**
 * 覆盖 `test-setup.ts` 那个内存 storage。它是本模板里少数「坏了不会当场报错、只会让别处莫名其妙」的文件:
 * zustand 的 persist 在**建 store 时**就取走 `localStorage`,取到 undefined 的话每个 `set()` 都抛
 * `Cannot read properties of undefined (reading 'setItem')`,而报错栈指向业务 action,和存储八竿子打不着。
 * 与其等 R4 搬 store 时被那个错误栈坑一次,不如在这里先把它钉住。
 */
test('test-setup 装上了可用的内存 storage', () => {
  localStorage.setItem('k', 'v')
  expect(localStorage.getItem('k')).toBe('v')
  expect(localStorage.length).toBe(1)
  expect(localStorage.key(0)).toBe('k')
  expect(localStorage.getItem('缺失的键')).toBeNull()
  localStorage.removeItem('k')
  expect(localStorage.getItem('k')).toBeNull()
  expect(localStorage.length).toBe(0)
  localStorage.setItem('a', '1')
  localStorage.clear()
  expect(localStorage.length).toBe(0)
})
