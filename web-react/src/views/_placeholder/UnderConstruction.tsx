import { Empty, Typography } from 'antd'
import { useLocation } from 'react-router-dom'

/**
 * 占位页。B6 只落地「动态路由骨架」——菜单能建路由、能跳、F5 能重建;真实业务页在 B11/批次 C 填。
 * 这里故意显示当前路径,好在联调时一眼确认「路由到对了、只是页面没实现」而不是「路由错了」。
 * 用作若干占位落点(dashboard / system/user / system/role …)的共用实现,B11 起逐个替换成真页。
 */
export default function UnderConstruction() {
  const { pathname } = useLocation()
  return (
    <div style={{ display: 'grid', placeItems: 'center', minHeight: 320, padding: 24 }}>
      <Empty
        description={
          <span>
            <Typography.Text strong>{pathname}</Typography.Text>
            <br />
            <Typography.Text type="secondary">页面建设中(B6 路由骨架已通)</Typography.Text>
          </span>
        }
      />
    </div>
  )
}
