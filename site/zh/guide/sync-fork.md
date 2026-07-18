# 同步你的 Fork 与上游代码

前端没有可 npm 安装的包，所以 fork 整个仓库、把 `web/` 当自己的起点，是官方支持的路径。升级于是不再是改一个版本号：上游的修复要靠 git 合并拉进来，而你自己这几个月的改动就躺在同一批文件里。

下面这套流程只针对这种情况，目标是两边都保住。

::: tip 先确认你属于哪种消费模式
- **纯后端消费方**（在自己独立的仓库里 `dotnet add package TenonAdmin` 或 `dotnet new tenon-app`）→ 用不上本页任何内容。更新靠升级 NuGet 包版本号，升级前看一眼 [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) 有没有破坏性变更。
- **fork 了仓库、在 `web/` 上做二次开发**（最常见的情况）→ 下面这套流程就是给你写的。
- **一次性快照消费方**（`npx degit Tenon-Net/TenonAdmin/web` 拉一份拷贝、完全自己维护，像 soybean / vite 脚手架那样）→ 你主动放弃了升级通道，本页的合并流程用不上；上游修复要自己读 diff 手动搬，前端也会与走 NuGet 升级的后端契约漂移。想持续吃上游修复就别走这条，回到上一条 fork 模式。
:::

## 1. Fork 并克隆

在 GitHub 上 fork [`Tenon-Net/TenonAdmin`](https://github.com/Tenon-Net/TenonAdmin)，然后克隆**你自己的 fork**：

```bash
git clone https://github.com/<你的用户名>/TenonAdmin.git
cd TenonAdmin
```

把原仓库加成第二个远程，按惯例叫 `upstream`：

```bash
git remote add upstream https://github.com/Tenon-Net/TenonAdmin.git
git remote -v
```

## 2. 选一条要跟踪的分支

仓库有两条长期分支，用途不同：

- **`main`**：只承载发布。每个提交都对应一个已打 tag、已发布的版本（`v0.1.0`、`v0.1.1` ……）。默认应该跟踪它，稳定。
- **`dev`**：日常开发分支，PR 都合到这里。更新更快，但可能包含两个发布之间的半成品。

除非你确实想要还没发布的最新改动，否则基于 `main` 建自己的分支：

```bash
git checkout -b my-product main
```

自己的二次开发放在 `my-product`（或它下面再分的分支）上。**不要直接在你还要拿来拉上游的 `main`/`dev` 同名分支上写自己的代码**，这样合并出问题时，不用从自己的提交历史里把它摘出来。

## 3. 拉取上游更新

隔一段时间（比如开始新一轮开发前，或看到 [CHANGELOG](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) 里出了新 tag 时）：

```bash
git fetch upstream
git merge upstream/main        # 或者用 rebase: git rebase upstream/main
```

两种都行：如果这条分支上的成果已经在别处发布过，merge 更省心；如果还没有，rebase 能保持历史线性。解决完冲突照常推到自己的 fork。

只想拉某个具体版本、而不是"main 上最新的一切":

```bash
git fetch upstream --tags
git merge v0.1.1
```

## 4. 把冲突控制到最小

大部分合并摩擦来自你和上游同时改了同一个文件。三个习惯能明显减少这种情况：

- **自己的代码放进新文件**，不要往已有文件里加。新文件永远不会冲突，只有共用文件才会。[加一个前端页面](/zh/guide/frontend-page)全程都是照这个来的，每类东西都有专门的去处：

  | 你的代码 | 放这里 | 别放这里 |
  |---|---|---|
  | 领域类型 | 新建 `web/src/types/<模块>.ts` | `types/api.ts` |
  | API 封装 | 新建 `web/src/api/<域>.ts`（从 `./index` 导入 `unwrap` / `pageParams` / `toPage`） | `api/index.ts` |
  | i18n 文案 | 新建 `web/src/locales/ext/<locale>/<模块>.ts`（glob 自动并入，见那里的 [README](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/src/locales/ext/README.md)） | `locales/zh-CN.ts` / `en-US.ts` |
  | 页面 | 新建 `web/src/views/<模块>/` 目录 | 任何现有 view |

  上面这四个上游文件是 `web/src` 里改动最频繁的，几乎每次发版都在动。正因如此，你的代码才不该住在里面。反过来，你拥有而上游极少碰的文件（`styles/tokens.css`、你自己的页面）可以随便改：冲突需要**双方**都改同一个文件才会发生。

- **如果确实要改共用文件**（布局、store、内置页面），那么上游改到同一处时就会冲突。这是正常现象，不代表哪里做错了。这类改动尽量少而小。

- **`web/src/api/schema.d.ts` 是特例：永远不要合并它，重新生成它。** 它是 6000 行的生成物，而你的后端有你自己的控制器，所以你这份从第一天起就和上游 100% 分叉。上游一动，它就是整文件冲突。别手动解：

  ```bash
  git checkout --ours web/src/api/schema.d.ts   # 保留你的，丢掉上游的
  npm run gen:api                               # 然后对着「你自己的」运行中后端重新生成
  git add web/src/api/schema.d.ts
  ```

  这个冲突其实是在帮你：它就是"后端契约变了，你的类型该重新生成了"的信号。本仓库**故意不**配 `merge=ours` gitattribute，正是为了不让这个信号被静默吞掉，害你拿着过期的契约继续跑。

## 5. 跟踪版本变化

- [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)：Keep a Changelog 格式，每次发布一条，前后端都覆盖，破坏性变更会明确标出（项目还在 0.x，接口仍可能变）。
- 登录页底部显示的版本号取自 `web/package.json` 的 `version`。合并完之后记得把它改成你合入的那个 tag，不然用户看到的版本号和实际跑的代码对不上。

以上讲的是把上游改动拉进你的 fork。反过来那件事，也就是把自己的改动贡献回 TenonAdmin，归[贡献指南](/zh/community/contributing)管。
