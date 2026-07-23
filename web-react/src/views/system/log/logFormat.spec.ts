import { describe, it, expect } from 'vitest'
import { operatorText, loginNameText, prettyParam, loginResultKey } from './logFormat'

describe('logFormat', () => {
  describe('operatorText', () => {
    it('有姓名 → 姓名', () => {
      expect(operatorText({ operatorName: '张三', operatorId: 9 })).toBe('张三')
    })
    it('无姓名有 id → 原始 id 串', () => {
      expect(operatorText({ operatorName: null, operatorId: 9 })).toBe('9')
    })
    it('姓名与 id 皆缺 → 占位', () => {
      expect(operatorText({ operatorName: null, operatorId: null })).toBe('—')
    })
  })

  describe('loginNameText', () => {
    it('有姓名 → 姓名', () => {
      expect(loginNameText({ name: '李四', account: 'lisi' })).toBe('李四')
    })
    it('无姓名有账号 → 账号', () => {
      expect(loginNameText({ name: null, account: 'lisi' })).toBe('lisi')
    })
    it('姓名与账号皆缺 → 占位', () => {
      expect(loginNameText({ name: null, account: '' })).toBe('—')
    })
  })

  describe('prettyParam', () => {
    it('合法 JSON → 缩进 2 美化(多行)', () => {
      const out = prettyParam('{"a":1,"b":2}')
      expect(out).toBe('{\n  "a": 1,\n  "b": 2\n}')
    })
    it('非法 JSON → 原样返回', () => {
      expect(prettyParam('not json')).toBe('not json')
    })
  })

  describe('loginResultKey', () => {
    it('0 → 成功键', () => {
      expect(loginResultKey(0)).toBe('log.success')
    })
    it('40004 → 账号锁定键', () => {
      expect(loginResultKey(40004)).toBe('error.auth.accountLocked')
    })
    it('40001 → 密码错键', () => {
      expect(loginResultKey(40001)).toBe('error.auth.passwordWrong')
    })
    it('未命中的码 → null(调用方回落原始数字)', () => {
      expect(loginResultKey(99999)).toBeNull()
    })
  })
})
