# 同步你的 Fork 与上游代码

在 Vue 或 React 模板上开发自己的业务后，可以通过 Git 合并上游修复，同时保留自己的修改。这个流程适用于保留了仓库历史的 Fork；只通过 NuGet 使用后端的项目，直接升级包即可。

使用 degit 下载的快照没有上游历史，需要手动迁移修复。两种方式都需要核对前后端契约和升级记录，Git 合并成功并不代表运行行为兼容。

## 1. Fork 并克隆

在 GitHub 上 Fork [Tenon-Net/TenonAdmin](https://github.com/Tenon-Net/TenonAdmin)，然后克隆自己的仓库：

```bash
git clone https://github.com/<your-username>/TenonAdmin.git
cd TenonAdmin
git remote add upstream https://github.com/Tenon-Net/TenonAdmin.git
git remote -v
```

输出应同时包含自己的 `origin` 和官方的 `upstream`。后续命令从仓库根目录执行。

## 2. 选一条要跟踪的分支

`main` 用于发布，`dev` 用于日常开发。希望使用已发布版本时，选择对应标签；希望跟踪发布分支时，可以从 `main` 创建自己的业务分支：

```bash
git checkout -b my-product main
```

将业务开发提交在自己的分支上，方便区分上游更新与业务改动。使用 `dev` 前，应接受其中可能包含未发布或尚在调整的功能。

## 3. 拉取上游更新

先提交或暂存当前工作，阅读目标版本的[更新日志](/zh/changelog)，再获取并合并上游：

```bash
git fetch upstream --tags
git merge upstream/main
```

如果只升级到某个已发布版本，将 `upstream/main` 替换为已确认的版本标签。共享分支通常使用 merge；rebase 会改写提交历史，仅在团队约定允许时使用。

合并冲突需要同时保留业务需求与上游修复。先解决源文件冲突，再重新生成 API 类型，最后执行本页末尾的验证。

## 4. 把冲突控制到最小

把业务代码放在独立文件中，可以减少与上游修改相同文件的机会，但不能保证没有冲突。Vue 模板的常用位置如下，React 请使用对应模板的目录和约定：

| 业务内容 | 推荐位置 |
|---|---|
| 领域类型 | `web/src/types/<module>.ts` |
| API 封装 | `web/src/api/<module>.ts` |
| 业务文案 | `web/src/locales/ext/<locale>/<module>.ts` |
| 页面 | `web/src/views/<module>/` |

需要修改布局、状态管理或内置页面时，保持改动集中，并记录业务原因，便于合并时判断是否仍需保留。不同版本提供的扩展位置可能不同，以所选版本源码为准。

`schema.d.ts` 是生成文件，应从合并后的后端重新生成。下面仅用于正在进行的 merge 冲突处理；先启动包含自己业务接口的后端，再在仓库根执行：

```bash
git checkout --ours web/src/api/schema.d.ts
npm --prefix web run gen:api
git add web/src/api/schema.d.ts
```

React 将命令中的 `web` 改为 `web-react`。`--ours` 只是暂时选取文件内容，不能代替生成；rebase 中它的含义不同，不要照搬为保留本地业务的操作。

## 5. 跟踪版本变化

在所选前端目录运行 `npm ci`、`npm run lint`、`npm run typecheck` 和 `npm run build`。后端扩展也要重新编译并运行相关测试。

登录后检查菜单、普通用户权限、核心业务操作和文件上传。确认生成的 API 类型包含自己的接口，再提交合并结果。登录页版本来自前端 `package.json`，应与实际交付版本一致；自有产品可使用自己的版本策略。

向上游贡献修改的流程见[贡献指南](/zh/community/contributing)。
