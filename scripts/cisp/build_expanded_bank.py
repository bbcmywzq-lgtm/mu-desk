from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from difflib import SequenceMatcher
from pathlib import Path
from typing import Iterable

from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[2]
LETTER_TO_INDEX = {letter: index for index, letter in enumerate("ABCD")}


@dataclass
class Question:
    stem: str
    options: list[str]
    correct_index: int
    explanation: str = "公开题库未附解析。"
    image_names: list[str] = field(default_factory=list)
    origin: str = ""
    origin_number: str = ""


def normalize(value: str) -> str:
    value = value.replace("\u00a0", " ").replace("\ufeff", " ")
    value = value.replace("Ａ", "A").replace("Ｂ", "B").replace("Ｃ", "C").replace("Ｄ", "D")
    return re.sub(r"\s+", " ", value).strip()


def fingerprint(value: str) -> str:
    value = normalize(value).lower()
    value = value.replace("chinesc", "chinese").replace("hesponse", "response")
    return re.sub(r"[^0-9a-z\u4e00-\u9fff]+", "", value)


def load_existing_questions(path: Path) -> list[Question]:
    script = path.read_text(encoding="utf-8").strip()
    prefix = "window.CISP_QUESTIONS = "
    if not script.startswith(prefix) or not script.endswith(";"):
        raise ValueError(f"Unsupported questions.js wrapper: {path}")
    payload = json.loads(script[len(prefix) : -1])
    return [
        Question(
            stem=item["stem"],
            options=list(item["options"]),
            correct_index=int(item["correctIndex"]),
            explanation=item.get("explanation") or "公开题库未附解析。",
            image_names=list(item.get("imageNames") or []),
            origin="npcola-public-bank",
            origin_number=str(item.get("sourceNumber") or index + 1),
        )
        for index, item in enumerate(payload)
    ]


def parse_answer_sheet(path: Path) -> dict[int, int]:
    text = "\n".join(page.extract_text() or "" for page in PdfReader(str(path)).pages)
    answers: dict[int, int] = {}
    for raw_number, letter in re.findall(r"(?<!\d)(\d{1,3})\s+([A-D])(?![A-Za-z])", text):
        number = int(raw_number)
        if 1 <= number <= 100:
            answers[number] = LETTER_TO_INDEX[letter]
    if len(answers) != 100:
        raise ValueError(f"Expected 100 answers in {path.name}, got {len(answers)}")
    return answers


def clean_pdf_page(text: str) -> str:
    kept: list[str] = []
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped:
            kept.append("")
            continue
        if re.fullmatch(r"\d+\s*/\s*\d+", stripped):
            continue
        if re.fullmatch(r"第\s*\d+\s*页\s*共\s*\d+\s*页", stripped):
            continue
        if "答案请写在各题号前" in stripped:
            continue
        if stripped.startswith("信息安全专业人员知识测试试题") or stripped.startswith("注册信息安全专业人员考试"):
            continue
        kept.append(stripped)
    return "\n".join(kept)


QUESTION_START = re.compile(r"(?m)^\s*(\d{1,3})[.、．]\s*")
OPTION_START = re.compile(r"(?:^|(?<=\s))([A-D])\s*[、,.，．。]\s*", re.M)


def select_sequential_question_matches(text: str) -> list[re.Match[str]]:
    all_matches = list(QUESTION_START.finditer(text))
    selected: list[re.Match[str]] = []
    expected = 1
    for match in all_matches:
        if int(match.group(1)) == expected:
            selected.append(match)
            expected += 1
            if expected == 101:
                break
    if len(selected) != 100:
        found = [int(item.group(1)) for item in selected]
        raise ValueError(f"Expected sequential question numbers 1..100, got {len(selected)} ending at {found[-1] if found else 0}")
    return selected


def find_option_matches(body: str) -> list[re.Match[str]]:
    matches = list(OPTION_START.finditer(body))
    first_a = next((index for index, match in enumerate(matches) if match.group(1) == "A"), None)
    if first_a is None:
        return []
    selected: dict[str, re.Match[str]] = {}
    for match in matches[first_a:]:
        selected.setdefault(match.group(1), match)
        if len(selected) == 4:
            break
    return sorted(selected.values(), key=lambda item: item.start())


def parse_question_set(question_path: Path, answer_path: Path, set_number: int) -> list[Question]:
    answers = parse_answer_sheet(answer_path)
    pages = [clean_pdf_page(page.extract_text() or "") for page in PdfReader(str(question_path)).pages]
    text = "\n".join(pages)
    starts = select_sequential_question_matches(text)
    questions: list[Question] = []
    for index, start in enumerate(starts):
        end = starts[index + 1].start() if index + 1 < len(starts) else len(text)
        body = text[start.end() : end].strip()
        body = body.translate(str.maketrans("ＡＢＣＤ．", "ABCD."))
        body = re.sub(r"^\d{3}[、.．]\s*", "", body)
        if set_number == 5 and index + 1 == 69:
            body = re.sub(r"(?m)^培训阶段\s+B\.", "A. 培训阶段 B.", body)
        explanation = "公开题库未附解析。"
        explanation_match = re.search(r"\n?\s*解释\s*[：:]\s*", body)
        if explanation_match:
            explanation = normalize(body[explanation_match.end() :]) or explanation
            body = body[: explanation_match.start()].rstrip()
        # Remove leaked form-control labels before normalizing bare option lines;
        # use horizontal whitespace only so a lone "A" cannot consume the next
        # line's "B" as its option text.
        body = re.sub(r"(?m)^[ \t]*[A-D][ \t]*[、,.，．。]?[ \t]*$", "", body)
        body = re.sub(r"(?m)^[ \t]*([A-D])[ \t]+(?=\S)", r"\1. ", body)
        body = re.sub(r"(?m)^[ \t]*([A-D])(?=[\u4e00-\u9fff《（])", r"\1. ", body)
        repaired_lines: list[str] = []
        for line in body.splitlines():
            # Some short-answer choices are emitted on one line without any
            # punctuation ("A foo B bar C baz D qux").
            if re.match(r"^\s*A(?:[、,.，．。]\s*|\s+)", line) and all(
                re.search(rf"(?<!\S){letter}(?:[、,.，．。]\s*|\s+)", line) for letter in "BCD"
            ):
                line = re.sub(r"(?<!\S)([A-D])(?:[、,.，．。]\s*|\s+)", r"\1. ", line)
            repaired_lines.append(line)
        body = "\n".join(repaired_lines)
        # One Linux-hardening item loses the printed D marker in the PDF text
        # layer, although its fourth option text remains intact.
        if set_number == 5 and index + 1 == 7:
            body = re.sub(r"(?m)^(编辑文件/etc/profile)", r"D. \1", body)
        options = find_option_matches(body)
        if len(options) != 4:
            # A few source-PDF questions are rendered entirely as diagrams.  They
            # cannot be reconstructed safely from the text layer, so omit them
            # instead of fabricating option text.  Equivalent copies that already
            # exist in the preserved baseline keep their original images.
            if len(options) == 0 and ("答案" in body or re.search(r"图中|图形|四张图|如下图", body)):
                continue
            raise ValueError(f"{question_path.name} question {index + 1}: expected four options, got {len(options)}")
        stem = normalize(body[: options[0].start()])
        option_values: list[str] = ["", "", "", ""]
        for option_index, option in enumerate(options):
            option_end = options[option_index + 1].start() if option_index + 1 < 4 else len(body)
            option_values[LETTER_TO_INDEX[option.group(1)]] = normalize(body[option.end() : option_end])
        if len(stem) < 5 or any(not item for item in option_values):
            raise ValueError(f"{question_path.name} question {index + 1}: empty stem or option")
        questions.append(
            Question(
                stem=stem,
                options=option_values,
                correct_index=answers[index + 1],
                explanation=explanation,
                origin=f"five-set-{set_number}",
                origin_number=str(index + 1),
            )
        )
    return questions


def yellow_ratio(image, box) -> float:
    import numpy as np

    points = np.asarray(box)
    x0 = max(0, int(points[:, 0].min()) - 3)
    y0 = max(0, int(points[:, 1].min()) - 3)
    x1 = min(image.shape[1], int(points[:, 0].max()) + 4)
    y1 = min(image.shape[0], int(points[:, 1].max()) + 4)
    crop = image[y0:y1, x0:x1]
    if crop.size == 0:
        return 0.0
    red, green, blue = crop[:, :, 0], crop[:, :, 1], crop[:, :, 2]
    return float(((red > 175) & (green > 175) & (blue < 175)).mean())


def build_review_ocr_cache(pdf_path: Path, cache_path: Path) -> list[dict]:
    if cache_path.exists():
        return json.loads(cache_path.read_text(encoding="utf-8"))

    import numpy as np
    import pypdfium2 as pdfium
    from rapidocr import RapidOCR

    engine = RapidOCR()
    document = pdfium.PdfDocument(str(pdf_path))
    cached_pages: list[dict] = []
    for page_index in range(1, len(document)):
        image = np.asarray(document[page_index].render(scale=2.0).to_pil().convert("RGB"))
        result = engine(image)
        lines = []
        if result.boxes is not None:
            for box, text, score in zip(result.boxes, result.txts, result.scores):
                lines.append(
                    {
                        "text": text,
                        "score": round(float(score), 5),
                        "box": [[round(float(point[0]), 2), round(float(point[1]), 2)] for point in box],
                        "yellow": round(yellow_ratio(image, box), 5),
                    }
                )
        lines.sort(key=lambda item: (min(point[1] for point in item["box"]), min(point[0] for point in item["box"])))
        cached_pages.append({"page": page_index + 1, "lines": lines})
        print(f"OCR review page {page_index + 1}/{len(document)}: {len(lines)} lines", flush=True)
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    cache_path.write_text(json.dumps(cached_pages, ensure_ascii=False), encoding="utf-8")
    return cached_pages


REVIEW_QUESTION = re.compile(r"^\s*(\d{1,3})\s*[、,.．，]\s*(.*)$")
REVIEW_OPTION = re.compile(r"^\s*([A-DＡ-Ｄ])\s*[、,.．，]\s*(.*)$")


def parse_review_questions(pages: list[dict]) -> tuple[list[Question], list[str]]:
    raw: dict[int, dict] = {}
    current: dict | None = None
    active_option: int | None = None
    diagnostics: list[str] = []

    for page in pages:
        for line in page["lines"]:
            text = normalize(line["text"])
            if not text or "中电运行技术研究院" in text or re.fullmatch(r"[-—.\s]*\d+[-—.\s]*", text):
                continue
            question_match = REVIEW_QUESTION.match(text)
            if question_match:
                number = int(question_match.group(1))
                if 1 <= number <= 350:
                    current = {
                        "number": number,
                        "stem": [question_match.group(2)],
                        "options": [[], [], [], []],
                        "yellow": [0.0, 0.0, 0.0, 0.0],
                        "scores": [float(line["score"])],
                        "pages": {page["page"]},
                    }
                    raw[number] = current
                    active_option = None
                    continue
            if current is None:
                continue
            option_match = REVIEW_OPTION.match(text)
            if option_match:
                letter = option_match.group(1).translate(str.maketrans("ＡＢＣＤ", "ABCD"))
                active_option = LETTER_TO_INDEX[letter]
                current["options"][active_option].append(option_match.group(2))
                current["yellow"][active_option] = max(current["yellow"][active_option], float(line["yellow"]))
            elif active_option is None:
                current["stem"].append(text)
            else:
                current["options"][active_option].append(text)
                current["yellow"][active_option] = max(current["yellow"][active_option], float(line["yellow"]))
            current["scores"].append(float(line["score"]))
            current["pages"].add(page["page"])

    questions: list[Question] = []
    for number in range(1, 351):
        item = raw.get(number)
        if item is None:
            diagnostics.append(f"review {number}: question number not detected")
            continue
        stem = normalize(" ".join(item["stem"]))
        options = [normalize(" ".join(parts)) for parts in item["options"]]
        highlighted = [index for index, value in enumerate(item["yellow"]) if value >= 0.035]
        if len(stem) < 6:
            diagnostics.append(f"review {number}: stem too short ({stem!r})")
            continue
        if any(not option for option in options):
            diagnostics.append(f"review {number}: missing option; lengths={[len(value) for value in options]}")
            continue
        if len(highlighted) != 1:
            diagnostics.append(f"review {number}: highlighted={highlighted}, ratios={item['yellow']}")
            continue
        if min(item["scores"], default=0) < 0.72:
            diagnostics.append(f"review {number}: low OCR confidence={min(item['scores']):.3f}")
            continue
        questions.append(
            Question(
                stem=stem,
                options=options,
                correct_index=highlighted[0],
                origin="review-350",
                origin_number=str(number),
            )
        )
    return questions, diagnostics


def option_similarity(left: Question, right: Question) -> float:
    left_options = [fingerprint(item) for item in left.options]
    right_options = [fingerprint(item) for item in right.options]
    scores = []
    for option in left_options:
        scores.append(max(SequenceMatcher(None, option, candidate).ratio() for candidate in right_options))
    return sum(scores) / len(scores)


def find_duplicate(candidate: Question, questions: list[Question], exact: dict[str, int]) -> int | None:
    key = fingerprint(candidate.stem)
    if key in exact:
        return exact[key]
    if len(key) < 18:
        return None
    # Near-identical calculation questions can differ only in their input
    # values.  Treating those as duplicates silently changes the exercise, so
    # require the same numeric signature before fuzzy matching.
    candidate_numbers = re.findall(r"\d+(?:\.\d+)?%?", normalize(candidate.stem))
    prefix = key[:18]
    for index, existing in enumerate(questions):
        existing_key = fingerprint(existing.stem)
        if not (existing_key.startswith(prefix[:10]) or key.startswith(existing_key[:10])):
            continue
        if candidate_numbers != re.findall(r"\d+(?:\.\d+)?%?", normalize(existing.stem)):
            continue
        stem_score = SequenceMatcher(None, key, existing_key).ratio()
        if stem_score >= 0.91 and option_similarity(candidate, existing) >= 0.78:
            return index
    return None


def merge_questions(base: list[Question], candidates: Iterable[Question], report: list[str]) -> tuple[int, int]:
    exact = {fingerprint(item.stem): index for index, item in enumerate(base)}
    added = 0
    duplicates = 0
    for candidate in candidates:
        duplicate_index = find_duplicate(candidate, base, exact)
        if duplicate_index is None:
            if re.search(r"下图|图中|如图|示意图|如下图", candidate.stem) and not candidate.image_names:
                report.append(f"SKIP missing figure [{candidate.origin}#{candidate.origin_number}] {candidate.stem[:60]}")
                continue
            base.append(candidate)
            exact[fingerprint(candidate.stem)] = len(base) - 1
            added += 1
            continue
        duplicates += 1
        existing = base[duplicate_index]
        if existing.explanation == "公开题库未附解析。" and candidate.explanation != "公开题库未附解析。":
            existing.explanation = candidate.explanation
        if candidate.origin.startswith("five-set") and existing.correct_index != candidate.correct_index:
            candidate_answer = fingerprint(candidate.options[candidate.correct_index])
            option_scores = [SequenceMatcher(None, candidate_answer, fingerprint(value)).ratio() for value in existing.options]
            mapped_index = max(range(4), key=option_scores.__getitem__)
            if option_scores[mapped_index] >= 0.9 and mapped_index != existing.correct_index:
                report.append(
                    f"ANSWER corrected #{duplicate_index + 1}: {chr(65 + existing.correct_index)} -> {chr(65 + mapped_index)} from {candidate.origin}#{candidate.origin_number}"
                )
                existing.correct_index = mapped_index
            elif option_scores[mapped_index] < 0.9:
                report.append(
                    f"ANSWER conflict kept #{duplicate_index + 1}: unable to map {candidate.origin}#{candidate.origin_number} answer text"
                )
    return added, duplicates


def markdown_escape(value: str) -> str:
    return value.replace("\r", " ").replace("\n", " ").strip()


def write_markdown(path: Path, questions: list[Question]) -> None:
    lines = [
        "# CISP 公开练习题库（非官方）",
        "",
        "> 本题库由多个公开学习资料合并、去重并结构化，仅供普通 CISP（CISE/CISO）复习，不是官方真题。",
        "> 题目可能存在过时、错别字或参考答案争议，请结合现行法规、标准和教材复核。",
        "",
    ]
    for number, question in enumerate(questions, start=1):
        lines.extend([f"{number}. {markdown_escape(question.stem)}", ""])
        for image_name in question.image_names:
            lines.extend([f"![题图](./pic/{image_name})", ""])
        for option_index, option in enumerate(question.options):
            value = f"{chr(65 + option_index)}. {markdown_escape(option)}"
            if option_index == question.correct_index:
                value = f"**{value}**"
            lines.extend([f"\t{value}", ""])
        if question.explanation and question.explanation != "公开题库未附解析。":
            lines.extend([f"> {markdown_escape(question.explanation)}", ""])
    path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--review-pdf", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--ocr-cache", type=Path, required=True)
    args = parser.parse_args()

    assets = ROOT / "src" / "Toolbox.App" / "Assets" / "Cisp"
    existing_script = ROOT / "src" / "CispTrainer.Android" / "app" / "src" / "main" / "assets" / "questions.js"
    questions = load_existing_questions(existing_script)
    if len(questions) != 303:
        raise ValueError(f"Expected the preserved baseline to contain 303 questions, got {len(questions)}")

    report: list[str] = [f"Preserved baseline: {len(questions)}"]
    five_sets: list[Question] = []
    for set_number in range(1, 6):
        parsed = parse_question_set(
            assets / "PdfSets" / f"set-{set_number}-questions.pdf",
            assets / "PdfSets" / f"set-{set_number}-answers.pdf",
            set_number,
        )
        five_sets.extend(parsed)
        report.append(f"Parsed set {set_number}: {len(parsed)}")
    added, duplicates = merge_questions(questions, five_sets, report)
    report.append(f"Five-set merge: added={added}, duplicates={duplicates}, total={len(questions)}")

    cached_pages = build_review_ocr_cache(args.review_pdf, args.ocr_cache)
    review, diagnostics = parse_review_questions(cached_pages)
    report.append(f"Review OCR: accepted={len(review)}, diagnostics={len(diagnostics)}")
    report.extend(f"OCR {item}" for item in diagnostics)
    added, duplicates = merge_questions(questions, review, report)
    report.append(f"Review merge: added={added}, duplicates={duplicates}, total={len(questions)}")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    write_markdown(args.output, questions)
    args.report.write_text("\n".join(report) + "\n", encoding="utf-8")
    print("\n".join(report[-8:]))
    print(f"FINAL_QUESTION_COUNT={len(questions)}")
    return 0 if len(questions) >= 800 else 4


if __name__ == "__main__":
    sys.exit(main())
