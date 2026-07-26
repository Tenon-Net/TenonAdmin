import { describe, expect, it } from 'vitest'
import dayjs from 'dayjs'
import type { SysJob } from '@/types/api'
import {
  blankJob, describeTrigger, formToInput, pairsToProps, parseHeaders, parsePropsJson, propsToPairs, rowToForm,
} from './jobForm'

/** 造一行最小 SysJob(覆盖必填,测试各自覆写关心字段)。 */
function makeRow(patch: Partial<SysJob>): SysJob {
  return {
    id: 1, code: 'demo', name: '演示任务', handlerKind: 1, handlerName: 'App.DemoJob',
    propsJson: null, triggerKind: 1, cronExpression: '0 0 0 * * ?',
    intervalSeconds: null, oneShotTime: null, startTime: null, endTime: null,
    misfireStrategy: 1, concurrencyMode: 1, status: 1,
    nextRunTime: null, lastRunTime: null, numberOfRuns: 0, numberOfErrors: 0, consecutiveErrors: 0,
    timeoutSeconds: 0, retryCount: 0, retryIntervalSeconds: 30, failAlertThreshold: 0,
    alertByNotice: false, alertEmails: null, isSystem: false, remark: null,
    ...patch,
  }
}

describe('属性包键值对', () => {
  it('parsePropsJson:null 值收成空串,坏 JSON 给空对象', () => {
    expect(parsePropsJson('{"a":"1","b":null}')).toEqual({ a: '1', b: '' })
    expect(parsePropsJson('not json')).toEqual({})
    expect(parsePropsJson(null)).toEqual({})
  })
  it('pairsToProps:空键行丢弃,键去空白,同键后者覆盖', () => {
    expect(pairsToProps([
      { key: ' a ', value: '1' }, { key: '', value: 'x' }, { key: 'a', value: '2' },
    ])).toEqual({ a: '2' })
  })
  it('propsToPairs ↔ pairsToProps 往返', () => {
    const props = { url: 'https://x', token: 'abc' }
    expect(pairsToProps(propsToPairs(props))).toEqual(props)
  })
  it('parseHeaders:JSON 对象 → 键值对;非对象/坏 JSON 给空', () => {
    expect(parseHeaders('{"X-Token":"********"}')).toEqual([{ key: 'X-Token', value: '********' }])
    expect(parseHeaders('[1,2]')).toEqual([])
    expect(parseHeaders('oops')).toEqual([])
    expect(parseHeaders(undefined)).toEqual([])
  })
})

describe('rowToForm', () => {
  it('HTTP 行:url/method/headers/body/successStatuses 平铺,headers 掩码值原样进 UI', () => {
    const r = makeRow({
      handlerKind: 2, handlerName: 'Http',
      propsJson: JSON.stringify({
        url: 'https://api.example.com/ping', method: 'POST',
        headers: '{"Authorization":"********"}', body: '{"a":1}', successStatuses: '200,204',
      }),
    })
    const v = rowToForm(r)
    expect(v.httpUrl).toBe('https://api.example.com/ping')
    expect(v.httpMethod).toBe('POST')
    expect(v.httpHeaders).toEqual([{ key: 'Authorization', value: '********' }])
    expect(v.httpBody).toBe('{"a":1}')
    expect(v.httpSuccessStatuses).toBe('200,204')
    expect(v.handlerName).toBe('') // HTTP 行不回填 handlerName(服务端固定填内置名)
  })
  it('SQL 行:sql 平铺;编译类行:props 全量进键值对', () => {
    const sqlRow = rowToForm(makeRow({ handlerKind: 3, propsJson: '{"sql":"DELETE FROM t"}' }))
    expect(sqlRow.sql).toBe('DELETE FROM t')
    const compiled = rowToForm(makeRow({ propsJson: '{"days":"30"}' }))
    expect(compiled.props).toEqual([{ key: 'days', value: '30' }])
    expect(compiled.handlerName).toBe('App.DemoJob')
  })
  it('时刻字段转 Dayjs', () => {
    const v = rowToForm(makeRow({ triggerKind: 3, oneShotTime: '2026-08-01T10:00:00' }))
    expect(v.oneShotTime?.isValid()).toBe(true)
    expect(v.oneShotTime?.format('YYYY-MM-DD HH:mm:ss')).toBe('2026-08-01 10:00:00')
  })
})

describe('formToInput', () => {
  it('HTTP:headers 序列化成 JSON 字符串放 properties.headers,掩码值原样回传即"不改"', () => {
    const v = { ...blankJob(), handlerKind: 2, name: 'x', httpUrl: ' https://x/ping ', httpMethod: 'POST', httpHeaders: [{ key: 'Authorization', value: '********' }] }
    const input = formToInput(v)
    expect(input.properties).toEqual({ url: 'https://x/ping', method: 'POST', headers: '{"Authorization":"********"}' })
    expect(input.handlerName).toBeUndefined()
  })
  it('HTTP:GET 与空 body/successStatuses 不进属性包(留后端默认)', () => {
    const v = { ...blankJob(), handlerKind: 2, name: 'x', httpUrl: 'https://x' }
    expect(formToInput(v).properties).toEqual({ url: 'https://x' })
  })
  it('编译类:键值对组装成对象;SQL:只有 sql 键', () => {
    const compiled = { ...blankJob(), name: 'x', handlerName: 'App.DemoJob', props: [{ key: 'days', value: '30' }] }
    expect(formToInput(compiled).properties).toEqual({ days: '30' })
    expect(formToInput(compiled).handlerName).toBe('App.DemoJob')
    const sql = { ...blankJob(), handlerKind: 3, name: 'x', sql: 'SELECT 1' }
    expect(formToInput(sql).properties).toEqual({ sql: 'SELECT 1' })
  })
  it('触发字段只带当前类型的,其余置 null(防脏残留)', () => {
    const interval = { ...blankJob(), name: 'x', triggerKind: 2, intervalSeconds: 30 }
    const i1 = formToInput(interval)
    expect(i1.intervalSeconds).toBe(30)
    expect(i1.cronExpression).toBeNull()
    expect(i1.oneShotTime).toBeNull()
    const oneShot = { ...blankJob(), name: 'x', triggerKind: 3, oneShotTime: dayjs('2026-08-01 10:00:00') }
    expect(formToInput(oneShot).oneShotTime).toBe('2026-08-01T10:00:00')
  })
  it('code 空串 → undefined(编辑时服务层本来就忽略);alertEmails/remark 空 → null', () => {
    const input = formToInput({ ...blankJob(), name: 'x' })
    expect(input.code).toBeUndefined()
    expect(input.alertEmails).toBeNull()
    expect(input.remark).toBeNull()
  })
})

describe('describeTrigger', () => {
  const t = (key: string, args?: Record<string, unknown>) => `${key}|${JSON.stringify(args ?? {})}`
  it('cron 显原文;间隔走 job.trigger.every;一次性走 job.trigger.at(截秒去 T)', () => {
    expect(describeTrigger(makeRow({}), t)).toBe('0 0 0 * * ?')
    expect(describeTrigger(makeRow({ triggerKind: 2, intervalSeconds: 30 }), t)).toBe('job.trigger.every|{"n":30}')
    expect(describeTrigger(makeRow({ triggerKind: 3, oneShotTime: '2026-08-01T10:00:00.123' }), t))
      .toBe('job.trigger.at|{"time":"2026-08-01 10:00:00"}')
  })
})
