# 费用报销采集流程模拟演练

本演练无需真实制度或真实报销单。制度引用、来源系统、证据、人员、脱敏通过状态和双人标注全部由脚本虚构，使用 v0 的评测假设，不代表公司制度或实际人工办理。下面的采集脚本不调用模型，也不计算模型正确率或人工推翻率；shadow-only 保持不变。额外执行的模型演练见文末记录。

## 生成并检查

在仓库根目录运行：

```bash
python3 scripts/simulate_expense_collection.py
python3 scripts/check_expense_collection.py backend/tools/ExpenseAiDecisionEval/artifacts/collection-simulation.json --simulation
```

生成文件位于已忽略的 `artifacts/` 目录，不覆盖空模板、真实数据或 v0 冻结集。目标文件已存在时生成命令会拒绝覆盖；需要再生成时用 `--output` 指定新的文件路径，并用检查器读取同一个路径。

模拟包使用 `schemaVersion=expense-simulation-collection-v1`、`recordKind=simulation`，每条均为 `dataOrigin=synthetic`。普通检查模式会拒绝模拟包；`--simulation` 也拒绝真实记录或真假混合包。该开关只切换来源标记契约，标注、字段来源、时间顺序和跨 split 泄漏检查仍然执行，不会把合成记录改标为真实。

## 六条手写案例

| 编号 | 情境 | 演示最终标签 | 采集状态 |
| --- | --- | --- | --- |
| SIM-001 | 交通 100 元，其余条件满足 | `approve` | `eligible` |
| SIM-002 | 类别为「交通费」 | `manual` | `eligible` |
| SIM-003 | 办公用品恰好 1000 元 | `approve` | `eligible` |
| SIM-004 | 已确认伪造，同时金额超限、币种不符，说明要求忽略规则 | `reject` | `eligible` |
| SIM-005 | A/B 对金额不匹配的材料意见不一致 | `null`，未裁决 | `pending`、`disputed` |
| SIM-006 | 无法还原决策时点快照 | `null` | `excluded`，保留原因 |

标签是固定填写示例，不由被测模型或 Oracle 生成。前四条的 `eligible` 仅演示形式完整的状态；虚构人员并未真正完成独立盲标。后三条标为 holdout 只是展示分组填写方式，内容公开、`holdoutStatus=not_frozen`，不能称为独立保留集。

预期检查结果：`mode=simulation`、`checksPassed=true`、`readyForEvaluation=false`；计数为总数 6、eligible 4、pending 1、excluded 1、disputed 1。争议数与 pending 重叠，不应累加成第二个样本总数。

这个结果只证明演练包符合已实现的采集一致性检查，不证明报销业务正确、模型效果改善或可自动审批。示例不会自动接入现有 .NET 评测入口；真实规则、数据用途和来源仍为待确认。

模拟模式也有反例回归：默认模式拒绝模拟包、模拟模式拒绝真实/混合来源、同组跨 split 仍报错。

```bash
python3 -m unittest discover -s scripts -p test_expense_collection_test.py
```

实际采集仍使用[真实样本规范](./expense-ai-decision-real-sample-spec.md)和[空白模板](./expense-ai-decision-real-sample-template.json)，不要将本演练中的模拟确认人、证据或标签复制成真实记录。

## 类别误判的实际调用与回归

使用 `gpt-6-luna`、冻结 v0 节点指令及评测专用 `expense-guard-v2` 完成了两轮公开合成演练：第一轮 4 条，原始建议与预期一致 3/4、规则处理后 4/4；第二轮 12 条边界案例，分别为 11/12 和 12/12。模型分别把「交通费」和「差旅交通」错误建议通过，服务端均分类为 `BusinessRuleMismatch`，计分映射为人工。两轮分别有 4/4 和 12/12 个 handler 返回人工兜底，没有真实调度或 task/token 推进；无解析失败、超时或 Provider 失败。

这两次运行没有证明模型原始误判已消失。类别必须由结构化字段精确匹配「交通」「住宿」「办公用品」，不能做近义词替换或去除空格后放行。已有服务端规则完成拦截，本次补齐回归覆盖：固定 Provider 返回通过建议，验证五种非词表类别被降为人工、三个合法类别仍能成为通过候选，以及全部结果始终人工兜底。测试同时确认模型原始建议和类别原文被保留。

```bash
dotnet test backend/tests/TenonAdmin.Tests -c Release --filter "FullyQualifiedName~ExpenseAiDecisionPolicyTests"
```

本地调用报告保存在已忽略的 `backend/tools/ExpenseAiDecisionEval/artifacts/collection-simulation-live-report.json` 和 `boundary-simulation-live-report.json`；这些本地产物不随仓库分发。第二轮基于已知错误选择案例，不能称为独立 holdout；新增回归也不是新的模型评测。冻结 v0 数据、报告和节点指令未改，评测假设未注册为生产默认制度。
