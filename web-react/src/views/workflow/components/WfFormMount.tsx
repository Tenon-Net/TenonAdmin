// 业务表单入口:优先渲染定义级 formSchema,否则按 formComponent(views 相对路径)动态加载消费者组件。
// 路径约定与菜单 component 一致,如 `biz/leave/form` → `/src/views/biz/leave/form.tsx`(同 buildRoutes 的 glob)。
// 组件不存在时给告警占位,不抛错打断审批主流程;非法双配置保留消费者挂载点。对应 Vue 侧 WfFormMount.vue。
import {
  Suspense,
  forwardRef,
  lazy,
  useImperativeHandle,
  useMemo,
  useRef,
  type ComponentType,
  type LazyExoticComponent,
} from 'react'
import { Alert } from 'antd'
import { useTranslation } from 'react-i18next'
import type { WfInstanceStatus } from '@/types/workflow'
import { parseWfFormValuesResult, serializeWfFormValues } from '@/workflow/formRuntime'
import type { WfId } from '@/workflow/id'
import type { WfFormFieldPerm, WfFormSchema } from '@/workflow/schema'
import { WfBuiltinForm, type WfBuiltinFormHandle, type WfFormMode } from './WfBuiltinForm'

/** 消费者业务表单组件收到的 props;新增项必须同步 Vue 侧,否则两套模板的挂载点契约会漂移。 */
export interface WfFormComponentProps {
  mode: WfFormMode
  definitionId?: WfId
  instanceId?: WfId
  businessKey?: string | null
  variablesJson?: string | null
  status?: WfInstanceStatus
}

type FormModule = { default: ComponentType<WfFormComponentProps> }
export type WfFormGlob = Record<string, () => Promise<FormModule>>

const viewModules = import.meta.glob<FormModule>([
  '/src/views/**/*.tsx',
  '!/src/views/**/*.spec.tsx',
]) as WfFormGlob

const lazyComponents = new WeakMap<() => Promise<FormModule>, LazyExoticComponent<ComponentType<WfFormComponentProps>>>()

/** loader → lazy 组件缓存:同一个挂载点重渲染不能换组件身份,否则消费者表单每次都重挂、丢内部状态。 */
function lazyFor(loader: () => Promise<FormModule>): LazyExoticComponent<ComponentType<WfFormComponentProps>> {
  const cached = lazyComponents.get(loader)
  if (cached) return cached
  const created = lazy(loader)
  lazyComponents.set(loader, created)
  return created
}

/** `views/biz/leave/form.tsx` / `/biz/leave/form` 等写法统一成 glob 键的相对部分。 */
export function normalizeViewPath(raw: string): string {
  let path = raw.trim().replace(/\\/g, '/')
  if (path.startsWith('views/')) path = path.slice('views/'.length)
  if (path.startsWith('/')) path = path.slice(1)
  if (path.endsWith('.tsx')) path = path.slice(0, -4)
  return path
}

export interface WfFormMountHandle {
  /** 提交前校验;变量 JSON 本身坏掉时直接拒绝,不给「看着没填错却提交了脏数据」的机会。 */
  validate: () => boolean
}

export interface WfFormMountProps {
  formComponent?: string | null
  formSchema?: WfFormSchema | null
  mode: WfFormMode
  permissions?: WfFormFieldPerm[] | null
  definitionId?: WfId
  instanceId?: WfId
  businessKey?: string | null
  variablesJson?: string | null
  status?: WfInstanceStatus
  onVariablesChange?: (json: string | null) => void
  /** 测试注入用;默认取真实 `import.meta.glob`(与 buildRoutes 同一套约定)。 */
  viewGlob?: WfFormGlob
}

export const WfFormMount = forwardRef<WfFormMountHandle, WfFormMountProps>(function WfFormMount(
  {
    formComponent, formSchema, mode, permissions,
    definitionId, instanceId, businessKey, variablesJson, status,
    onVariablesChange, viewGlob = viewModules,
  },
  ref,
) {
  const { t } = useTranslation()
  const builtinRef = useRef<WfBuiltinFormHandle | null>(null)

  // 值不留本地副本:JSON 是唯一真相,父级回写后重新解析(与 Vue 的 watch 回环等价,但没有两份状态)。
  const parsed = useMemo(() => parseWfFormValuesResult(variablesJson), [variablesJson])

  // formComponent 与 formSchema 双配置属非法定义:保留消费者挂载点,内置表单让位。
  const builtinSchema = formComponent?.trim()
    ? null
    : formSchema?.fields?.length ? formSchema : null

  const resolved = useMemo(() => {
    const raw = formComponent?.trim()
    if (!raw) return null
    const key = `/src/views/${normalizeViewPath(raw)}.tsx`
    const loader = viewGlob[key]
    return loader ? { component: lazyFor(loader) } : { component: null }
  }, [formComponent, viewGlob])

  useImperativeHandle(ref, () => ({
    validate: () => {
      if (parsed.error) return false
      return builtinRef.current?.validate() ?? true
    },
  }), [parsed.error])

  if (builtinSchema && parsed.error) {
    return <Alert type="error" showIcon message={t('workflow.form.runtime.invalidVariables')} />
  }

  if (builtinSchema) {
    return (
      <WfBuiltinForm
        ref={builtinRef}
        schema={builtinSchema}
        value={parsed.values}
        mode={mode}
        permissions={permissions}
        onChange={(values) => onVariablesChange?.(serializeWfFormValues(values))}
      />
    )
  }

  if (!resolved) return null

  if (!resolved.component) {
    return <Alert type="warning" showIcon message={t('workflow.form.missing', { path: formComponent })} />
  }

  const Consumer = resolved.component
  return (
    <Suspense fallback={null}>
      <Consumer
        mode={mode}
        definitionId={definitionId}
        instanceId={instanceId}
        businessKey={businessKey}
        variablesJson={variablesJson}
        status={status}
      />
    </Suspense>
  )
})
