#!/usr/bin/env python3
"""只读检查真实报销采集格式；不判定业务标签，不调用模型，不输出样本值。"""

import argparse
from collections import Counter
from datetime import datetime, timedelta
from decimal import Decimal
import json
from pathlib import Path
import re
import sys


INPUT_FIELDS = {
    "amount", "currency", "category", "receiptStatus", "amountMatches",
    "purposeClear", "duplicateEvidence", "personalConsumption", "applicantNote",
}
LABELS = {"approve", "reject", "manual"}
MAX_BYTES = 16 * 1024 * 1024
NOTICE = "仅检查采集元数据和一致性；不验证真实制度、脱敏效果、分组完整性或摘要内容，不授予评测、外发或审批权限。"


def check_collection(data, *, simulation=False):
    issues = []
    counts = Counter(total=0, eligible=0, pending=0, excluded=0, disputed=0)

    def issue(path, code):
        issues.append({"path": path, "code": code})

    def obj(value, path):
        if not isinstance(value, dict):
            issue(path, "object_required")
            return {}
        return value

    def text(value):
        return isinstance(value, str) and bool(value.strip())

    def required(value, path, names):
        for name in names.split():
            if not text(value.get(name)):
                issue(f"{path}.{name}", "text_required")

    def choice(value, path, allowed):
        if not isinstance(value, str) or value not in allowed:
            issue(path, "invalid_choice")
            return None
        return value

    def utc(value, path):
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
            if parsed.utcoffset() != timedelta(0):
                raise ValueError()
            return parsed
        except (AttributeError, TypeError, ValueError):
            issue(path, "utc_timestamp_required")
            return None

    def strings(value, path, nonempty=False):
        if not isinstance(value, list) or (nonempty and not value) or not all(map(text, value)):
            issue(path, "text_array_required")
            return []
        return value

    root = obj(data, "$")
    expected_schema = "expense-simulation-collection-v1" if simulation else "expense-real-collection-v1"
    if root.get("schemaVersion") != expected_schema:
        issue("$.schemaVersion", "unsupported_schema")
    if root.get("recordKind") != ("simulation" if simulation else "collection"):
        issue("$.recordKind", "simulation_required" if simulation else "collection_required")
    batch = obj(root.get("batch"), "$.batch")
    required(batch, "$.batch", "datasetId datasetVersion")
    if batch.get("supersedes") is not None or batch.get("changeReason") is not None:
        required(batch, "$.batch", "supersedes changeReason")
    for section, names in {
        "policy": "reference version effectiveFrom rulesTableRef confirmedBy",
        "dataUse": "ownerId permittedPurpose storageRef retentionUntil permittedProviderProcessing",
        "sampling": "population windowStart windowEnd method exclusionLogRef",
        "splitPlan": "groupingMethod assignedBy exposureLogRef",
    }.items():
        value = obj(batch.get(section), f"$.batch.{section}")
        required(value, f"$.batch.{section}", names)
    policy = batch.get("policy") if isinstance(batch.get("policy"), dict) else {}
    utc(policy.get("confirmedAtUtc"), "$.batch.policy.confirmedAtUtc")
    sampling = batch.get("sampling") if isinstance(batch.get("sampling"), dict) else {}
    strata = strings(sampling.get("strata"), "$.batch.sampling.strata", nonempty=True)
    if type(sampling.get("targetCount")) is not int or sampling["targetCount"] <= 0:
        issue("$.batch.sampling.targetCount", "positive_integer_required")
    split = batch.get("splitPlan") if isinstance(batch.get("splitPlan"), dict) else {}
    utc(split.get("assignedAtUtc"), "$.batch.splitPlan.assignedAtUtc")
    if type(split.get("seed")) is not int:
        issue("$.batch.splitPlan.seed", "integer_required")
    proportion = split.get("devProportion")
    if isinstance(proportion, bool) or not isinstance(proportion, (int, float, Decimal)) or not 0 < proportion < 1:
        issue("$.batch.splitPlan.devProportion", "proportion_required")
    holdout = choice(split.get("holdoutStatus"), "$.batch.splitPlan.holdoutStatus",
                     {"not_frozen", "sealed", "exposed"})
    freeze = obj(batch.get("freeze"), "$.batch.freeze")
    if holdout in {"sealed", "exposed"}:
        utc(freeze.get("frozenAtUtc"), "$.batch.freeze.frozenAtUtc")
        required(freeze, "$.batch.freeze", "extractionVersion deIdentificationVersion modelId policyVersion")
        if not isinstance(freeze.get("gitCommit"), str) or not re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", freeze["gitCommit"]):
            issue("$.batch.freeze.gitCommit", "commit_hash_required")
        for name in ("splitManifestSha256", "inputsSha256", "labelsSha256", "systemInstructionSha256",
                     "nodeInstructionsSha256", "policyImplementationSha256"):
            if not isinstance(freeze.get(name), str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", freeze[name]):
                issue(f"$.batch.freeze.{name}", "sha256_required")

    samples = root.get("samples")
    if not isinstance(samples, list) or not samples:
        issue("$.samples", "nonempty_array_required")
        samples = []
    case_ids, groups = set(), {}
    for index, value in enumerate(samples):
        path = f"$.samples[{index}]"
        counts["total"] += 1
        sample = obj(value, path)
        required(sample, path, "caseId leakageGroupId samplingStratum")
        case_id = sample.get("caseId")
        if text(case_id):
            if case_id in case_ids:
                issue(path + ".caseId", "duplicate_case")
            case_ids.add(case_id)
        if sample.get("dataOrigin") != ("synthetic" if simulation else "real"):
            issue(path + ".dataOrigin", "synthetic_origin_required" if simulation else "real_origin_required")
        if sample.get("samplingStratum") not in strata:
            issue(path + ".samplingStratum", "unknown_stratum")
        strings(sample.get("tags"), path + ".tags")
        assigned = choice(sample.get("split"), path + ".split", {"dev", "holdout", "unassigned"})
        group = sample.get("leakageGroupId")
        if text(group) and assigned in {"dev", "holdout"}:
            if group in groups and groups[group] != assigned:
                issue(path + ".leakageGroupId", "cross_split_group")
            groups[group] = assigned
        if holdout in {"sealed", "exposed"} and assigned == "unassigned":
            issue(path + ".split", "frozen_split_required")
        quality = obj(sample.get("quality"), path + ".quality")
        status = choice(quality.get("scoringStatus"), path + ".quality.scoringStatus",
                        {"pending", "eligible", "excluded"})
        if status:
            counts[status] += 1
        if status == "excluded":
            required(quality, path + ".quality", "exclusionReason")
        elif quality.get("exclusionReason") is not None:
            issue(path + ".quality.exclusionReason", "nonexcluded_reason_must_be_null")
        outcome = obj(sample.get("humanOutcome"), path + ".humanOutcome")
        choice(outcome.get("decision"), path + ".humanOutcome.decision",
               {"approved", "rejected", "withdrawn", "cancelled", "pending", "unknown"})
        for flag in ("additionalEvidenceUsed", "policyExceptionApplied", "modelSuggestionSeen"):
            if outcome.get(flag) is not None and type(outcome[flag]) is not bool:
                issue(path + ".humanOutcome." + flag, "boolean_or_null_required")
        redaction = obj(quality.get("redaction"), path + ".quality.redaction")
        redaction_status = choice(redaction.get("status"), path + ".quality.redaction.status",
                                  {"pending", "passed", "blocked"})
        if redaction_status == "passed":
            required(redaction, path + ".quality.redaction", "reviewerId")
            utc(redaction.get("reviewedAtUtc"), path + ".quality.redaction.reviewedAtUtc")
        annotations = obj(sample.get("annotations"), path + ".annotations")
        final_path = path + ".annotations.adjudication"
        final = obj(annotations.get("adjudication"), final_path)
        final_status = choice(final.get("status"), final_path + ".status",
                              {"pending", "agreed", "disputed", "adjudicated"})
        if final_status == "disputed":
            counts["disputed"] += 1
        if final_status in {"pending", "disputed"} and final.get("label") is not None:
            issue(final_path + ".label", "unresolved_label_must_be_null")
        snapshot = obj(sample.get("snapshot"), path + ".snapshot")
        inputs = obj(snapshot.get("modelInput"), path + ".snapshot.modelInput")
        if set(inputs) - INPUT_FIELDS:
            issue(path + ".snapshot.modelInput", "unsupported_input_fields")
        if status != "eligible":
            continue
        if redaction_status != "passed":
            issue(path + ".quality.redaction.status", "redaction_not_passed")
        if assigned not in {"dev", "holdout"}:
            issue(path + ".split", "assigned_split_required")
        required(snapshot, path + ".snapshot", "sourceRef")
        cutoff = utc(snapshot.get("availableAtUtc"), path + ".snapshot.availableAtUtc")
        captured = utc(snapshot.get("capturedAtUtc"), path + ".snapshot.capturedAtUtc")
        if cutoff and captured and captured < cutoff:
            issue(path + ".snapshot.capturedAtUtc", "snapshot_time_order")
        provenance = snapshot.get("fieldProvenance")
        if not isinstance(provenance, list):
            issue(path + ".snapshot.fieldProvenance", "array_required")
            provenance = []
        seen_fields = set()
        for offset, entry in enumerate(provenance):
            entry_path = path + f".snapshot.fieldProvenance[{offset}]"
            source = obj(entry, entry_path)
            field = choice(source.get("field"), entry_path + ".field", INPUT_FIELDS)
            if field in seen_fields:
                issue(entry_path + ".field", "duplicate_field")
            seen_fields.add(field)
            availability = choice(source.get("availability"), entry_path + ".availability",
                                   {"present", "missing", "redacted", "after_snapshot"})
            if availability != "present":
                if field in inputs:
                    issue(entry_path, "unavailable_field_in_input")
                if availability == "after_snapshot":
                    available = utc(source.get("availableAtUtc"), entry_path + ".availableAtUtc")
                    if cutoff and available and available <= cutoff:
                        issue(entry_path + ".availableAtUtc", "after_snapshot_time_order")
                continue
            if field not in inputs:
                issue(entry_path, "present_field_missing_from_input")
            choice(source.get("sourceKind"), entry_path + ".sourceKind",
                   {"applicant", "system", "human_verification"})
            required(source, entry_path, "sourceSystem sourceField")
            strings(source.get("evidenceRefs"), entry_path + ".evidenceRefs", nonempty=True)
            available = utc(source.get("availableAtUtc"), entry_path + ".availableAtUtc")
            if cutoff and available and available > cutoff:
                issue(entry_path + ".availableAtUtc", "future_information")
            if source.get("sourceKind") == "human_verification":
                required(source, entry_path, "verificationMethod verifiedBy")
        if INPUT_FIELDS - seen_fields:
            issue(path + ".snapshot.fieldProvenance", "field_provenance_incomplete")
        reviewers = []
        labeled_times = []
        for name in ("reviewerA", "reviewerB"):
            review_path = path + ".annotations." + name
            review = obj(annotations.get(name), review_path)
            reviewers.append(review)
            required(review, review_path, "reviewerId rationale")
            choice(review.get("label"), review_path + ".label", LABELS)
            strings(review.get("ruleIds"), review_path + ".ruleIds", nonempty=True)
            strings(review.get("evidenceRefs"), review_path + ".evidenceRefs", nonempty=True)
            labeled = utc(review.get("labeledAtUtc"), review_path + ".labeledAtUtc")
            if labeled:
                labeled_times.append(labeled)
                if captured and labeled < captured:
                    issue(review_path + ".labeledAtUtc", "label_before_snapshot")
            for flag in ("modelOutputVisible", "laterOutcomeVisible"):
                if review.get(flag) is not False:
                    issue(review_path + "." + flag, "blind_label_required")
        a, b = reviewers
        if a.get("reviewerId") == b.get("reviewerId"):
            issue(path + ".annotations", "distinct_reviewers_required")
        if final_status not in {"agreed", "adjudicated"}:
            issue(final_path + ".status", "final_label_unresolved")
        choice(final.get("label"), final_path + ".label", LABELS)
        required(final, final_path, "rationale")
        strings(final.get("ruleIds"), final_path + ".ruleIds", nonempty=True)
        strings(final.get("evidenceRefs"), final_path + ".evidenceRefs", nonempty=True)
        resolved = utc(final.get("resolvedAtUtc"), final_path + ".resolvedAtUtc")
        if resolved and any(resolved < time for time in labeled_times):
            issue(final_path + ".resolvedAtUtc", "resolution_before_labels")
        if final_status == "agreed" and not a.get("label") == b.get("label") == final.get("label"):
            issue(final_path, "agreed_labels_differ")
        if final_status == "adjudicated":
            required(final, final_path, "adjudicatorId")
            if final.get("adjudicatorId") in (a.get("reviewerId"), b.get("reviewerId")):
                issue(final_path + ".adjudicatorId", "independent_adjudicator_required")

    return {"mode": "simulation" if simulation else "collection",
            "checksPassed": not issues, "readyForEvaluation": False,
            "notice": ("模拟演练，不是真实记录或模型评测。" if simulation else "") + NOTICE,
            "counts": dict(counts), "issues": issues}


def load_collection(path):
    def unique_pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("duplicate_key")
            result[key] = value
        return result

    def reject_constant(_):
        raise ValueError("non_json_number")

    with path.open("rb") as stream:
        content = stream.read(MAX_BYTES + 1)
    if len(content) > MAX_BYTES:
        raise ValueError("file_too_large")
    return json.loads(content, parse_float=Decimal, object_pairs_hook=unique_pairs, parse_constant=reject_constant)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="受控位置中的采集 JSON（最多 16 MiB）")
    parser.add_argument("--simulation", action="store_true", help="仅检查显式标记的模拟包，拒绝真实来源记录")
    args = parser.parse_args(argv)
    try:
        report = check_collection(load_collection(args.input), simulation=args.simulation)
    except (OSError, ValueError, ArithmeticError, RecursionError):
        # 不打印异常正文、路径、重复键名或原始样本内容。
        print(json.dumps({"checksPassed": False, "readyForEvaluation": False, "notice": NOTICE,
                          "issues": [{"path": "$", "code": "unreadable_or_invalid_json"}]}))
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["checksPassed"] else 1


if __name__ == "__main__":
    sys.exit(main())
