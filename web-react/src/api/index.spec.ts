import { describe, it, expect } from 'vitest'
import { unwrap, ApiError, pageParams, toPage } from './index'

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

  it('rejects a successful response without an envelope', () => {
    const res = {
      data: { value: 'not-an-envelope' },
      error: undefined,
      response: new Response(null, { status: 200 }),
    }
    expect(() => unwrap(res)).toThrow(ApiError)
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

/**
 * `pageParams` / `toPage` 是**消费者接缝**:导出给消费者写自己的 `api/<域>.ts` 用,
 * 站内没有调用方(见 `docs/coding-standards.md` §2.1)。正因为没有调用方,它们坏了不会有任何东西变红 ——
 * 改错 `Current`/`Size` 的大小写、或让 `toPage` 漏掉 `total`,消费者那边才发现。
 * Vue 侧同样没测(那是同一个缺口,不是本模板独有的)。
 */
describe('分页接缝(站内无调用方,只能靠用例守着)', () => {
  it('pageParams:前端 {page,pageSize} → 后端 PascalCase {Current,Size}', () => {
    expect(pageParams({ page: 3, pageSize: 20 })).toEqual({ Current: 3, Size: 20 })
  })

  it('toPage:后端 PagedList → ProTable 契约的 {items,total}', () => {
    const res = {
      data: { code: 0, data: { current: 2, size: 10, total: 57, items: [{ id: 1 }] } },
      error: undefined,
      response: new Response(null, { status: 200 }),
    }
    expect(toPage(res)).toEqual({ items: [{ id: 1 }], total: 57 })
  })

  it('rejects malformed paged data', () => {
    const res = {
      data: { code: 0, data: { current: 2, size: 10, total: '57', items: {} } },
      error: undefined,
      response: new Response(null, { status: 200 }),
    }
    expect(() => toPage(res)).toThrow(ApiError)
  })

  it('toPage 沿用 unwrap 的错误分支:业务错照样抛 ApiError', () => {
    const res = {
      data: { code: 40001, msgKey: 'error.x' },
      error: undefined,
      response: new Response(null, { status: 200 }),
    }
    expect(() => toPage(res)).toThrow(ApiError)
  })
})
