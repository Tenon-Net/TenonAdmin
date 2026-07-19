import { reactive } from 'vue'
import { configApi } from '@/api'

// 站点品牌信息(匿名 GET /sys/config/site 下发)。App.vue 启动、登录页各皮肤、登录后框架(侧栏/顶栏/水印)
// 共用同一份响应式数据,只拉一次(模块级缓存);改配置保存后 loadSite(true) 强制重取即时生效。
export interface SiteInfo {
  title: string
  subtitle: string
  copyright: string
  copyrightUrl: string
  captchaEnabled: boolean
  smsLoginEnabled: boolean
}

// 版本号是构建期常量(vite.config 的 define 注入 package.json 的 version),不走后端配置。
// typeof 兜底:define 仅在 vite 启动时生效;老会话 HMR 到本文件而未重启时该全局不存在,避免 ReferenceError。
export const appVersion: string = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : ''

// title 初值给内置名,防 siteInfo 到达前品牌词首帧空白。
const site = reactive<SiteInfo>({
  title: 'TenonAdmin',
  subtitle: '',
  copyright: '',
  copyrightUrl: '',
  captchaEnabled: false,
  smsLoginEnabled: false,
})

let inflight: Promise<void> | null = null

export function loadSite(force = false): Promise<void> {
  if (inflight && !force) return inflight
  inflight = configApi
    .siteInfo()
    .then((s) => {
      if (s.title) site.title = s.title
      site.subtitle = s.subtitle ?? ''
      site.copyright = s.copyright ?? ''
      site.copyrightUrl = s.copyrightUrl ?? ''
      site.captchaEnabled = !!s.captchaEnabled
      site.smsLoginEnabled = !!s.smsLoginEnabled
    })
    .catch(() => {
      // 拉取失败保留内置默认(title=TenonAdmin),不阻塞登录/渲染
    })
  return inflight
}

export function useSite() {
  return { site, appVersion, loadSite }
}
