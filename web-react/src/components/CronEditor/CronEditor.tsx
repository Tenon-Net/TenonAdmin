// CronEditor —— cron 表达式可视化编辑器(自包含,契约在此;登记于 COMPONENTS.md)。
//
// 契约:`{ value?: string; onChange?: (v: string) => void; previewCount?: number }`(受控,
// value/onChange 声明可选以便直接放进 antd Form.Item)。previewCount 默认 5。
//
// 6 个段页签(秒/分/时/日/月/周),每段:每 · 区间 · 步长 · 指定值;日页签额外 L / L-n / nW / LW 专项,
// 周页签额外 nL(最后一个周几)/ n#m(第 m 个周几)专项;日与周互斥 —— 一侧受限时另一侧自动落 `?`
// (后端日周同限直接拒 47003,互斥逻辑在 cronParts.setSegment)。「表达式」页签支持直填任意表达式;
// 解析不进已知模式的段(名字/混合列表)显示"自定义片段"提示,选择模式即覆盖。
//
// 底部预览:防抖 400ms 调 POST /sys/job/preview-cron(任何登录用户可用),显示归一化结果 + 未来
// previewCount 次时刻;非法时显示后端 47003 文案(translateError);everySecondWarning 为真时给
// "等效每秒执行"的警告(不拦截 —— 后端也是故意放行的)。
import { useEffect, useMemo, useRef, useState } from 'react'
import { Alert, Input, InputNumber, Radio, Select, Space, Spin, Tabs, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import type { CronPreviewOutput } from '@/types/api'
import {
  composeSegment, joinCron, parseSegment, setSegment, splitCron,
  SEG_RANGES, type SegIndex, type SegState,
} from './cronParts'

export interface CronEditorProps {
  value?: string
  onChange?: (value: string) => void
  /** 预览条数,默认 5(后端上限 20)。 */
  previewCount?: number
}

const SEG_KEYS = ['seconds', 'minutes', 'hours', 'day', 'month', 'week'] as const

/** 各段可选模式(日/周有专项;? 只在日/周出现)。 */
function modesFor(idx: SegIndex): SegState['mode'][] {
  const base: SegState['mode'][] = ['every', 'range', 'step', 'values']
  if (idx === 3) return [...base, 'unspecified', 'lastDay', 'lastOffset', 'nearestWeekday', 'lastWeekday']
  if (idx === 5) return [...base, 'unspecified', 'lastDow', 'nthDow']
  return base
}

/** 切模式时的缺省状态(用户再微调参数)。 */
function defaultState(mode: SegState['mode'], idx: SegIndex): SegState {
  const [min, max] = SEG_RANGES[idx]!
  switch (mode) {
    case 'every': return { mode: 'every' }
    case 'unspecified': return { mode: 'unspecified' }
    case 'range': return { mode: 'range', from: min, to: max }
    case 'step': return { mode: 'step', from: null, step: 5 }
    case 'values': return { mode: 'values', values: [min] }
    case 'lastDay': return { mode: 'lastDay' }
    case 'lastOffset': return { mode: 'lastOffset', n: 1 }
    case 'nearestWeekday': return { mode: 'nearestWeekday', n: 1 }
    case 'lastWeekday': return { mode: 'lastWeekday' }
    case 'lastDow': return { mode: 'lastDow', dow: 1 }
    case 'nthDow': return { mode: 'nthDow', dow: 1, nth: 1 }
  }
}

export function CronEditor({ value, onChange, previewCount = 5 }: CronEditorProps) {
  const { t } = useTranslation()
  const expr = value ?? ''
  const segs = useMemo(() => splitCron(expr), [expr])

  // ── 预览:防抖 400ms + seq 竞态守卫(只认最新一次在途请求)──
  const [preview, setPreview] = useState<CronPreviewOutput | null>(null)
  const [previewError, setPreviewError] = useState('')
  const [previewing, setPreviewing] = useState(false)
  const seqRef = useRef(0)
  useEffect(() => {
    const id = ++seqRef.current
    if (!expr.trim()) {
      setPreview(null)
      setPreviewError('')
      setPreviewing(false)
      return
    }
    setPreviewing(true)
    const timer = setTimeout(async () => {
      try {
        const out = await jobApi.previewCron({ cron: expr, count: previewCount })
        if (id !== seqRef.current) return
        setPreview(out)
        setPreviewError('')
      } catch (e) {
        if (id !== seqRef.current) return
        setPreview(null)
        setPreviewError(translateError(e))
      } finally {
        if (id === seqRef.current) setPreviewing(false)
      }
    }, 400)
    return () => clearTimeout(timer)
  }, [expr, previewCount])

  /** 段编辑落地:无法拆段的表达式从默认底子改起。 */
  const updateSegment = (idx: SegIndex, text: string) => {
    const base = segs ?? ['0', '*', '*', '*', '*', '?']
    onChange?.(joinCron(setSegment(base, idx, text)))
  }

  const dowOptions = useMemo(
    () => Array.from({ length: 7 }, (_, i) => ({ label: t(`job.cron.dow${i}`), value: i })),
    [t],
  )

  /** 单段编辑面板(渲染函数而非组件:面板内无独立 hook,免 re-mount 丢焦点)。 */
  const renderSegment = (idx: SegIndex) => {
    if (!segs) return <Typography.Text type="secondary">{t('job.cron.unsplittable')}</Typography.Text>
    const text = segs[idx]!
    const state = parseSegment(text, idx)
    const [min, max] = SEG_RANGES[idx]!
    const apply = (s: SegState) => updateSegment(idx, composeSegment(s))
    const valueOptions =
      idx === 5 ? dowOptions : Array.from({ length: max - min + 1 }, (_, i) => ({ label: String(min + i), value: min + i }))

    return (
      <Space orientation="vertical" size={12} style={{ width: '100%' }}>
        <Radio.Group
          value={state?.mode}
          onChange={(e) => apply(defaultState(e.target.value as SegState['mode'], idx))}
          options={modesFor(idx).map((m) => ({ label: t(`job.cron.${m}`), value: m }))}
        />
        {!state && (
          <Typography.Text type="secondary">{t('job.cron.customSegment', { text })}</Typography.Text>
        )}
        {state?.mode === 'range' && (
          <Space size={8}>
            <InputNumber min={min} max={max} value={state.from} onChange={(v) => apply({ ...state, from: Number(v ?? min) })} />
            <span>{t('job.cron.rangeTo')}</span>
            <InputNumber min={min} max={max} value={state.to} onChange={(v) => apply({ ...state, to: Number(v ?? max) })} />
          </Space>
        )}
        {state?.mode === 'step' && (
          <Space size={8}>
            <span>{t('job.cron.stepFrom')}</span>
            <InputNumber
              min={min} max={max} placeholder="*" value={state.from ?? undefined}
              onChange={(v) => apply({ ...state, from: v == null ? null : Number(v) })}
            />
            <span>{t('job.cron.stepEvery')}</span>
            <InputNumber min={1} value={state.step} onChange={(v) => apply({ ...state, step: Number(v ?? 1) })} />
          </Space>
        )}
        {state?.mode === 'values' && (
          <Select
            mode="multiple"
            style={{ minWidth: 260, maxWidth: '100%' }}
            placeholder={t('job.cron.valuesPlaceholder')}
            value={state.values}
            options={valueOptions}
            onChange={(vals: number[]) => apply({ ...state, values: vals })}
          />
        )}
        {state?.mode === 'lastOffset' && (
          <Space size={8}>
            <span>{t('job.cron.lastOffsetPrefix')}</span>
            <InputNumber min={1} max={30} value={state.n} onChange={(v) => apply({ ...state, n: Number(v ?? 1) })} />
            <span>{t('job.cron.lastOffsetSuffix')}</span>
          </Space>
        )}
        {state?.mode === 'nearestWeekday' && (
          <Space size={8}>
            <InputNumber min={1} max={31} value={state.n} onChange={(v) => apply({ ...state, n: Number(v ?? 1) })} />
            <span>{t('job.cron.nearestWeekdaySuffix')}</span>
          </Space>
        )}
        {state?.mode === 'lastDow' && (
          <Select style={{ width: 160 }} value={state.dow} options={dowOptions} onChange={(dow: number) => apply({ ...state, dow })} />
        )}
        {state?.mode === 'nthDow' && (
          <Space size={8}>
            <span>{t('job.cron.nthPrefix')}</span>
            <InputNumber min={1} max={5} value={state.nth} onChange={(v) => apply({ ...state, nth: Number(v ?? 1) })} />
            <Select style={{ width: 140 }} value={state.dow} options={dowOptions} onChange={(dow: number) => apply({ ...state, dow })} />
          </Space>
        )}
        {(idx === 3 || idx === 5) && <Typography.Text type="secondary">{t('job.cron.mutexHint')}</Typography.Text>}
      </Space>
    )
  }

  return (
    <div>
      <Tabs
        size="small"
        items={[
          ...SEG_KEYS.map((key, i) => ({
            key,
            label: t(`job.cron.${key}`),
            children: renderSegment(i as SegIndex),
          })),
          {
            key: 'expression',
            label: t('job.cron.expression'),
            children: (
              <Input
                value={expr}
                placeholder={t('job.cron.expressionPlaceholder')}
                onChange={(e) => onChange?.(e.target.value)}
              />
            ),
          },
        ]}
      />
      {/* 预览区:归一化 + 未来时刻;非法显后端 47003 文案;everySecondWarning 提示不拦截 */}
      <div style={{ marginTop: 4 }}>
        {previewError && <Alert type="error" showIcon title={previewError} />}
        {!previewError && preview && (
          <Space orientation="vertical" size={4} style={{ width: '100%' }}>
            {preview.everySecondWarning && <Alert type="warning" showIcon title={t('job.cron.everySecondWarning')} />}
            <Typography.Text type="secondary">
              {t('job.cron.normalized')}: <Typography.Text code>{preview.normalized}</Typography.Text>
            </Typography.Text>
            <Typography.Text type="secondary">{t('job.cron.next')}:</Typography.Text>
            {preview.occurrences.length === 0 && <Typography.Text type="secondary">{t('job.cron.noOccurrence')}</Typography.Text>}
            {preview.occurrences.map((o) => (
              <Typography.Text key={o} style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 12 }}>
                {o.replace('T', ' ')}
              </Typography.Text>
            ))}
          </Space>
        )}
        {!previewError && !preview && (
          <Typography.Text type="secondary">
            {previewing ? <Spin size="small" /> : t('job.cron.previewEmpty')}
          </Typography.Text>
        )}
      </div>
    </div>
  )
}
