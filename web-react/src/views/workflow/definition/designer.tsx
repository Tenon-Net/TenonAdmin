// 流程设计器。菜单 component 填 `workflow/definition/designer`;`?id=` 打开已有定义,无 id 先建草稿。
// 灰底可缩放画布 + 递归节点树 + 节点配置抽屉;保存前先过 validateModel,不把非法结构推给后端。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, Card, Empty, Form, Input, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { AppIcon } from '@/components/AppIcon'
import { DetailPage } from '@/components/DetailPage'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { wfDefinitionApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { cloneModel, createDefaultModel, validateModel } from '@/workflow/model'
import { DEF_STATUS } from '@/workflow/statusLabels'
import type { WfDefinitionInput } from '@/types/workflow'
import type { WfModel, WfNode } from '@/workflow/schema'
import { WfConfigDrawer } from './components/WfConfigDrawer'
import { WfNodeTree } from './components/WfNodeTree'
import './components/wf-designer.css'
import '../wf-identity.css'

const ZOOM_MIN = 50
const ZOOM_MAX = 200
const ZOOM_STEP = 10

interface DesignerFormValues {
  name?: string
  groupName?: string
}

/** 定义状态 tag(与列表页同一套色)。 */
const STATUS_TAG: Record<number, { color: string; key: string }> = {
  [DEF_STATUS.DRAFT]: { color: 'default', key: 'workflow.definition.status.draft' },
  [DEF_STATUS.PUBLISHED]: { color: 'success', key: 'workflow.definition.status.published' },
  [DEF_STATUS.DISABLED]: { color: 'warning', key: 'workflow.definition.status.disabled' },
}

function clampZoom(value: number): number {
  const snapped = Math.round(value / ZOOM_STEP) * ZOOM_STEP
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, snapped))
}

export default function WfDesignerPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { run } = useConfirm()
  const has = useHasPerm()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const [form] = Form.useForm<DesignerFormValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [defId, setDefId] = useState<number | null>(null)
  const [status, setStatus] = useState<number | null>(null)
  const [model, setModel] = useState<WfModel>(createDefaultModel())
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [newName, setNewName] = useState('')

  const [zoom, setZoom] = useState(100)
  const canvasRef = useRef<HTMLDivElement>(null)
  const stageRef = useRef<HTMLDivElement>(null)

  const queryId = useMemo(() => {
    const n = Number(searchParams.get('id'))
    return Number.isFinite(n) && n > 0 ? n : null
  }, [searchParams])

  const errorIds = useMemo(
    () => new Set(validateModel(model).map((issue) => issue.nodeId).filter(Boolean) as string[]),
    [model],
  )

  const load = useCallback(
    async (id: number) => {
      setLoading(true)
      try {
        const detail = await wfDefinitionApi.get(id)
        setDefId(Number(detail.id))
        setStatus(detail.status == null ? null : Number(detail.status))
        setModel(cloneModel((detail.model as WfModel | undefined) ?? createDefaultModel()))
        form.setFieldsValue({ name: detail.name ?? '', groupName: detail.groupName ?? '' })
      } catch (e) {
        message.error(translateError(e))
      } finally {
        setLoading(false)
      }
    },
    [form, message],
  )

  useEffect(() => {
    if (queryId) void load(queryId)
  }, [queryId, load])

  /** 适配画布:按未缩放尺寸算比例,只缩不放(超出可视区才动)。 */
  const fitZoom = () => {
    const canvas = canvasRef.current
    const stage = stageRef.current
    if (!canvas || !stage) return
    const pad = 72
    const availW = Math.max(1, canvas.clientWidth - pad)
    const availH = Math.max(1, canvas.clientHeight - pad)
    const scaleNow = zoom / 100
    const w = Math.max(1, stage.offsetWidth / scaleNow)
    const h = Math.max(1, stage.offsetHeight / scaleNow)
    setZoom(clampZoom(Math.min(1, availW / w, availH / h) * 100))
  }

  const onSelect = (node: WfNode) => {
    setSelectedId(node.id)
    setDrawerOpen(true)
  }

  /** 保存草稿。返回是否成功(发布前先存一次)。结构非法时不提交。 */
  const save = async (): Promise<boolean> => {
    if (!defId) return false
    const v = await form.validateFields().catch(() => null)
    if (!v) return false
    if (validateModel(model).length) {
      message.warning(t('workflow.designer.invalid'))
      return false
    }
    setSaving(true)
    try {
      await wfDefinitionApi.update({
        id: defId,
        name: v.name?.trim() || t('workflow.designer.untitled'),
        groupName: v.groupName?.trim() || null,
        model: model as WfDefinitionInput['model'],
      })
      message.success(t('common.success'))
      return true
    } catch (e) {
      message.error(translateError(e))
      return false
    } finally {
      setSaving(false)
    }
  }

  const publish = async () => {
    if (!defId) return
    if (!(await save())) return
    const ok = await run(() => wfDefinitionApi.publish(defId), t('workflow.designer.published'))
    if (ok) await load(defId)
  }

  /** 空态建草稿:add 拿到 id 后就地打开(带 `?id=`),不跳回列表再点一次「设计」。 */
  const createDraft = async () => {
    const name = newName.trim()
    if (!name) {
      message.warning(t('workflow.designer.nameRequired'))
      return
    }
    setSaving(true)
    try {
      const id = Number(await wfDefinitionApi.add({ name, model: createDefaultModel() as WfDefinitionInput['model'] }))
      navigate(`/workflow/definition/designer?id=${id}`, { replace: true })
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  const statusTag = status == null ? null : STATUS_TAG[status]

  return (
    <DetailPage
      title={t('workflow.designer.title')}
      loading={loading}
      onBack={() => navigate('/workflow/definition')}
      actions={
        defId ? (
          <div className="wf-toolbar">
            <Form form={form} layout="inline">
              <Form.Item
                name="name"
                rules={[{ required: true, whitespace: true, message: t('workflow.designer.nameRequired') }]}
              >
                <Input className="wf-name" placeholder={t('workflow.designer.name')} />
              </Form.Item>
              <Form.Item name="groupName">
                <Input placeholder={t('workflow.definition.group')} />
              </Form.Item>
            </Form>
            <Space size={8}>
              {statusTag ? <Tag color={statusTag.color}>{t(statusTag.key)}</Tag> : null}
              <Button icon={<AppIcon icon="ph:floppy-disk" size={16} />} loading={saving} onClick={() => void save()}>
                {t('common.save')}
              </Button>
              {has('POST:/api/v1/workflow/definition/publish') && (
                <Button type="primary" icon={<AppIcon icon="ph:paper-plane-tilt" size={16} />} loading={saving} onClick={() => void publish()}>
                  {t('workflow.designer.publish')}
                </Button>
              )}
            </Space>
          </div>
        ) : null
      }
    >
      {defId === null && !loading ? (
        <Card size="small">
          <Empty description={t('workflow.designer.needId')}>
            <Space size={8}>
              <Input
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                onPressEnter={() => void createDraft()}
                placeholder={t('workflow.designer.name')}
                style={{ width: 260 }}
              />
              <Button type="primary" loading={saving} onClick={() => void createDraft()}>
                {t('workflow.designer.create')}
              </Button>
            </Space>
          </Empty>
        </Card>
      ) : (
        <div className="wf-canvas-wrap">
          <div ref={canvasRef} className="wf-canvas">
            <div className="wf-stage">
              {/* CSS `zoom` 而不是 transform:布局跟着缩放走,画布滚动区尺寸才对得上(与 Vue 侧同一手法)。 */}
              <div ref={stageRef} className="wf-stage-inner" style={{ zoom: zoom / 100 }}>
                <WfNodeTree
                  model={model}
                  selectedId={selectedId}
                  errorIds={errorIds}
                  onModelChange={setModel}
                  onSelect={onSelect}
                />
              </div>
            </div>
          </div>

          <div className="wf-zoom" role="toolbar" aria-label={t('workflow.designer.zoom')}>
            <button
              type="button" className="wf-zoom-btn" disabled={zoom <= ZOOM_MIN}
              aria-label={t('workflow.designer.zoomOut')} onClick={() => setZoom(clampZoom(zoom - ZOOM_STEP))}
            >
              <AppIcon icon="ph:minus" size={14} />
            </button>
            <span className="wf-zoom-pct">{zoom}%</span>
            <button
              type="button" className="wf-zoom-btn" disabled={zoom >= ZOOM_MAX}
              aria-label={t('workflow.designer.zoomIn')} onClick={() => setZoom(clampZoom(zoom + ZOOM_STEP))}
            >
              <AppIcon icon="ph:plus" size={14} />
            </button>
            <button
              type="button" className="wf-zoom-btn wf-zoom-fit"
              aria-label={t('workflow.designer.zoomFit')} onClick={fitZoom}
            >
              <AppIcon icon="ph:corners-out" size={14} />
            </button>
          </div>
        </div>
      )}

      <WfConfigDrawer
        open={drawerOpen}
        model={model}
        nodeId={selectedId}
        onOpenChange={setDrawerOpen}
        onModelChange={setModel}
      />
    </DetailPage>
  )
}
