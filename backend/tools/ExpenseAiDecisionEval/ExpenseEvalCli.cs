using Microsoft.Extensions.Configuration;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tools.ExpenseAiDecisionEval;

public static class ExpenseEvalCli
{
    public const string LiveCommand =
        "dotnet run --project backend/tools/ExpenseAiDecisionEval -- --live --split holdout";

    public static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        IConfiguration? configuration = null,
        HttpMessageHandler? httpHandler = null,
        CancellationToken cancellationToken = default)
    {
        var output = stdout ?? Console.Out;
        var error = stderr ?? Console.Error;
        if (!TryParse(args, out var options, out var parseError))
        {
            await error.WriteLineAsync(parseError);
            return 1;
        }

        if (options.Help)
        {
            await output.WriteLineAsync(HelpText());
            return 0;
        }

        ExpenseEvalDataset dataset;
        ExpenseEvalSmoke smoke;
        try
        {
            dataset = ExpenseEvalDataset.LoadOfficial(ExpenseEvalPaths.DatasetPath());
            smoke = await ExpenseEvalHarness.RunSmokeAsync(dataset.Cases, cancellationToken, options.ExpensePolicy);
        }
        catch (ExpenseEvalException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }

        var reportPath = options.OutputPath ?? ExpenseEvalPaths.DefaultReportPath();
        if (!options.Live)
        {
            var selfCheck = ExpenseEvalReport.Create(
                dataset,
                new AiDecisionPolicyOptions(),
                smoke,
                liveExecuted: false,
                liveSkippedReason: null,
                split: null,
                model: null,
                timeoutSeconds: null,
                providerObservations: null,
                providerMetrics: null,
                expensePolicyEnabled: options.ExpensePolicy);
            selfCheck.Write(reportPath);
            await output.WriteLineAsync(SelfCheckSummary(dataset, selfCheck, reportPath));
            if (options.Split is not null)
            {
                await output.WriteLineAsync("split 只对 --live 生效；自检仍检查全部案例。");
            }

            return 0;
        }

        IConfiguration config;
        try
        {
            config = configuration ?? BuildConfiguration(options.AppsettingsPath);
        }
        catch (Exception)
        {
            WriteSkipped(
                dataset,
                smoke,
                reportPath,
                "provider-configuration-invalid",
                options.Split ?? "holdout",
                options.ExpensePolicy);
            await output.WriteLineAsync("真实评测未执行。");
            await error.WriteLineAsync("配置文件无法读取，未发起请求。");
            await output.WriteLineAsync(LiveCommand);
            return 2;
        }

        var setup = ExpenseEvalProviders.Resolve(config, httpHandler);
        if (setup is LiveProviderSetup.Skipped skipped)
        {
            var skippedReport = ExpenseEvalReport.Create(
                dataset,
                new AiDecisionPolicyOptions(),
                smoke,
                liveExecuted: false,
                liveSkippedReason: skipped.Reason,
                split: options.Split ?? "holdout",
                model: null,
                timeoutSeconds: null,
                providerObservations: null,
                providerMetrics: null,
                expensePolicyEnabled: options.ExpensePolicy);
            skippedReport.Write(reportPath);
            await output.WriteLineAsync(SelfCheckSummary(dataset, skippedReport, reportPath));
            await output.WriteLineAsync("真实评测未执行。");
            await output.WriteLineAsync(skipped.Reason == "provider-not-enabled"
                ? "OpenAI-compatible Provider 未启用。"
                : "Provider 配置无效，未发起请求。");
            await output.WriteLineAsync(LiveCommand);
            await output.WriteLineAsync("配置键沿用 TenonAdmin:Workflow:AiDecision，不要把密钥写进命令行。");
            await output.WriteLineAsync("TenonAdmin__Workflow__AiDecision__OpenAiCompatible__Enabled");
            await output.WriteLineAsync("TenonAdmin__Workflow__AiDecision__OpenAiCompatible__Endpoint");
            await output.WriteLineAsync("TenonAdmin__Workflow__AiDecision__OpenAiCompatible__Model");
            await output.WriteLineAsync("TenonAdmin__Workflow__AiDecision__OpenAiCompatible__ApiKey");
            await output.WriteLineAsync("TenonAdmin__Workflow__AiDecision__OpenAiCompatible__TimeoutSeconds");
            return 2;
        }

        var ready = (LiveProviderSetup.Ready)setup;
        using var lifetime = ready.Lifetime;
        var split = options.Split ?? "holdout";
        IReadOnlyList<ExpenseEvalCase> selected;
        try
        {
            selected = dataset.Select(split);
            ExpenseEvalLimits.EnsureCallBudget(selected.Count);
        }
        catch (ExpenseEvalException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }

        var budget = ExpenseEvalLimits.CallBudget(selected.Count);
        await output.WriteLineAsync(
            $"真实调用预算：{selected.Count} 条 × 最多 {ExpenseEvalLimits.MaxAttemptsPerCase} 次 = 最多 {budget} 次请求，单次超时 {ready.TimeoutSeconds} 秒。");
        IReadOnlyList<EvalObservation> observations;
        try
        {
            observations = await ExpenseEvalHarness.RunProviderAsync(
                selected,
                ready.Provider,
                options.ExpensePolicy
                    ? new ExpenseAiDecisionPolicyEvaluator(ready.Policy)
                    : new AiDecisionPolicyEvaluator(ready.Policy),
                ready.TimeoutSeconds,
                cancellationToken);
        }
        catch (ExpenseEvalException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }
        foreach (var observation in observations)
        {
            var raw = observation.RawRecommendation is null
                ? "-"
                : ExpenseEvalOracle.ToWire(observation.RawRecommendation.Value);
            var policy = observation.PolicyClassification?.ToString() ?? "-";
            await output.WriteLineAsync(
                $"{observation.CaseId} raw={raw} policy={policy} technical={observation.TechnicalFailure ?? "-"} attempts={observation.Attempts}");
        }

        var liveReport = ExpenseEvalReport.Create(
            dataset,
            ready.Policy,
            smoke,
            liveExecuted: true,
            liveSkippedReason: null,
            split: split,
            model: ready.Model,
            timeoutSeconds: ready.TimeoutSeconds,
            providerObservations: observations,
            providerMetrics: ExpenseEvalScoring.Summarize("provider", observations),
            httpApiKeyOptIn: ready.HttpApiKeyOptIn,
            expensePolicyEnabled: options.ExpensePolicy);
        liveReport.Write(reportPath);
        await output.WriteLineAsync($"真实评测已写报告：{reportPath}");
        await output.WriteLineAsync("该结果不保证完全确定，也不能作为自动审批上线证明。");
        return 0;
    }

    private static void WriteSkipped(
        ExpenseEvalDataset dataset,
        ExpenseEvalSmoke smoke,
        string reportPath,
        string reason,
        string split,
        bool expensePolicyEnabled)
    {
        ExpenseEvalReport.Create(
            dataset,
            new AiDecisionPolicyOptions(),
            smoke,
            liveExecuted: false,
            liveSkippedReason: reason,
            split: split,
            model: null,
            timeoutSeconds: null,
            providerObservations: null,
            providerMetrics: null,
            expensePolicyEnabled: expensePolicyEnabled).Write(reportPath);
    }

    private static IConfiguration BuildConfiguration(string? appsettingsPath)
    {
        var builder = new ConfigurationBuilder();
        if (!string.IsNullOrWhiteSpace(appsettingsPath))
        {
            builder.AddJsonFile(Path.GetFullPath(appsettingsPath), optional: false);
        }

        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static string SelfCheckSummary(ExpenseEvalDataset dataset, ExpenseEvalReport report, string path)
    {
        var scoring = report.ScoringSelfTest;
        return string.Join(
            Environment.NewLine,
            "自检完成，未调用模型。",
            $"数据集 {dataset.Cases.Count} 条（dev {report.Dataset.Dev}，holdout {report.Dataset.Holdout}）。",
            $"计分夹具：原始错误建议通过 {scoring.FalseApproveRaw.Count}/{scoring.FalseApproveRaw.Denominator}，解析失败 {scoring.ParseFailureCases}，超时 {scoring.TimeoutCases}，Provider 失败 {scoring.ProviderFailureCases}，样本仍为 {scoring.Total}。",
            $"接线：人工兜底 {report.PipelineSmoke.ManualFallback}/{report.PipelineSmoke.Cases}，成功推进 {report.PipelineSmoke.Succeeded}。",
            "报告：" + path);
    }

    private static bool TryParse(string[] args, out CliOptions options, out string error)
    {
        options = new CliOptions();
        error = "";
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                case "--self-check":
                    break;
                case "--live":
                    options.Live = true;
                    break;
                case "--expense-policy":
                    options.ExpensePolicy = true;
                    break;
                case "--split":
                    if (!TryNext(args, ref index, out var split))
                    {
                        error = "参数缺少取值。";
                        return false;
                    }

                    options.Split = split;
                    break;
                case "--output":
                    if (!TryNext(args, ref index, out var outputPath))
                    {
                        error = "参数缺少取值。";
                        return false;
                    }

                    options.OutputPath = outputPath;
                    break;
                case "--appsettings":
                    if (!TryNext(args, ref index, out var appsettingsPath))
                    {
                        error = "参数缺少取值。";
                        return false;
                    }

                    options.AppsettingsPath = appsettingsPath;
                    break;
                default:
                    if (arg.StartsWith("--api-key", StringComparison.OrdinalIgnoreCase)
                        || arg.StartsWith("--endpoint", StringComparison.OrdinalIgnoreCase)
                        || arg.Equals("--max-attempts", StringComparison.Ordinal))
                    {
                        error = "密钥、地址和重试上限不能从命令行覆盖。";
                        return false;
                    }

                    error = "无法识别的参数。使用 --help 查看命令。";
                    return false;
            }
        }

        if (options.Split is not null && options.Split is not ("dev" or "holdout" or "all"))
        {
            error = "split 只能是 dev、holdout 或 all。";
            return false;
        }

        return true;

        static bool TryNext(string[] args, ref int index, out string value)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
            {
                value = "";
                return false;
            }

            value = args[++index];
            return true;
        }
    }

    private static string HelpText() =>
        """
        费用报销 AI Decision 离线评测 v0

          dotnet run --project backend/tools/ExpenseAiDecisionEval -- --self-check
          dotnet run --project backend/tools/ExpenseAiDecisionEval -- --live --split holdout

        --self-check 不访问模型。--live 才调用现有 OpenAI-compatible Provider。
        --expense-policy 显式启用评测专用结构化规则门槛；默认仍使用出厂 policy。
        规则门槛只降级候选，所有结果仍为人工兜底；改动后复用原 holdout 不能作为独立验证。
        配置键为 TenonAdmin:Workflow:AiDecision。每条最多 2 次尝试，总请求不超过 128。
        HTTP 上带密钥必须同时设置 AllowInsecureHttp 和评测专用 AllowHttpApiKey。生产 Provider 不接受这个组合。
        """;

    private sealed class CliOptions
    {
        public bool Help { get; set; }

        public bool Live { get; set; }

        public bool ExpensePolicy { get; set; }

        public string? Split { get; set; }

        public string? OutputPath { get; set; }

        public string? AppsettingsPath { get; set; }
    }
}
