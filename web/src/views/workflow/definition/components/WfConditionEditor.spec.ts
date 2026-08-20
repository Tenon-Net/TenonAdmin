import { afterEach, describe, expect, it } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import { createI18n } from 'vue-i18n'

import type { WfConditionExpr, WfModel } from '@/workflow/schema'
import { createConditionGroup, createConditionLeaf } from '@/workflow/configuration'
import WfConditionEditor from './WfConditionEditor.vue'
import WfConfigDrawer from './WfConfigDrawer.vue'

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
})

function mountEditor(expression: WfConditionExpr): HTMLElement {
  const model = ref(expression)
  const host = document.createElement('div')
  document.body.append(host)
  const Root = defineComponent({
    setup: () => () => h(WfConditionEditor, {
      modelValue: model.value,
      'onUpdate:modelValue': (value: WfConditionExpr) => { model.value = value },
    }),
  })
  app = createApp(Root)
  app.use(createI18n({
    legacy: false,
    locale: 'en-US',
    messages: {
      'en-US': {
        common: { delete: 'Delete' },
        workflow: {
          condition: {
            logicLabel: 'Condition logic',
            logic: { and: 'Match all', or: 'Match any' },
            field: 'Field',
            operator: 'Operator',
            value: 'Value',
            noValue: 'No value required',
            addCondition: 'Add condition',
            addGroup: 'Add group',
            emptyGroup: 'No conditions',
            op: {
              eq: 'Equals', ne: 'Not equal', gt: 'Greater than', gte: 'At least',
              lt: 'Less than', lte: 'At most', in: 'In', notIn: 'Not in',
              contains: 'Contains', empty: 'Empty', notEmpty: 'Not empty',
            },
          },
        },
      },
    },
  }))
  app.mount(host)
  return host
}

function mountDrawer(model: WfModel): HTMLElement {
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(WfConfigDrawer, { show: true, model, nodeId: 'branch' })
  app.use(createI18n({
    legacy: false,
    locale: 'en-US',
    messages: {
      'en-US': {
        common: { cancel: 'Cancel', save: 'Save', delete: 'Delete' },
        workflow: {
          designer: { configTitle: 'Settings · {name}', nodeName: 'Node name', armName: 'Arm' },
          condition: {
            defaultHint: 'Default arm',
            logicLabel: 'Condition logic',
            logic: { and: 'Match all', or: 'Match any' },
            field: 'Field', operator: 'Operator', value: 'Value', noValue: 'No value required',
            addCondition: 'Add condition', addGroup: 'Add group', emptyGroup: 'No conditions',
            op: {
              eq: 'Equals', ne: 'Not equal', gt: 'Greater than', gte: 'At least',
              lt: 'Less than', lte: 'At most', in: 'In', notIn: 'Not in',
              contains: 'Contains', empty: 'Empty', notEmpty: 'Not empty',
            },
          },
        },
      },
    },
  }))
  app.mount(host)
  return host
}

function directItems(container: Element): Element[] {
  return Array.from(container.children).filter((child) => child.classList.contains('n-collapse-item'))
}

describe('WfConditionEditor disclosure', () => {
  it('opens only the first root child and keeps nested descendants closed by default', async () => {
    const nested = {
      ...createConditionGroup('or'),
      children: [{ ...createConditionLeaf(), field: 'nested' }],
    }
    const expression = {
      ...createConditionGroup(),
      children: [
        { ...createConditionLeaf(), field: 'first' },
        { ...createConditionLeaf(), field: 'second' },
        nested,
      ],
    }
    const host = mountEditor(expression)
    await nextTick()

    const rootCollapse = host.querySelector('.wf-condition-children[data-depth="0"]')
    expect(rootCollapse).not.toBeNull()
    const rootItems = directItems(rootCollapse!)
    expect(rootItems).toHaveLength(3)
    expect(rootItems.filter((item) => item.classList.contains('n-collapse-item--active'))).toEqual([rootItems[0]])

    const thirdHeader = rootItems[2]?.querySelector<HTMLElement>('.n-collapse-item__header-main')
    expect(thirdHeader).not.toBeNull()
    thirdHeader!.click()
    await nextTick()
    await nextTick()

    expect(rootItems[2]?.classList.contains('n-collapse-item--active')).toBe(true)
    const nestedCollapse = rootItems[2]?.querySelector('.wf-condition-children[data-depth="1"]')
    expect(nestedCollapse).not.toBeNull()
    const nestedItems = directItems(nestedCollapse!)
    expect(nestedItems.some((item) => item.classList.contains('n-collapse-item--active'))).toBe(false)

    nestedItems[0]?.querySelector<HTMLElement>('.n-collapse-item__header-main')?.click()
    await nextTick()
    expect(nestedItems[0]?.classList.contains('n-collapse-item--active')).toBe(true)
    expect(nestedItems[0]?.querySelector<HTMLInputElement>('input')?.value).toBe('nested')
  })

  it('opens only the first non-default branch arm in the real drawer', async () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start', type: 'start', name: 'Start',
        next: {
          id: 'branch', type: 'branch', name: 'Branch', next: null,
          conditions: [
            { id: 'default', name: 'Default', isDefault: true, expr: null, next: null },
            { id: 'arm-a', name: 'A', isDefault: false, expr: createConditionGroup(), next: null },
            { id: 'arm-b', name: 'B', isDefault: false, expr: createConditionGroup(), next: null },
          ],
        },
      },
    }
    mountDrawer(model)
    await nextTick()
    await nextTick()

    const armCollapse = document.body.querySelector('.wf-branch-conditions')
    expect(armCollapse).not.toBeNull()
    const armItems = directItems(armCollapse!)
    expect(armItems).toHaveLength(3)
    expect(armItems.filter((item) => item.classList.contains('n-collapse-item--active'))).toEqual([armItems[1]])
  })
})
