import fs from "node:fs";
import path from "node:path";

const root = path.resolve(process.argv[2] || "app/src/main/assets");
const questionScript = fs.readFileSync(path.join(root, "questions.js"), "utf8").trim();
const prefix = "window.CISP_QUESTIONS = ";
if (!questionScript.startsWith(prefix) || !questionScript.endsWith(";")) throw new Error("questions.js wrapper is invalid");
const questions = JSON.parse(questionScript.slice(prefix.length, -1));
if (questions.length !== 907) throw new Error(`Expected 907 questions, got ${questions.length}`);
if (new Set(questions.map((item) => item.id)).size !== questions.length) throw new Error("Duplicate question IDs");
for (const question of questions) {
    if (!question.stem || question.options.length !== 4 || question.correctIndex < 0 || question.correctIndex > 3) {
        throw new Error(`Invalid question ${question.id}`);
    }
    for (const imageName of question.imageNames) {
        if (!fs.existsSync(path.join(root, "images", imageName))) throw new Error(`Missing image ${imageName}`);
    }
}
const setScript = fs.readFileSync(path.join(root, "question-sets.js"), "utf8").trim();
const setPrefix = "window.CISP_QUESTION_SETS = ";
if (!setScript.startsWith(setPrefix) || !setScript.endsWith(";")) throw new Error("question-sets.js wrapper is invalid");
const catalog = JSON.parse(setScript.slice(setPrefix.length, -1));
if (!Array.isArray(catalog.sets) || catalog.sets.length !== 8) throw new Error("Expected eight isolated question sets");
const setQuestions = catalog.sets.flatMap((set) => {
    if (set.questionCount !== set.questions.length || !set.questions.length) throw new Error(`Invalid set count ${set.id}`);
    if (set.questions.some((question) => question.setId !== set.id || !question.id.startsWith(`${set.id}-`))) {
        throw new Error(`Set ownership mismatch ${set.id}`);
    }
    return set.questions;
});
if (setQuestions.length !== 797) throw new Error(`Expected 797 source-set questions, got ${setQuestions.length}`);
if (new Set(setQuestions.map((item) => item.id)).size !== setQuestions.length) throw new Error("Duplicate source-set question IDs");
for (const question of setQuestions) {
    if (!question.stem || question.options.length !== 4 || question.correctIndex < 0 || question.correctIndex > 3) {
        throw new Error(`Invalid source-set question ${question.id}`);
    }
    for (const imageName of question.imageNames) {
        if (!fs.existsSync(path.join(root, "images", imageName))) throw new Error(`Missing source-set image ${imageName}`);
    }
}
const pdfs = fs.readdirSync(path.join(root, "pdfs")).filter((name) => name.endsWith(".pdf"));
if (pdfs.length !== 10) throw new Error(`Expected 10 PDFs, got ${pdfs.length}`);
const html = fs.readFileSync(path.join(root, "index.html"), "utf8");
const script = fs.readFileSync(path.join(root, "app.js"), "utf8");
for (const match of script.matchAll(/byId\("([A-Za-z0-9_-]+)"\)/g)) {
    if (!html.includes(`id="${match[1]}"`)) throw new Error(`Missing HTML element #${match[1]}`);
}
const allImages = new Set(questions.concat(setQuestions).flatMap((item) => item.imageNames));
console.log(`Verified ${questions.length} combined questions, ${catalog.sets.length} isolated sets (${setQuestions.length} questions), ${allImages.size} images, and ${pdfs.length} PDFs.`);
