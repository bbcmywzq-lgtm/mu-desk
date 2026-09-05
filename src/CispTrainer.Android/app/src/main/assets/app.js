(() => {
    "use strict";

    const questions = Array.isArray(window.CISP_QUESTIONS) ? window.CISP_QUESTIONS : [];
    const questionSets = Array.isArray(window.CISP_QUESTION_SETS?.sets) ? window.CISP_QUESTION_SETS.sets : [];
    const allQuestions = questions.concat(questionSets.flatMap((set) => set.questions || []));
    const storageKey = "cisp-progress-v1";
    const letters = ["A", "B", "C", "D"];
    const byId = (id) => document.getElementById(id);
    const ui = {
        practiceMode: byId("practiceMode"), setPicker: byId("setPicker"), setPickerGroup: byId("setPickerGroup"),
        domain: byId("domainPicker"), sessionSize: byId("sessionSize"), applySize: byId("applySessionSize"),
        wrongOnly: byId("wrongOnly"), markedOnly: byId("markedOnly"), excludedOnly: byId("excludedOnly"),
        questionPanel: byId("questionPanel"), emptyPanel: byId("emptyPanel"), emptyText: byId("emptyText"),
        domainText: byId("domainText"), sourceText: byId("sourceText"), positionText: byId("positionText"),
        stem: byId("stemText"), images: byId("imagePanel"), options: byId("optionsPanel"),
        answerPanel: byId("answerPanel"), answerTitle: byId("answerTitle"), explanation: byId("answerExplanation"),
        copy: byId("copyButton"), mark: byId("markButton"), removeWrong: byId("removeWrongButton"),
        exclude: byId("excludeButton"), submit: byId("submitButton"), next: byId("nextButton"),
        questionCount: byId("questionCount"), maxCount: byId("maxQuestionCount"), attempted: byId("attemptedText"),
        accuracy: byId("accuracyText"), wrongCount: byId("wrongCountText"), markedCount: byId("markedCountText"),
        excludedCount: byId("excludedCountText"), export: byId("exportButton"), source: byId("sourceButton"),
        materials: byId("materialsButton"), materialsModal: byId("materialsModal"), closeMaterials: byId("closeMaterials"),
        materialList: byId("materialList"), toast: byId("toast")
    };

    let progress = loadProgress();
    let session = [];
    let index = 0;
    let sessionAttemptCount = 0;
    let sessionCorrectCount = 0;
    let selectedIndex = null;
    let submitted = false;
    let toastTimer = null;

    function loadProgress() {
        const fallback = { sessionSize: 100, questions: {} };
        try {
            const parsed = JSON.parse(localStorage.getItem(storageKey) || "null");
            if (!parsed || typeof parsed !== "object") return fallback;
            parsed.questions = parsed.questions && typeof parsed.questions === "object" ? parsed.questions : {};
            parsed.sessionSize = clampNumber(parsed.sessionSize, 1, Math.max(allQuestions.length, 1), 100);
            return parsed;
        } catch (_) {
            return fallback;
        }
    }

    function saveProgress() {
        localStorage.setItem(storageKey, JSON.stringify(progress));
        updateSummary();
    }

    function clampNumber(value, min, max, fallback) {
        const number = Number.parseInt(value, 10);
        return Number.isFinite(number) ? Math.min(max, Math.max(min, number)) : fallback;
    }

    function stateFor(question, create = false) {
        if (!progress.questions[question.id] && create) {
            progress.questions[question.id] = {
                attemptCount: 0, correctCount: 0, wrongCount: 0, lastWasCorrect: false,
                inWrongBook: false, marked: false, excluded: false, lastSelectedIndex: null
            };
        }
        return progress.questions[question.id] || null;
    }

    function isWrong(item) {
        return !!item && (item.inWrongBook === true || item.isInWrongBook === true);
    }

    function isMarked(item) {
        return !!item && (item.marked === true || item.isMarked === true);
    }

    function isExcluded(item) {
        return !!item && (item.excluded === true || item.isExcludedFromDraw === true);
    }

    function selectedSet() {
        return questionSets.find((set) => set.id === ui.setPicker.value) || questionSets[0] || null;
    }

    function currentQuestions() {
        return ui.practiceMode.value === "sets" ? (selectedSet()?.questions || []) : questions;
    }

    function filteredQuestions() {
        return currentQuestions().filter((question) => {
            const item = stateFor(question);
            if (ui.domain.value && question.domain !== ui.domain.value) return false;
            if (ui.wrongOnly.checked && !isWrong(item)) return false;
            if (ui.markedOnly.checked && !isMarked(item)) return false;
            if (ui.excludedOnly.checked) return isExcluded(item);
            return !isExcluded(item);
        });
    }

    function shuffle(items) {
        const result = items.slice();
        for (let i = result.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [result[i], result[j]] = [result[j], result[i]];
        }
        return result;
    }

    function startSession() {
        const pool = currentQuestions();
        const count = clampNumber(ui.sessionSize.value, 1, Math.max(pool.length, 1), Math.min(progress.sessionSize || 100, Math.max(pool.length, 1)));
        progress.sessionSize = count;
        ui.sessionSize.value = String(count);
        ui.maxCount.textContent = String(pool.length);
        ui.sessionSize.max = String(Math.max(pool.length, 1));
        saveProgress();
        session = shuffle(filteredQuestions()).slice(0, count);
        index = 0;
        sessionAttemptCount = 0;
        sessionCorrectCount = 0;
        renderCurrent();
        updateSummary();
    }

    function renderCurrent() {
        selectedIndex = null;
        submitted = false;
        const question = session[index];
        if (!question) {
            ui.questionPanel.classList.add("hidden");
            ui.emptyPanel.classList.remove("hidden");
            ui.emptyText.textContent = ui.excludedOnly.checked
                ? "还没有被设为“不再抽取”的题目。"
                : "此筛选条件下没有可抽取的题目，请调整左侧设置。";
            return;
        }
        ui.emptyPanel.classList.add("hidden");
        ui.questionPanel.classList.remove("hidden");
        const item = stateFor(question);
        ui.domainText.textContent = question.domain;
        ui.sourceText.textContent = question.setName
            ? `${question.setName} #${question.sourceNumber}`
            : `公开题源 #${question.sourceNumber}`;
        ui.positionText.textContent = `本组 ${index + 1} / ${session.length}`;
        ui.stem.textContent = question.stem;
        ui.images.textContent = "";
        (question.imageNames || []).forEach((name) => {
            const image = document.createElement("img");
            image.src = `images/${encodeURIComponent(name)}`;
            image.alt = "题目配图";
            image.loading = "lazy";
            ui.images.appendChild(image);
        });
        ui.options.textContent = "";
        question.options.forEach((option, optionIndex) => {
            const button = document.createElement("button");
            button.className = "option";
            const letter = document.createElement("span");
            letter.className = "option-letter";
            letter.textContent = letters[optionIndex];
            const text = document.createElement("span");
            text.className = "option-text";
            text.textContent = option;
            button.append(letter, text);
            button.addEventListener("click", () => selectOption(optionIndex));
            ui.options.appendChild(button);
        });
        ui.answerPanel.classList.add("hidden");
        ui.submit.disabled = false;
        ui.next.disabled = true;
        ui.next.textContent = index + 1 >= session.length ? "完成本组" : "下一题";
        ui.mark.textContent = isMarked(item) ? "★ 已标记" : "☆ 标记本题";
        ui.removeWrong.classList.toggle("hidden", !isWrong(item));
        ui.exclude.classList.toggle("hidden", !(isExcluded(item) || (item && item.lastWasCorrect)));
        ui.exclude.textContent = isExcluded(item) ? "恢复抽取" : "以后不再抽到";
    }

    function selectOption(optionIndex) {
        if (submitted) return;
        selectedIndex = optionIndex;
        [...ui.options.children].forEach((button, i) => button.classList.toggle("selected", i === optionIndex));
    }

    function submitAnswer() {
        if (submitted) return;
        if (selectedIndex === null) {
            showToast("请先选择一个答案。");
            return;
        }
        const question = session[index];
        const item = stateFor(question, true);
        const correct = selectedIndex === question.correctIndex;
        sessionAttemptCount++;
        if (correct) sessionCorrectCount++;
        item.attemptCount = (item.attemptCount || 0) + 1;
        item.correctCount = (item.correctCount || 0) + (correct ? 1 : 0);
        item.wrongCount = (item.wrongCount || 0) + (correct ? 0 : 1);
        item.lastWasCorrect = correct;
        item.lastSelectedIndex = selectedIndex;
        item.lastAnsweredAt = new Date().toISOString();
        if (!correct) item.inWrongBook = true;
        submitted = true;
        saveProgress();
        [...ui.options.children].forEach((button, optionIndex) => {
            button.disabled = true;
            if (optionIndex === question.correctIndex) button.classList.add("correct");
            if (!correct && optionIndex === selectedIndex) button.classList.add("wrong");
        });
        ui.answerTitle.textContent = correct ? `回答正确，答案 ${letters[question.correctIndex]}` : `回答错误，答案 ${letters[question.correctIndex]}`;
        ui.answerTitle.className = correct ? "answer-correct" : "answer-wrong";
        ui.explanation.textContent = question.explanation || "公开题库未附解析。";
        ui.answerPanel.classList.remove("hidden");
        ui.submit.disabled = true;
        ui.next.disabled = false;
        ui.removeWrong.classList.toggle("hidden", !isWrong(item));
        ui.exclude.classList.toggle("hidden", !correct);
        ui.exclude.textContent = "以后不再抽到";
    }

    function nextQuestion() {
        if (!submitted) return;
        if (index + 1 >= session.length) {
            showToast(`本组 ${session.length} 道题已完成。`);
            startSession();
            return;
        }
        index++;
        renderCurrent();
    }

    function toggleMark() {
        const question = session[index];
        if (!question) return;
        const item = stateFor(question, true);
        item.marked = !isMarked(item);
        saveProgress();
        ui.mark.textContent = item.marked ? "★ 已标记" : "☆ 标记本题";
        if (ui.markedOnly.checked && !item.marked) removeCurrentFromSession();
    }

    function removeFromWrongBook() {
        const question = session[index];
        if (!question) return;
        stateFor(question, true).inWrongBook = false;
        saveProgress();
        ui.removeWrong.classList.add("hidden");
        showToast("已移出错题本。");
        if (ui.wrongOnly.checked) removeCurrentFromSession();
    }

    function toggleExcluded() {
        const question = session[index];
        if (!question) return;
        const item = stateFor(question, true);
        if (!isExcluded(item) && !item.lastWasCorrect) {
            showToast("答对后才能设为以后不再抽取。");
            return;
        }
        item.excluded = !isExcluded(item);
        saveProgress();
        showToast(item.excluded ? "以后练习将不再抽到本题。" : "本题已恢复抽取。");
        if ((ui.excludedOnly.checked && !item.excluded) || (!ui.excludedOnly.checked && item.excluded)) {
            removeCurrentFromSession();
        } else {
            renderCurrent();
        }
    }

    function removeCurrentFromSession() {
        session.splice(index, 1);
        if (index >= session.length) index = Math.max(0, session.length - 1);
        renderCurrent();
    }

    function copyCurrent() {
        const question = session[index];
        if (!question) return;
        const text = formatQuestion(question, false);
        if (window.AndroidBridge) window.AndroidBridge.copyText(text);
        else if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(() => showToast("已复制。"));
    }

    function formatQuestion(question, includeAnswer) {
        const source = question.setName ? `${question.setName} #${question.sourceNumber}` : `公开题源 #${question.sourceNumber}`;
        let text = `[${question.domain}] ${source}\n${question.stem}\n`;
        question.options.forEach((option, i) => { text += `${letters[i]}. ${option}\n`; });
        if (includeAnswer) text += `参考答案：${letters[question.correctIndex]}\n题源解析：${question.explanation}`;
        return text.trim();
    }

    function exportWrongBook() {
        const wrong = allQuestions.filter((question) => isWrong(stateFor(question)));
        if (!wrong.length) {
            showToast("错题本还是空的，暂无可导出内容。");
            return;
        }
        const lines = [
            "# CISP 错题请教", "",
            "请逐题解释：正确选项为什么正确，其他选项错在哪里，并给出相关 CISP 知识点和记忆方法。如题目或答案已过时，请明确指出。", "",
            `> 共 ${wrong.length} 道错题；题源为公开网络学习资料，不是官方真题。`, ""
        ];
        const imageNames = new Set();
        wrong.forEach((question, questionIndex) => {
            const item = stateFor(question);
            const source = question.setName ? `${question.setName} #${question.sourceNumber}` : `公开题源 #${question.sourceNumber}`;
            lines.push(`## ${questionIndex + 1}. [${question.domain}] ${source}`, "", question.stem, "");
            (question.imageNames || []).forEach((name) => {
                imageNames.add(name);
                lines.push(`![题图](images/${name})`, "");
            });
            question.options.forEach((option, optionIndex) => lines.push(`- ${letters[optionIndex]}. ${option}`));
            const selected = item && Number.isInteger(item.lastSelectedIndex) ? letters[item.lastSelectedIndex] : "未记录";
            lines.push("", `- 我最近选的：${selected}`, `- 参考答案：${letters[question.correctIndex]}`, `- 题源解析：${question.explanation}`, "");
        });
        if (window.AndroidBridge) {
            window.AndroidBridge.exportWrongBook(lines.join("\n"), JSON.stringify([...imageNames]));
        } else {
            showToast("请在 Android 应用内使用导出功能。");
        }
    }

    function updateSummary() {
        const pool = currentQuestions();
        const items = Object.values(progress.questions || {});
        const attempted = items.filter((item) => (item.attemptCount || 0) > 0).length;
        const attempts = items.reduce((sum, item) => sum + (item.attemptCount || 0), 0);
        const correct = items.reduce((sum, item) => sum + (item.correctCount || 0), 0);
        ui.questionCount.textContent = `${pool.length} 道`;
        if (ui.practiceMode.value === "sets") {
            ui.attempted.textContent = `本组已答 ${sessionAttemptCount} 道`;
            ui.accuracy.textContent = sessionAttemptCount
                ? `本组正确率 ${Math.round(sessionCorrectCount * 100 / sessionAttemptCount)}%`
                : "本组正确率 --";
        } else {
            ui.attempted.textContent = `已做 ${attempted} 道`;
            ui.accuracy.textContent = attempts ? `正确率 ${Math.round(correct * 100 / attempts)}%` : "正确率 --";
        }
        ui.wrongCount.textContent = `错题本 ${pool.filter((q) => isWrong(stateFor(q))).length} 道`;
        ui.markedCount.textContent = `标记 ${pool.filter((q) => isMarked(stateFor(q))).length} 道`;
        ui.excludedCount.textContent = `不再抽取 ${pool.filter((q) => isExcluded(stateFor(q))).length} 道`;
    }

    function buildMaterials() {
        for (let set = 1; set <= 5; set++) {
            [["questions", `第 ${set} 套 · 题目`], ["answers", `第 ${set} 套 · 答案`]].forEach(([kind, label]) => {
                const button = document.createElement("button");
                button.textContent = label;
                button.addEventListener("click", () => {
                    if (window.AndroidBridge) window.AndroidBridge.openPdf(`set-${set}-${kind}.pdf`);
                    ui.materialsModal.classList.add("hidden");
                });
                ui.materialList.appendChild(button);
            });
        }
    }

    function showToast(message) {
        ui.toast.textContent = message;
        ui.toast.classList.remove("hidden");
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => ui.toast.classList.add("hidden"), 2400);
    }

    function initialize() {
        const domains = [...new Set(allQuestions.map((question) => question.domain))];
        domains.forEach((domain) => {
            const option = document.createElement("option");
            option.value = domain;
            option.textContent = domain;
            ui.domain.appendChild(option);
        });
        questionSets.forEach((set) => {
            const option = document.createElement("option");
            option.value = set.id;
            option.textContent = `${set.name}（${set.questionCount}题）`;
            ui.setPicker.appendChild(option);
        });
        ui.maxCount.textContent = String(questions.length);
        ui.sessionSize.max = String(Math.max(questions.length, 1));
        ui.sessionSize.value = String(progress.sessionSize);
        ui.applySize.addEventListener("click", startSession);
        ui.practiceMode.addEventListener("change", () => {
            ui.setPickerGroup.classList.toggle("hidden", ui.practiceMode.value !== "sets");
            startSession();
        });
        ui.setPicker.addEventListener("change", startSession);
        [ui.domain, ui.wrongOnly, ui.markedOnly, ui.excludedOnly].forEach((control) => control.addEventListener("change", startSession));
        ui.submit.addEventListener("click", submitAnswer);
        ui.next.addEventListener("click", nextQuestion);
        ui.mark.addEventListener("click", toggleMark);
        ui.removeWrong.addEventListener("click", removeFromWrongBook);
        ui.exclude.addEventListener("click", toggleExcluded);
        ui.copy.addEventListener("click", copyCurrent);
        ui.export.addEventListener("click", exportWrongBook);
        ui.source.addEventListener("click", () => { if (window.AndroidBridge) window.AndroidBridge.openSource(); });
        ui.materials.addEventListener("click", () => ui.materialsModal.classList.remove("hidden"));
        ui.closeMaterials.addEventListener("click", () => ui.materialsModal.classList.add("hidden"));
        ui.materialsModal.addEventListener("click", (event) => { if (event.target === ui.materialsModal) ui.materialsModal.classList.add("hidden"); });
        buildMaterials();
        updateSummary();
        startSession();
    }

    initialize();
})();
