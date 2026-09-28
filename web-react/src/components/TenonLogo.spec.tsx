import { describe, it, expect } from 'vitest'
import { logoColors } from './TenonLogo'

describe('logoColors', () => {
  it('暗色', () => {
    expect(logoColors(true)).toEqual({ bg: '#181A2A', mark: '#8B91FF' })
  })
  it('亮色', () => {
    expect(logoColors(false)).toEqual({ bg: '#646CFF', mark: '#FFFFFF' })
  })
})
