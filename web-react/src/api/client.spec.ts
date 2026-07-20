// @vitest-environment node
//
// **这份用 node 环境跑,不用默认的 happy-dom。** 不是偏好,是判别力:
// 机制①(请求体重放)靠的是 `Request.body` 的一次性流语义,而 **happy-dom 没实现它** ——
// 它把 body 缓冲起来,`new Request(已消费的 Request)` 照样吐得出原 body。实测:
//     happy-dom: 原请求 bodyUsed=true,但 `new Request(原请求)` 仍然拿到 '{"a":1}'
//     undici   : 同样情况抛 `TypeError: Cannot construct a Request with a Request object
//                that has already been used.`,克隆则正常
// 后果:在 happy-dom 下把 `replayable.set(request, request.clone())` 改成存原请求、
// 甚至整行删掉,这份用例**全绿** —— 那正是机制①存在的唯一理由,却测不出来。
// 换句话说,环境本身没有那个陷阱,再怎么调桩也造不出来。
//
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import type { client as Client } from './client'
import type { useUserStore as UseUserStore } from '@/stores/user'

/**
 * 中间件的三个机制。它们防的失败模式有个共同点:**都只在时序恰好对上时才出现**,
 * 事后从日志里几乎归因不到,所以这里逐条钉死。
 *
 * ① 请求体重放 —— `Request.body` 是一次性流,首次 fetch 就被消费。令牌恰好在一次 POST 中途过期时,
 *    直接重放原请求会发出一个**空 body 的 POST**:后端收到的是「字段全缺」,报的是校验错误,
 *    而真正的原因在三层之外。
 * ② 并发 401 合流 —— 一屏十个组件同时拉数据、令牌同时过期,不合流就是十次刷新;
 *    后端刷新令牌多为一次性轮换,第一次成功之后剩下九次拿着旧的刷新令牌全部失败,
 *    表现为「偶发被踢下线」。
 * ③ 刷新自身 401 不递归。
 */
type Call = { url: string; method: string; body: string | null; auth: string | null }

const SESSION = { accessToken: 'NEW', refreshToken: 'R2', userId: 1, account: 'a', name: 'n', mustChangePassword: false }
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

let calls: Call[]
let assigned: string[]

let client: typeof Client
let useUserStore: typeof UseUserStore

/**
 * 打桩 fetch,然后**重新求值 `client` 模块**再取它。
 *
 * 这一步不是仪式:`createClient()` 在**模块求值时**就把 `globalThis.fetch` 抓进闭包了
 * (openapi-fetch 的 `baseFetch = globalThis.fetch` 默认值),晚于它打的桩一律不生效 ——
 * 症状是用例去打真网络、报 `NetworkError: ... http://localhost:3000/...`。
 * (`stores/app.ts` 的 matchMedia 桥是同一个模式。)
 *
 * `handler` 决定每次调用返回什么;记账与 body 读取在这里统一做。
 *
 * **body 必须用 `req.text()` 直接读,不能用 `req.clone().text()`。** 这一条是踩出来的:
 * 用 clone 读的话原始请求的流**从没被消费过**,而「一次性流」正是机制①要防的东西 ——
 * 于是把 `replayable.set(request, request.clone())` 改成存原请求(甚至整行删掉),
 * 用例**照样全绿**。桩自己把要测的那个 bug 消掉了。
 * 真实的 fetch 会消费请求体,桩就必须也消费,否则测的是一个不存在的世界。
 */
async function setup(handler: (c: Call, n: number) => Response | Promise<Response>) {
  // node 环境没有文档源,相对 URL 无法解析(生产里 baseUrl 为空 = 同源,靠的正是浏览器那个源)。
  // 给一个绝对源只为让 URL 解析得动,被测的路径部分原样保留。
  vi.stubEnv('VITE_API_BASE', 'http://api.test')
  // 记账数组**每次 setup 新建、由桩闭包持有**,不是往模块级的 `calls` 上推。
  //
  // 这是踩出来的:原先桩里写 `calls.push(...)`,而 `calls` 是模块级变量、`resetModules` 不碰它,
  // 上一个 setup 建的 fetch 桩又仍被上一个 client 模块闭包持有 —— 它的**迟到回调会推进新数组**。
  // 症状是全量跑时偶发一条红(实测 5 次全量里中过 1 次:「没有 refreshToken 时连刷新都不发」
  // 数到了 1 次刷新,而那条用例自己只发一个 GET),单跑那个文件永远绿。
  // 每个 setup 各持一份,迟到回调就只会推进自己那个已经没人看的数组。
  const mine: Call[] = []
  calls = mine
  vi.stubGlobal('fetch', async (req: Request) => {
    const body = req.method === 'GET' || req.method === 'HEAD' ? null : await req.text()
    const c: Call = { url: req.url, method: req.method, body, auth: req.headers.get('Authorization') }
    mine.push(c)
    return handler(c, mine.length - 1)
  })
  vi.resetModules()
  ;({ client } = await import('./client'))
  ;({ useUserStore } = await import('@/stores/user'))
  useUserStore.setState({ accessToken: 'OLD', refreshToken: 'R1', userInfo: null })
}

beforeEach(() => {
  calls = [] // setup() 会换成它自己那份;这里只保证「没调 setup 的用例」也有个空数组可读
  localStorage.clear() // persist 会在每次重新求值时水合,不清就跨用例串味
  // node 环境没有 `window`。被测代码用 `window.location` 是对的(它是浏览器专属模块),
  // 所以桩要按浏览器的形状给,而不是把生产代码改成裸 `location` 去迁就测试环境。
  //
  // `localStorage` 必须一并挂上:zustand 的 persist 是按 `typeof window !== 'undefined'` 判断有没有
  // 浏览器存储的,一旦定义了 `window` 它就会去拿 `window.localStorage` —— 桩里没有的话报的是
  // `Cannot read properties of undefined (reading 'setItem')`,和 location 毫无关系。
  const mineAssigned: string[] = []
  assigned = mineAssigned
  vi.stubGlobal('window', {
    localStorage: globalThis.localStorage,
    location: { pathname: '/system/user', assign: (u: string) => mineAssigned.push(u) },
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.unstubAllEnvs()
  vi.resetModules()
})

const isRefresh = (c: Call) => c.url.includes('/auth/refresh')
const refreshCount = () => calls.filter(isRefresh).length

describe('① 令牌过期重放:请求体不能丢', () => {
  it('POST 撞 401 → 刷新后重放,body 与原请求逐字相同、令牌是新的', async () => {
    const payload = { name: '张三', code: 'zhangsan', sortCode: 42 }
    await setup((c) => {
      if (isRefresh(c)) return json({ code: 0, data: SESSION })
      // 第一次业务请求 401,重放那次成功
      const businessCalls = calls.filter((x) => !isRefresh(x)).length
      return businessCalls === 1 ? json({}, 401) : json({ code: 0, data: { id: 9 } })
    })

    await client.POST('/api/v1/sys/dict/type', { body: payload as never })

    const business = calls.filter((c) => !isRefresh(c))
    expect(business).toHaveLength(2)
    // 这才是重点:重放那次的 body **不是空串**,而且与首次逐字相同。
    expect(business[1]!.body).toBe(business[0]!.body)
    expect(JSON.parse(business[1]!.body!)).toEqual(payload)
    // 重放必须带**新**令牌,否则立刻再吃一个 401。
    expect(business[0]!.auth).toBe('Bearer OLD')
    expect(business[1]!.auth).toBe('Bearer NEW')
  })

  it('GET 无 body,不克隆也能重放', async () => {
    await setup((c) => {
      if (isRefresh(c)) return json({ code: 0, data: SESSION })
      const businessCalls = calls.filter((x) => !isRefresh(x)).length
      return businessCalls === 1 ? json({}, 401) : json({ code: 0, data: [] })
    })

    await client.GET('/api/v1/sys/dict/items/{typeCode}', { params: { path: { typeCode: 'gender' } } })

    const business = calls.filter((c) => !isRefresh(c))
    expect(business).toHaveLength(2)
    expect(business[1]!.auth).toBe('Bearer NEW')
  })
})

describe('② 并发 401 合流到同一次刷新', () => {
  it('三个请求同时 401 → 只刷新一次,三个都被重放', async () => {
    await setup((c) => {
      if (isRefresh(c)) return json({ code: 0, data: SESSION })
      // 凡是带旧令牌来的都 401(重放带的是新令牌 → 放行)。
      // 这比「第几次调用」的写法结实:它描述的是**服务端真实规则**,不依赖请求到达顺序。
      return c.auth === 'Bearer OLD' ? json({}, 401) : json({ code: 0, data: {} })
    })

    await Promise.all([
      client.GET('/api/v1/sys/dict/type/page', { params: { query: {} } as never }),
      client.GET('/api/v1/sys/config/page', { params: { query: {} } as never }),
      client.GET('/api/v1/sys/dict/item/page', { params: { query: {} } as never }),
    ])

    expect(refreshCount()).toBe(1)
    const business = calls.filter((c) => !isRefresh(c))
    expect(business.filter((c) => c.auth === 'Bearer OLD')).toHaveLength(3) // 三次原始请求
    expect(business.filter((c) => c.auth === 'Bearer NEW')).toHaveLength(3) // 三次重放
  })

  it('刷新完成后 refreshing 归位:下一轮 401 会重新刷新,不吃上一次的缓存', async () => {
    await setup((c) => {
      if (isRefresh(c)) return json({ code: 0, data: SESSION })
      return c.auth === 'Bearer OLD' ? json({}, 401) : json({ code: 0, data: {} })
    })
    await client.GET('/api/v1/sys/dict/type/page', { params: { query: {} } as never })
    expect(refreshCount()).toBe(1)

    // 令牌再次过期(把 store 拨回旧令牌),应当再刷一次而不是复用上次那个已 settle 的 Promise。
    useUserStore.setState({ accessToken: 'OLD', refreshToken: 'R1' })
    await client.GET('/api/v1/sys/config/page', { params: { query: {} } as never })
    expect(refreshCount()).toBe(2)
  })
})

describe('③ 刷新失败:清会话 + 跳登录,且不递归', () => {
  it('刷新自身 401 → 不触发第二次刷新,清会话并跳 /login', async () => {
    await setup(() => json({}, 401)) // 什么都 401,包括刷新本身

    await client.GET('/api/v1/sys/dict/type/page', { params: { query: {} } as never })

    expect(refreshCount()).toBe(1) // 恰好一次:没有递归
    expect(useUserStore.getState().accessToken).toBe('')
    expect(assigned).toEqual(['/login'])
  })

  it('没有 refreshToken 时连刷新都不发,直接跳登录', async () => {
    await setup(() => json({}, 401))
    useUserStore.setState({ accessToken: 'OLD', refreshToken: '' }) // 必须在 setup 之后:它自己会置回 R1

    await client.GET('/api/v1/sys/dict/type/page', { params: { query: {} } as never })

    expect(refreshCount()).toBe(0)
    expect(assigned).toEqual(['/login'])
  })

  it('已经在登录页时不再跳转(否则是刷新循环)', async () => {
    const mineAssigned: string[] = []
    assigned = mineAssigned
    vi.stubGlobal('window', {
      localStorage: globalThis.localStorage,
      location: { pathname: '/login', assign: (u: string) => mineAssigned.push(u) },
    })
    await setup(() => json({}, 401))
    useUserStore.setState({ refreshToken: '' })

    await client.GET('/api/v1/sys/dict/type/page', { params: { query: {} } as never })

    expect(assigned).toEqual([])
  })

  /**
   * **`bare` 需要一条属于自己的判据。**
   *
   * 变异实测:把 `bare` 换成 `client`(刷新客户端挂上刷新中间件)——**八条用例全绿**;
   * 单独删掉 `onResponse` 里 `/auth/refresh` 那条 URL 短路——**也全绿**;
   * 两个同时拆才红(而且是 5 秒超时,真的无限递归)。
   * 也就是说防递归是**两道冗余的闸**,任何单点变异都测不出来 ——
   * 将来谁把 `bare` 整个删掉,上面那些用例一条都不会红。
   *
   * 所以这条不测「会不会递归」(测不出),改测 `bare` 的**直接可观测后果**:
   * 它没挂 authMiddleware,所以刷新请求**不带** Authorization 头。
   * 这既钉住了 `bare` 的存在,顺带说明了它真正的第二个价值:
   * 刷新用的是 refreshToken,本就不该把一个已经过期的 accessToken 捎上去。
   */
  it('刷新请求走 bare 客户端:不带 Authorization 头', async () => {
    await setup((c) => (isRefresh(c) ? json({ code: 0, data: SESSION }) : c.auth === 'Bearer OLD' ? json({}, 401) : json({ code: 0, data: {} })))

    await client.GET('/api/v1/sys/dict/type/page', { params: { query: {} } as never })

    const refresh = calls.filter(isRefresh)
    expect(refresh).toHaveLength(1)
    expect(refresh[0]!.auth).toBeNull()
  })

  it('登录接口的 401 不被拦截(那是"密码错误",不是"令牌过期")', async () => {
    await setup(() => json({ code: 40001 }, 401))

    await client.POST('/api/v1/auth/login', { body: { account: 'a', password: 'x' } as never })

    expect(refreshCount()).toBe(0)
    expect(assigned).toEqual([])
    expect(useUserStore.getState().accessToken).toBe('OLD') // 会话没被清
  })
})
