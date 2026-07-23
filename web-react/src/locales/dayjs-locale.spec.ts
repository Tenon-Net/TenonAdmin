import { describe, it, expect } from 'vitest'
import dayjsGenerateConfig from '@rc-component/picker/es/generate/dayjs'
import antdZhCN from 'antd/locale/zh_CN'
import antdEnUS from 'antd/locale/en_US'
import '@/locales' // 副作用:注册 dayjs 的 zh-cn(与 i18next 接线同一个模块)

/**
 * DatePicker/Calendar 面板里的**月份名、星期缩写、周起始日**归 dayjs 管,不归 antd 的 locale 对象管:
 * `ConfigProvider locale` 只切按钮/占位符那类 chrome 文案,面板内容由
 * `@rc-component/picker/generate/dayjs` 拿 `DatePicker.lang.locale` 去查 dayjs 的 localeData。
 * 而 antd **一行 dayjs locale 都不 import**,dayjs 对未注册的 locale 名是**静默回落 en**。
 * 漏掉注册的症状:中文界面 + `Jan/Feb` + `Su~Sa` + 周从**周日**起 —— 一页两种语言,
 * tsc / lint / 其余用例全绿,控制台一声不吭。
 *
 * 判据走的是 **antd 自己那条路**(它自己的 generate config、它自己的 dayjs 实例、它自己的
 * locale 标识符),而不是 `dayjs().locale('zh-cn')`。这不是讲究,是判别力:
 *   - 直接断 `dayjs()` 的话,`node_modules` 里有两份 dayjs 时(我们注册进 A、antd 用 B)**照样绿**,
 *     而面板依旧是英文 —— 那条用例分不出这两个世界。
 *   - locale 标识符从 antd 的 locale 对象里取而不是硬编码 `'zh_CN'`,antd 改名时这里会跟着走。
 */
const pickerLocale = dayjsGenerateConfig.locale

describe('dayjs locale 注册(DatePicker 面板的真正语言来源)', () => {
  it('中文:月份名是中文、星期缩写是中文、周从周一起', () => {
    const id = antdZhCN.DatePicker!.lang.locale // 'zh_CN'
    expect(pickerLocale.getShortMonths!(id).slice(0, 3)).toEqual(['1月', '2月', '3月'])
    expect(pickerLocale.getShortWeekDays!(id)[0]).toBe('日')
    expect(pickerLocale.getWeekFirstDay(id)).toBe(1) // 未注册时回落 en → 0(周日)
  })

  it('英文:dayjs 内置 en,不必 import', () => {
    const id = antdEnUS.DatePicker!.lang.locale // 'en_US'
    expect(pickerLocale.getShortMonths!(id).slice(0, 3)).toEqual(['Jan', 'Feb', 'Mar'])
    expect(pickerLocale.getWeekFirstDay(id)).toBe(0)
  })
})
