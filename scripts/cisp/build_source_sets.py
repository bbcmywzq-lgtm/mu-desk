from __future__ import annotations

import argparse
import json
from pathlib import Path

from build_expanded_bank import Question, load_existing_questions
from import_word_bank import parse_document


SET_METADATA = [
    ("wx2026-01", "CISE 练习题（A卷）", "2026 · 已审+解析"),
    ("wx2026-02", "CISO 练习题（B卷）", "2026 · 已审+解析"),
    ("wx2026-03", "CISP 复习题（1）", "100题 · 有答案+解析"),
    ("wx2026-04", "CISP 复习题（2）", "原卷顺序 · 有答案+解析"),
    ("wx2026-05", "CISP 复习题（3）", "100题 · 有答案+解析"),
    ("wx2026-06", "CISP 复习题（4）", "原卷顺序 · 有答案+解析"),
    ("wx2026-07", "CISP 复习题（5）", "原卷顺序 · 有答案+解析"),
    ("wx2026-08", "CISP 复习题（6）", "100题 · 有答案+解析"),
]

# Five answer blocks are visually valid in the source but their options are too
# irregular for the generic parser. They already exist in the merged bank, so
# use those vetted copies to keep the original set complete without guessing.
RECOVERY_MATCHES = {
    (1, 51): ("小王在学习定量风险评估方法后", "400万元", "暴露系数"),
    (1, 71): ("按照我国信息安全等级保护的有关政策和标准", "自主定级", "公安机关备案"),
    (1, 74): ("小赵是某大学计算机科学与技术专业的毕业生", "大量用户", "自主访问控制"),
    (8, 53): ("社会工程学本质上是一种", "西奥迪尼", "人类天性"),
    (8, 86): ("风险评估文档是指在整个风险评估过程中", "评估的目的", "判断依据"),
}


def classify(text: str) -> str:
    if any(value.lower() in text.lower() for value in ("网络安全法", "保密法", "法律", "法规", "标准", "GB/", "等级保护", "合规", "国家秘密", "条例")):
        return "信息安全法规标准"
    if any(value.lower() in text.lower() for value in ("SSE-CMM", "工程", "生命周期", "需求分析", "软件开发", "项目管理", "威胁建模", "代码审核", "测试")):
        return "信息安全工程"
    if any(value.lower() in text.lower() for value in ("ISMS", "风险", "管理", "资产", "业务连续", "应急响应", "审计", "PDCA", "RPO", "RTO", "灾难恢复")):
        return "信息安全管理"
    if any(value.lower() in text.lower() for value in ("密码", "加密", "操作系统", "Linux", "Windows", "网络", "防火墙", "数据库", "攻击", "漏洞", "恶意代码", "访问控制", "身份认证", "IP", "Web", "SQL")):
        return "信息安全技术"
    return "信息安全保障"


def clone_question(question: Question) -> Question:
    return Question(
        stem=question.stem,
        options=list(question.options),
        correct_index=question.correct_index,
        explanation=question.explanation,
        image_names=list(question.image_names),
    )


def find_recovery(base: list[Question], phrases: tuple[str, ...]) -> Question:
    matches = [question for question in base if all(phrase in question.stem for phrase in phrases)]
    if len(matches) != 1:
        raise ValueError(f"Expected one recovery match for {phrases!r}, got {len(matches)}")
    return clone_question(matches[0])


def question_payload(question: Question, set_id: str, set_name: str, source_number: int) -> dict:
    combined = f"{question.stem} {' '.join(question.options)}"
    return {
        "id": f"{set_id}-{source_number:03d}",
        "setId": set_id,
        "setName": set_name,
        "sourceNumber": str(source_number),
        "domain": classify(combined),
        "stem": question.stem,
        "options": question.options,
        "correctIndex": question.correct_index,
        "explanation": question.explanation,
        "imageNames": question.image_names,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--converted-dir", type=Path, required=True)
    parser.add_argument("--base-questions-js", type=Path, required=True)
    parser.add_argument("--image-dir", type=Path, required=True)
    parser.add_argument("--json-output", type=Path, required=True)
    parser.add_argument("--js-output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()

    files = sorted(args.source_dir.glob("*.docx")) + sorted(args.converted_dir.glob("*.docx"))
    if len(files) != 8:
        raise ValueError(f"Expected eight source documents, got {len(files)}")
    base = load_existing_questions(args.base_questions_js)
    sets: list[dict] = []
    report_lines: list[str] = []

    for document_index, (path, metadata) in enumerate(zip(files, SET_METADATA, strict=True), start=1):
        set_id, set_name, subtitle = metadata
        parsed = parse_document(path, args.image_dir, document_index)
        by_number = {int(question.origin_number): question for question in parsed.questions}
        for (recovery_document, source_number), phrases in RECOVERY_MATCHES.items():
            if recovery_document == document_index:
                by_number[source_number] = find_recovery(base, phrases)

        marker_count = parsed.answer_markers
        questions = [
            question_payload(by_number[number], set_id, set_name, number)
            for number in range(1, marker_count + 1)
            if number in by_number
        ]
        if len(questions) != marker_count:
            missing = [number for number in range(1, marker_count + 1) if number not in by_number]
            raise ValueError(f"Unrecovered questions in {set_name}: {missing}")
        sets.append(
            {
                "id": set_id,
                "name": set_name,
                "subtitle": subtitle,
                "sourceFile": path.name,
                "questionCount": len(questions),
                "questions": questions,
            }
        )
        report_lines.append(f"{set_id} {set_name}: {len(questions)} questions")

    payload = {"version": 1, "sets": sets}
    serialized = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.js_output.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.write_text(serialized + "\n", encoding="utf-8")
    args.js_output.write_text(f"window.CISP_QUESTION_SETS = {serialized};\n", encoding="utf-8")

    all_questions = [question for item in sets for question in item["questions"]]
    all_ids = [question["id"] for question in all_questions]
    if len(all_ids) != len(set(all_ids)):
        raise ValueError("Question set IDs are not unique")
    referenced = sorted({name for question in all_questions for name in question["imageNames"]})
    missing_images = [name for name in referenced if not (args.image_dir / name).is_file()]
    if missing_images:
        raise ValueError(f"Missing set images: {missing_images}")

    report_lines.append(f"total: {len(all_questions)} questions")
    report_lines.append(f"images: {len(referenced)}")
    args.report.write_text("\n".join(report_lines) + "\n", encoding="utf-8")
    print("\n".join(report_lines))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
