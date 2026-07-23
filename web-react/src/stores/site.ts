// 站点品牌信息(匿名 GET /sys/config/site 下发)。登录页各皮肤、登录后框架(侧栏/顶栏/水印)共用同一份,
// 只拉一次(模块级在途缓存);改配置保存后 `load(true)` 强制重取即时生效。
// 对应 Vue 侧 `composables/useSite.ts`(那边是 `reactive` + 模块级变量,这里是 zustand store)。
import { create } from 'zustand'
import { configApi } from '@/api'

export interface SiteInfo {
  title: string
  subtitle: string
  copyright: string
  copyrightUrl: string
  logo: string
  captchaEnabled: boolean
  smsLoginEnabled: boolean
}

// 版本号是构建期常量(vite.config 的 define 注入 package.json 的 version),**不走后端配置**。
// typeof 兜底:define 仅在 vite 启动时生效;老会话 HMR 到本文件而未重启时该全局不存在,避免 ReferenceError。
export const appVersion: string = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : ''

// title 初值给内置名,防 siteInfo 到达前品牌词首帧空白。
const DEFAULTS: SiteInfo = {
  title: 'TenonAdmin',
  subtitle: '',
  copyright: '',
  copyrightUrl: '',
  logo: '',
  captchaEnabled: false,
  smsLoginEnabled: false,
}

// 在途缓存:模块级,不进 state(Promise 不该进 devtools/序列化)。与 `stores/dict.ts` 同一个理由。
let inflight: Promise<void> | null = null

export interface SiteState {
  site: SiteInfo
  /** 拉取站点信息;`force` 之外只发一次。失败保留内置默认,不阻塞登录/渲染。 */
  load: (force?: boolean) => Promise<void>
}

export const useSiteStore = create<SiteState>()((set) => ({
  site: DEFAULTS,
  load: (force = false) => {
    if (inflight && !force) return inflight
    inflight = configApi
      .siteInfo()
      .then((s) => {
        set({
          site: {
            // 只有 title 走 `||` 而不是 `??`:后端把它配成空串时应当回落内置名,
            // 而其余字段空串就是"这项没配",原样留空。
            // (Vue 侧写的是 `if (s.title) site.title = s.title` —— 首次加载两者等价;
            // 差别只在"已成功拉过一次、再 force 重取却拿到空 title"时:那边留住上一次的值,
            // 这边回落内置名。取这边的写法是因为结果只取决于本次响应,不取决于调用历史。)
            title: s.title || DEFAULTS.title,
            subtitle: s.subtitle ?? '',
            copyright: s.copyright ?? '',
            copyrightUrl: s.copyrightUrl ?? '',
            logo: s.logo ?? '',
            captchaEnabled: !!s.captchaEnabled,
            smsLoginEnabled: !!s.smsLoginEnabled,
          },
        })
      })
      .catch(() => {
        // 拉取失败保留内置默认(title=TenonAdmin),不阻塞登录/渲染。
        //
        // 注意这里**与 `stores/dict.ts` 的失败语义不同,是照搬 Vue 侧的有意选择**:
        // dict 失败会清 pending → 下次访问自然重试;这里 `inflight` 留着 → **失败不会自动重试**,
        // 除非 `load(true)` 或整页重载。后果是"后端刚起来那一下拉失败,品牌词就一直是内置名"。
        // 站点信息是纯装饰、且每个消费者都会调 load(),自动重试反而会在后端持续不可用时反复打点。
        // 真要改语义,两个模板一起改,别只改一边。
      })
    return inflight
  },
}))
