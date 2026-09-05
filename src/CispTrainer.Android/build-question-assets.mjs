import fs from "node:fs";
import path from "node:path";

if (process.argv.length !== 4) {
    console.error("Usage: node build-question-assets.mjs <public-bank.md> <questions.js>");
    process.exit(2);
}

const sourcePath = process.argv[2];
const outputPath = process.argv[3];
const lines = fs.readFileSync(sourcePath, "utf8").replaceAll("\r\n", "\n").split("\n");
const starts = [];
for (let index = 0; index < lines.length; index++) {
    if (/^\s*(\d+)\.\s+(.+)$/.test(lines[index])) starts.push(index);
}

const normalize = (value) => value.replace(/\s+/g, " ").trim();
const clean = (value) => normalize(value.trim().replace(/^>\s*/, "").replace(/^`|`$/g, "").replaceAll("**", ""));
const containsAny = (text, values) => values.some((value) => text.toLocaleLowerCase().includes(value.toLocaleLowerCase()));
const classify = (text) => {
    if (containsAny(text, ["网络安全法", "保密法", "法律", "法规", "标准", "GB/", "GB／", "等级保护", "合规", "国家秘密", "条例"])) return "信息安全法规标准";
    if (containsAny(text, ["SSE-CMM", "工程", "生命周期", "需求分析", "软件开发", "项目管理", "威胁建模", "代码审核", "测试"])) return "信息安全工程";
    if (containsAny(text, ["ISMS", "风险", "管理", "资产", "业务连续", "应急响应", "审计", "PDCA", "RPO", "RTO", "灾难恢复"])) return "信息安全管理";
    if (containsAny(text, ["密码", "加密", "操作系统", "Linux", "Windows", "网络", "防火墙", "数据库", "攻击", "漏洞", "恶意代码", "访问控制", "身份认证", "IP", "Web", "SQL"])) return "信息安全技术";
    return "信息安全保障";
};

const questions = [];
for (let block = 0; block < starts.length; block++) {
    const start = starts[block];
    const end = starts[block + 1] ?? lines.length;
    const first = lines[start].match(/^\s*(\d+)\.\s+(.+)$/);
    const sourceNumber = first[1];
    const stemParts = [first[2]];
    const options = new Map();
    const correctLetters = new Set();
    const explanationParts = [];
    const imageNames = [];
    let foundFirstOption = false;

    for (let lineIndex = start + 1; lineIndex < end; lineIndex++) {
        const raw = lines[lineIndex];
        for (const image of raw.matchAll(/!\[[^\]]*\]\((?:\.\/)?pic\/([^\)]+)\)/gi)) {
            const name = path.basename(image[1].replaceAll("\\", "/"));
            if (name && !imageNames.some((item) => item.toLowerCase() === name.toLowerCase())) imageNames.push(name);
        }
        const option = raw.match(/^\s*(?:`)?(?:\*\*)?([A-D])[\.．。]\s*(.+?)(?:\*\*)?(?:`)?\s*$/);
        if (option) {
            foundFirstOption = true;
            const letter = option[1];
            const text = clean(option[2]);
            if (text && !options.has(letter)) options.set(letter, text);
            if (raw.includes("**")) correctLetters.add(letter);
            continue;
        }
        const cleaned = clean(raw);
        if (!cleaned || cleaned.startsWith("![")) continue;
        if (!foundFirstOption && !raw.trimStart().startsWith(">")) stemParts.push(cleaned);
        else if (foundFirstOption && raw.trimStart().startsWith(">")) explanationParts.push(cleaned);
    }

    if (options.size !== 4 || [..."ABCD"].some((letter) => !options.has(letter)) || correctLetters.size !== 1) continue;
    const stem = normalize(stemParts.join(" "));
    const orderedOptions = [..."ABCD"].map((letter) => options.get(letter));
    if (stem.length < 6 || stem.includes("�") || orderedOptions.some((option) => !option || option.includes("�"))) continue;
    const answer = [...correctLetters][0];
    const explanation = explanationParts.length ? normalize(explanationParts.join(" ")) : "公开题库未附解析。";
    questions.push({
        id: `npcola-${String(questions.length + 1).padStart(3, "0")}`,
        sourceNumber,
        domain: classify(`${stem} ${orderedOptions.join(" ")}`),
        stem,
        options: orderedOptions,
        correctIndex: answer.charCodeAt(0) - 65,
        explanation,
        imageNames
    });
}

if (questions.length < 200) {
    console.error(`Only ${questions.length} valid questions were parsed; refusing to generate the Android bank.`);
    process.exit(3);
}
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `window.CISP_QUESTIONS = ${JSON.stringify(questions)};\n`, "utf8");
console.log(`Generated ${questions.length} questions at ${outputPath}`);
