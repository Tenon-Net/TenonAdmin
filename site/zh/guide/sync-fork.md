# 同步你的 Fork 与上游代码

本页只针对一种情况:你 fork/克隆了整个 `Tenon-Net/TenonAdmin` 仓库,把 `web/` 当作自己前端的起点做了二次开发,现在想把 TenonAdmin 上游的修复和改进拉进来,又不想丢自己的改动。

::: tip 先确认你属于哪种消费模式
- **纯后端消费者**(在自己独立的仓库里 `dotnet add package TenonAdmin` 或 `dotnet new tenon-app`)→ 用不上本页任何内容。更新靠升级 NuGet 包版本号,升级前看一眼 [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) 有没有破坏性变更。
- **fork 了仓库、在 `web/` 上做二次开发**(最常见的情况——前端没有可 npm 安装的包,直接基于 `web/` 开发是官方支持的路径)→ 本页就是给你写的。
:::

## 1. Fork 并克隆

在 GitHub 上 fork [`Tenon-Net/TenonAdmin`](https://github.com/Tenon-Net/TenonAdmin),然后克隆**你自己的 fork**:

```bash
git clone https://github.com/<你的用户名>/TenonAdmin.git
cd TenonAdmin
```

把原仓库加成第二个远程,按惯例叫 `upstream`:

```bash
git remote add upstream https://github.com/Tenon-Net/TenonAdmin.git
git remote -v
```

## 2. 选一条要跟踪的分支

仓库有两条长期分支,用途不同:

- **`main`**——只承载发布。每个提交都对应一个已打 tag、已发布的版本(`v0.1.0`、`v0.1.1` ……)。默认应该跟踪它,稳定。
- **`dev`**——日常开发分支,PR 都合到这里。更新更快,但可能包含两个发布之间的半成品。

除非你确实想要还没发布的最新改动,否则基于 `main` 建自己的分支:

```bash
git checkout -b my-product main
```

自己的二次开发放在 `my-product`(或它下面再分的分支)上——**不要直接在你还要拿来拉上游的 `main`/`dev` 同名分支上写自己的代码**,这样合并出问题时,不用从自己的提交历史里把它摘出来。

## 3. 拉取上游更新

隔一段时间(比如开始新一轮开发前,或看到 [CHANGELOG](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md) 里出了新 tag 时):

```bash
git fetch upstream
git merge upstream/main        # 或者用 rebase: git rebase upstream/main
```

两种都行:如果这条分支上的成果已经在别处发布过,merge 更省心;如果还没有,rebase 能保持历史线性。解决完冲突照常推到自己的 fork。

只想拉某个具体版本、而不是"main 上最新的一切":

```bash
git fetch upstream --tags
git merge v0.1.1
```

## 4. 把冲突控制到最小

大部分合并摩擦来自你和上游同时改了同一个文件。两个习惯能明显减少这种情况:

- **自己的页面/组件/接口模块放进新文件**,不要往已有文件里加——比如新建一个 `web/src/views/your-module/` 目录,而不是往现有 view 里加路由。新文件永远不会冲突,只有共用文件才会。
- **如果确实要改共用文件**(布局、store、`styles/tokens.css`)——上游改到同一处时冲突就会出现,这是正常现象,不代表哪里做错了。

## 5. 跟踪版本变化

- [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)——Keep a Changelog 格式,每次发布一条,前后端都覆盖,破坏性变更会明确标出(项目还在 0.x,接口仍可能变)。
- 登录页底部显示的版本号取自 `web/package.json` 的 `version`——合并完之后记得把它改成你合入的那个 tag,不然用户看到的版本号和实际跑的代码对不上。

本页讲的是把上游改动拉进你的 fork。反过来的那种用法——把自己的改动贡献回 TenonAdmin——是[贡献指南](/zh/community/contributing)的事。
