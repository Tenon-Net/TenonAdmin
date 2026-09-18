import { describe, expect, it } from 'vitest'
import { coerceVarValue, serializeVars } from './startForm'

describe('startForm', () => {
  it('转换布尔值与有限安全数字', () => {
    expect(coerceVarValue('true')).toBe(true)
    expect(coerceVarValue('false')).toBe(false)
    expect(coerceVarValue('42')).toBe(42)
    expect(coerceVarValue('-12.5')).toBe(-12.5)
  })

  it('保留超出安全整数范围的十进制字符串', () => {
    expect(coerceVarValue('1500000000000000001')).toBe('1500000000000000001')
    expect(coerceVarValue('9007199254740992')).toBe('9007199254740992')
  })

  it('只转换规范文本能无损往返的小数', () => {
    expect(coerceVarValue('1.0')).toBe(1)
    expect(coerceVarValue('0.10')).toBe(0.1)
    expect(coerceVarValue('-0.00')).toBe(0)
    expect(coerceVarValue('9007199254740991.1')).toBe('9007199254740991.1')
  })

  it('序列化时忽略 __proto__ 键', () => {
    expect(serializeVars([
      { key: '__proto__', value: '{"polluted":true}' },
      { key: 'enabled', value: 'true' },
    ])).toBe('{"enabled":true}')
    expect(({} as { polluted?: boolean }).polluted).toBeUndefined()
  })
})
