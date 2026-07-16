import { describe, it, expect } from 'vitest'
import { unwrap, ApiError } from './index'

// 零 mock:直接手工构造 openapi-fetch 返回形状 { data, error, response },测 unwrap 的分支覆盖。
describe('unwrap', () => {
  it('2xx 信封 code 0 → 返回 data.data', () => {
    const res = {
      data: { code: 0, data: { foo: 'bar' } },
      error: undefined,
      response: new Response(null, { status: 200 }),
    }
    expect(unwrap(res)).toEqual({ foo: 'bar' })
  })

  it('2xx code≠0 → 抛 ApiError 且 code/msgKey/args 透传', () => {
    const res = {
      data: { code: 40001, msgKey: 'error.auth.passwordWrong', args: { a: 1 }, message: 'bad' },
      error: undefined,
      response: new Response(null, { status: 200 }),
    }
    expect(() => unwrap(res)).toThrow(ApiError)
    try {
      unwrap(res)
      expect.unreachable()
    } catch (e) {
      const err = e as ApiError
      expect(err.code).toBe(40001)
      expect(err.msgKey).toBe('error.auth.passwordWrong')
      expect(err.args).toEqual({ a: 1 })
    }
  })

  it('非 2xx 且 error 带 code(如 401 信封)→ ApiError 用业务 code', () => {
    const res = {
      data: undefined,
      error: { code: 40006, msgKey: 'error.auth.tokenInvalid' },
      response: new Response(null, { status: 401 }),
    }
    try {
      unwrap(res)
      expect.unreachable()
    } catch (e) {
      const err = e as ApiError
      expect(err.code).toBe(40006)
      expect(err.msgKey).toBe('error.auth.tokenInvalid')
    }
  })

  it('非 2xx ProblemDetails(无 code)→ ApiError(response.status),message 取 title ?? detail ?? statusText', () => {
    // title 优先于 detail
    const withTitle = {
      data: undefined,
      error: { title: 'Bad Request', detail: 'field invalid' },
      response: new Response(null, { status: 400, statusText: 'Bad Request' }),
    }
    try {
      unwrap(withTitle)
      expect.unreachable()
    } catch (e) {
      const err = e as ApiError
      expect(err.code).toBe(400)
      expect(err.message).toBe('Bad Request')
    }

    // 无 title 时退回 detail
    const detailOnly = {
      data: undefined,
      error: { detail: 'field invalid' },
      response: new Response(null, { status: 422 }),
    }
    try {
      unwrap(detailOnly)
      expect.unreachable()
    } catch (e) {
      const err = e as ApiError
      expect(err.code).toBe(422)
      expect(err.message).toBe('field invalid')
    }
  })
})
